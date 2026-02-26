# Developer Role - SurrealDB.Client

## Overview

The Developer role is responsible for the day-to-day implementation work on the SurrealDB.Client library. This includes implementing features defined by the Product Owner, fixing bugs identified through code review, and maintaining code quality standards established by the Architect.

SurrealDB.Client is a C# .NET library targeting `net8.0` and `net9.0`. It provides a protocol-agnostic client for SurrealDB, supporting both HTTP and WebSocket transport layers with an integrated connection pool.

---

## Primary Responsibilities

### Feature Implementation
- Implement the unfinished CRUD operations in `SurrealDbClient.cs` — all operations (`CreateAsync`, `GetAsync`, `SelectAsync`, `UpdateAsync`, `DeleteAsync`, `UpsertAsync`) are currently TODO stubs that return dummy values
- Implement `DisconnectAsync` to release the current connection back to the pool
- Implement `LogoutAsync` to invalidate the auth session on the server side
- Implement all `SurrealDbTransaction` methods: `CommitAsync`, `RollbackAsync`, typed `QueryAsync<T>`
- Implement `QueryAsync` and `QueryAsync<T>` with real SurrealQL execution via `IProtocolAdapter.SendAsync`
- Develop the `ISurrealDbSession` session/context pattern described in `ARCHITECTURE.md`
- Build `ChangeTracker` for differential updates (send only changed properties)
- Implement `IQueryable<T>` composable query support

### Bug Fixing (Critical — Do These First)
- **DEADLOCK**: `ConnectionPool.DisposeAsync` acquires `_disposeSemaphore` then calls `ClearAsync()`, which also acquires `_disposeSemaphore` — this will deadlock. Fix: inline the clear logic or skip the semaphore for the internal call.
- **DATA RACE**: `_allConnections` (a `HashSet<PooledConnection>`) is iterated without a lock in `GetStatistics()` and in `DisposeAsync`. Fix: acquire `lock (_allConnections)` in both places.
- **NULL REFERENCE WINDOW**: In `AcquireAsync`, a new `PooledConnection` with `Adapter = null` is added to `_allConnections` before the adapter is created. If `GetStatistics()` or `DisposeAsync` runs during this window, it will throw a `NullReferenceException`. Fix: create the adapter before adding to the set, or use a nullable adapter type with a null guard.
- **TRUNCATED RESPONSES**: `WebSocketProtocolAdapter.SendAsync` uses a fixed 4 KB receive buffer. SurrealDB query responses for large datasets exceed this, causing silent data loss. Fix: loop `ReceiveAsync` and accumulate bytes until `EndOfMessage` is true.
- **SEMAPHORE LEAK ON SUCCESS**: In `AcquireAsync`, the semaphore `Release()` in the `catch` block is correct; however, developers calling `AcquireAsync` must be sure to call `ReleaseAsync` after use. Document this contract clearly and enforce it with try/finally at every call site.

### Code Quality
- Write or extend unit tests for all new code in `tests/SurrealDB.Client.Tests.Unit/`
- Write integration tests in `tests/SurrealDB.Client.Tests.Integration/`
- Maintain XML doc comments on all public APIs
- Ensure all async methods propagate `CancellationToken` and use `ConfigureAwait(false)`

---

## Key Files

