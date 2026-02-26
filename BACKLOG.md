# SurrealDB.Client Development Backlog

**Last Updated:** 2026-02-26
**Status:** Expert review complete, ready for execution
**Priority Ordering:** Critical bugs first, then foundation, then features

---

## 🚨 CRITICAL BUGS (P0 - BLOCKING)

These must be fixed before any feature work begins. Each blocks Week 4 implementation.

### Bug P0.1: DisposeAsync Deadlock + Null-Adapter Window
**Location:** `src/SurrealDB.Client/Connection/ConnectionPool.cs` (lines 217-235, 79-108)
**Impact:** Every application that disposes a client hangs indefinitely; concurrent dispose can corrupt pool with null adapters
**Root Cause:**
- `DisposeAsync` acquires `_disposeSemaphore` then calls `ClearAsync()` which also acquires it (non-reentrant)
- `AcquireAsync` adds `PooledConnection` to `_allConnections` with `Adapter = null` before adapter is created

**Fix:**
1. Extract shared drain logic into `private async Task ClearConnectionsAsync()`
2. Both `ClearAsync()` and `DisposeAsync()` call `ClearConnectionsAsync()` after acquiring semaphore
3. Create adapter **before** adding to `_allConnections` set
4. See detailed fix code in `/docs/roles/10x-developer/guidelines.md` lines 14-65, 225-262

**Acceptance Criteria:**
- [ ] `[Fact(Timeout = 5000)]` test: `DisposeAsync` completes within timeout
- [ ] Test: concurrent `DisposeAsync` from 2 threads — no deadlock
- [ ] Test: `ClearAsync` works independently from `DisposeAsync`
- [ ] Test: concurrent `AcquireAsync` during `DisposeAsync` — no `NullReferenceException`
- [ ] PR description explicitly calls out both fixes as atomic

**Effort:** 3–4 hours (includes tests)

---

### Bug P0.2: GetStatistics Data Race
**Location:** `src/SurrealDB.Client/Connection/ConnectionPool.cs` (lines 204-215)
**Impact:** `GetStatistics()` throws `InvalidOperationException` when called concurrently with `AcquireAsync`; production monitoring crashes
**Root Cause:** `HashSet<PooledConnection>` accessed without lock in `GetStatistics()`

**Fix:**
1. Wrap `_allConnections` access in `lock (_allConnections)` in `GetStatistics()`
2. Use `Interlocked.Read()` for long counters
3. See detailed fix code in `/docs/roles/10x-developer/guidelines.md` lines 71-108

**Acceptance Criteria:**
- [ ] Stress test: 100 concurrent `AcquireAsync` + 100 concurrent `GetStatistics()` calls
- [ ] Verify statistics invariant: `TotalConnections >= InUseConnections + AvailableConnections`
- [ ] No `InvalidOperationException` thrown
- [ ] Lock does not become bottleneck (measure with profiler)

**Effort:** 30 minutes

---

### Bug P0.3: WebSocket Response Truncation
**Location:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` (lines 109-115)
**Impact:** All responses > 4 KB return silently truncated/corrupt JSON; real-world queries fail without error message
**Root Cause:** Fixed 4 KB buffer, no loop accumulation for multi-frame messages

**Fix:**
1. Use `ArrayPool<byte>.Shared.Rent(16 * 1024)` for buffer allocation
2. Accumulate frames in `MemoryStream` until `EndOfMessage == true`
3. Replace `ms.ToArray()` with `ms.GetBuffer()` to avoid double allocation
4. Add 50 MB size limit to prevent OOM
5. See detailed fix code in `/docs/roles/10x-developer/guidelines.md` lines 112-157

**Acceptance Criteria:**
- [ ] Unit test: WebSocket server returns exactly 4097 bytes (> 4 KB limit)
- [ ] Unit test: response spans 3 WebSocket frames
- [ ] Unit test: response exactly at frame boundary
- [ ] Unit test: `EndOfMessage` flag properly handled in loop
- [ ] Unit test: `ArrayPool<byte>` buffers returned in all code paths (no leaks)
- [ ] Integration test: real SurrealDB returns 1 MB response correctly
- [ ] No response truncation observed

**Effort:** 4–5 hours (includes tests)

---

## 🏗️ FOUNDATION FIXES (P0 - BLOCKING)

These are not bugs but architectural prerequisites. Week 4 feature work cannot proceed without them.

### P0.4: Implement USE NS / USE DB in ConnectAsync
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs` (ConnectAsync method)
**Impact:** Every CRUD operation will fail at the database layer without this; deployment blocker
**Requirement:** After successful `ConnectAsync`, immediately send `USE NS <ns> DB <db>` statement

