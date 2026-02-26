# Developer Assignment: Critical Bug Fixes (P0.1–P0.3)

**Assigned To:** Primary Developer
**Status:** ACTIVE
**Deadline:** Complete all 3 bugs with passing tests before PR review
**Review Path:** 10x Developer → Architect → Merge

---

## 📌 YOUR MISSION

Fix 3 critical blocking bugs in the connection pool and WebSocket adapter. These bugs cause:
- Every application to hang on dispose (P0.1)
- Production monitoring to crash (P0.2)
- Data loss on responses > 4 KB (P0.3)

**Estimated Effort:** 7–9 hours total
**Testing Requirement:** All acceptance criteria must pass before PR review

---

## 🚨 BUG ASSIGNMENTS (Priority Order)

### P0.1: DisposeAsync Deadlock + Null-Adapter Window ⚠️ CRITICAL
**File:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
**Time:** 3–4 hours
**Impact:** Every dispose hangs indefinitely (application deployment blocker)

#### What's Broken
1. `DisposeAsync()` acquires `_disposeSemaphore`, then calls `ClearAsync()` which **also** acquires it (causes deadlock)
2. `AcquireAsync()` adds `PooledConnection` to `_allConnections` **before** creating adapter (null-reference window)

#### Your Fix
1. **Extract `ClearConnectionsAsync()` method:**
   - Create new `private async Task ClearConnectionsAsync()`
   - Move drain logic from `ClearAsync()` into it
   - No semaphore acquired inside (semaphore acquired by caller)

2. **Update `ClearAsync()`:**
   ```csharp
   public async Task ClearAsync()
   {
       await _disposeSemaphore.WaitAsync();
       try
       {
           await ClearConnectionsAsync();  // Now uses shared logic
       }
       finally
       {
           _disposeSemaphore.Release();
       }
   }
   ```

3. **Update `DisposeAsync()`:**
   ```csharp
   public async ValueTask DisposeAsync()
   {
       await _disposeSemaphore.WaitAsync();
       try
       {
           await ClearConnectionsAsync();  // Reuses same logic, no re-entry
       }
       finally
       {
           _disposeSemaphore.Release();
       }
   }
   ```

4. **Fix Null-Adapter Window in `AcquireAsync()`:**
   - Create adapter **BEFORE** adding to `_allConnections`
   - Ensure `PooledConnection` has non-null adapter when added to set
   - See detailed code in `/docs/roles/10x-developer/guidelines.md` lines 14-65, 225-262

#### Tests You Must Write
```csharp
[Fact(Timeout = 5000)]
public async Task DisposeAsync_CompletesWithinTimeout()
{
    // DisposeAsync must complete in < 5s (proves no deadlock)
}

[Fact]
public async Task DisposeAsync_ConcurrentFromTwoThreads_NoDeadlock()
{
    // Two tasks call DisposeAsync concurrently → both complete
}

[Fact]
public async Task ClearAsync_Independent_FromDisposeAsync()
{
    // ClearAsync works standalone (proves no shared state issue)
}

[Fact]
public async Task AcquireAsync_DuringDispose_NoNullAdapterReference()
{
    // Concurrent acquire during dispose → no NullReferenceException
}
```

#### Acceptance Criteria
- [ ] Code review: ClearConnectionsAsync extracted correctly
- [ ] Code review: No semaphore double-acquire possible
- [ ] Code review: Adapter created before _allConnections.Add()
- [ ] **All 4 tests pass**
- [ ] No new compiler warnings
- [ ] PR description explains BOTH fixes are atomic

---

### P0.2: GetStatistics Data Race 🔴 HIGH
**File:** `src/SurrealDB.Client/Connection/ConnectionPool.cs`
**Time:** 30 minutes
**Impact:** Monitoring calls crash production (`InvalidOperationException`)

#### What's Broken
`GetStatistics()` accesses `HashSet<PooledConnection>` without lock while `AcquireAsync()` modifies it.

#### Your Fix
In `GetStatistics()`:
```csharp
public PoolStatistics GetStatistics()
{
    lock (_allConnections)  // ADD THIS LOCK
    {
        return new PoolStatistics
        {
            TotalConnections = _allConnections.Count,
            InUseConnections = /* ... */,
            AvailableConnections = Interlocked.Read(ref _availableCount)  // Use Interlocked
        };
    }
}
```