| File | Purpose |
|------|---------|
| `src/SurrealDB.Client/SurrealDbClient.cs` | Main client — all CRUD stubs and TODOs |
| `src/SurrealDB.Client/ISurrealDbClient.cs` | Public interface contract, `QueryResult`, `ISurrealDbTransaction` |
| `src/SurrealDB.Client/Connection/ConnectionPool.cs` | Pool lifecycle — contains known bugs (deadlock, data race) |
| `src/SurrealDB.Client/Connection/IConnectionPool.cs` | Pool interface and `PoolStatistics` struct |
| `src/SurrealDB.Client/Protocol/IProtocolAdapter.cs` | Protocol abstraction and `PooledConnection` struct |
| `src/SurrealDB.Client/Protocol/HttpProtocolAdapter.cs` | HTTP transport — `SendAsync`, `HealthCheckAsync` |
| `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` | WebSocket transport — truncation bug at 4 KB |
| `src/SurrealDB.Client/Protocol/ProtocolAdapterFactory.cs` | Factory selecting adapter by `SurrealDbClientOptions.Protocol` |
| `src/SurrealDB.Client/Serialization/SystemTextJsonSerializer.cs` | Default JSON serializer |
| `src/SurrealDB.Client/SurrealDbClientOptions.cs` | All configuration options and `Validate()` |
| `tests/SurrealDB.Client.Tests.Unit/ConnectionPoolTests.cs` | Pool unit tests — expand with concurrency cases |
| `tests/SurrealDB.Client.Tests.Unit/SurrealDbClientTests.cs` | Client unit tests — most currently test stubs |

---

## Day-to-Day Workflow

1. Pull latest from `main` before starting any work
2. Create a feature branch: `git checkout -b feature/implement-crud-operations`
3. Write failing tests before implementing (TDD preferred)
4. Implement the feature
5. Run all tests: `dotnet test`
6. Build for both targets: `dotnet build -f net8.0` and `dotnet build -f net9.0`
7. Open a pull request against `main`

---

## Coding Standards

### Async/Await
- All I/O operations must be async with `CancellationToken` support
- Use `ConfigureAwait(false)` in library code to avoid context capture overhead
- Never use `.Result` or `.Wait()` — these cause deadlocks in ASP.NET contexts

### Null Safety
- Nullable reference types are enabled in `Directory.Build.props`
- Use `?` annotations and null checks throughout
- The `ThrowIfDisposed()` pattern is established — use it in every public method

### Exception Handling
- Never swallow exceptions silently outside of disposal code
- Always wrap non-SurrealDB exceptions in the appropriate typed subtype from `Exceptions/`
- Follow the established `when (!(ex is SurrealDbException))` guard pattern:

```csharp
catch (Exception ex) when (!(ex is SurrealDbException))
{
    throw new QueryException("Descriptive context message", ex);
}
```

### Resource Management
- Implement `IAsyncDisposable` for any type that holds connections or managed resources
- Use `await using` for all `IAsyncDisposable` objects
- The `_disposed` flag + `ThrowIfDisposed()` pattern must be in every disposable class

---

## Current Priority Backlog

1. **CRITICAL**: Fix `ConnectionPool.DisposeAsync` deadlock — acquires `_disposeSemaphore` then calls `ClearAsync` which re-acquires it
2. **CRITICAL**: Fix `GetStatistics()` data race — iterates `_allConnections` HashSet without holding the lock
3. **CRITICAL**: Fix null-adapter window in `AcquireAsync` — `PooledConnection` is added to `_allConnections` before adapter is created
4. **HIGH**: Implement all CRUD operations in `SurrealDbClient.cs`
5. **HIGH**: Fix WebSocket receive buffer — 4 KB is too small, large responses are silently truncated
6. **HIGH**: Implement `DisconnectAsync` to release connection back to pool
7. **MEDIUM**: Implement `ISurrealDbSession` with `ChangeTracker` for differential updates
8. **MEDIUM**: Implement `IQueryable<T>` composable query support

---

## Getting Started

```bash
# Build the solution
cd /home/user/SurrealDB.Client
dotnet build

# Run unit tests
dotnet test tests/SurrealDB.Client.Tests.Unit/

# Run integration tests (requires a running SurrealDB instance)
dotnet test tests/SurrealDB.Client.Tests.Integration/

# Build for a specific target framework
dotnet build src/SurrealDB.Client/ -f net8.0
dotnet build src/SurrealDB.Client/ -f net9.0

# Run a single test by name
dotnet test tests/SurrealDB.Client.Tests.Unit/ --filter "ConnectionPool_Initialize_CreatesConnections"
```
