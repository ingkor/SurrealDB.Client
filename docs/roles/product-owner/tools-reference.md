# Product Owner Tools Reference - SurrealDB.Client

## Tracking Feature Completeness

### Count TODO stubs in the codebase
```bash
grep -rn "// TODO" /home/user/SurrealDB.Client/src/ --include="*.cs"
```
Expected: 0 TODO stubs before Phase 1 release.

### Count unimplemented methods (returning dummy values)
```bash
grep -n "await Task.CompletedTask" /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs
```

### List all public methods on ISurrealDbClient
```bash
grep -n "Task" /home/user/SurrealDB.Client/src/SurrealDB.Client/ISurrealDbClient.cs
```

---

## Checking Test Coverage

### Run tests with coverage collection
```bash
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --collect:"XPlat Code Coverage" \
  --results-directory /tmp/coverage
```

### View test results summary
```bash
dotnet test /home/user/SurrealDB.Client/SurrealDB.Client.sln --logger "console;verbosity=normal"
```

### Count total tests
```bash
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ --list-tests | wc -l
```

---

## Checking Build Health

### Build with warnings as errors (zero-warning policy check)
```bash
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -warnaserror 2>&1 | tail -20
```

### Build for both target frameworks
```bash
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -f net8.0 && \
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -f net9.0
```

---

## Reviewing Architecture Documents

| Document | Location | Purpose |
|----------|----------|---------|
| Architecture overview | `/home/user/SurrealDB.Client/ARCHITECTURE.md` | Roadmap, design decisions, EF Core comparison |
| Risk register | `/home/user/SurrealDB.Client/RISK_ASSESSMENT.md` | All known risks with severity and mitigation |
| Grade baseline | `/home/user/SurrealDB.Client/B_GRADE_BASELINE.md` | Honest assessment of current state |
| Design decisions | `/home/user/SurrealDB.Client/DESIGN_DECISIONS.md` | Rationale for key choices |
| Security considerations | `/home/user/SurrealDB.Client/SECURITY.md` | Auth and security practices |

---

## Checking for Critical Bugs

### Find all places where semaphore release may be missing
```bash
grep -n "Release\|WaitAsync" /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
```

### Find all places where _allConnections is accessed (check for locks)
```bash
grep -n "_allConnections" /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
```

### Check WebSocket receive buffer size
```bash
grep -n "byte\[" /home/user/SurrealDB.Client/src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs
```

---

## Release Checklist Commands

### Check for any Console.WriteLine debug code left in source
```bash
grep -rn "Console\." /home/user/SurrealDB.Client/src/ --include="*.cs"
```

### Check XML documentation coverage on public types
```bash
grep -rn "/// <summary>" /home/user/SurrealDB.Client/src/ --include="*.cs" | wc -l
grep -rn "public " /home/user/SurrealDB.Client/src/ --include="*.cs" | wc -l
```

### Check that no test project references are in the main library
```bash
grep -rn "Moq\|xunit\|Xunit" /home/user/SurrealDB.Client/src/ --include="*.csproj"
```

---

## Git History Commands

### View recent commits
```bash
git -C /home/user/SurrealDB.Client log --oneline -20
```

### See what changed between two commits
```bash
git -C /home/user/SurrealDB.Client diff HEAD~5..HEAD -- src/
```

### See which files changed most recently
```bash
git -C /home/user/SurrealDB.Client log --name-only --oneline -10
```

---

## Performance Target Validation

From `ARCHITECTURE.md`, Phase 1 performance targets:

| Operation | Target | How to Measure |
|-----------|--------|----------------|
| Connection pool setup | < 1 second | Time `pool.InitializeAsync()` in integration test |
| Authentication | < 500 ms | Time `AuthenticateAsync()` call |
| Simple SELECT query | < 50 ms | Time `SelectAsync<T>()` with 1 row |
| Typical update | < 100 ms | Time `UpdateAsync()` end-to-end |
| Batch (100 records) | < 200 ms | Time bulk `CreateAsync()` |

These are validated by the 10x Developer role but the PO owns the decision on whether they are acceptance criteria for a release.