#### Tests You Must Write
```csharp
[Fact]
public async Task GetStatistics_Concurrent_WithAcquireAsync_NoException()
{
    // 100 concurrent AcquireAsync + 100 concurrent GetStatistics
    // → No InvalidOperationException
}

[Fact]
public async Task GetStatistics_Invariant_Holds()
{
    // Verify: TotalConnections >= InUseConnections + AvailableConnections
    // (always true after lock fix)
}
```

#### Acceptance Criteria
- [ ] Code review: `_allConnections` access wrapped in lock
- [ ] Code review: Lock does NOT become bottleneck (measure with profiler)
- [ ] **Both stress tests pass**
- [ ] Statistics invariant holds under concurrent load

---

### P0.3: WebSocket Response Truncation 🔴 CRITICAL
**File:** `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs`
**Time:** 4–5 hours
**Impact:** Silent data loss — responses > 4 KB truncated without error

#### What's Broken
1. Fixed 4 KB buffer: `byte[] buffer = new byte[4096];`
2. No loop to accumulate multi-frame WebSocket messages
3. `EndOfMessage` flag ignored

#### Your Fix
1. **Replace fixed buffer with dynamic allocation:**
   ```csharp
   // OLD (WRONG):
   // byte[] buffer = new byte[4096];

   // NEW (RIGHT):
   byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
   try
   {
       // ... use buffer ...
   }
   finally
   {
       ArrayPool<byte>.Shared.Return(buffer);
   }
   ```

2. **Accumulate frames in MemoryStream until `EndOfMessage`:**
   ```csharp
   var ms = new MemoryStream();
   WebSocketReceiveResult result;

   do
   {
       result = await _websocket.ReceiveAsync(
           new ArraySegment<byte>(buffer, 0, buffer.Length),
           CancellationToken.None);

       ms.Write(buffer, 0, result.Count);
   }
   while (!result.EndOfMessage);  // Loop until end of message

   return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
   ```

3. **Add 50 MB size limit (prevent OOM):**
   ```csharp
   if (ms.Length > 50 * 1024 * 1024)  // 50 MB limit
       throw new InvalidOperationException("Response too large");
   ```

4. **Use `GetBuffer()` instead of `ToArray()`:**
   - Saves double allocation per response
   - See P0.10 (allocation fix)

#### Tests You Must Write
```csharp
[Fact]
public async Task ReceiveAsync_Response4097Bytes_Complete()
{
    // Mock WebSocket returns exactly 4097 bytes (> 4 KB)
    // → Full response received, not truncated
}

[Fact]
public async Task ReceiveAsync_ResponseSpans3Frames_Accumulated()
{
    // Response split across 3 WebSocket frames
    // → All frames accumulated correctly
}

[Fact]
public async Task ReceiveAsync_ResponseAtFrameBoundary_Correct()
{
    // Response exactly at 4 KB frame boundary
    // → No off-by-one errors
}

[Fact]
public async Task ReceiveAsync_ArrayPoolBuffer_Returned()
{
    // Verify buffer returned to pool in all code paths (success, error, timeout)
    // → No leaks
}

[Fact]
public async Task ReceiveAsync_1MBResponse_IntegrationTest()
{
    // Real SurrealDB returns 1 MB response
    // → Received correctly
}

[Fact]
public async Task ReceiveAsync_50MBLimit_Enforced()
{
    // Response exceeds 50 MB limit
    // → Throws InvalidOperationException
}
```

#### Acceptance Criteria
- [ ] Code review: Buffer allocated from ArrayPool
- [ ] Code review: Buffer returned in finally block (no leaks)
- [ ] Code review: Loop accumulates until `EndOfMessage == true`
- [ ] Code review: 50 MB limit enforced
- [ ] **All 6 tests pass**
- [ ] Memory profiler shows no leaks
- [ ] Integration test: 1 MB SurrealDB response succeeds

---

## 📋 YOUR WORKFLOW

### Step 1: Create Feature Branch
```bash
# Branch already exists
git fetch origin claude/create-feature-plan-nfPfA
git checkout claude/create-feature-plan-nfPfA
```