**Fix:**
1. Store `Namespace` and `Database` from `SurrealDbClientOptions`
2. After authentication in `ConnectAsync`, send: `USE NS <namespace> DB <database>;`
3. Validate response indicates success
4. Throw `ConnectionException` if either namespace or database is null
5. Add integration test that verifies `USE NS/DB` was sent

**Acceptance Criteria:**
- [ ] `ConnectAsync` validates `options.Namespace` and `options.Database` are not null
- [ ] After successful auth, `USE NS <ns> DB <db>` is sent
- [ ] Integration test: query succeeds after connect (proves USE NS/DB worked)
- [ ] Integration test: subsequent queries use correct namespace/database

**Effort:** 2 hours

---

### P0.5: Harden SurrealDbClientOptions.Validate()
**Location:** `src/SurrealDB.Client/SurrealDbClientOptions.cs` (Validate method)
**Impact:** Confusing runtime errors; prevents actionable validation at config time
**Requirement:** Namespace and Database must be required, not optional

**Fix:**
1. Add check: `if (string.IsNullOrWhiteSpace(Namespace)) throw new ValidationException("Namespace is required...")`
2. Add check: `if (string.IsNullOrWhiteSpace(Database)) throw new ValidationException("Database is required...")`
3. Call in `SurrealDbClient.ConnectAsync` before `InitializeAsync`

**Acceptance Criteria:**
- [ ] Attempting to connect without Namespace throws `ValidationException` with clear message
- [ ] Attempting to connect without Database throws `ValidationException` with clear message
- [ ] Unit test: Validate() throws for missing namespace
- [ ] Unit test: Validate() throws for missing database

**Effort:** 30 minutes

---

### P0.6: Replace QueryResult with SurrealDbResponse<T>
**Location:** `src/SurrealDB.Client/SurrealDbResponse.cs` (new file)
**Impact:** CRUD operations cannot deserialize correctly without proper envelope model
**Requirement:** Model actual SurrealDB response: `{ status: "OK", time: "1.5ms", result: T[] }`

**Fix:**
1. Create `SurrealDbResponse<T>` class
2. Properties: `Status` (string), `Time` (string), `Result` (T[])
3. Update `ISerializer` to expose method to deserialize with envelope unwrapping
4. Update all CRUD return types to use `SurrealDbResponse<T>`
5. Handle empty result arrays (not-found vs empty array distinction)

**Acceptance Criteria:**
- [ ] `SurrealDbResponse<T>` properly deserializes SurrealDB JSON envelope
- [ ] Status validation: throw if not "OK"
- [ ] Unit test: deserialize envelope with typed result array
- [ ] Unit test: deserialize empty result array
- [ ] Unit test: deserialize with error status throws exception

**Effort:** 4–6 hours (includes tests)

---

### P0.7: Set up IProtocolAdapter Mock for Unit Tests
**Location:** `tests/SurrealDB.Client.Tests.Unit/Mocks/` (new folder)
**Impact:** Cannot achieve 70%+ coverage in CI without server-independent tests
**Requirement:** `IProtocolAdapter` mock that returns canned SurrealDB responses

