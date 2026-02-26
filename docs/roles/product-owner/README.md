# Product Owner Role - SurrealDB.Client

## Overview

The Product Owner (PO) is responsible for defining what gets built, in what order, and why. For SurrealDB.Client, the PO owns the feature roadmap, translates user and stakeholder needs into actionable requirements, and ensures the development team is always working on the highest-value items.

SurrealDB.Client is a .NET library consumed by application developers. The primary customers are .NET teams that want to use SurrealDB as their backend database. The library competes with SurrealDB's official C# SDK and adjacent ORMs like Entity Framework Core.

---

## Primary Responsibilities

- Maintain and prioritize the product backlog
- Define acceptance criteria for each feature before development begins
- Review completed work against acceptance criteria before marking items done
- Communicate roadmap and priorities to the development team
- Represent the voice of the library consumer (application developers)
- Align the library's API design with what EF Core developers already know
- Decide which known bugs block a release vs. which are acceptable post-release
- Own the public NuGet package versioning and release notes

---

## Current State of the Product

### What Works
- Project structure builds for `net8.0` and `net9.0`
- Connection pooling infrastructure (`ConnectionPool`) exists
- HTTP and WebSocket protocol adapters exist (with known bugs)
- Authentication via credentials and token is scaffolded
- Exception hierarchy is defined
- Serialization via `System.Text.Json` exists

### What Is Missing (All CRUD Operations Are Stubs)
- `CreateAsync` returns the input data unchanged without touching the database
- `GetAsync` always returns `default` (null) regardless of arguments
- `SelectAsync` always returns an empty collection
- `UpdateAsync` returns the input data unchanged
- `DeleteAsync` does nothing
- `UpsertAsync` returns the input data unchanged
- `QueryAsync` returns a dummy OK result without executing any query
- `DisconnectAsync` sets a flag but does not close connections
- `LogoutAsync` does nothing
- All transaction methods (`CommitAsync`, `RollbackAsync`) are stubs

### Known Critical Bugs (Blocking Production Use)
- `ConnectionPool.DisposeAsync` contains a deadlock
- `ConnectionPool.GetStatistics()` has a data race on `_allConnections`
- `WebSocketProtocolAdapter` silently truncates responses larger than 4 KB

---

## Roadmap Phases (from ARCHITECTURE.md)

### Phase 1 — Foundation (Weeks 1–4)
Deliver a working library that can perform real database operations.

**Must-have acceptance criteria:**
- All CRUD operations execute real SurrealQL against a live SurrealDB instance
- ConnectionPool deadlock and data race bugs are fixed
- WebSocket buffer truncation is fixed
- Unit test coverage ≥ 85%
- Integration tests pass against SurrealDB latest
- No public API breaking changes from current interface

### Phase 2 — Features (Weeks 5–8)
Add the session/context pattern and composable queries.

**Must-have acceptance criteria:**
- `ISurrealDbSession` with `ChangeTracker` (differential updates)
- `IQueryable<T>` composable query API
- Optimistic concurrency tokens (`[ConcurrencyToken]` attribute)
- Real-time subscriptions via WebSocket `LIVE SELECT`
- Query logging and metrics

### Phase 3 — Polish (Weeks 9–12)
Production hardening and ecosystem integrations.

**Must-have acceptance criteria:**
- Response streaming for large result sets
- Query result caching layer
- Advanced error recovery (circuit breaker, retry policies)
- NuGet package published
- Migration tooling

---

## Key Stakeholder Concerns

| Stakeholder | Concern | PO Response |
|-------------|---------|-------------|
| .NET App Developers | "Does it work like EF Core?" | Yes — design aligns with DbContext/Session pattern |
| DevOps / Ops Teams | "Is the connection pool reliable?" | Phase 1 priority — all pool bugs fixed before release |
| Security Teams | "Are credentials stored safely?" | Auth tokens are in-memory only, not persisted |
| Performance Engineers | "Will it scale?" | Change tracking reduces bandwidth 90%+; pool is configurable |

---

## Acceptance Criteria Template

For each user story, the PO must define:

```
Given [initial context]
When [action is taken]
Then [observable result]
And [error case: if X fails, Y exception is thrown]
```

Example for `GetAsync`:
```
Given the client is connected and authenticated
When GetAsync<User>("users:1") is called
Then the User record with id "users:1" is returned
And if the record does not exist, null is returned
And if the connection is closed, ConnectionException is thrown
And if the record id is empty, ValidationException is thrown
```

---

## Definition of Done

A backlog item is "Done" when:
1. Feature code is merged to `main`
2. All acceptance criteria are met and verified
3. Unit test coverage for the new code is ≥ 85%
4. No new warnings in `dotnet build`
5. Public API has XML documentation
6. `ARCHITECTURE.md` roadmap status is updated if applicable
7. Release notes entry is drafted
