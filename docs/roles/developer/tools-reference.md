# Developer Tools Reference - SurrealDB.Client

## Build Commands

```bash
# Build entire solution
dotnet build /home/user/SurrealDB.Client/SurrealDB.Client.sln

# Build only the library
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDB.Client.csproj

# Build for a specific framework
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -f net8.0
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -f net9.0

# Build in Release mode
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -c Release
```

## Test Commands

```bash
# Run all tests
dotnet test /home/user/SurrealDB.Client/SurrealDB.Client.sln

# Run unit tests only
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/

# Run integration tests only
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Integration/

# Run a single test method by name
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --filter "ConnectionPool_Acquire_ReturnsConnection"

# Run tests matching a class name
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --filter "ClassName=ConnectionPoolTests"

# Run tests with verbose output
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ -v normal

# Run tests and collect code coverage (requires coverlet)
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --collect:"XPlat Code Coverage"

# List all available tests without running them
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ --list-tests
```

## Code Analysis Commands

```bash
# Check for warnings in the library
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -warnaserror

# Run Roslyn analyzers (included via SDK)
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ /p:RunAnalyzers=true

# Format code (requires dotnet-format tool)
dotnet format /home/user/SurrealDB.Client/SurrealDB.Client.sln

# Check format without applying (dry run)
dotnet format /home/user/SurrealDB.Client/SurrealDB.Client.sln --verify-no-changes
```

## NuGet / Package Commands

```bash
# Restore packages
dotnet restore /home/user/SurrealDB.Client/SurrealDB.Client.sln

# List all outdated packages in the solution
dotnet list /home/user/SurrealDB.Client/SurrealDB.Client.sln package --outdated

# Add a package to the library
dotnet add /home/user/SurrealDB.Client/src/SurrealDB.Client/ package SomePackage

# Add a test package to unit tests
dotnet add /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ package Moq
```

## Git Workflow Commands

```bash
# Start a feature branch
git -C /home/user/SurrealDB.Client checkout -b feature/implement-crud-operations

# Stage specific files
git -C /home/user/SurrealDB.Client add src/SurrealDB.Client/SurrealDbClient.cs

# Commit with a descriptive message
git -C /home/user/SurrealDB.Client commit -m "Implement GetAsync with SELECT query via protocol adapter"

# Push feature branch
git -C /home/user/SurrealDB.Client push origin feature/implement-crud-operations

# Sync with main before opening a PR
git -C /home/user/SurrealDB.Client fetch origin
git -C /home/user/SurrealDB.Client rebase origin/main
```

## Key Source Locations

| What | Path |
|------|------|
| Main client implementation | `src/SurrealDB.Client/SurrealDbClient.cs` |
| Public interface | `src/SurrealDB.Client/ISurrealDbClient.cs` |
| Connection pool | `src/SurrealDB.Client/Connection/ConnectionPool.cs` |
| HTTP adapter | `src/SurrealDB.Client/Protocol/HttpProtocolAdapter.cs` |
| WebSocket adapter | `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` |
| Exception types | `src/SurrealDB.Client/Exceptions/` |
| Unit tests | `tests/SurrealDB.Client.Tests.Unit/` |
| Integration tests | `tests/SurrealDB.Client.Tests.Integration/` |

## Debugging Tips

### Diagnosing ConnectionPool Deadlocks

If a test hangs indefinitely, it is likely a semaphore deadlock. Use a timeout in the test:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await Assert.ThrowsAnyAsync<OperationCanceledException>(
    () => pool.AcquireAsync(cts.Token));
```

### Tracing WebSocket Messages

Add temporary structured logging in `WebSocketProtocolAdapter.SendAsync` during local debugging:

```csharp
// Temporary debug only — remove before committing
Console.Error.WriteLine($"[WS Send] {message}");
Console.Error.WriteLine($"[WS Recv] {response}");
```

### Checking Pool State

Call `pool.GetStatistics()` and inspect the result during debugging:

```csharp
var stats = pool.GetStatistics();
Console.Error.WriteLine(
    $"Pool: total={stats.TotalConnections} available={stats.AvailableConnections} " +
    $"inUse={stats.InUseConnections} acquisitions={stats.TotalAcquisitions}");
```

## Environment Variables for Integration Tests

| Variable | Purpose | Example |
|----------|---------|---------|
| `SURREALDB_URL` | SurrealDB connection URL | `surreal://localhost:8000` |
| `SURREALDB_USER` | Root username | `root` |
| `SURREALDB_PASS` | Root password | `root` |
| `SURREALDB_NS` | Namespace for test data | `testns` |
| `SURREALDB_DB` | Database for test data | `testdb` |

## Useful .NET SDK References

- [SemaphoreSlim docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- [CancellationToken docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken)
- [ClientWebSocket docs](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket)
- [System.Text.Json docs](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [IAsyncDisposable pattern](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync)
