# Role-Based Skills Documentation - SurrealDB.Client

This directory contains role-specific documentation for everyone working on the SurrealDB.Client library. Each role has its own subfolder with four files:

- **README.md** — Role overview, responsibilities, and key focus areas
- **checklist.md** — Quick task checklists for common scenarios
- **guidelines.md** — Best practices and patterns specific to this role
- **tools-reference.md** — Useful commands, scripts, and tools

---

## Roles

| Role | Directory | Primary Concern |
|------|-----------|-----------------|
| [Developer](developer/) | `developer/` | Daily coding, feature implementation, bug fixes |
| [Product Owner](product-owner/) | `product-owner/` | Feature prioritization, requirements, stakeholder management |
| [Architect](architect/) | `architect/` | System design, patterns, technical decisions, scalability |
| [Tester](tester/) | `tester/` | Quality assurance, test planning, coverage, edge cases |
| [DB Owner](db-owner/) | `db-owner/` | SurrealQL queries, schema, data integrity, performance |
| [Critical 10x Developer](10x-developer/) | `10x-developer/` | Performance optimization, critical bug fixes, impact prioritization |

---

## Project Context

**SurrealDB.Client** is a C# .NET library targeting `net8.0` and `net9.0`. It provides a protocol-agnostic client for SurrealDB supporting HTTP and WebSocket transports with integrated connection pooling.

### Current State (February 2026)

**Architecture grade: B+ (Strong Foundation with Critical Gaps)**

Working:
- Project structure and build system
- Connection pool infrastructure (`ConnectionPool`)
- HTTP and WebSocket protocol adapters (with known bugs — see below)
- Authentication scaffolding (basic auth and token auth)
- Exception type hierarchy
- System.Text.Json serialization

Critical bugs that must be fixed before production use:
1. `ConnectionPool.DisposeAsync` contains a deadlock
2. `ConnectionPool.GetStatistics()` has a data race on `_allConnections`
3. `WebSocketProtocolAdapter.SendAsync` silently truncates responses > 4 KB

Not yet implemented (all CRUD methods are stubs):
- `CreateAsync`, `GetAsync`, `SelectAsync`, `UpdateAsync`, `DeleteAsync`, `UpsertAsync`
- `QueryAsync` (raw SurrealQL execution)
- `DisconnectAsync`, `LogoutAsync`
- All `SurrealDbTransaction` methods
- `ISurrealDbSession` / `ChangeTracker` (Phase 2)
- `IQueryable<T>` composable queries (Phase 2)

### Key Source Files

```
src/SurrealDB.Client/
├── ISurrealDbClient.cs          # Public interface contract
├── SurrealDbClient.cs           # Main implementation (has TODO stubs)
├── SurrealDbClientOptions.cs    # Configuration
├── Connection/
│   ├── IConnectionPool.cs
│   └── ConnectionPool.cs        # Has critical bugs
├── Protocol/
│   ├── IProtocolAdapter.cs
│   ├── HttpProtocolAdapter.cs
│   ├── WebSocketProtocolAdapter.cs  # Has 4KB truncation bug
│   └── ProtocolAdapterFactory.cs
├── Serialization/
│   ├── ISerializer.cs
│   └── SystemTextJsonSerializer.cs
├── Authentication/
│   └── IAuthenticationProvider.cs
└── Exceptions/
    ├── SurrealDbException.cs    # Base
    ├── ConnectionException.cs
    ├── QueryException.cs
    ├── AuthenticationException.cs
    ├── ValidationException.cs
    ├── SerializationException.cs
    └── TimeoutException.cs

tests/
├── SurrealDB.Client.Tests.Unit/
│   ├── ConnectionPoolTests.cs
│   ├── SurrealDbClientTests.cs
│   ├── SurrealDbClientOptionsTests.cs
│   └── ExceptionTests.cs
└── SurrealDB.Client.Tests.Integration/
    └── ConnectionIntegrationTests.cs
```

### Architecture Documents

| Document | Contents |
|----------|----------|
| `ARCHITECTURE.md` | Layered architecture, EF Core comparison, implementation roadmap |
| `RISK_ASSESSMENT.md` | All risks with severity ratings and mitigation strategies |
| `DESIGN_DECISIONS.md` | Rationale for key technical decisions |
| `SECURITY.md` | Authentication and security considerations |
| `B_GRADE_BASELINE.md` | Honest current-state assessment |
