# Tester Checklist - SurrealDB.Client

## Before Testing a New Feature

- [ ] Read the acceptance criteria on the story — confirm they are testable
- [ ] Identify the happy path (normal, expected usage)
- [ ] Identify at least 3 error paths (invalid input, network failure, timeout, disposed object)
- [ ] Check if the feature involves async code — if yes, test cancellation and timeout scenarios
- [ ] Check if the feature involves shared state — if yes, plan a concurrency test

---

## Unit Test Completeness Checklist

For each new class or method:

- [ ] Happy path: method returns correct value for valid inputs
- [ ] Null/empty inputs: `null` table, empty string ID, null data — expect `ValidationException`
- [ ] Disposed object: call method after `DisposeAsync()` — expect `ObjectDisposedException`
- [ ] Cancellation: pass a pre-cancelled `CancellationToken` — expect `OperationCanceledException` or timeout exception
- [ ] Adapter failure: mock `IProtocolAdapter` to throw — verify the correct `SurrealDbException` subtype is re-thrown
- [ ] Return value deserialization: mock adapter to return well-formed JSON — verify output is correctly deserialized

---

## ConnectionPool Test Checklist

The pool is the most bug-prone component. Ensure these scenarios are covered:

- [ ] **Initialize**: after `InitializeAsync`, `AvailableCount > 0` and `CurrentSize == initialSize`
- [ ] **Acquire healthy**: `AcquireAsync` returns an adapter when one is available
- [ ] **Acquire from empty pool**: `AcquireAsync` creates a new connection when all existing are in use
- [ ] **Acquire at limit**: acquiring when pool is at `PoolSize` blocks until one is released
- [ ] **Acquire timeout**: `AcquireAsync` throws within configured timeout when pool is exhausted
- [ ] **Release healthy**: after `ReleaseAsync(conn, healthy: true)`, `AvailableCount` is restored
- [ ] **Release unhealthy**: after `ReleaseAsync(conn, healthy: false)`, connection is disposed, not returned to pool
- [ ] **Unhealthy eviction**: if `HealthCheckAsync` returns false, connection is removed from pool
- [ ] **Statistics accuracy**: `GetStatistics()` returns counts matching actual state
- [ ] **Clear**: after `ClearAsync`, `CurrentSize == 0` and `AvailableCount == 0`
- [ ] **Dispose**: after `DisposeAsync`, calling `AcquireAsync` throws `ObjectDisposedException`
- [ ] **Dispose does not deadlock**: `DisposeAsync` completes within 5 seconds (use `CancellationToken` as timeout guard)
- [ ] **Concurrent acquire**: 10 concurrent `AcquireAsync` calls on a pool of size 5 — 5 succeed immediately, 5 block until first 5 are released
- [ ] **Statistics thread safety**: calling `GetStatistics()` concurrently with `AcquireAsync` does not throw

---

## CRUD Operations Test Checklist

For each operation (once stubs are replaced with real implementations):

### CreateAsync
- [ ] Creates a record and returns it with an assigned ID from SurrealDB
- [ ] Empty table name throws `ValidationException`
- [ ] Null data... document the expected behavior (throw or reject?)
- [ ] Bulk create returns all created records

### GetAsync
- [ ] Returns the correct record for a valid ID
- [ ] Returns `null` for a non-existent ID
- [ ] Empty record ID throws `ValidationException`
- [ ] Malformed record ID (no colon separator) — document expected behavior

### SelectAsync
- [ ] Returns all records in the table
- [ ] Returns empty collection for an empty table
- [ ] Empty table name throws `ValidationException`

### UpdateAsync
- [ ] Updates the record and returns the updated state from SurrealDB
- [ ] Non-existent record ID... document expected behavior (create? throw?)
- [ ] Empty record ID throws `ValidationException`

### DeleteAsync
- [ ] Deletes an existing record without throwing
- [ ] Deleting a non-existent record — document expected behavior (throw or succeed silently?)
- [ ] Empty record ID throws `ValidationException`

### UpsertAsync
- [ ] Creates a new record if ID does not exist
- [ ] Updates an existing record if ID exists
- [ ] Returns the final state of the record

---

## Protocol Adapter Test Checklist

### HttpProtocolAdapter
- [ ] `ConnectAsync` succeeds when server returns 2xx
- [ ] `ConnectAsync` throws `ConnectionException` when server returns non-2xx
- [ ] `ConnectAsync` throws `TimeoutException` when server does not respond within timeout
- [ ] `SendAsync` returns response body on success
- [ ] `SendAsync` throws `QueryException` on non-2xx response
- [ ] `HealthCheckAsync` returns true when server health endpoint returns 2xx
- [ ] `HealthCheckAsync` returns false when server is unreachable

### WebSocketProtocolAdapter
- [ ] `ConnectAsync` establishes a WebSocket connection
- [ ] `SendAsync` sends a JSON-RPC message and receives a response
- [ ] `SendAsync` handles responses larger than 4 KB (multi-frame)
- [ ] `SendAsync` handles `WebSocketState.Closed` — throws `ConnectionException`
- [ ] `HealthCheckAsync` returns false when WebSocket is not Open
- [ ] `DisposeAsync` closes the WebSocket cleanly

---

## Integration Test Checklist

- [ ] Test setup creates isolated test data (unique table or namespace per test run)
- [ ] Test teardown deletes all created test data regardless of test outcome
- [ ] Tests use `IAsyncLifetime` for async setup/teardown
- [ ] Tests are skipped (not fail) when `SURREALDB_URL` env var is not set
- [ ] Each integration test is independent — no shared mutable state between tests

---

## Regression Test Checklist (for Bug Fixes)

For each fixed bug:

- [ ] Write a test that fails on the buggy code and passes on the fix
- [ ] Add a code comment on the test referencing the bug report or PR number
- [ ] Verify the test is not trivially passing (run it against the old code to confirm it fails)

Example regression test comment:
```csharp
// Regression test for ConnectionPool deadlock bug.
// DisposeAsync was calling ClearAsync while holding _disposeSemaphore,
// causing it to deadlock on ClearAsync's attempt to acquire the same semaphore.
// Fixed in PR #42.
[Fact(Timeout = 5000)]
public async Task ConnectionPool_Dispose_DoesNotDeadlock()
{
    ...
}
```