**Fix:**
1. Create `MockProtocolAdapter : IProtocolAdapter`
2. Implement `SendAsync` to return pre-canned JSON matching SurrealDB response envelopes
3. Provide test data for: CREATE, GET (found), GET (not-found), SELECT (multiple), UPDATE, DELETE, UPSERT
4. Rewrite existing `SurrealDbClientTests` to use mock adapter instead of live server
5. Add test attribute `[Trait("Category", "Unit")]` to distinguish from integration tests

**Acceptance Criteria:**
- [ ] Mock adapter implements all `IProtocolAdapter` methods
- [ ] Mock returns valid SurrealDB response JSON for each CRUD operation
- [ ] Existing unit tests rewritten to use mock (no live server dependency)
- [ ] CI unit tests pass without SurrealDB running
- [ ] Integration tests marked separately and can be skipped in standard CI

**Effort:** 4–6 hours

---

## 📦 FOUNDATION OPTIMIZATIONS (P0 - WITH BUGS)

These are part of the critical bug fixes but listed separately for clarity.

### P0.8: Health Check Grace-Period Throttling
**Location:** `src/SurrealDB.Client/Connection/ConnectionPool.cs` (in AcquireAsync)
**Impact:** Eliminates 99% of health check round trips; reduces latency by 10–50ms per operation
**Requirement:** Skip health check for connections used recently (last 30 seconds)

**Fix:**
1. Add `LastUsedAt` tracking to `PooledConnection`
2. In `AcquireAsync`, check: `if (DateTime.UtcNow - pooledConnection.LastUsedAt < TimeSpan.FromSeconds(30))`
3. If true, skip health check; return immediately
4. See fix code in `/docs/roles/10x-developer/guidelines.md` lines 168-193

**Acceptance Criteria:**
- [ ] `LastUsedAt` is tracked and updated on every acquire
- [ ] Health checks skipped for recently-used connections
- [ ] Health check still runs for idle connections
- [ ] Measurement: health check calls reduced by ~99% under sustained load

**Effort:** 1 hour (do as part of P0.1 fix)

---

### P0.9: Replace ConcurrentBag.Count with Interlocked Counter
**Location:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
**Impact:** `ConcurrentBag.Count` is O(n); pool size calculation takes 100 iterations for pool size 100
**Requirement:** Use `Interlocked` counter for O(1) availability check

**Fix:**
1. Add `private int _availableCount = 0;`
2. Replace all `_availableConnections.Count` with `_availableCount`
3. Increment on release: `Interlocked.Increment(ref _availableCount)`
4. Decrement on acquire: `Interlocked.Decrement(ref _availableCount)`

**Acceptance Criteria:**
- [ ] Pool availability count is O(1) operation
- [ ] Interlocked counter stays in sync with actual available connections
- [ ] Stress test: counter remains accurate under concurrent acquire/release

**Effort:** 30 minutes (do as part of P0.1 fix)

---

### P0.10: WebSocket MemoryStream Allocation Fix
**Location:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` (in receive loop)
**Impact:** Eliminates duplicate memory allocation on every WebSocket response (50% reduction)
**Requirement:** Use `GetBuffer()` instead of `ToArray()`

**Fix:**
1. In WebSocket receive loop, replace: `return Encoding.UTF8.GetString(ms.ToArray());`
2. With: `return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);`

**Acceptance Criteria:**
- [ ] Memory profiler shows 50% reduction in peak allocation per WebSocket response
- [ ] Responses deserialized correctly with `GetBuffer()`
- [ ] No buffer corruption from `GetBuffer()` usage

**Effort:** 5 minutes (do as part of P0.3 fix)

---

### P0.11: WebSocket SendAsync Serialization Lock
**Location:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` (SendAsync method)
**Impact:** Makes concurrent calls safe (serialized) instead of broken (interleaved)
**Requirement:** Add per-adapter lock to serialize WebSocket operations

**Fix:**
1. Add `private readonly SemaphoreSlim _sendLock = new(1, 1);` to `WebSocketProtocolAdapter`
2. Wrap `SendAsync` body:
   ```csharp
   await _sendLock.WaitAsync(cancellationToken);
   try { /* existing send/receive logic */ }
   finally { _sendLock.Release(); }
   ```
