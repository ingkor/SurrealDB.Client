# Critical 10x Developer Role - SurrealDB.Client

## Overview

The Critical 10x Developer is responsible for the performance, efficiency, and highest-impact code decisions in the SurrealDB.Client library. This role operates at the intersection of deep technical knowledge, systems thinking, and prioritization ruthlessness.

A 10x developer on this project does not just write more code — they write better code in the right places, eliminate the bugs that will waste the entire team's time, and ensure the foundational pieces are correct before building higher-level features on top of them.

This role has veto power over code that introduces known performance regressions, concurrency bugs, or architectural violations.

---

## Core Focus Areas

### 1. Eliminate the Known Critical Bugs First

There are three bugs in the current codebase that will cause catastrophic failures in production. Nothing else matters until these are fixed:

**Bug 1: ConnectionPool Deadlock in DisposeAsync**

Location: `src/SurrealDB.Client/Connection/ConnectionPool.cs`, `DisposeAsync`

```csharp
// CURRENT CODE — DEADLOCKS
public async ValueTask DisposeAsync()
{
    if (_disposed) return;
    _disposed = true;
    await _disposeSemaphore.WaitAsync();  // Acquires semaphore
    try
    {
        await ClearAsync();  // ClearAsync also calls _disposeSemaphore.WaitAsync() — DEADLOCK
    }
    finally { ... }
}
```

Impact: Any application that creates and disposes a `SurrealDbClient` will hang indefinitely.

**Bug 2: Data Race on _allConnections in GetStatistics()**

Location: `ConnectionPool.GetStatistics()`

```csharp
// CURRENT CODE — DATA RACE
return new PoolStatistics
{
    TotalConnections = _allConnections.Count,         // No lock
    InUseConnections = _allConnections.Count(c => c.InUse),  // No lock — iterates while other threads may add/remove
};
```

Impact: Corrupted statistics, potential `InvalidOperationException` from concurrent HashSet modification, or worse — silent memory corruption.

**Bug 3: WebSocket Response Truncation**

Location: `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs`, `SendAsync`

```csharp
// CURRENT CODE — TRUNCATES RESPONSES > 4KB
var buffer = new byte[1024 * 4];  // 4 KB hard limit
var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
// If response > 4KB, the rest is silently discarded
```

Impact: Any query returning more than ~4 KB of data will return silently truncated/corrupt JSON. This will cause serialization failures or, worse, silently return partial data.

### 2. Connection Pool Performance

The `ConnectionPool` class is the most critical performance component. It determines the maximum throughput of the entire library.

Current issues:
- `_allConnections` (HashSet) uses a coarse `lock` for every read/write — consider `ConcurrentDictionary` with sentinel values
- `PooledConnection.InUse` is a `bool` set without atomicity guarantees — use `Interlocked` or consolidate under a lock
- The `AvailableConnections` counter (`_availableConnections.Count`) on `ConcurrentBag` is O(n) — use a separate counter

### 3. Async Path Efficiency

Every CRUD operation will go through:
1. `SurrealDbClient.XxxAsync` → validate → acquire connection
2. `ConnectionPool.AcquireAsync` → semaphore wait → health check
3. `IProtocolAdapter.SendAsync` → HTTP or WebSocket round trip
4. Deserialization → return

The health check on every acquire (`HealthCheckAsync`) is an extra network round trip per operation. Consider:
- Skipping health check for connections used recently (track `LastUsedAt` with a configurable threshold)
- Making health checks periodic (background task) rather than on-acquire

### 4. WebSocket vs. HTTP Performance

For workloads with many small queries, WebSocket significantly outperforms HTTP because it avoids:
- TCP handshake setup for each request
- HTTP header overhead
- TLS negotiation (for HTTPS)

However, `WebSocketProtocolAdapter.SendAsync` is currently implemented as a send-then-synchronous-receive. In a multi-user scenario, responses to different requests can arrive interleaved on the same WebSocket connection. The current single receive-per-send implementation is fundamentally broken for concurrent use.

For correctness, one of these approaches must be taken:
1. **One WebSocket per connection in the pool** (current design intent) — each connection from the pool gets its own WebSocket, no multiplexing needed
2. **Multiplex with request correlation** — send multiple requests on one WebSocket, match responses by `id` field in JSON-RPC

Option 1 is simpler and aligns with the existing pool design. Option 2 would enable fewer connections with higher throughput.

### 5. Memory Allocation Profile

Every `QueryAsync` call in the current stub implementation allocates:
- `new QueryResult { Status = "OK" }` — trivial but pattern is established

In the real implementation, hot paths should minimize allocations:
- Use `ArrayPool<byte>.Shared` for WebSocket receive buffers (not `new byte[n]`)
- Reuse `StringBuilder` or `MemoryStream` for response accumulation
- Consider `Span<T>` / `Memory<T>` for serialization where possible

---

## Key Files for Performance Work

| File | Performance Concern |
|------|-------------------|
| `src/SurrealDB.Client/Connection/ConnectionPool.cs` | Semaphore latency, lock granularity, health check overhead |
| `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` | Buffer allocation, multi-frame reads, concurrency model |
| `src/SurrealDB.Client/Protocol/HttpProtocolAdapter.cs` | HttpClient reuse, connection keep-alive |
| `src/SurrealDB.Client/Serialization/SystemTextJsonSerializer.cs` | JSON deserialization allocation |
| `src/SurrealDB.Client/SurrealDbClient.cs` | Per-operation connection acquire/release overhead |

---

## Performance Targets (from ARCHITECTURE.md)

| Metric | Target | Current Status |
|--------|--------|---------------|
| Pool initialization | < 1 second | Not measured (stubs) |
| Authentication | < 500 ms | Not measured (stubs) |
| Simple SELECT | < 50 ms | Not implemented |
| Typical UPDATE | < 100 ms | Not implemented |
| Batch 100 records | < 200 ms | Not implemented |
| Bandwidth: 1 property update vs full object | 5x reduction | Needs ChangeTracker |

---

## Impact Prioritization Matrix

When deciding where to spend engineering time, use this matrix:

| Task | User Impact | Technical Risk | Priority |
|------|------------|---------------|---------|
| Fix DisposeAsync deadlock | Critical — every app hangs | Low — clear fix | P0 |
| Fix GetStatistics data race | High — corrupts stats + potential crash | Low — add lock | P0 |
| Fix WebSocket 4KB truncation | Critical — silent data corruption | Medium — needs read loop | P0 |
| Implement CRUD operations | Critical — library is unusable | Medium | P1 |
| Fix null-adapter window in AcquireAsync | High — null reference crash under load | Medium | P1 |
| Add health check throttling | Medium — reduces latency 10–50ms/op | Low | P2 |
| ArrayPool buffer for WebSocket | Medium — reduces GC pressure | Low | P2 |
| ChangeTracker differential updates | High — 10x bandwidth reduction | High | P2 |
| ConcurrentDictionary for _allConnections | Medium — removes coarse lock | Medium | P3 |
