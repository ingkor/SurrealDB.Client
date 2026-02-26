# SurrealDB.Client Execution Checklist

**Quick reference for task execution. See BACKLOG.md for detailed tasks.**

---

## 🚨 PHASE 1: CRITICAL BUG FIXES (P0.1–P0.3)
**Total Effort:** 7–9 hours | **Status:** READY TO START

These three bugs can be worked in parallel.

### P0.1: DisposeAsync Deadlock + Null-Adapter Window
- **File:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
- **Effort:** 3–4 hours
- **Blocking:** Yes (every dispose hangs)
- **Checklist:**
  - [ ] Extract `ClearConnectionsAsync()` private method
  - [ ] Update `DisposeAsync()` to call `ClearConnectionsAsync()` (no re-entry)
  - [ ] Update `ClearAsync()` to call `ClearConnectionsAsync()`
  - [ ] Move adapter creation before `_allConnections.Add()`
  - [ ] Write timeout test for `DisposeAsync`
  - [ ] Write concurrent dispose test
  - [ ] Write test for null-adapter window scenario

### P0.2: GetStatistics Data Race
- **File:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
- **Effort:** 30 minutes
- **Blocking:** Yes (monitoring crashes)
- **Checklist:**
  - [ ] Add `lock (_allConnections)` in `GetStatistics()`
  - [ ] Use `Interlocked.Read()` for long counters
  - [ ] Write stress test: 100 concurrent AcquireAsync + GetStatistics
  - [ ] Verify statistics invariant holds

### P0.3: WebSocket Response Truncation
- **File:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs`
- **Effort:** 4–5 hours
- **Blocking:** Yes (silent data loss)
- **Checklist:**
  - [ ] Use `ArrayPool<byte>.Shared.Rent(16 * 1024)` for buffer
  - [ ] Accumulate frames in `MemoryStream` until `EndOfMessage`
  - [ ] Replace `ms.ToArray()` with `ms.GetBuffer()`
  - [ ] Add 50 MB size limit
  - [ ] Write unit tests for 4KB boundary, multi-frame, frame boundary
  - [ ] Write integration test for 1 MB response
  - [ ] Verify no ArrayPool leaks

---

## 🏗️ PHASE 2: FOUNDATION FIXES (P0.4–P0.12)
**Total Effort:** 11–17 hours | **Status:** BLOCKED BY PHASE 1

All P0.1–P0.3 must complete before starting P0.4.

### P0.4: Implement USE NS / USE DB
- **File:** `src/SurrealDB.Client/SurrealDbClient.cs`
- **Effort:** 2 hours
- **Checklist:**
  - [ ] After auth in `ConnectAsync`, send `USE NS <ns> DB <db>`
  - [ ] Validate response indicates success
  - [ ] Write integration test that queries after connect

### P0.5: Harden Options Validation
- **File:** `src/SurrealDB.Client/SurrealDbClientOptions.cs`
- **Effort:** 30 minutes
- **Checklist:**
  - [ ] Make `Namespace` required in `Validate()`
  - [ ] Make `Database` required in `Validate()`
  - [ ] Write unit tests for validation

### P0.6: Replace QueryResult with SurrealDbResponse<T>
- **File:** `src/SurrealDB.Client/SurrealDbResponse.cs` (new)
- **Effort:** 4–6 hours
- **Checklist:**
  - [ ] Create `SurrealDbResponse<T>` class
  - [ ] Properties: Status, Time, Result (T[])
  - [ ] Update serializer to handle envelope
  - [ ] Write tests for envelope deserialization
  - [ ] Update CRUD method signatures (when implemented)

### P0.7: Set up Mock Adapter for Testing
- **File:** `tests/SurrealDB.Client.Tests.Unit/Mocks/`
- **Effort:** 4–6 hours
- **Checklist:**
  - [ ] Create `MockProtocolAdapter : IProtocolAdapter`
  - [ ] Implement `SendAsync` with canned responses
  - [ ] Provide test data for all CRUD operations
  - [ ] Rewrite existing unit tests to use mock
  - [ ] Add `[Trait("Category", "Unit")]` markers
  - [ ] Verify unit tests pass in CI without server

### P0.8: Health Check Grace-Period Throttling
- **File:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
- **Effort:** 1 hour (include with P0.1)
- **Checklist:**
  - [ ] Add `LastUsedAt` tracking to `PooledConnection`
  - [ ] Add grace period check in `AcquireAsync`
  - [ ] Skip health check for recently-used connections
  - [ ] Test: health checks reduced by ~99%

### P0.9: Replace ConcurrentBag.Count
- **File:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
- **Effort:** 30 minutes (include with P0.1)
- **Checklist:**
  - [ ] Add `private int _availableCount`
  - [ ] Replace `_availableConnections.Count` with counter
  - [ ] Use `Interlocked.Increment/Decrement`
  - [ ] Verify counter stays in sync

### P0.10: WebSocket GetBuffer() Allocation Fix
- **File:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs`
- **Effort:** 5 minutes (include with P0.3)
- **Checklist:**
  - [ ] Replace `ms.ToArray()` with `ms.GetBuffer()`
  - [ ] Verify 50% allocation reduction
  - [ ] Test responses deserialize correctly