3. Document: "WebSocket adapter serializes concurrent calls; for concurrent throughput use multiple pool connections"

**Acceptance Criteria:**
- [ ] Concurrent `SendAsync` calls serialize instead of interleaving
- [ ] Test: two concurrent sends complete in order without message corruption
- [ ] Documentation clarifies concurrency model

**Effort:** 30 minutes (do as part of P0.3 fix)

---

### P0.12: CI Enforcement of ConfigureAwait(false)
**Location:** `.github/workflows/` (CI configuration)
**Impact:** Prevents context-capture overhead debt in new CRUD code
**Requirement:** Build fails if any library code has `await` without `ConfigureAwait(false)`

**Fix:**
1. Add CI step to check library code:
   ```bash
   grep -rn "await " src/SurrealDB.Client --include="*.cs" | grep -v "ConfigureAwait" && exit 1 || exit 0
   ```
2. Exclude test files from check
3. Document this requirement in Developer role guidelines

**Acceptance Criteria:**
- [ ] CI step added to build pipeline
- [ ] Library code with missing `ConfigureAwait` causes build failure
- [ ] Test code exempted from check
- [ ] Documented in developer guidelines

**Effort:** 1 hour

---

## 🎯 IMPLEMENTATION FEATURES (P1 - BLOCKED BY P0)

Can only start after all P0 bugs and foundation work complete.

### Feature 1.1: Implement CreateAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Signature: `Task<T?> CreateAsync<T>(string table, T record, CancellationToken ct = default)`
- SurrealQL: `CREATE table:id CONTENT { ... } RETURN AFTER`
- Auto-generate ID if not provided
- Return created object with populated ID

**Acceptance Criteria:**
- [ ] Create new record successfully
- [ ] Auto-generated ID is returned
- [ ] User-provided ID is respected
- [ ] Unit test against mock adapter
- [ ] Integration test against real SurrealDB

**Effort:** 4–6 hours

---

### Feature 1.2: Implement GetAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Signature: `Task<T?> GetAsync<T>(string recordId, CancellationToken ct = default)`
- SurrealQL: `SELECT * FROM recordId`
- Return `null` for not-found (empty result array), not throw

**Acceptance Criteria:**
- [ ] Retrieve existing record
- [ ] Return `null` for not-found (no exception)
- [ ] Correct record ID format validation
- [ ] Unit test against mock adapter
- [ ] Integration test against real SurrealDB

**Effort:** 3–4 hours

---

### Feature 1.3: Implement SelectAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Signature: `Task<List<T>> SelectAsync<T>(string table, int limit = 1000, CancellationToken ct = default)`
- SurrealQL: `SELECT * FROM table LIMIT limit`
- Default limit 1000 (prevent accidental full table scans)
- Document that callers must pass `int.MaxValue` explicitly for unlimited

**Acceptance Criteria:**
- [ ] SELECT with default limit returns up to 1000 records
- [ ] SELECT with explicit limit respected
- [ ] Empty table returns empty list, not null
- [ ] Documentation warns about no-limit danger
- [ ] Unit test against mock adapter
- [ ] Integration test with large table

**Effort:** 3–4 hours

---

### Feature 1.4: Implement UpdateAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Signature: `Task<T?> UpdateAsync<T>(string recordId, T updates, CancellationToken ct = default)`
- SurrealQL: `UPDATE recordId CONTENT { ... } RETURN AFTER`
- Full object update (last-write-wins semantics)
- Document concurrency limitation prominently

**Acceptance Criteria:**
- [ ] Update existing record
- [ ] Return updated object
- [ ] XML docs include `<remarks>` warning about last-write-wins
- [ ] No optimistic concurrency (documented limitation)
- [ ] Unit test against mock adapter
- [ ] Integration test against real SurrealDB

**Effort:** 3–4 hours

---

### Feature 1.5: Implement DeleteAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Signature: `Task<bool> DeleteAsync(string recordId, CancellationToken ct = default)`
- SurrealQL: `DELETE recordId`
- Return `true` if DELETE RPC was sent/acknowledged (idempotent)
- Deleting non-existent record also returns `true`

