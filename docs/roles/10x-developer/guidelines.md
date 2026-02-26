# Critical 10x Developer Guidelines - SurrealDB.Client

## Principle: Fix the Foundation Before Building Higher

The three critical bugs (DisposeAsync deadlock, GetStatistics data race, WebSocket truncation) must be fixed before implementing ANY new features. A library with these bugs cannot be used in production regardless of how many features it has.

The return on investment for fixing these bugs is approximately:
- DisposeAsync deadlock: Unblocks every consumer that disposes a client — affects 100% of usage
- GetStatistics data race: Prevents production crashes in monitoring/metrics code
- WebSocket truncation: Unblocks any query returning data larger than 4 KB — affects most real-world queries

---

## Fix 1: DisposeAsync Deadlock

The root cause is that `DisposeAsync` acquires `_disposeSemaphore` and then calls `ClearAsync()`, which also tries to acquire `_disposeSemaphore`. Since `SemaphoreSlim` is not reentrant by design, this blocks forever.

**Correct pattern**: Extract the shared logic into a private non-semaphore method:

```csharp
private async Task ClearConnectionsAsync()
{
    // Drain available connections
    while (_availableConnections.TryTake(out var pooled))
    {
        await DisposeConnectionAsync(pooled);
    }
    lock (_allConnections)
    {
        _allConnections.Clear();
    }
}

public async Task ClearAsync()
{
    ThrowIfDisposed();
    await _disposeSemaphore.WaitAsync().ConfigureAwait(false);
    try
    {
        await ClearConnectionsAsync().ConfigureAwait(false);
    }
    finally
    {
        _disposeSemaphore.Release();
    }
}

public async ValueTask DisposeAsync()
{
    if (_disposed) return;
    _disposed = true;

    await _disposeSemaphore.WaitAsync().ConfigureAwait(false);
    try
    {
        await ClearConnectionsAsync().ConfigureAwait(false);  // No re-entry
    }
    finally
    {
        _disposeSemaphore.Release();
        _disposeSemaphore.Dispose();
        _acquireSemaphore.Dispose();
    }
}
```

Note the subtle secondary issue: setting `_disposed = true` before acquiring `_disposeSemaphore` means `ThrowIfDisposed()` in `ClearAsync` will throw if called externally after `DisposeAsync` starts. This is actually the correct behavior — external calls should be rejected once disposal begins.

---

## Fix 2: GetStatistics Data Race

The `HashSet<PooledConnection>` requires external synchronization for all operations. The current `GetStatistics()` reads from it without a lock.

```csharp
// CURRENT — DATA RACE
public PoolStatistics GetStatistics()
{
    return new PoolStatistics
    {
        TotalConnections = _allConnections.Count,
        AvailableConnections = _availableConnections.Count,
        InUseConnections = _allConnections.Count(c => c.InUse),  // Enumerates without lock
        ...
    };
}

// FIXED — lock for the snapshot
public PoolStatistics GetStatistics()
{
    lock (_allConnections)
    {
        return new PoolStatistics
        {
            TotalConnections = _allConnections.Count,
            AvailableConnections = _availableConnections.Count,
            InUseConnections = _allConnections.Count(c => c.InUse),
            TotalAcquisitions = Interlocked.Read(ref _totalAcquisitions),
            TotalReleases = Interlocked.Read(ref _totalReleases),
            FailedHealthChecks = Interlocked.Read(ref _failedHealthChecks)
        };
    }
}
```

Note: `Interlocked.Read` on `long` fields is necessary on 32-bit systems to avoid torn reads, even though modern 64-bit systems handle it atomically.

**Secondary issue**: `_allConnections.Count(c => c.InUse)` reads `PooledConnection.InUse` which is a plain `bool`. This field is written from `AcquireAsync` and `ReleaseAsync` under no lock. Consider using `Interlocked.CompareExchange(ref int, ...)` with a separate integer counter for in-use tracking.

---

## Fix 3: WebSocket Response Accumulation

The `ClientWebSocket.ReceiveAsync` call returns one buffer's worth of data. For messages larger than the buffer, it sets `result.EndOfMessage = false` to signal more frames are coming. The current code ignores this flag and silently returns a truncated response.

