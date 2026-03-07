# Integration Tests with .NET Aspire

This project demonstrates end-to-end integration testing using **.NET Aspire** for container orchestration and resource management.

## Overview

Integration tests automatically:
- ✅ Start SurrealDB container via Aspire
- ✅ Discover connection endpoints dynamically
- ✅ Run full CRUD lifecycle tests
- ✅ Clean up resources automatically
- ✅ Skip in standard CI (optional flag)

## Running Integration Tests Locally

### Prerequisites

- .NET 8.0 SDK or later
- Docker (for container support)

### Quick Start

```bash
# Run all integration tests (starts SurrealDB automatically)
dotnet test --filter "Category=Integration"

# Or specific test
dotnet test --filter "Category=Integration&FullName~FullCrudLifecycle"
```

## How It Works

### 1. Aspire AppHost (`SurrealDB.Client.AppHost`)

Defines the SurrealDB resource:

```csharp
var surrealdb = builder
    .AddContainer("surrealdb", "surrealdb/surrealdb")
    .WithCommand("start", "--auth", "--user", "admin", "--pass", "password")
    .WithEnvironment("SURREAL_AUTH", "true")
    .WithHttpEndpoint(targetPort: 8000, name: "http")
    .WithHealthCheck();
```

### 2. Test Integration

Tests use `DistributedApplicationTestingExtensions` to:

```csharp
public class SurrealDbClientIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private SurrealDbClient? _client;

    public async Task InitializeAsync()
    {
        // Start Aspire app (includes SurrealDB container)
        _app = await DistributedApplicationTestingExtensions
            .BuildAndStartAsync(typeof(AppHost).Assembly);

        // Get SurrealDB resource dynamically
        var surrealdb = _app.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources
            .OfType<ContainerResource>()
            .First(r => r.Name == "surrealdb");

        // Extract endpoint and initialize client
        var endpoint = surrealdb.GetEndpoint("http");
        _client = new SurrealDbClient($"surreal://localhost:{endpoint.Port}");

        await _client.ConnectAsync();
        await _client.AuthenticateAsync("admin", "password");
    }

    public async Task DisposeAsync()
    {
        await _client?.DisposeAsync();
        await _app?.StopAsync();
    }
}
```

## Project Structure

```
tests/
├── SurrealDB.Client.AppHost/
│   ├── Program.cs              # Aspire app definition
│   └── *.csproj                # AppHost project file
│
└── SurrealDB.Client.Tests.Integration/
    ├── SurrealDbClientIntegrationTests.cs   # Test suite
    ├── *.csproj                 # Test project file
    └── README.md                # This file
```

## Configuration

### Credentials (AppHost)

Default test credentials:
- **Username:** `admin`
- **Password:** `password`

To change, update `Program.cs`:

```csharp
.WithEnvironment("SURREAL_USER", "your_user")
.WithEnvironment("SURREAL_PASS", "your_password")
```

### Test Timeout

Default connection timeout: 15 seconds

To adjust in test fixtures:

```csharp
var options = new SurrealDbClientOptions
{
    ConnectionTimeout = TimeSpan.FromSeconds(30)
};
```

## CI/CD Integration

### Skip Integration Tests in Standard CI

```bash
# Run only unit tests (no container required)
dotnet test --filter "Category!=Integration"
```

### Run Full Pipeline (Including Integration)

```bash
# No additional setup needed - Aspire handles everything
dotnet test

# Or explicitly
dotnet test --filter "Category=Integration"
```

## Test Coverage

### Implemented Tests

- ✅ **Full CRUD Lifecycle**: Create → Read → Update → Delete
- ✅ **Batch Operations**: Create/Select multiple records
- ✅ **UPSERT**: Create-or-update scenarios
- ✅ **Raw Queries**: Custom SurrealQL execution
- ✅ **Transactions**: BEGIN → COMMIT → ROLLBACK

### Example Test

```csharp
[Fact]
public async Task FullCrudLifecycle_CreateReadUpdateDelete_SucceedsEndToEnd()
{
    // CREATE
    var created = await _client!.CreateAsync("users", testUser);

    // READ
    var retrieved = await _client.GetAsync<User>(created.Id!);

    // UPDATE
    var updated = await _client.UpdateAsync(created.Id!, modifiedUser);

    // DELETE
    await _client.DeleteAsync(created.Id!);

    // VERIFY
    Assert.Null(await _client.GetAsync<User>(created.Id!));
}
```

## Troubleshooting

### Tests Fail with "Docker not found"

**Solution:** Ensure Docker is installed and running

```bash
docker ps  # Verify Docker is accessible
```

### Container Timeout

**Problem:** Aspire can't start SurrealDB container

**Solution:** Increase timeout or check Docker logs

```bash
docker logs surrealdb  # View container logs
```

### Connection Refused

**Problem:** Tests fail connecting to SurrealDB

**Solution:** Verify Aspire app started correctly

1. Check endpoint discovery in test output
2. Verify credentials match AppHost configuration
3. Check SurrealDB container health

## Advantages of Aspire

| Feature | Benefit |
|---------|---------|
| **Automatic Startup** | No manual `docker run` needed |
| **Dynamic Discovery** | Endpoints discovered at runtime |
| **Resource Management** | Automatic cleanup and health checks |
| **.NET Integration** | Native to .NET testing patterns |
| **Scalability** | Easy to add more containers (DB, cache, etc.) |
| **CI/CD Friendly** | Works in standard CI pipelines |

## Adding New Integration Tests

Template:

```csharp
[Fact]
public async Task NewFeature_Scenario_ExpectedResult()
{
    Assert.NotNull(_client);

    // Arrange
    var data = new TestModel { /* ... */ };

    // Act
    var result = await _client!.MethodAsync(data);

    // Assert
    Assert.NotNull(result);
    Assert.True(/* condition */);
}
```

## Performance Notes

- **Per-test overhead:** ~2-3 seconds (Aspire startup + connection)
- **Total suite runtime:** ~30-60 seconds
- **Optimization:** Group related tests to share client instance if needed

## References

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview)
- [Aspire Integration Testing Patterns](https://github.com/Aaronontheweb/dotnet-skills/tree/master/skills%2Faspire-integration-testing)
- [SurrealDB Documentation](https://surrealdb.com/docs)
