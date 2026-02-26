# Product Owner Guidelines - SurrealDB.Client

## Prioritization Framework

### Severity Tiers

| Tier | Type | Example | Action |
|------|------|---------|--------|
| P0 | Production blocker / data loss | `ConnectionPool` deadlock, data race in stats | Fix this sprint — block release |
| P1 | Feature incomplete — all CRUD stubs | `GetAsync` returns null always | Fix before Phase 1 ships |
| P2 | User experience degradation | 4 KB WebSocket buffer truncates large responses | Fix before Phase 1 ships |
| P3 | Architecture gap | No `ISurrealDbSession` / `ChangeTracker` | Phase 2 target |
| P4 | Nice to have | Query plan caching, IQueryable composability | Phase 2–3 |

### Value vs. Risk Matrix

Place each backlog item into one of four quadrants:

- **High value, low risk**: Do first — quick wins with real user benefit
- **High value, high risk**: Do second with careful design review
- **Low value, low risk**: Do when there's capacity
- **Low value, high risk**: Question whether to do at all

All three of the current critical bugs (`ConnectionPool` deadlock, data race, WebSocket truncation) are **high value, medium risk** — they must ship in Phase 1.

---

## Writing Good Acceptance Criteria

Acceptance criteria must be:

1. **Testable** — a developer can write a passing/failing test for it
2. **Specific** — not "works correctly" but "returns the record with the given ID"
3. **Bounded** — covers the happy path AND the top 2–3 error paths
4. **User-focused** — phrased from the perspective of the library consumer

### Example: Good vs. Poor AC

Poor:
> "CreateAsync should work properly and return the created record."

Good:
> "Given a connected and authenticated client, when `CreateAsync<User>("users", new User { Name = "Alice" })` is called, then the response from SurrealDB contains the assigned ID and the same Name field. If the table name is null or empty, `ValidationException` is thrown before any network call is made. If the SurrealDB server returns an error status, `QueryException` is thrown with the server's error message in the message."

---

## Versioning Policy

Follow Semantic Versioning (`MAJOR.MINOR.PATCH`):

- **PATCH** (e.g., `1.0.1`): Bug fixes, no API changes, no new features
  - Example: Fix `ConnectionPool` deadlock
- **MINOR** (e.g., `1.1.0`): New features, backward compatible
  - Example: Add `ISurrealDbSession` and `ChangeTracker`
- **MAJOR** (e.g., `2.0.0`): Breaking changes to public API
  - Example: Rename `ISurrealDbClient` methods, change return types

### Pre-1.0 Policy

The library is currently pre-1.0. In this state:
- MINOR versions may include breaking changes
- Breaking changes must be documented in release notes
- Deprecated APIs should get at least one MINOR release before removal

---

## Communicating with the Development Team

### Story Format

Each backlog item should include:

```
Title: [Action verb] [component] [outcome]
Examples: "Implement GetAsync to SELECT from SurrealDB"
          "Fix ConnectionPool deadlock in DisposeAsync"
          "Add ISurrealDbSession with ChangeTracker"

Context:
  Why this is important. What problem it solves for the user.

File(s) to change:
  src/SurrealDB.Client/SurrealDbClient.cs — GetAsync method

Acceptance Criteria:
  - Given...When...Then...
  - Error case: ...

Out of scope:
  - IQueryable support (deferred to Phase 2)
```

### Escalation Triggers for the PO

The PO must be immediately notified if:
- A bug is discovered that causes silent data corruption (e.g., `UpdateAsync` overwrites with wrong data)
- A proposed API change would break existing callers of `ISurrealDbClient`
- The Phase 1 timeline is at risk by more than one week
- A new dependency is being added that has a conflicting license

---

## Phase 1 Release Criteria Summary

The library must NOT be released as a stable package until all of the following are true:

1. Every `ISurrealDbClient` method executes a real operation against SurrealDB (no stubs)
2. `ConnectionPool` bugs are fixed (deadlock, data race, null-adapter window)
3. `WebSocketProtocolAdapter` handles multi-frame responses
4. Unit test coverage ≥ 85% for `src/SurrealDB.Client/`
5. Integration tests pass against SurrealDB `v2.x` latest
6. Public API is documented with XML comments
7. `ARCHITECTURE.md` Phase 1 checklist is 100% complete

---

## Communicating with Stakeholders

### Key Messages for Phase 1

- "The architecture is solid — we have connection pooling, protocol abstraction, and proper exception hierarchy. The CRUD implementation is the remaining work."
- "Critical reliability issues in the connection pool are being fixed before any release."
- "The API is deliberately designed to feel like EF Core, reducing the learning curve for .NET teams."

### Metrics to Report

| Metric | Target | Source |
|--------|--------|--------|
| Unit test coverage | ≥ 85% | `dotnet test --collect:"XPlat Code Coverage"` |
| Build warnings | 0 | `dotnet build -warnaserror` |
| Critical bugs open | 0 at release | `RISK_ASSESSMENT.md` + issue tracker |
| CRUD stub methods remaining | 0 at Phase 1 | `SurrealDbClient.cs` TODO count |
