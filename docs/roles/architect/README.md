# Architect Role - SurrealDB.Client

## Overview

The Architect is responsible for the technical integrity of the SurrealDB.Client library. This means setting and enforcing design patterns, making decisions on technology choices, reviewing code for architectural correctness, and ensuring the codebase can scale in capability and maintainability over time.

The current architecture is rated **B+ (Strong Foundation with Critical Gaps)** — see `ARCHITECTURE.md` for the full analysis.

---

## Primary Responsibilities

- Own the overall layered architecture: Session, Query, Protocol, Serialization, Resilience, Diagnostics
- Define and document design patterns that all developers must follow
- Review pull requests for architectural correctness, not just functional correctness
- Drive the addition of missing architectural components: `ISurrealDbSession`, `ChangeTracker`, `IQueryable<T>`, optimistic concurrency
- Ensure the protocol abstraction (`IProtocolAdapter`) remains clean and never leaks implementation details
- Resolve technical ambiguities and competing design options
- Establish performance targets and validate they are met at each phase
- Ensure multi-target (`net8.0`, `net9.0`) compatibility is maintained

---

## Key Architectural Decisions Already Made

### Protocol Abstraction
`IProtocolAdapter` is the boundary between connection management and transport. HTTP and WebSocket adapters implement this interface. No consumer of `IProtocolAdapter` should know which transport is in use. This abstraction must not be broken.

### Connection Pool Design
`ConnectionPool` uses two semaphores:
- `_acquireSemaphore` — permits up to `PoolSize` concurrent holders. Released by `ReleaseAsync`, NOT by `AcquireAsync`. This design means every acquire must be paired with a release.
- `_disposeSemaphore` — mutex for the pool lifecycle. Must never be re-entered (i.e., never call a method that acquires it while already holding it).

`PooledConnection` records are tracked in `_allConnections` (HashSet, protected by `lock`) and live connections are in `_availableConnections` (ConcurrentBag, lock-free).

### Exception Hierarchy
All exceptions must derive from `SurrealDbException`. The hierarchy is flat (one level below base). Do not introduce deep exception trees. Distinguish by error category: connection, auth, query, validation, serialization.

### Disposal Pattern
All resource-holding types implement `IAsyncDisposable`. Disposal errors are suppressed via bare `catch {}`. The `_disposed` flag prevents double-disposal. `ThrowIfDisposed()` guards every public method.

---

## Critical Architectural Gaps (Must Address in Phase 1–2)

### 1. No Unit of Work / Session Pattern

The current `SurrealDbClient` performs operations immediately — there is no concept of a scope or transaction boundary. Every CRUD call is independent. This leads to:
- 10–50x bandwidth waste (full object sent on every update)
- No atomicity across multiple operations
- No change tracking

**Required addition**: `ISurrealDbSession` analogous to EF Core's `DbContext`.

### 2. No Change Tracking

Without a `ChangeTracker`, every `UpdateAsync` call must serialize the entire object. A 1-byte change sends 20 KB. At scale, this is a network and database bottleneck.

**Required addition**: `ChangeTracker` with snapshot-based comparison.

### 3. Non-Composable Queries

The current API returns data immediately. There is no `IQueryable<T>` — queries cannot be composed, reused, or tested in isolation.

**Required addition**: `IQueryable<T>` backed by a SurrealQL expression translator.

### 4. No Optimistic Concurrency

Silent data loss is possible in concurrent update scenarios. Two users loading and saving the same record will overwrite each other without error.

**Required addition**: `[ConcurrencyToken]` attribute and `ConcurrencyException` type.

---

## Design Patterns Enforced in This Codebase

| Pattern | Where Used | Rule |
|---------|-----------|------|
| Interface segregation | `IProtocolAdapter`, `IConnectionPool`, `ISerializer` | Code against interfaces, not implementations |
| Factory method | `ProtocolAdapterFactory` | Adapter creation is centralized — never `new HttpProtocolAdapter()` in client code |
| Semaphore-as-gate | `ConnectionPool._acquireSemaphore` | Acquire = take a slot, Release = return it. Always in try/finally |
| Disposal with flag | All `IAsyncDisposable` types | `_disposed` flag + `ThrowIfDisposed()` in all public methods |
| Exception wrapping | All public methods | Non-SurrealDb exceptions wrapped with `when (!(ex is SurrealDbException))` |
| Immutable options | `SurrealDbClientOptions` | Options are validated once in constructor and treated as immutable thereafter |

---

## Architecture Layers

```
┌──────────────────────────────────────────────────┐
│         Public API (ISurrealDbClient)             │
│   SurrealDbClient, ISurrealDbSession (planned)    │
├──────────────────────────────────────────────────┤
│         Query Layer (planned)                     │
│   IQueryable<T>, SurrealQL expression translator  │
├──────────────────────────────────────────────────┤
│         Connection Layer                          │
│   ConnectionPool, IConnectionPool                 │
├──────────────────────────────────────────────────┤
│         Protocol Layer                            │
│   IProtocolAdapter, HttpProtocolAdapter,          │
│   WebSocketProtocolAdapter, ProtocolAdapterFactory│
├──────────────────────────────────────────────────┤
│         Serialization Layer                       │
│   ISerializer, SystemTextJsonSerializer           │
├──────────────────────────────────────────────────┤
│         Resilience / Diagnostics (planned)        │
│   CircuitBreaker, RetryPolicy, Interceptors       │
└──────────────────────────────────────────────────┘
```

---

## Key Files for Architecture Work

| File | Architectural Significance |
|------|---------------------------|
| `src/SurrealDB.Client/ISurrealDbClient.cs` | Public API contract — any change here is a potential breaking change |
| `src/SurrealDB.Client/Connection/ConnectionPool.cs` | Most complex class — semaphore logic, thread safety, lifecycle |
| `src/SurrealDB.Client/Protocol/IProtocolAdapter.cs` | Layer boundary interface |
| `src/SurrealDB.Client/Protocol/ProtocolAdapterFactory.cs` | Centralized adapter creation |
| `ARCHITECTURE.md` | Design rationale, EF Core comparison, roadmap |
| `RISK_ASSESSMENT.md` | Risks with severity ratings |
| `DESIGN_DECISIONS.md` | Archived decision records |

---

## Performance Targets (Phase 1)

| Operation | Target |
|-----------|--------|
| Pool initialization | < 1 second |
| Authentication | < 500 ms |
| Simple SELECT | < 50 ms |
| Typical UPDATE | < 100 ms |
| Batch 100 records | < 200 ms |
| Property-level update vs. full update | 5x less bandwidth |
