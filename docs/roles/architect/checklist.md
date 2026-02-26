# Architect Checklist - SurrealDB.Client

## Pull Request Architecture Review

For any PR touching core library code (`src/SurrealDB.Client/`):

### Protocol Layer
- [ ] Does new code go through `IProtocolAdapter`? No direct `HttpClient` or `ClientWebSocket` usage in `SurrealDbClient`
- [ ] Does the PR change `IProtocolAdapter`? If yes, both `HttpProtocolAdapter` and `WebSocketProtocolAdapter` must be updated
- [ ] Is `ProtocolAdapterFactory` the only place adapters are constructed? No `new HttpProtocolAdapter()` outside the factory

### Connection Pool Layer
- [ ] Does `AcquireAsync` always have a corresponding `ReleaseAsync` in a `finally` block?
- [ ] Are all accesses to `_allConnections` (HashSet) inside `lock (_allConnections)`?
- [ ] Does the PR avoid acquiring `_disposeSemaphore` from inside a method that may already hold it?
- [ ] Is `_acquireSemaphore.Release()` only in `catch` blocks (not on the success path)?

### Public API
- [ ] Does the PR change any `public` signature in `ISurrealDbClient.cs`? If yes, this is a breaking change — flag it
- [ ] Do new public methods have XML documentation (`<summary>`, `<param>`, `<returns>`, `<exception>`)?
- [ ] Do new public methods call `ThrowIfDisposed()` as the first statement?
- [ ] Do new public methods accept and propagate `CancellationToken`?

### Exception Handling
- [ ] Are all new exceptions typed (not raw `Exception`)? Use types from `Exceptions/`
- [ ] Is the `when (!(ex is SurrealDbException))` guard present in every `catch` that wraps exceptions?
- [ ] Are disposal catch blocks documented with a comment explaining why swallowing is intentional?

### Async Correctness
- [ ] Every `await` in library code has `.ConfigureAwait(false)`?
- [ ] No `.Result` or `.Wait()` in library code?
- [ ] No `async void` methods (only `async Task` or `async ValueTask`)?

### Resource Management
- [ ] New types holding unmanaged resources implement `IAsyncDisposable`?
- [ ] `DisposeAsync` uses `_disposed` flag to prevent double-disposal?
- [ ] `await using` used at call sites for all `IAsyncDisposable` objects?

---

## Monthly Architecture Health Check

- [ ] Review `RISK_ASSESSMENT.md` — any new risks discovered? Update severity scores
- [ ] Review `ARCHITECTURE.md` roadmap — are Phase 1 items still on track?
- [ ] Check for any violations of the protocol abstraction (grep for `HttpClient` in `SurrealDbClient.cs`)
- [ ] Check for any synchronous waits: `grep -rn "\.Result\|\.Wait()" src/`
- [ ] Check for direct `Console.WriteLine` calls: `grep -rn "Console\." src/`
- [ ] Verify `net8.0` and `net9.0` builds are both clean

---

## New Component Introduction Checklist

When a new significant component is proposed (e.g., `ISurrealDbSession`, `ChangeTracker`):

- [ ] Is there an existing pattern in this codebase or EF Core that should be followed?
- [ ] Does the component's interface live in the correct layer (not crossing layer boundaries)?
- [ ] Is the component testable in isolation with a mock adapter?
- [ ] Is the public surface minimal (interfaces expose only what callers need)?
- [ ] Is disposal handled correctly if the component holds resources?
- [ ] Is thread safety documented explicitly — is this component safe for concurrent use?
- [ ] Does the component have its own focused unit test file?
- [ ] Is the ARCHITECTURE.md updated to reflect the new component?

---

## Phase Transition Gate

Before closing Phase 1 and opening Phase 2:

- [ ] All CRUD operations implemented and tested against real SurrealDB
- [ ] `ConnectionPool` deadlock fixed and verified with concurrent test
- [ ] `ConnectionPool` data race fixed and verified with stress test
- [ ] WebSocket multi-frame receive loop implemented and tested
- [ ] Unit test coverage ≥ 85%
- [ ] `ARCHITECTURE.md` Phase 1 checklist marked complete
- [ ] No P0 or P1 bugs open
- [ ] Performance targets validated (see `ARCHITECTURE.md`)
