# Tester Guidelines - SurrealDB.Client

## Test Architecture Principles

### Unit Tests Must Be Isolated

Unit tests in `SurrealDB.Client.Tests.Unit` must never:
- Make HTTP requests
- Open WebSocket connections
- Depend on `SURREALDB_URL` or any environment variable
- Have non-deterministic behavior (no `Thread.Sleep`, no random IDs without seeding)

All external dependencies must be mocked using `Moq`.

### Integration Tests Must Be Idempotent

Each integration test must leave the database in the same state it found it. Use `IAsyncLifetime` to set up and tear down test data:

```csharp
public class CrudIntegrationTests : IAsyncLifetime
{
    private SurrealDbClient _client = null!;
    private const string TestTable = "test_crud_" + nameof(CrudIntegrationTests);

    public async Task InitializeAsync()
    {
        _client = new SurrealDbClient(TestConnectionString);
        await _client.ConnectAsync();
        await _client.AuthenticateAsync("root", "root");
        // Delete any leftover test data
        await _client.QueryAsync($"DELETE {TestTable}");
    }

    public async Task DisposeAsync()
    {
        // Clean up even if tests failed
        await _client.QueryAsync($"DELETE {TestTable}");
        await _client.DisposeAsync();
    }
}
```

---

## Writing Effective Concurrency Tests

### The Standard Concurrent Stress Pattern

```csharp
[Fact(Timeout = 10_000)]  // 10 second hard timeout to catch deadlocks
public async Task ConnectionPool_ConcurrentAcquireRelease_DoesNotDeadlock()
{
    // Arrange
    var options = new SurrealDbClientOptions { PoolSize = 5 };
    var mockAdapter = CreateMockAdapter(healthy: true);
    var pool = new ConnectionPool(options, _ => Task.FromResult(mockAdapter.Object));
    await pool.InitializeAsync();

    // Act: 20 concurrent acquire+release cycles on a pool of 5
    var tasks = Enumerable.Range(0, 20).Select(async _ =>
    {
        var conn = await pool.AcquireAsync();
        await Task.Delay(10); // simulate work
        await pool.ReleaseAsync(conn, healthy: true);
    });

    // Should complete without deadlock (timeout enforces this)
    await Task.WhenAll(tasks);
}
```

### Barrier Synchronization for Race Conditions

When testing race conditions where precise timing matters, use `Barrier`:

```csharp
[Fact]
public async Task ConnectionPool_Statistics_ThreadSafeUnderConcurrentAccess()
{
    var options = new SurrealDbClientOptions { PoolSize = 10 };
    var pool = new ConnectionPool(options, CreateFactory());
    await pool.InitializeAsync();

    var barrier = new Barrier(participantCount: 11); // 10 workers + 1 stats reader
    var exceptions = new ConcurrentBag<Exception>();

    // 10 threads acquiring connections simultaneously
    var acquireTasks = Enumerable.Range(0, 10).Select(async _ =>
    {
        barrier.SignalAndWait();
        try { var conn = await pool.AcquireAsync(); }
        catch (Exception ex) { exceptions.Add(ex); }
    });

    // 1 thread reading statistics simultaneously
    var statTask = Task.Run(() =>
    {
        barrier.SignalAndWait();
        for (int i = 0; i < 100; i++)
        {
            try { pool.GetStatistics(); }
            catch (Exception ex) { exceptions.Add(ex); }
        }
    });

    await Task.WhenAll(acquireTasks.Append(statTask));
    Assert.Empty(exceptions);
}
```

---

## Mocking IProtocolAdapter

The standard mock setup used in `ConnectionPoolTests.cs`:

```csharp
private static Mock<IProtocolAdapter> CreateMockAdapter(
    bool healthy = true,
    string sendResponse = "{\"result\":[],\"status\":\"OK\"}")
{
    var mock = new Mock<IProtocolAdapter>();
    mock.Setup(a => a.IsConnected).Returns(true);
    mock.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(healthy);
    mock.Setup(a => a.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(sendResponse);
    mock.Setup(a => a.DisposeAsync())
        .Returns(ValueTask.CompletedTask);
    return mock;
}
```

### Testing Exception Scenarios

```csharp
// Mock an adapter that throws on HealthCheck (simulates dead connection)
var deadAdapter = new Mock<IProtocolAdapter>();
deadAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(false);

// Mock an adapter that throws on SendAsync (simulates query failure)
var failingAdapter = new Mock<IProtocolAdapter>();
failingAdapter.Setup(a => a.SendAsync(
        It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
    .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));
```

---

## Timeout-Based Deadlock Detection

When a method might deadlock, wrap it in a `CancellationTokenSource` with a short timeout:

```csharp
[Fact]
public async Task SomeMethod_DoesNotDeadlock()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    // If this deadlocks, it will throw OperationCanceledException after 5 seconds
    // rather than hanging the test suite indefinitely
    await pool.DisposeAsync(); // the deadlock candidate

    // If we reach here, no deadlock occurred
}
```

Alternatively, use the xUnit `[Fact(Timeout = 5000)]` attribute (timeout in milliseconds).

---

## Test Data Management

### Unique Table Names in Integration Tests

Use a unique suffix per test class to avoid inter-test interference:

```csharp
// In each integration test class
private const string TestTable = "test_" + nameof(CrudIntegrationTests) + "_records";
```

### SurrealDB Record ID Format

SurrealDB record IDs use the format `table:id`. When creating test data:

```csharp
// Specific ID
await client.CreateAsync("users:test1", new User { Name = "Test" });

// Auto-generated ID
var created = await client.CreateAsync("users", new User { Name = "Test" });
// created.Id will be something like "users:abc123def456"
```

---

## Coverage Targets

| Area | Current Estimate | Phase 1 Target |
|------|-----------------|----------------|
| `SurrealDbClient.cs` (CRUD) | ~20% (stubs) | 85% |
| `ConnectionPool.cs` | ~60% | 90% |
| `HttpProtocolAdapter.cs` | ~30% | 80% |
| `WebSocketProtocolAdapter.cs` | ~30% | 80% |
| `SurrealDbClientOptions.cs` | ~90% | 90% |
| Exception types | ~50% | 80% |
| **Overall** | **~40%** | **85%** |

---

## Test Maintenance

### When a Test Starts Failing Unexpectedly
1. Check if the test was passing before a recent code change: `git log --oneline -10`
2. Check if the test has a timing dependency (use `Task.Delay` or external resource)
3. Check if environment variables are set for integration tests
4. Do NOT comment out or skip failing tests without a linked issue and a timeline to fix

### When Refactoring Tests
- Keep one assertion per test where possible (or group tightly related assertions)
- Do NOT remove tests for fixed bugs — they are regression guards
- When a class is renamed, update all test class names to match the convention `ClassNameTests`
