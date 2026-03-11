# ✅ DONE — Scenario 1 — Critical Hot Fixes

```xml
<project>
  <name>SurrealDB.Client — Critical Hot Fixes</name>
  <description>
    Three targeted surgical fixes for critical bugs that make the library unsafe
    for production use. No new features. No new packages. Pure correctness fixes.

    Fix 1: WebSocketProtocolAdapter — add a receive-side SemaphoreSlim so that
            concurrent callers (AuthenticateAsync, HealthCheckAsync, SendAsync)
            cannot interleave WebSocket ReceiveAsync calls on the same socket.

    Fix 2: SurrealDbSessionTransaction.RollbackAsync — currently only calls
            Discard() (client-side state reset). Must also send ROLLBACK; to
            the database so the server-side transaction is actually aborted.

    Fix 3: SurrealDbSessionTransaction.CommitAsync — currently sets _isActive=false
            and returns without enforcing that SaveChangesAsync was called first.
            Must throw InvalidOperationException if called when the session has
            uncommitted changes (HasChanges == true), because changes that haven't
            been saved will be silently discarded.
  </description>
  <language>C# 13 / .NET 9</language>
  <package_manager>dotnet CLI</package_manager>
  <working_directory>C:\Projects\SurrealDB.Client</working_directory>
</project>

<scope>
  BUILD:
  1. Add _receiveLock (SemaphoreSlim(1,1)) field to WebSocketProtocolAdapter.
     Every code path that calls ReceiveFullMessageAsync (both direct and via the
     shared static helper) must await _receiveLock before calling and release it
     in a finally block.
  2. In SurrealDbSessionTransaction.RollbackAsync: after calling _session.Discard(),
     send ROLLBACK; to the database via _session's underlying client. See algorithm
     for the exact call chain.
  3. In SurrealDbSessionTransaction.CommitAsync: before setting _isActive=false,
     check whether the session HasChanges. If true, throw InvalidOperationException
     with message: "Cannot commit: session has unsaved changes. Call SaveChangesAsync
     before CommitAsync."
  4. Add unit tests for all three fixes in the existing test files (or create new
     ones). See testing section.

  DO NOT BUILD:
  - Do not change public API signatures on ISurrealDbClient, ISurrealDbSession, or
    ISurrealDbTransaction.
  - Do not add new packages.
  - Do not refactor code outside the three touched files.
  - Do not add logging (that is Scenario 3).
</scope>

<constraints>
  - Target framework: net9.0
  - No new NuGet packages
  - File-scoped namespaces
  - ConfigureAwait(false) on all awaited calls inside library code
  - The _receiveLock must use SemaphoreSlim(1, 1) — not lock() — because
    ReceiveFullMessageAsync is async
  - ALL existing 323 unit tests must still pass after this task
</constraints>

<architecture>
  Files modified (no new files required):

  src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs
    ← Add _receiveLock field
    ← Wrap every ReceiveFullMessageAsync call with lock acquire/release

  src/SurrealDB.Client/Session/SurrealDbSession.cs
    ← SurrealDbSessionTransaction.RollbackAsync: send ROLLBACK;
    ← SurrealDbSessionTransaction.CommitAsync: guard HasChanges

  tests/SurrealDB.Client.Tests.Unit/WebSocketConcurrencyTests.cs  (NEW)
    ← Fix 1 tests

  tests/SurrealDB.Client.Tests.Unit/TransactionSemanticsTests.cs  (NEW)
    ← Fix 2 and Fix 3 tests
</architecture>

<models>
  <!-- WebSocketProtocolAdapter — field additions only -->
  private readonly SemaphoreSlim _receiveLock = new(1, 1);  // NEW

  <!-- SurrealDbSession — field already exists -->
  // _client: SurrealDbClient (already present)

  <!-- SurrealDbSessionTransaction — internal nested class in SurrealDbSession.cs -->
  // _session: SurrealDbSession (already present)
  // IsActive: bool (already present)
</models>

<algorithm>
  ═══════════════════════════════════════════════════════════════
  FIX 1 — WebSocket receive lock
  ═══════════════════════════════════════════════════════════════

  Current state: ReceiveFullMessageAsync is a private static async method.
  It is called from:
    - SendAsync (line ~150): after _sendLock.WaitAsync, calls ReceiveFullMessageAsync
    - AuthenticateAsync (line ~181): calls ReceiveFullMessageAsync directly
    - HealthCheckAsync (line ~241): calls ReceiveFullMessageAsync directly

  Pattern to apply at every call site:

    await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        var response = await ReceiveFullMessageAsync(_webSocket!, cancellationToken)
            .ConfigureAwait(false);
        // ... existing processing ...
    }
    finally
    {
        _receiveLock.Release();
    }

  NOTE: ReceiveFullMessageAsync itself does NOT need to acquire the lock —
  the lock is acquired by each caller at the call site. This keeps the static
  helper lock-free and testable in isolation.

  ═══════════════════════════════════════════════════════════════
  FIX 2 — RollbackAsync sends ROLLBACK to DB
  ═══════════════════════════════════════════════════════════════

  Current SurrealDbSessionTransaction.RollbackAsync:
    _session.Discard();
    _isActive = false;

  After fix:
    _session.Discard();
    // Send ROLLBACK to server (best-effort — if DB call fails, still mark inactive)
    try
    {
        await _session.SendRollbackAsync(cancellationToken).ConfigureAwait(false);
    }
    catch
    {
        // suppress — if connection is gone we still want to clean up local state
    }
    _isActive = false;

  Add to SurrealDbSession (internal method, NOT on ISurrealDbSession):
    internal async Task SendRollbackAsync(CancellationToken cancellationToken = default)
    {
        await _client.QueryAsync("ROLLBACK TRANSACTION;", null, cancellationToken)
            .ConfigureAwait(false);
    }

  IMPORTANT: QueryAsync already exists on SurrealDbClient and accepts
  (string surrealQL, Dictionary&lt;string,object&gt;? parameters, CancellationToken).
  No new method needed on the client.

  ═══════════════════════════════════════════════════════════════
  FIX 3 — CommitAsync guards uncommitted changes
  ═══════════════════════════════════════════════════════════════

  Current SurrealDbSessionTransaction.CommitAsync:
    if (!_isActive) throw new InvalidOperationException("Transaction is not active");
    _isActive = false;

  After fix:
    if (!_isActive)
        throw new InvalidOperationException("Transaction is not active");
    if (_session.HasChanges)
        throw new InvalidOperationException(
            "Cannot commit: session has unsaved changes. Call SaveChangesAsync before CommitAsync.");
    _isActive = false;

  HasChanges is already a public property on SurrealDbSession (returns
  _changeTracker.GetChangedEntities().Any()). The transaction already holds
  a reference to _session, so no new field needed.
</algorithm>

<edge_cases>
  1. Concurrent SendAsync + HealthCheckAsync calls on same adapter:
     Both callers will await _receiveLock. One proceeds; the other waits.
     After first completes and releases, second proceeds. No interleaving.

  2. ReceiveFullMessageAsync throws (e.g. connection dropped):
     The finally block in the caller releases _receiveLock unconditionally.
     Next caller can proceed (will also fail with connection error, but no deadlock).

  3. CancellationToken cancelled while waiting for _receiveLock:
     SemaphoreSlim.WaitAsync(cancellationToken) throws OperationCanceledException.
     The try/finally is NOT entered, so _receiveLock.Release() is NOT called.
     This is CORRECT — the semaphore was never acquired, so it must not be released.

  4. RollbackAsync when already disconnected:
     QueryAsync will throw ConnectionException. The try/catch in SendRollbackAsync's
     call site suppresses it. Local state (Discard + _isActive=false) still applies.

  5. RollbackAsync called twice:
     Second call hits "Transaction is not active" guard and throws.
     SendRollbackAsync is NOT called a second time.

  6. CommitAsync when HasChanges == true:
     Throws InvalidOperationException. _isActive remains true. User can call
     SaveChangesAsync and then CommitAsync again — that is the correct workflow.

  7. CommitAsync when HasChanges == false but no changes were ever made:
     Succeeds (this is the happy path for read-only transactions).

  8. DisposeAsync on a transaction that was neither committed nor rolled back:
     The existing code calls RollbackAsync. After Fix 2, this will attempt to
     send ROLLBACK; to the DB. The suppress-catch ensures Dispose never throws.
</edge_cases>

<testing>
  ─────────────────────────────────────────────────────────────────
  File: tests/SurrealDB.Client.Tests.Unit/WebSocketConcurrencyTests.cs
  Trait: [Trait("Category", "Unit")]
  ─────────────────────────────────────────────────────────────────

  NOTE: WebSocketProtocolAdapter is internal. Tests access it via InternalsVisibleTo.
  Use reflection to read the _receiveLock field and assert its count, OR test the
  observable behaviour with a subclass or fake. The simplest approach: use a real
  WebSocketProtocolAdapter instance and verify that _receiveLock is present and
  initialised (InitialCount == 1). Deeper concurrency correctness is validated by
  integration tests, not unit tests.

  TEST CASES:

  1. WebSocketAdapter_HasReceiveLock_Field
     Act: var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), opts);
          var field = typeof(WebSocketProtocolAdapter)
              .GetField("_receiveLock", BindingFlags.NonPublic | BindingFlags.Instance);
     Assert: field is not null
             field!.GetValue(adapter) is SemaphoreSlim sem
             sem.CurrentCount == 1

  2. WebSocketAdapter_ReceiveLock_IsReleasedAfterException
     This test verifies the finally block releases the lock even on failure.
     Since we cannot open a real WebSocket in unit tests, verify via reflection
     by calling a wrapper that acquires the lock, then confirm CurrentCount
     returns to 1 after any throw.
     Act: use reflection to get _receiveLock, manually call WaitAsync()+Release()
          to confirm round-trip (this confirms the pattern compiles and works).
     Assert: sem.CurrentCount == 1 after release

  ─────────────────────────────────────────────────────────────────
  File: tests/SurrealDB.Client.Tests.Unit/TransactionSemanticsTests.cs
  Trait: [Trait("Category", "Unit")]
  Uses: SurrealDbSession (internal, accessible via InternalsVisibleTo)
        SurrealDbClient (real instance, disconnected — same pattern as AuditAttributeTests)
  ─────────────────────────────────────────────────────────────────

  HELPER:
  private static SurrealDbSession CreateSession()
  {
      var client = new SurrealDbClient(new SurrealDbClientOptions
      {
          ConnectionString = "surreal://localhost:8000",
          Namespace = "test",
          Database = "test"
      });
      return new SurrealDbSession(client);
  }

  TEST CASES:

  3. CommitAsync_WhenNoChanges_Succeeds
     var session = CreateSession();
     var txn = session.BeginTransaction();
     // no Add/Update/Remove called
     var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
     Assert.Null(ex);

  4. CommitAsync_WhenHasUnsavedChanges_ThrowsInvalidOperationException
     var session = CreateSession();
     session.Add(new TestEntity { Id = "test:1" });
     var txn = session.BeginTransaction();
     var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
     Assert.IsType&lt;InvalidOperationException&gt;(ex);
     Assert.Contains("unsaved changes", ex!.Message);

  5. CommitAsync_AfterSaveThenChanges_ThrowsIfNotSavedAgain
     // Verifies that HasChanges is checked at commit time, not at begin time.
     var session = CreateSession();
     var txn = session.BeginTransaction();
     session.Add(new TestEntity { Id = "test:2" });
     // At this point HasChanges == true, but we attempt commit without SaveChangesAsync
     var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
     Assert.IsType&lt;InvalidOperationException&gt;(ex);

  6. RollbackAsync_CallsDiscard_LocalChangesCleared
     var session = CreateSession();
     var entity = new TestEntity { Id = "test:3" };
     session.Add(entity);
     Assert.True(session.HasChanges);
     var txn = session.BeginTransaction();
     await txn.RollbackAsync();
     Assert.False(session.HasChanges);

  7. RollbackAsync_IsActiveBecomeFalse_AfterRollback
     var session = CreateSession();
     var txn = (SurrealDbSessionTransaction)session.BeginTransaction();
     await txn.RollbackAsync();
     Assert.False(txn.IsActive);

  8. CommitAsync_WhenNotActive_ThrowsInvalidOperationException
     var session = CreateSession();
     var txn = session.BeginTransaction();
     await txn.RollbackAsync(); // deactivate
     var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
     Assert.IsType&lt;InvalidOperationException&gt;(ex);
     Assert.Contains("not active", ex!.Message);

  INNER CLASS for tests:
  private class TestEntity { public string? Id { get; set; } }
</testing>

<implementation_order>
  STEP 1 — Fix WebSocket receive lock
    Edit src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs:
      a. Add field: private readonly SemaphoreSlim _receiveLock = new(1, 1);
      b. Wrap every call to ReceiveFullMessageAsync with WaitAsync/Release in finally.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 2 — Fix RollbackAsync
    Edit src/SurrealDB.Client/Session/SurrealDbSession.cs:
      a. Add internal method SendRollbackAsync to SurrealDbSession class.
      b. Update SurrealDbSessionTransaction.RollbackAsync as per algorithm.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 3 — Fix CommitAsync
    Edit src/SurrealDB.Client/Session/SurrealDbSession.cs:
      a. Update SurrealDbSessionTransaction.CommitAsync as per algorithm.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 4 — Write tests
    Create tests/SurrealDB.Client.Tests.Unit/WebSocketConcurrencyTests.cs
    Create tests/SurrealDB.Client.Tests.Unit/TransactionSemanticsTests.cs
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: all pass, total count >= 331 (323 + 8 new).

  STEP 5 — Full regression
    Run: dotnet build
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Assert: 0 failures, solution builds cleanly.
</implementation_order>

<quality>
  - File-scoped namespaces
  - ConfigureAwait(false) on all awaited calls in library code
  - No Console.WriteLine
  - No new XML doc comments on unchanged methods
  - SemaphoreSlim must be disposed if the adapter is disposed — add to
    WebSocketProtocolAdapter.DisposeAsync if it exists, or ensure the field
    is properly cleaned up. Check if DisposeAsync already disposes _sendLock
    and apply the same pattern to _receiveLock.
</quality>

<bootstrap>
  1. dotnet --version          → must be 9.x.x
  2. dotnet build              → 0 errors
  3. dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
                               → 323 passed, 0 failed
  If any step fails, stop and diagnose before writing any code.
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code from the root of
> `C:\Projects\SurrealDB.Client`. Zero follow-up questions required.