```csharp
// FIXED — accumulate until EndOfMessage
public async Task<string> SendAsync(
    string method,
    string path,
    string? body = null,
    CancellationToken cancellationToken = default)
{
    // ... send logic unchanged ...

    // Receive loop
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(_options.CommandTimeout);

    var buffer = ArrayPool<byte>.Shared.Rent(1024 * 16);  // 16KB initial buffer
    try
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
                throw new ConnectionException("WebSocket closed during receive");

            ms.Write(buffer, 0, result.Count);

            // Enforce a maximum response size to prevent OOM
            if (ms.Length > 50 * 1024 * 1024)  // 50MB limit
                throw new QueryException("Response exceeded maximum size limit");

        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

Key decisions in this implementation:
1. `ArrayPool<byte>.Shared.Rent(16 * 1024)` — avoids per-call allocation
2. `MemoryStream` accumulates across frames
3. Size limit prevents OOM from oversized responses
4. `MessageType == Close` is handled — prevents silent corruption

---

## Performance Optimization: Health Check Throttling

Every `AcquireAsync` currently calls `HealthCheckAsync`, which is a network round trip. For a pool of 5 connections with 100 req/s, this is 100 health checks per second against the server.

**Optimization**: Skip health check for connections used in the last N seconds:

```csharp
private static readonly TimeSpan HealthCheckGracePeriod = TimeSpan.FromSeconds(30);

// In AcquireAsync, before calling HealthCheckAsync:
var timeSinceLastUse = DateTime.UtcNow - pooledConnection.LastUsedAt;
if (timeSinceLastUse < HealthCheckGracePeriod)
{
    // Skip health check — connection was recently used and is likely still healthy
    pooledConnection.InUse = true;
    pooledConnection.LastUsedAt = DateTime.UtcNow;
    pooledConnection.UsageCount++;
    Interlocked.Increment(ref _totalAcquisitions);
    return pooledConnection.Adapter;
}
// Only check connections that have been idle
if (await pooledConnection.Adapter.HealthCheckAsync(cancellationToken))
{ ... }
```

This reduces health check calls by ~99% under typical load while still detecting dead connections.

---

## Memory Profile: What to Measure

For the CRUD implementation, measure per-operation allocations using `GC.GetAllocatedBytesForCurrentThread()`:

```csharp
long allocBefore = GC.GetAllocatedBytesForCurrentThread();
await client.GetAsync<User>("users:test1");
long allocAfter = GC.GetAllocatedBytesForCurrentThread();
long allocatedBytes = allocAfter - allocBefore;
```

Target allocation budget per operation:
- Simple GET: < 10 KB total allocation
- Simple SELECT (1 record): < 10 KB
- Serialize/deserialize round trip: < 5 KB

---

## Identifying and Eliminating Virtual Dispatch in Hot Paths

The pool acquire path calls `IProtocolAdapter.HealthCheckAsync` through an interface (virtual dispatch). This is fine for correctness, but in very hot paths (10K+ calls/sec) consider:
- Caching the concrete type check to avoid repeated interface dispatch
- Using sealed classes where polymorphism is not needed

In practice, for database client code, virtual dispatch overhead is negligible compared to network latency. Do not micro-optimize this unless profiler data proves it is a bottleneck.

---

## The Null-Adapter Window Bug

In `AcquireAsync`, there is a window where a `PooledConnection` is added to `_allConnections` but its `Adapter` is `null`:

```csharp
// CURRENT CODE — NULL WINDOW
lock (_allConnections)
{
    pooledConnection = new PooledConnection { Adapter = null! };  // Adapter is null
    _allConnections.Add(pooledConnection);  // Added to set with null Adapter
}
// ... other threads can now see this entry with Adapter = null ...
pooledConnection.Adapter = await _connectionFactory(cancellationToken);  // Adapter set later
```

If `GetStatistics()` or `DisposeAsync` runs between the add and the assignment, they will encounter a `null` adapter and throw `NullReferenceException`.

**Fix**: Create the adapter before adding to the set:

```csharp
var adapter = await _connectionFactory(cancellationToken).ConfigureAwait(false);
pooledConnection = new PooledConnection
{
    Id = Guid.NewGuid(),
    Adapter = adapter,  // Adapter set before adding to set
    CreatedAt = DateTime.UtcNow,
    LastUsedAt = DateTime.UtcNow,
    InUse = true,
    UsageCount = 1
};

lock (_allConnections)
{
    _allConnections.Add(pooledConnection);  // Now Adapter is always non-null
}
```

This approach creates the adapter outside the lock, which is correct because adapter creation is async and should not hold the lock.
