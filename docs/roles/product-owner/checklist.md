# Product Owner Checklist - SurrealDB.Client

## Sprint Planning

- [ ] Backlog is prioritized by value and risk, not by ease of implementation
- [ ] Each story in the sprint has written acceptance criteria before dev starts
- [ ] Critical bugs from `RISK_ASSESSMENT.md` are in the current sprint if they block release
- [ ] Developers have enough context to start work without scheduling a meeting
- [ ] No story is larger than 2 days of work for a single developer — break them down if so

---

## Before Accepting a Feature as Complete

- [ ] Read the acceptance criteria written at story creation — do they still reflect what was needed?
- [ ] Ask: "Can I call this method on a real SurrealDB instance and get real data back?"
- [ ] For CRUD operations: test `CreateAsync`, `GetAsync`, and `DeleteAsync` end-to-end
- [ ] Confirm the feature does not silently succeed — errors must throw the correct exception type
- [ ] Check that the public API has XML documentation (`<summary>`, `<exception>`)
- [ ] Verify no regression in previously passing tests: `dotnet test`
- [ ] For connection/pool changes: confirm the pool deadlock is not reintroduced

---

## Before a Release

- [ ] All Phase acceptance criteria are met (see README.md Roadmap)
- [ ] Known critical bugs listed in `RISK_ASSESSMENT.md` are resolved or documented as deferred
- [ ] NuGet package version follows SemVer: breaking changes = major, new features = minor, bug fixes = patch
- [ ] Release notes draft is ready — list new features, bug fixes, and breaking changes
- [ ] `ARCHITECTURE.md` roadmap phase status is updated
- [ ] `README.md` at root is updated with new usage examples if the API changed
- [ ] At least one integration test runs against the SurrealDB version targeted by this release
- [ ] No TODO stubs remain in production code paths (stubs are only acceptable in Phase 1 for items explicitly deferred to Phase 2+)

---

## Stakeholder Communication

- [ ] Roadmap changes communicated to the team before the next sprint
- [ ] Breaking API changes communicated at least one sprint in advance
- [ ] Issues blocking Phase 1 completion are flagged to the Architect within 24 hours of discovery
- [ ] Performance targets from `ARCHITECTURE.md` are validated by the 10x Developer role before release claim

---

## Backlog Health Check (Weekly)

- [ ] Backlog has at least 2 sprints of ready stories (acceptance criteria written)
- [ ] No story older than 3 sprints without a decision (do it, defer it, or delete it)
- [ ] Critical bugs at severity 8-10 in `RISK_ASSESSMENT.md` have a fix in the current or next sprint
- [ ] Phase 1 completion blockers are tracked as P0 items at the top of the backlog

---

## API Design Review

When the Architect or Developer proposes a new public API surface:

- [ ] Does it feel familiar to EF Core developers? Compare to `DbContext`, `DbSet<T>`, `IQueryable<T>`
- [ ] Can a developer use it correctly without reading documentation? (pit of success principle)
- [ ] Does it handle the most common error scenario gracefully (connection not open, invalid input)?
- [ ] Is it additive? (Does it break any existing callers of `ISurrealDbClient`?)
- [ ] Would a developer using it in ASP.NET dependency injection need any non-obvious setup?
