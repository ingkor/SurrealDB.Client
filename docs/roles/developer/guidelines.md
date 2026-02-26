# Developer Guidelines - SurrealDB.Client

## Architecture Principles

### The Protocol Adapter Pattern

All communication with SurrealDB must go through `IProtocolAdapter`. `SurrealDbClient` holds a reference to `IProtocolAdapter` (`_currentConnection`) and must never create an `HttpClient` or `ClientWebSocket` directly. This abstraction is what allows the same client code to work over HTTP or WebSocket.

```csharp
// Correct: delegate to the adapter
var json = await _currentConnection.SendAsync("POST", "sql", queryBody, cancellationToken)
    .ConfigureAwait(false);

// Wrong: bypass the adapter
using var http = new HttpClient(); // never do this in SurrealDbClient
```

### Connection Pool Ownership

`SurrealDbClient.ConnectAsync` owns the `ConnectionPool` lifecycle. All CRUD and query methods must acquire a connection from the pool for the duration of a single operation, then release it. The current code holds `_currentConnection` permanently after `ConnectAsync`; this should be refactored so each operation acquires/releases independently, enabling true concurrent usage.

Pattern for safe acquire/release:

```csharp
IProtocolAdapter? connection = null;
try
{
    connection = await _connectionPool!.AcquireAsync(cancellationToken).ConfigureAwait(false);
    var response = await connection.SendAsync("POST", "sql", body, cancellationToken).ConfigureAwait(false);
    return DeserializeResult<T>(response);
}
catch
{
    // Mark connection unhealthy on exception
    if (connection != null)
        await _connectionPool!.ReleaseAsync(connection, healthy: false).ConfigureAwait(false);
    connection = null;
    throw;
}
finally
{
    if (connection != null)
        await _connectionPool!.ReleaseAsync(connection, healthy: true).ConfigureAwait(false);
}
```

### Semaphore Rules

`ConnectionPool` uses two semaphores:
- `_acquireSemaphore(initialCount: PoolSize)` — limits concurrent active connections
- `_disposeSemaphore(1, 1)` — mutual exclusion for the dispose/clear path

**Never acquire `_disposeSemaphore` from code that may already hold it.** The current `DisposeAsync` → `ClearAsync` path violates this rule and will deadlock. If you need to share logic between the two, extract it into a private method that does not acquire the semaphore, and let the callers manage locking.

### ConfigureAwait(false) Is Mandatory

Library code must never capture the synchronization context. This prevents deadlocks for callers running in ASP.NET, WPF, or other contexts with a synchronization context.

```csharp
// Always
await _pool.AcquireAsync(ct).ConfigureAwait(false);

// Never in library code
await _pool.AcquireAsync(ct); // captures context
```

---

## Implementing CRUD Operations

SurrealDB's HTTP API accepts SurrealQL at `POST /sql`. Build the query string and pass it as the body.

### SurrealQL Quick Reference

| Operation | SurrealQL |
|-----------|-----------|
| Create with auto-ID | `CREATE <table> CONTENT <json>` |
| Create with known ID | `CREATE <table>:<id> CONTENT <json>` |
| Get by ID | `SELECT * FROM <table>:<id>` |
| Select all | `SELECT * FROM <table>` |
| Update (full replace) | `UPDATE <table>:<id> CONTENT <json>` |
| Update (partial merge) | `UPDATE <table>:<id> MERGE <json>` |
| Delete | `DELETE <table>:<id>` |
| Upsert | `INSERT INTO <table> (SELECT * FROM [<json>]) ON DUPLICATE KEY UPDATE ...` or use `RELATE` |

### Serialization Pattern

```csharp
// Serialize request
var body = _serializer.Serialize(data);

// Execute
var rawResponse = await connection.SendAsync("POST", "sql", body, cancellationToken)
    .ConfigureAwait(false);

// Deserialize response
var result = _serializer.Deserialize<SurrealQueryResponse<T>>(rawResponse);
if (!result.IsSuccess)
    throw new QueryException(result.Error ?? "Query failed");

return result.Data;
```

---

## Error Handling

Use the typed exception hierarchy in `src/SurrealDB.Client/Exceptions/`. Never throw `Exception` directly.

| Scenario | Exception Type |
|----------|---------------|
| Network failure, connection refused | `ConnectionException` |
| Query timed out | `TimeoutException` |
| Invalid SurrealQL, query parse error | `QueryException` |
| Wrong credentials, invalid token | `AuthenticationException` |
| Empty table name, invalid record ID | `ValidationException` |
| Serialization / deserialization failure | `SerializationException` |

The `SurrealDbException` base class should be used as the catch-all when no specific type fits.

---

## Testing Patterns

### Mocking the Protocol Adapter

Use `Moq` (already a test dependency) to mock `IProtocolAdapter`:

```csharp
var mockAdapter = new Mock<IProtocolAdapter>();
mockAdapter.Setup(a => a.IsConnected).Returns(true);
mockAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(true);
mockAdapter.Setup(a => a.SendAsync("POST", "sql", It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync("[{\"result\":[{\"id\":\"user:1\",\"name\":\"Test\"}],\"status\":\"OK\"}]");
```

### Concurrency Tests for the Pool

Use `Task.WhenAll` to stress the pool:

```csharp
var tasks = Enumerable.Range(0, pool.MaxSize * 2)
    .Select(_ => pool.AcquireAsync());
// Should not deadlock; excess requests should timeout or throw
```

### Integration Tests

Integration tests in `SurrealDB.Client.Tests.Integration` require a live SurrealDB instance. They are skipped in CI unless `SURREALDB_URL` environment variable is set.

---

## Code Review Expectations

When reviewing others' PRs or preparing your own:

1. Every new `async` method must have `CancellationToken` support
2. Every public method must call `ThrowIfDisposed()` first
3. No raw `HashSet` or `List` access without a lock from multiple threads
4. No `.Result` or `.Wait()` — always `await`
5. XML docs on all new public members
6. At least one test per logical branch (happy path + at least one error path)
7. Disposal paths that suppress exceptions are intentional — add a comment explaining why