### P0.11: WebSocket SendAsync Lock
- **File:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs`
- **Effort:** 30 minutes (include with P0.3)
- **Checklist:**
  - [ ] Add `_sendLock = new SemaphoreSlim(1, 1)`
  - [ ] Wrap `SendAsync` in lock
  - [ ] Document concurrency contract
  - [ ] Test concurrent sends serialize

### P0.12: CI ConfigureAwait(false) Enforcement
- **File:** `.github/workflows/`
- **Effort:** 1 hour
- **Checklist:**
  - [ ] Add CI check for missing `ConfigureAwait(false)`
  - [ ] Exclude test code from check
  - [ ] Document requirement in developer guidelines
  - [ ] Test that CI fails on violation

---

## 🎯 PHASE 3: IMPLEMENTATION FEATURES (1.1–1.13)
**Total Effort:** 40–58 hours | **Status:** BLOCKED BY PHASE 2

All P0 items must complete before starting P1.

### ⚡ DECISION LOCKED: SurrealDB 3.0+ (Non-Negotiable)
- ✅ **Target: SurrealDB 3.0 or newer**
- ✅ Enforce in `ConnectAsync`: throw if server < 3.0
- ✅ Document in README.md
- ✅ Use 3.0+ features (UPSERT, etc.)

### Feature 1.1: Implement CreateAsync
- **Effort:** 4–6 hours
- **Checklist:**
  - [ ] Write signature and SurrealQL
  - [ ] Handle auto-generated IDs
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.2: Implement GetAsync
- **Effort:** 3–4 hours
- **Checklist:**
  - [ ] Write signature and SurrealQL
  - [ ] Return `null` for not-found
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.3: Implement SelectAsync
- **Effort:** 3–4 hours
- **Checklist:**
  - [ ] Default limit = 1000
  - [ ] Document unlimited is explicit opt-in
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.4: Implement UpdateAsync
- **Effort:** 3–4 hours
- **Checklist:**
  - [ ] Write signature and SurrealQL
  - [ ] Add concurrency warning to XML docs
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.5: Implement DeleteAsync
- **Effort:** 2–3 hours
- **Checklist:**
  - [ ] Document idempotent contract
  - [ ] Return `true` for non-existent delete
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.6: Implement UpsertAsync
- **Effort:** 4–6 hours
- **Checklist:**
  - [ ] Use SurrealDB 2.x `UPSERT` syntax
  - [ ] Handle create case
  - [ ] Handle update case
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.7: Implement QueryAsync
- **Effort:** 3–4 hours
- **Checklist:**
  - [ ] Parameterized queries only
  - [ ] Prevent string interpolation
  - [ ] Write unit test (mock)
  - [ ] Write integration test (real DB)

### Feature 1.8: Implement SurrealDbTransaction
- **Effort:** 6–8 hours
- **Prerequisite:** Design document approved
- **Checklist:**
  - [ ] Design document signed off by Architect
  - [ ] `BeginTransactionAsync` acquires connection
  - [ ] `CommitAsync` / `RollbackAsync` release connection
  - [ ] Hold connection for lifetime
  - [ ] Write unit tests
  - [ ] Write integration tests

### Feature 1.9: Add Validation (Table Names, IDs)
- **Effort:** 1 hour
- **Checklist:**
  - [ ] Table name regex validation
  - [ ] Record ID regex validation
  - [ ] Validate in all CRUD methods
  - [ ] Write unit tests

### Feature 1.10: Write Unit Tests (Mock-based)
- **Effort:** 8–12 hours
- **Target:** 70%+ coverage
- **Checklist:**
  - [ ] Tests for all CRUD operations
  - [ ] Success, error, not-found scenarios
  - [ ] 70%+ code coverage
  - [ ] All tests pass in CI

### Feature 1.11: Write Integration Tests
- **Effort:** 3–4 hours
- **Checklist:**
  - [ ] Full CRUD lifecycle
  - [ ] Marked `[Trait("Category", "Integration")]`
  - [ ] Docker SurrealDB setup in CI
  - [ ] Tests pass against real DB

### Feature 1.12: Document UpdateAsync Limitation
- **Effort:** 15 minutes
- **Checklist:**
  - [ ] Add `<remarks>` to UpdateAsync
  - [ ] Warn about last-write-wins
  - [ ] Reference Phase 2 optimistic concurrency

### Feature 1.13: Document SurrealDB Version
- **Effort:** 1–2 hours
- **Checklist:**
  - [ ] Document minimum version in README
  - [ ] Version validation in ConnectAsync
  - [ ] Version error message is clear

---

## 📊 PROGRESS TRACKING

| Phase | Tasks | Completed | % Done |
|-------|-------|-----------|--------|
| **P0 Bugs** | 3 | [ ] | 0% |
| **P0 Foundation** | 9 | [ ] | 0% |
| **P1 Features** | 13 | [ ] | 0% |

---

## 🔑 REMAINING DECISIONS

- ✅ **SurrealDB Version Target:** LOCKED at 3.0+ (non-negotiable)
- [ ] **Transaction Connection Hold:** Design document needed before Feature 1.8
- [ ] **Team Assignment:** Who owns P0.1, P0.2, P0.3? (can be parallel) → **PRIMARY DEVELOPER ASSIGNMENT COMING**

---

## ⚠️ DO NOT START P1 UNTIL ALL P0 COMPLETE

The P0 bugs are blocking. Starting feature work before these are fixed will:
- Waste time debugging on corrupted pool state
- Discover the "USE NS/DB" blocker during integration
- Hit query deserialization failures due to wrong envelope model
- Have no CI-passing unit tests

**Wait for full P0 completion signal** ✋

---

## 📞 Questions for Expert Panel

If blocked or uncertain:
- **Architecture questions** → Ask Architect role (check `/docs/roles/architect/`)
- **Data/schema questions** → Ask DB Owner role (check `/docs/roles/db-owner/`)
- **Performance/concurrency questions** → Ask 10x Developer role (check `/docs/roles/10x-developer/`)
