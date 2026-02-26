# Architect Guidelines - SurrealDB.Client

## Layered Architecture Rules

Each layer in the stack has a strict responsibility. Do not violate layer boundaries.

| Layer | Allowed Dependencies | Forbidden Dependencies |
|-------|---------------------|----------------------|
| Public API (`SurrealDbClient`) | Connection Layer, Serialization Layer | Direct `HttpClient`, direct `ClientWebSocket` |
| Connection Layer (`ConnectionPool`) | Protocol Layer | Serialization Layer, Public API |
| Protocol Layer (`HttpProtocolAdapter`, `WebSocketProtocolAdapter`) | Standard `System.Net.*` | Connection Layer, Serialization Layer |
| Serialization Layer (`SystemTextJsonSerializer`) | `System.Text.Json` | All other layers |

If a dependency would violate this table, the architecture must be discussed before the implementation proceeds.

---

## Thread Safety Contract

Every type must explicitly declare its thread-safety contract in its XML doc comment:

```csharp
/// <summary>
/// Thread-safe: yes — all mutable state protected by lock or atomic operations.
/// </summary>
internal class ConnectionPool : IConnectionPool { ... }

/// <summary>
/// Thread-safe: no — designed for single-threaded or locked access.
/// </summary>
internal class PooledConnection { ... }
```

### Current Thread Safety Inventory

| Type | Thread Safe? | Mechanism |
|------|-------------|-----------|
| `ConnectionPool` | Partially | `_allConnections` via `lock`, `_availableConnections` via `ConcurrentBag`, counters via `Interlocked` |
| `HttpProtocolAdapter` | No | Single `HttpClient` shared; not designed for parallel calls |
| `WebSocketProtocolAdapter` | No | Single `ClientWebSocket` cannot interleave sends/receives |
| `SurrealDbClient` | No | `_currentConnection` and `_authSession` are not protected |
| `SurrealDbClientOptions` | Yes (after construction) | Immutable after validation |

The implication: `SurrealDbClient` is NOT designed for concurrent use from multiple threads. The connection pool enables multiple independent `SurrealDbClient` instances, not concurrent use of a single instance.

---

## Semaphore Design Pattern

The `ConnectionPool` uses semaphores as gates, not mutexes. Understanding the distinction:

```
Mutex semantics: only one thread at a time
Gate semantics: up to N threads at a time, gate blocks when all N slots taken

_acquireSemaphore(PoolSize) = gate
_disposeSemaphore(1, 1)     = mutex
```

**Key rule**: Never acquire a semaphore from inside a call stack that already holds it. This causes deadlock. The current bug in `DisposeAsync` violates this rule exactly:

```csharp
// BUG: DisposeAsync holds _disposeSemaphore, then calls ClearAsync
// which tries to acquire _disposeSemaphore again = deadlock

public async ValueTask DisposeAsync()
{
    await _disposeSemaphore.WaitAsync();  // Acquired here
    try
    {
        await ClearAsync();  // BUG: ClearAsync also calls _disposeSemaphore.WaitAsync()
    }
    finally { ... }
}
```

**Fix pattern**: Extract the work into a private method that does not acquire the semaphore, and call it from both `DisposeAsync` and `ClearAsync` (the latter still guards with the semaphore for external callers):

```csharp
private async Task ClearInternalAsync()
{
    // actual clear logic, no semaphore
}

public async Task ClearAsync()
{
    await _disposeSemaphore.WaitAsync();
    try { await ClearInternalAsync(); }
    finally { _disposeSemaphore.Release(); }
}

public async ValueTask DisposeAsync()
{
    await _disposeSemaphore.WaitAsync();
    try { await ClearInternalAsync(); }  // No re-entry
    finally { ... }
}
```

---

## ISurrealDbSession Design (Phase 2)

When implementing the session pattern, follow this design:

```csharp
public interface ISurrealDbSession : IAsyncDisposable
{
    ChangeTracker ChangeTracker { get; }
    IQueryable<T> Set<T>(string table) where T : class;
    Task<T?> FindAsync<T>(string id, CancellationToken ct = default) where T : class;
    void Add<T>(T entity) where T : class;
    void Update<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<ISurrealDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
```

Design rules for `ISurrealDbSession`:
- Session instances are short-lived (one per request/unit of work) — do NOT make them singletons
- Session creates a scope around `ChangeTracker` — all tracked entities belong to this session
- `SaveChangesAsync` generates minimal SurrealQL (only changed properties)
- `Set<T>()` returns an unexecuted `IQueryable<T>` — execution happens at `ToListAsync()`, `FirstAsync()`, etc.
- Session disposes cleanly even if `SaveChangesAsync` was not called (pending changes are discarded)

---

## IQueryable<T> Implementation (Phase 2)

Implement a custom LINQ provider that translates expression trees to SurrealQL:

1. `SurrealDbQueryProvider : IQueryProvider` — translates `Expression` to SurrealQL string
2. `SurrealDbQueryable<T> : IQueryable<T>` — wraps provider and expression
3. Expression visitor that handles: `Where`, `Select`, `OrderBy`, `Take`, `Skip`, `First`, `Any`, `Count`
4. Compiled query plan cache (by expression tree hash) to avoid re-translation on repeated queries

Unsupported operations must throw a clear `NotSupportedException` with a message explaining which SurrealQL feature would be needed.

---

## Adding New Protocol Adapters

If a new transport is added (e.g., gRPC when SurrealDB adds support), the steps are:

1. Create `GrpcProtocolAdapter : IProtocolAdapter` in `src/SurrealDB.Client/Protocol/`
2. Add `ProtocolType.Grpc` to the `ProtocolType` enum in `SurrealDbClientOptions.cs`
3. Add a branch to `ProtocolAdapterFactory.CreateAdapterAsync`
4. Add unit tests for the new adapter
5. Update `ARCHITECTURE.md` to document the new protocol

**Do not** add any gRPC-specific logic to `SurrealDbClient` or `ConnectionPool` — all transport details stay in the adapter.

---

## Dependency Management Policy

- Core library (`src/SurrealDB.Client/`) must have zero external NuGet dependencies when possible. Use `System.*` APIs first.
- If an external dependency is necessary (e.g., `Newtonsoft.Json` for alternative serialization), it must be optional — never force it on consumers.
- Test projects may use any dependencies needed (`xunit`, `Moq`, etc.).
- `Directory.Build.props` at root controls shared build configuration. Changes there affect all projects.

---

## Documentation Standards for Architecture

When adding a significant new component:

1. Update `ARCHITECTURE.md` with a diagram showing where the new component sits in the layer stack
2. Add a section to `DESIGN_DECISIONS.md` explaining why the design was chosen and what alternatives were rejected
3. Add risk entries to `RISK_ASSESSMENT.md` for any new risks the component introduces
4. Update the `README.md` if the public API surface changes

New interfaces and classes must have XML comments explaining:
- **Purpose** of the type
- **Thread safety** contract
- **Disposal** requirements (if applicable)
- **Performance** characteristics if non-obvious
