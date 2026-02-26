# Critical 10x Developer Checklist - SurrealDB.Client

## Code Review for High-Impact Changes

### Connection Pool Changes

- [ ] Does `DisposeAsync` avoid acquiring `_disposeSemaphore` from a code path that already holds it?
- [ ] Does every `AcquireAsync` call site have a corresponding `ReleaseAsync` in a `finally` block?
- [ ] Is `_allConnections` always accessed under `lock (_allConnections)`?
- [ ] Is `PooledConnection.InUse` set atomically or under a lock?
- [ ] Is the `AvailableCount` property O(1) or O(n)? (`ConcurrentBag.Count` is O(n))
- [ ] Are health checks throttled to avoid an extra network round-trip on every acquire?

### WebSocket Implementation

- [ ] Does `SendAsync` loop `ReceiveAsync` until `result.EndOfMessage == true`?
- [ ] Is the receive buffer allocated from `ArrayPool<byte>.Shared` (not `new byte[n]`)?
- [ ] Is the buffer returned to the pool even on exception paths?
- [ ] Is there a maximum response size limit to prevent OOM from malicious/corrupt servers?
- [ ] Does `HealthCheckAsync` avoid triggering a full request/response cycle if connection state already indicates it is closed?
- [ ] Are concurrent `SendAsync` calls on the same adapter safe? (They should not be — document this contract)

### Async Performance

- [ ] Is `.ConfigureAwait(false)` on every `await` in library code?
- [ ] Are `ValueTask` used instead of `Task` for frequently-called methods that often complete synchronously?
- [ ] Is `Task.CompletedTask` used instead of `async Task` for no-op methods?
- [ ] Are there any `async` methods that `await` only a single thing and can be simplified to `return` directly?

### Memory Allocation

- [ ] Does the hot path (CRUD operations) allocate any closures unnecessarily?
- [ ] Are string concatenations in hot paths replaced with `StringBuilder` or string interpolation?
- [ ] Is JSON deserialization using `Utf8JsonReader` (streaming) or `JsonSerializer.Deserialize<T>` from string? Prefer `ReadOnlySpan<byte>` overloads
- [ ] Are any large temporary arrays allocated per-operation that could be pooled?

---

## Performance Benchmarking Checklist

Before claiming a performance target is met:

- [ ] Run at least 1000 iterations and report the p50, p95, and p99 latencies — not just the mean
- [ ] Measure with a warm connection pool (exclude initialization time)
- [ ] Measure against a local SurrealDB instance to isolate network variance
- [ ] Measure with GC pressure enabled (`GC.Collect()` before benchmark) and without
- [ ] Compare HTTP vs WebSocket protocol performance for the same operation
- [ ] Document the measurement environment (CPU, RAM, SurrealDB version, .NET version)

---

## Bug Fix Verification Checklist

### For the DisposeAsync Deadlock Fix

- [ ] Write a test with `[Fact(Timeout = 5000)]` that calls `DisposeAsync` — it must complete within the timeout
- [ ] Write a test that calls `DisposeAsync` concurrently from 2 threads — no deadlock
- [ ] Verify `ClearAsync` still works correctly when called independently (not from `DisposeAsync`)
- [ ] Confirm the fix does not introduce a new race condition in the disposal sequence

### For the GetStatistics Data Race Fix

- [ ] Add a stress test: 100 concurrent `AcquireAsync` calls while reading `GetStatistics()` in a loop
- [ ] Verify statistics are consistent (TotalConnections >= InUseConnections + AvailableConnections)
- [ ] Confirm adding `lock (_allConnections)` in `GetStatistics` does not create a new bottleneck under load

### For the WebSocket Buffer Fix

- [ ] Write a test that mocks a WebSocket server returning a response exactly 4097 bytes
- [ ] Write a test for a response that spans 3 WebSocket frames
- [ ] Write a test for a response that is exactly at the frame boundary
- [ ] Verify the `EndOfMessage` flag is properly handled
- [ ] Verify `ArrayPool<byte>` buffers are returned in all code paths (no leaks)

---

## Code Impact Assessment

When evaluating a PR or change, ask:

1. **What is the worst-case behavior?** (Not average, but worst case under load or failure)
2. **Can this deadlock?** (Any semaphore acquired → method called that acquires same semaphore?)
3. **Can this race?** (Any shared state accessed from multiple threads without synchronization?)
4. **Can this OOM?** (Any unbounded allocation? WebSocket response with no size limit?)
5. **Can this silently fail?** (Any exception swallowed without logging? Any truncated data without error?)
6. **What happens when the server is slow/unresponsive?** (All blocking calls need timeout and cancellation)

---

## Identifying N+1 Patterns

N+1 query problems will appear when:
- A `SelectAsync` is called to get a list, then `GetAsync` is called for each item
- A `ChangeTracker` triggers one UPDATE per changed entity instead of batching

Spot these patterns during code review:

```csharp
// N+1 PROBLEM — one GetAsync per user
var userIds = await client.SelectAsync<string>("userIds");
foreach (var id in userIds)
{
    var user = await client.GetAsync<User>(id);  // N separate round trips
}

// BETTER — one SelectAsync with filter
var users = await client.QueryAsync<User>("SELECT * FROM users WHERE id IN $ids",
    new Dictionary<string, object> { ["ids"] = userIds });
```

Document N+1 risks in API documentation and provide examples of the preferred patterns.

---

## Pre-Release Performance Sign-Off

Before signing off on a Phase 1 release:

- [ ] Simple SELECT against local SurrealDB completes in < 50ms median
- [ ] Connection pool initialization (2 connections) completes in < 1 second
- [ ] Authentication completes in < 500ms
- [ ] Calling `DisposeAsync` never hangs (run 100 times with 5-second timeout)
- [ ] Calling `GetStatistics()` concurrently with `AcquireAsync` never throws
- [ ] WebSocket responses up to 1 MB are deserialized correctly (test against real SurrealDB)
- [ ] Memory allocations per CRUD operation are measured and documented
- [ ] No P0 or P1 performance issues open