**Acceptance Criteria:**
- [ ] Delete existing record returns true
- [ ] Delete non-existent record returns true (idempotent contract)
- [ ] XML docs document this contract
- [ ] Unit test against mock adapter
- [ ] Integration test against real SurrealDB

**Effort:** 2–3 hours

---

### Feature 1.6: Implement UpsertAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7 + SurrealDB version decision
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Decision required: Target SurrealDB 2.x only? Or support both 1.x and 2.x?
- **Recommended: 2.x only**
- SurrealQL (2.x): `UPSERT recordId CONTENT { ... } RETURN AFTER`
- Return created or updated object

**Acceptance Criteria:**
- [ ] SurrealDB version target decided and documented
- [ ] Upsert creates record if not exists
- [ ] Upsert updates record if exists
- [ ] Return value is created/updated object
- [ ] Unit test against mock adapter
- [ ] Integration test against real SurrealDB

**Effort:** 4–6 hours

---

### Feature 1.7: Implement QueryAsync
**Depends On:** P0.4, P0.5, P0.6, P0.7
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs`

**Requirements:**
- Signature: `Task<List<T>> QueryAsync<T>(string surrealql, Dictionary<string, object>? parameters = null, CancellationToken ct = default)`
- Raw SurrealQL support with parameterization
- Parameters use `$param` syntax only (no string interpolation)
- Validate query does not contain string interpolation

**Acceptance Criteria:**
- [ ] Execute raw SurrealQL with parameters
- [ ] Parameters properly escaped/parameterized
- [ ] Query with string interpolation rejected or flagged
- [ ] Unit test against mock adapter
- [ ] Integration test with complex query

**Effort:** 3–4 hours

---

### Feature 1.8: Implement SurrealDbTransaction
**Depends On:** P0.4, P0.5, P0.6 + transaction design document
**Location:** `src/SurrealDB.Client/SurrealDbTransaction.cs` + `ISurrealDbTransaction.cs`

**Requirements:**
- Design document must be approved before coding
- Answers: connection-hold semantics, SLA, GC behavior, pool exhaustion risk
- Signature: `ISurrealDbTransaction BeginTransactionAsync(CancellationToken ct)`
- Methods: `CommitAsync()`, `RollbackAsync()`, `ExecuteQueryAsync<T>(string sql, ...)`
- Hold dedicated pool connection for transaction lifetime

**Acceptance Criteria:**
- [ ] Design document approved by Architect
- [ ] Transaction acquires pool connection in `BeginTransactionAsync`
- [ ] Transaction holds connection through commit/rollback
- [ ] Connection released back to pool after commit/rollback
- [ ] Multiple concurrent transactions possible (limited by pool size)
- [ ] Unit test: transaction isolation
- [ ] Integration test: full transaction cycle

**Effort:** 6–8 hours

---

### Feature 1.9: Add Table Name and Record ID Validation
**Depends On:** P0.5
**Location:** `src/SurrealDB.Client/Validation/` (new folder)

**Requirements:**
- Regex for valid table names: `^[a-zA-Z_][a-zA-Z0-9_]{0,63}$`
- Regex for valid record IDs: `^[a-zA-Z0-9_:.-]{1,255}$`
- Validate in all CRUD methods before sending to server

**Acceptance Criteria:**
- [ ] Invalid table name throws `ValidationException` with clear message
- [ ] Invalid record ID throws `ValidationException`
- [ ] Valid names/IDs pass validation
- [ ] Unit tests for edge cases

**Effort:** 1 hour

---

### Feature 1.10: Write Unit Tests (CRUD against Mock)
**Depends On:** P0.7
**Location:** `tests/SurrealDB.Client.Tests.Unit/`

**Requirements:**
- Test all CRUD operations against `MockProtocolAdapter`
- Cover success, not-found, error scenarios
- Target 70%+ code coverage
- All tests pass in CI without live server

**Test Matrix:**
- CreateAsync: success, validation errors
- GetAsync: found, not-found, invalid ID
- SelectAsync: multiple results, empty, limit enforcement
- UpdateAsync: success, not-found, validation
- DeleteAsync: success, not-found (idempotent)
- UpsertAsync: create, update
- QueryAsync: parameterized, error handling
- Transaction: begin, commit, rollback

**Acceptance Criteria:**
- [ ] 70%+ code coverage achieved
- [ ] All unit tests pass in CI
- [ ] No external dependencies (mock-only)
- [ ] Tests document expected behavior

**Effort:** 8–12 hours

---

### Feature 1.11: Write Integration Tests
**Depends On:** All Features 1.1–1.10
**Location:** `tests/SurrealDB.Client.Tests.Integration/`

**Requirements:**
- Test against real SurrealDB instance
- Full create → read → update → delete lifecycle
- CI step spins up SurrealDB in Docker
- Marked `[Trait("Category", "Integration")]`

**Acceptance Criteria:**
- [ ] Full CRUD lifecycle works end-to-end
- [ ] Integration tests separate from unit tests
- [ ] CI can skip integration tests in standard pipeline
- [ ] Docker SurrealDB setup documented

**Effort:** 3–4 hours

---

### Feature 1.12: Document UpdateAsync Last-Write-Wins Limitation
**Depends On:** Feature 1.4
**Location:** `src/SurrealDB.Client/SurrealDbClient.cs` (XML docs)

**Requirements:**
- Add `<remarks>` to `UpdateAsync` XML docs
- Warn about concurrent update data loss
- Reference Phase 2 optimistic concurrency feature

**Acceptance Criteria:**
- [ ] XML docs include clear concurrency warning
- [ ] Developers cannot miss the limitation
- [ ] Non-negotiable before feature release

**Effort:** 15 minutes

---

### Feature 1.13: Document SurrealDB Minimum Version
**Depends On:** SurrealDB version decision
**Location:** `README.md`

**Requirements:**
- Document minimum SurrealDB version (2.x recommended)
- Explain version-specific features (UPSERT syntax)
- Validate version in `ConnectAsync`

**Acceptance Criteria:**
- [ ] README documents minimum version
- [ ] Version validation in `ConnectAsync` throws clear error if server is too old
- [ ] Release notes call out version requirement

**Effort:** 1–2 hours

---

## 📊 SUMMARY

| Category | Count | Effort | Status |
|----------|-------|--------|--------|
| **Critical Bugs (P0)** | 3 | 7–9 h | BLOCKING |
| **Foundation Fixes (P0)** | 9 | 11–17 h | BLOCKING |
| **Implementation Features (P1)** | 13 | 40–58 h | BLOCKED |
| **TOTAL** | 25 | **58–84 h** | ~2 weeks at solid pace |

---

## 🚀 EXECUTION ORDER

1. **Start P0 Bugs (parallel execution possible)**
   - Assign P0.1 (deadlock + null-adapter)
   - Assign P0.2 (GetStatistics)
   - Assign P0.3 (WebSocket truncation)
   - These are independent; can start simultaneously

2. **Then P0 Foundation (sequential, depends on bugs fixed)**
   - P0.4: USE NS / USE DB
   - P0.5: Validate Namespace/Database
   - P0.6: Replace QueryResult with SurrealDbResponse<T>
   - P0.7: Mock adapter setup

3. **Then P1 Features (sequential, depends on foundation)**
   - Features 1.1–1.7: CRUD operations
   - Feature 1.8: Transactions
   - Features 1.9–1.13: Validation, tests, docs

---

## ✅ DEFINITION OF DONE (Per Task)

Each task must satisfy:
- [ ] Code written and reviewed
- [ ] Tests pass (unit or integration as specified)
- [ ] CI passes (linter, build, test coverage)
- [ ] Documentation updated (XML docs, README, etc.)
- [ ] Commit message explains the "why"
- [ ] No P0 or P1 issues introduced