### Step 2: Read the Guidelines
Before coding, read the detailed fix code:
- **P0.1:** `/docs/roles/10x-developer/guidelines.md` lines 14-65, 225-262
- **P0.2:** `/docs/roles/10x-developer/guidelines.md` lines 71-108
- **P0.3:** `/docs/roles/10x-developer/guidelines.md` lines 112-157

These contain exact code snippets. Use them as your reference implementation.

### Step 3: Implement Each Bug
**Do them in this order:**
1. P0.3 (WebSocket) — easiest to test in isolation
2. P0.1 (DisposeAsync) — most complex
3. P0.2 (GetStatistics) — simplest

**For each bug:**
- [ ] Read the fix guidance (see Step 2)
- [ ] Write tests FIRST (test-driven approach)
- [ ] Implement the fix
- [ ] Run tests: `dotnet test`
- [ ] Commit with clear message:
  ```
  fix(P0.1): Fix DisposeAsync deadlock and null-adapter window

  - Extract ClearConnectionsAsync to prevent semaphore re-entry
  - Create adapter before adding to _allConnections set
  - Add timeout test, concurrent dispose test, null-check test

  Fixes #[issue-number]
  ```

### Step 4: Run Full Test Suite
```bash
dotnet test --configuration Release
```
All tests must pass, including existing tests.

### Step 5: Create Pull Request
```bash
git push -u origin claude/create-feature-plan-nfPfA
```

**PR Title:** `[P0 BUGS] Fix DisposeAsync, GetStatistics, WebSocket truncation`

**PR Body:**
```markdown
## Summary
Fixes 3 critical blocking bugs that prevent any feature work:

- **P0.1:** DisposeAsync deadlock (semaphore re-entry) + null-adapter window
- **P0.2:** GetStatistics data race (concurrent modification)
- **P0.3:** WebSocket response truncation (4 KB buffer, no frame accumulation)

## Testing
- All 11 new tests pass
- All existing tests pass
- No compiler warnings
- Memory profiler shows 50% allocation reduction (P0.3)
- Stress test: 100 concurrent operations (P0.2)

## Changes
- ConnectionPool.cs: Extract ClearConnectionsAsync, fix adapter timing
- ConnectionPool.cs: Add lock to GetStatistics
- WebSocketProtocolAdapter.cs: Dynamic buffer, frame accumulation, 50 MB limit

## Reviewed By
- [ ] 10x Developer (performance, concurrency)
- [ ] Architect (design, dependencies)
```

### Step 6: Wait for Review
**Reviewers (in order):**
1. **10x Developer** — checks concurrency, performance, memory safety
2. **Architect** — checks design, dependencies, breaking changes

**You must:**
- [ ] Address all comments before merge
- [ ] Re-run tests if any code changes after review
- [ ] Get approval from both reviewers

### Step 7: Merge
When both reviewers approve:
```bash
git pull origin claude/create-feature-plan-nfPfA
# PR merged via GitHub UI
```

---

## ⚠️ CRITICAL REQUIREMENTS

**Non-negotiable:**
- ✅ All 3 bugs fixed in a single PR (atomic)
- ✅ All acceptance criteria met
- ✅ All new tests passing
- ✅ All existing tests still passing
- ✅ No compiler warnings
- ✅ PR reviewed by both 10x Developer AND Architect before merge

**If tests fail:**
- Do NOT push
- Debug locally
- Re-run tests
- Fix the issue
- Re-commit and re-push

**If a reviewer requests changes:**
- Fix it
- Re-run full test suite
- Commit with message: `fix: Address review comment in P0.X`
- Push again
- Mark comment as resolved

---

## 📞 SUPPORT

If blocked:
- **Concurrency questions:** Ask 10x Developer (→ `/docs/roles/10x-developer/`)
- **Architecture questions:** Ask Architect (→ `/docs/roles/architect/`)
- **WebSocket details:** Check protocol specs in `/docs/`

---

## ✅ SUCCESS CRITERIA

You're done when:
- [ ] P0.1 implemented and tested
- [ ] P0.2 implemented and tested
- [ ] P0.3 implemented and tested
- [ ] PR created with all 3 fixes
- [ ] 10x Developer approved PR
- [ ] Architect approved PR
- [ ] PR merged to branch

Then: **Foundation work (P0.4–P0.12) can begin** 🚀

---

**Start now.** These bugs are blocking all other work.

https://claude.ai/code/session_01PSh4EuXiAJw6WN6ei4TcLK
