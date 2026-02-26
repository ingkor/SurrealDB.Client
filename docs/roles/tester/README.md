# Tester Role - SurrealDB.Client

## Overview

The Tester is responsible for ensuring the SurrealDB.Client library behaves correctly, reliably, and safely under all expected and unexpected conditions. This includes designing test strategies, writing and maintaining tests, identifying coverage gaps, and verifying bug fixes.

The current test suite covers basic happy-path scenarios. Critical areas — concurrency, error paths, connection lifecycle, and CRUD correctness — have significant gaps.

---

## Primary Responsibilities

- Design and execute test plans for each feature and bug fix
- Write unit tests in `tests/SurrealDB.Client.Tests.Unit/` using xUnit and Moq
- Write integration tests in `tests/SurrealDB.Client.Tests.Integration/` against a live SurrealDB instance
- Identify and document test coverage gaps in the issue tracker
- Test edge cases and error paths — not just the happy path
- Verify that fixed bugs have regression tests that would have caught the original issue
- Monitor test execution time and flag tests that are unreasonably slow
- Ensure that integration tests clean up their test data after execution

---

## Current Test Coverage Overview

### Tests That Exist

**Unit Tests** (`tests/SurrealDB.Client.Tests.Unit/`):

| Test Class | What It Tests | Known Gaps |
|-----------|--------------|------------|
| `ConnectionPoolTests` | Initialize, Acquire, Release, Statistics, Clear, Dispose | Missing: concurrent acquire stress test, unhealthy connection eviction, pool exhaustion behavior, semaphore leak detection |
| `SurrealDbClientTests` | Client creation, connect, disconnect, auth, CRUD stubs, dispose | All CRUD tests pass trivially — they test stubs that return dummy data, not real behavior |
| `SurrealDbClientOptionsTests` | Options validation | Good coverage |
| `ExceptionTests` | Exception hierarchy | Basic only — no round-trip serialization tests |

**Integration Tests** (`tests/SurrealDB.Client.Tests.Integration/`):
- `ConnectionIntegrationTests` — basic connection lifecycle (requires live SurrealDB)

### Critical Gaps

1. **No concurrency tests for `ConnectionPool`** — the deadlock and data race bugs were not caught because no test ever accessed the pool from multiple threads simultaneously
2. **All CRUD tests test stubs** — they will pass even with wrong implementations because the stubs return input data unchanged
3. **No error path tests for protocol adapters** — what happens when the server returns HTTP 500? When WebSocket closes mid-message?
4. **No WebSocket multi-frame tests** — the 4 KB buffer truncation was not caught by any test
5. **No disposal sequence tests** — what happens if `DisposeAsync` is called while a connection is being acquired?
6. **No authentication failure tests** — wrong credentials, expired tokens, missing auth before CRUD

---

## Test Types and When to Use Each

### Unit Tests
- Test a single class or method in isolation
- Mock all external dependencies (use `Moq` for `IProtocolAdapter`, `IConnectionPool`)
- Should be fast (< 100ms per test), deterministic, and not require any external services
- Files: `tests/SurrealDB.Client.Tests.Unit/`

### Integration Tests
- Test real behavior against a live SurrealDB instance
- Require `SURREALDB_URL`, `SURREALDB_USER`, `SURREALDB_PASS` env vars
- Each test must set up its own data and tear it down in `Dispose` or `IAsyncLifetime`
- Files: `tests/SurrealDB.Client.Tests.Integration/`

### Concurrency / Stress Tests
- Test thread safety of `ConnectionPool` and `SurrealDbClient`
- Use `Task.WhenAll` to simulate concurrent access
- Use `Barrier` or `SemaphoreSlim` to synchronize threads to a precise start point
- Place in unit test project with the `[Trait("Category", "Concurrency")]` attribute

---

## Key Files for Testing

| File | Testing Focus |
|------|--------------|
| `tests/SurrealDB.Client.Tests.Unit/ConnectionPoolTests.cs` | Pool lifecycle — needs major expansion |
| `tests/SurrealDB.Client.Tests.Unit/SurrealDbClientTests.cs` | Client behavior — needs real CRUD tests once stubs are implemented |
| `tests/SurrealDB.Client.Tests.Unit/SurrealDbClientOptionsTests.cs` | Options validation — reasonably complete |
| `tests/SurrealDB.Client.Tests.Unit/ExceptionTests.cs` | Exception types |
| `tests/SurrealDB.Client.Tests.Integration/ConnectionIntegrationTests.cs` | Live connection tests |
| `src/SurrealDB.Client/Connection/ConnectionPool.cs` | Primary target for concurrency tests |
| `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` | Buffer size bug — needs multi-frame test |

---

## Testing Frameworks and Libraries

| Library | Purpose | Used In |
|---------|---------|---------|
| xUnit | Test runner and assertions | All test projects |
| Moq | Mocking `IProtocolAdapter`, `IConnectionPool` | Unit tests |
| xUnit `IAsyncLifetime` | Async setup/teardown for integration tests | Integration tests |
| `CancellationTokenSource` with timeout | Deadlock detection in tests | Concurrency tests |

---

## Test Naming Convention

```
[ClassName]_[MethodName]_[Scenario]

Examples:
ConnectionPool_Acquire_ReturnsHealthyConnection
ConnectionPool_Acquire_ThrowsWhenPoolExhausted
ConnectionPool_Release_ReturnsConnectionToPoolWhenHealthy
ConnectionPool_Dispose_DoesNotDeadlock
SurrealDbClient_GetAsync_ReturnsNullWhenRecordNotFound
SurrealDbClient_CreateAsync_ThrowsValidationExceptionForEmptyTable
WebSocketProtocolAdapter_SendAsync_HandlesMultiFrameResponse
```

---

## Getting Started

```bash
# Run all unit tests
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/

# Run integration tests (requires SurrealDB)
export SURREALDB_URL=surreal://localhost:8000
export SURREALDB_USER=root
export SURREALDB_PASS=root
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Integration/

# Run with verbose output
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ -v normal

# Run a specific test
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --filter "ConnectionPool_Acquire_ReturnsConnection"
```
