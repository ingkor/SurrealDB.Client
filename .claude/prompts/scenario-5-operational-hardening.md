# ✅ DONE — Scenario 5 — Operational Hardening

```xml
<project>
  <name>SurrealDB.Client — Operational Hardening</name>
  <description>
    Four targeted hardening fixes that prevent silent resource exhaustion and
    incorrect timeout enforcement in production:

    Fix A — MemoryQueryCache LRU eviction:
      Add a MaxItems capacity limit (default 10,000). When the cache exceeds
      MaxItems, evict the Least Recently Used (LRU) entry before inserting
      a new one. Prevents unbounded memory growth in long-running applications.

    Fix B — Background health check task:
      EnableHealthChecks and HealthCheckInterval exist in SurrealDbClientOptions
      but are completely unimplemented. Wire them up: when EnableHealthChecks=true,
      start a background Timer that calls IsConnectedAsync on the interval and
      re-connects if the result is false and EnableAutoReconnect=true.

    Fix C — Per-operation CommandTimeout enforcement:
      CommandTimeout (default 120s) is defined in options but not enforced for
      any operation. Wrap every public async method in SurrealDbClient with a
      linked CancellationTokenSource that cancels after CommandTimeout, merged
      with the caller's token.

    Fix D — Connection string URI validation at construction time:
      SurrealDbClientOptions.Validate() already checks for embedded credentials.
      Add a Uri.TryCreate check to catch malformed connection strings early
      (e.g. "not a url", "surreal//missing-colon") and throw ValidationException
      immediately, before ConnectAsync is called.
  </description>
  <language>C# 13 / .NET 9</language>
  <package_manager>dotnet CLI</package_manager>
  <working_directory>C:\Projects\SurrealDB.Client</working_directory>
</project>

<scope>
  BUILD:
  A1. Add int CacheMaxItems property to SurrealDbClientOptions (default: 10_000).
  A2. Add LRU eviction to MemoryQueryCache: track access order with a
      LinkedList&lt;string&gt; + Dictionary&lt;string, LinkedListNode&lt;string&gt;&gt; inside a lock.
      On every Get hit, move the accessed key to the front of the list.
      On every Set, if Count == CacheMaxItems, remove the last (LRU) entry.
  B1. Add _healthCheckTimer (System.Threading.Timer) field to SurrealDbClient.
  B2. Start the timer in ConnectAsync when EnableHealthChecks == true.
      The timer callback calls IsConnectedAsync; if false and EnableAutoReconnect,
      calls ConnectAsync (in a fire-and-forget Task.Run, suppressing exceptions).
  B3. Dispose the timer in SurrealDbClient.DisposeAsync.
  C1. Create a private CreateLinkedTimeout(CancellationToken callerToken) helper
      in SurrealDbClient that returns a linked CancellationTokenSource capped at
      CommandTimeout.
  C2. Apply the helper to every public async method body (not ConnectAsync — it
      has ConnectionTimeout already enforced by the pool).
  D1. In SurrealDbClientOptions.Validate(), after the existing null check for
      ConnectionString, add Uri.TryCreate validation for the surreal:// / http:// /
      ws:// / wss:// / https:// schemes. Unknown schemes are rejected.

  DO NOT BUILD:
  - Do not change public interface signatures on ISurrealDbClient.
  - Do not add new packages.
  - Do not implement cache persistence (in-memory only).
  - Do not implement distributed cache (that is a separate concern).
  - The background health check must never throw from the timer callback.
</scope>

<constraints>
  - Target: net9.0, no new packages
  - File-scoped namespaces
  - ConfigureAwait(false) in all library async paths
  - LRU eviction must be thread-safe — all LinkedList operations inside lock(_lruLock)
  - The health check timer period is HealthCheckInterval (default 30s).
      Due timer: first fire at HealthCheckInterval, then every HealthCheckInterval.
  - CommandTimeout linked CTS must be disposed in a finally block to prevent leaks
  - All 323 existing unit tests must still pass
  - CacheMaxItems = 0 means unlimited (existing behaviour, for backward compat).
      Only enforce the limit when CacheMaxItems > 0.
</constraints>

<architecture>
  src/SurrealDB.Client/
  ├── Caching/
  │   └── MemoryQueryCache.cs           ← MODIFY: add LRU eviction
  ├── SurrealDbClient.cs                ← MODIFY: health timer, command timeout helper
  └── SurrealDbClientOptions.cs         ← MODIFY: CacheMaxItems, URI validation

  tests/SurrealDB.Client.Tests.Unit/
  └── OperationalHardeningTests.cs      ← NEW: 12 unit tests
</architecture>

<models>
  <!-- SurrealDbClientOptions additions -->
  /// Maximum number of entries in the in-memory query cache.
  /// 0 = unlimited (backward compatible default behaviour).
  /// Recommended production value: 10_000.
  public int CacheMaxItems { get; set; } = 0;

  <!-- MemoryQueryCache — internal additions (not on IQueryCache) -->
  private readonly LinkedList&lt;string&gt; _lruOrder = new();
  private readonly Dictionary&lt;string, LinkedListNode&lt;string&gt;&gt; _lruNodes = new();
  private readonly object _lruLock = new();
  private int _maxItems; // set from SurrealDbClientOptions.CacheMaxItems in constructor

  // MemoryQueryCache needs a new constructor overload:
  public MemoryQueryCache(int maxItems = 0) { _maxItems = maxItems; }

  // SurrealDbClient creates it as:
  _cache = new MemoryQueryCache(_options.CacheMaxItems);

  <!-- SurrealDbClient — new fields -->
  private Timer? _healthCheckTimer;
</models>

<algorithm>
  ═══════════════════════════════════════════════════════════════
  FIX A — LRU eviction in MemoryQueryCache
  ═══════════════════════════════════════════════════════════════

  Set(key, value, expiration):
    lock (_lruLock)
    {
        if (_lruNodes.TryGetValue(key, out var existingNode))
        {
            // Update existing entry — move to front
            _lruOrder.Remove(existingNode);
        }
        else if (_maxItems > 0 && _cache.Count >= _maxItems)
        {
            // Evict LRU (last node)
            var lruKey = _lruOrder.Last!.Value;
            _lruOrder.RemoveLast();
            _lruNodes.Remove(lruKey);
            _cache.TryRemove(lruKey, out _);
        }

        var node = _lruOrder.AddFirst(key);
        _lruNodes[key] = node;
    }
    // Then upsert _cache as before (ConcurrentDictionary.AddOrUpdate)

  Get(key):
    If hit:
      lock (_lruLock)
      {
          if (_lruNodes.TryGetValue(key, out var node))
          {
              _lruOrder.Remove(node);
              _lruNodes[key] = _lruOrder.AddFirst(key);
          }
      }
      RecordHit(); return value;
    If miss or expired:
      lock (_lruLock) { _lruNodes.Remove(key); // clean up LRU metadata }
      RecordMiss(); return default;

  Remove(key):
    _cache.TryRemove(key, out _);
    lock (_lruLock) { if (_lruNodes.Remove(key, out var node)) _lruOrder.Remove(node); }

  Clear():
    _cache.Clear();
    lock (_lruLock) { _lruOrder.Clear(); _lruNodes.Clear(); }

  NOTE: The existing _cache (ConcurrentDictionary) remains as the primary store.
  The LinkedList is purely for eviction order tracking. The two must stay in sync
  via the _lruLock. _cache is accessed lock-free outside eviction-critical sections.

  ═══════════════════════════════════════════════════════════════
  FIX B — Background health check timer
  ═══════════════════════════════════════════════════════════════

  In SurrealDbClient.ConnectAsync (after _isConnected = true):

  if (_options.EnableHealthChecks)
  {
      _healthCheckTimer?.Dispose();
      _healthCheckTimer = new Timer(
          async _ =>
          {
              try
              {
                  var healthy = await IsConnectedAsync().ConfigureAwait(false);
                  if (!healthy && _options.EnableAutoReconnect && !_disposed)
                  {
                      _ = Task.Run(async () =>
                      {
                          try { await ConnectAsync().ConfigureAwait(false); }
                          catch { /* suppress — next tick will retry */ }
                      });
                  }
              }
              catch { /* suppress — timer callback must never throw */ }
          },
          state: null,
          dueTime: _options.HealthCheckInterval,
          period: _options.HealthCheckInterval);
  }

  In DisposeAsync:
  _healthCheckTimer?.Dispose();
  _healthCheckTimer = null;

  ═══════════════════════════════════════════════════════════════
  FIX C — Per-operation CommandTimeout enforcement
  ═══════════════════════════════════════════════════════════════

  Add private helper to SurrealDbClient:

  private CancellationTokenSource CreateLinkedTimeout(CancellationToken callerToken)
      => CancellationTokenSource.CreateLinkedTokenSource(
             callerToken,
             new CancellationTokenSource(_options.CommandTimeout).Token);

  Usage pattern in every wrapped method (after ThrowIfDisposed):

  using var cts = CreateLinkedTimeout(cancellationToken);
  var ct = cts.Token;
  // use ct instead of cancellationToken for all downstream calls

  Apply to: QueryAsync (both), CreateAsync (both), GetAsync, SelectAsync,
            UpdateAsync, DeleteAsync, UpsertAsync, AuthenticateAsync (both),
            LogoutAsync, IsConnectedAsync, MigrateAsync, RollbackAsync,
            CreateManyAsync, UpdateManyAsync, DeleteManyAsync (if Scenario 4 is done).
  Exempt: ConnectAsync (uses ConnectionTimeout enforced by pool),
          DisconnectAsync (cleanup path, should not timeout).

  ═══════════════════════════════════════════════════════════════
  FIX D — URI validation in SurrealDbClientOptions.Validate()
  ═══════════════════════════════════════════════════════════════

  Add after the existing ConnectionString null check:

  private static readonly HashSet&lt;string&gt; _validSchemes =
      new(StringComparer.OrdinalIgnoreCase)
      { "surreal", "http", "https", "ws", "wss" };

  // In Validate():
  if (!Uri.TryCreate(ConnectionString, UriKind.Absolute, out var uri))
      throw new ValidationException(
          $"ConnectionString '{ConnectionString}' is not a valid absolute URI.");

  if (!_validSchemes.Contains(uri.Scheme))
      throw new ValidationException(
          $"ConnectionString scheme '{uri.Scheme}' is not supported. " +
          $"Valid schemes: {string.Join(", ", _validSchemes)}.");
</algorithm>

<edge_cases>
  A1. CacheMaxItems = 0 → no eviction, existing unlimited behaviour. LRU tracking
      still runs (tracking is cheap) but eviction branch never fires. Do not skip
      LRU tracking — Clear() and Remove() must still maintain consistency.

  A2. Cache entry expires on Get → remove from both _cache and _lruNodes in the
      expired path. Otherwise _lruNodes grows unboundedly.

  A3. Set() called for an existing key → update value in _cache AND move LRU node
      to front. Do not count this as a new entry against MaxItems.

  B1. ConnectAsync called while health timer already running (e.g. reconnect loop):
      Dispose existing timer before creating a new one (_healthCheckTimer?.Dispose()).

  B2. DisposeAsync called while health check callback is executing:
      Timer.Dispose() cancels pending callbacks but an in-flight callback can still
      run briefly. The _disposed flag check inside the callback prevents re-connect
      attempts after disposal.

  B3. EnableHealthChecks = false → timer is never created. No overhead.

  C1. CancellationTokenSource.CreateLinkedTokenSource creates a new CTS.
      The inner timeout CTS (new CancellationTokenSource(_options.CommandTimeout))
      is created inline. It must be disposed too. Use:
        using var timeoutCts = new CancellationTokenSource(_options.CommandTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken, timeoutCts.Token);
      This ensures both are disposed.

  C2. CommandTimeout expires → OperationCanceledException is thrown with the linked
      token. Callers cannot distinguish timeout from caller-initiated cancellation
      by the exception alone (both use the same exception type). This is acceptable
      and matches HttpClient behaviour.

  D1. ConnectionString = "surreal://localhost:8000" → Uri.TryCreate succeeds,
      scheme "surreal" is in the allowed set. Passes.

  D2. ConnectionString = "not a url" → Uri.TryCreate returns false → throws
      ValidationException immediately at construction time.

  D3. ConnectionString = "ftp://localhost:8000" → scheme "ftp" not in allowed set
      → throws ValidationException.

  D4. ConnectionString = "surreal//missing-colon" → Uri.TryCreate returns false
      (no ":" after scheme) → throws ValidationException.
</edge_cases>

<testing>
  File: tests/SurrealDB.Client.Tests.Unit/OperationalHardeningTests.cs
  Trait: [Trait("Category", "Unit")]
  Framework: xUnit (no Moq needed for these tests)

  TEST CASES:

  Fix A — LRU Cache:

  1. Cache_LRU_EvictsLeastRecentlyUsed_WhenAtCapacity
     var cache = new MemoryQueryCache(maxItems: 3);
     cache.Set("a", "A"); cache.Set("b", "B"); cache.Set("c", "C");
     _ = cache.Get&lt;string&gt;("a"); // access "a" → it becomes MRU
     cache.Set("d", "D");        // evict LRU which is "b" (oldest unaccessed)
     Assert.Null(cache.Get&lt;string&gt;("b")); // evicted
     Assert.NotNull(cache.Get&lt;string&gt;("a")); // still present
     Assert.NotNull(cache.Get&lt;string&gt;("c")); // still present
     Assert.NotNull(cache.Get&lt;string&gt;("d")); // newly added

  2. Cache_LRU_ZeroMaxItems_NoEviction
     var cache = new MemoryQueryCache(maxItems: 0);
     for (int i = 0; i &lt; 100; i++) cache.Set($"key{i}", i);
     Assert.Equal(100, cache.GetStatistics().ItemCount);

  3. Cache_LRU_UpdateExistingKey_DoesNotIncrementCount
     var cache = new MemoryQueryCache(maxItems: 3);
     cache.Set("a", "A1"); cache.Set("b", "B"); cache.Set("c", "C");
     cache.Set("a", "A2"); // update, not new entry — count stays 3
     Assert.Equal(3, cache.GetStatistics().ItemCount);
     Assert.Equal("A2", cache.Get&lt;string&gt;("a"));

  4. Cache_LRU_Clear_ResetsEvictionState
     var cache = new MemoryQueryCache(maxItems: 2);
     cache.Set("a", "A"); cache.Set("b", "B");
     cache.Clear();
     cache.Set("c", "C"); cache.Set("d", "D"); // should work without evicting
     Assert.Equal(2, cache.GetStatistics().ItemCount);

  Fix C — Command Timeout:

  5. SurrealDbClientOptions_CommandTimeout_DefaultIs120Seconds
     var opts = new SurrealDbClientOptions { ConnectionString = "surreal://localhost:8000",
         Namespace = "test", Database = "test" };
     Assert.Equal(TimeSpan.FromSeconds(120), opts.CommandTimeout);

  Fix D — URI Validation:

  6. Validate_ValidSurrealScheme_DoesNotThrow
     var opts = new SurrealDbClientOptions { ConnectionString = "surreal://localhost:8000",
         Namespace = "test", Database = "test" };
     var ex = Record.Exception(() => opts.Validate());
     Assert.Null(ex);

  7. Validate_ValidWssScheme_DoesNotThrow
     var opts = new SurrealDbClientOptions { ConnectionString = "wss://db.example.com:8000",
         Namespace = "test", Database = "test" };
     var ex = Record.Exception(() => opts.Validate());
     Assert.Null(ex);

  8. Validate_MalformedUri_ThrowsValidationException
     var opts = new SurrealDbClientOptions { ConnectionString = "not a url",
         Namespace = "test", Database = "test" };
     Assert.Throws&lt;ValidationException&gt;(() => opts.Validate());

  9. Validate_UnsupportedScheme_ThrowsValidationException
     var opts = new SurrealDbClientOptions { ConnectionString = "ftp://localhost:8000",
         Namespace = "test", Database = "test" };
     var ex = Assert.Throws&lt;ValidationException&gt;(() => opts.Validate());
     Assert.Contains("ftp", ex.Message);

  10. Validate_MissingColonInScheme_ThrowsValidationException
      var opts = new SurrealDbClientOptions { ConnectionString = "surreal//localhost:8000",
          Namespace = "test", Database = "test" };
      Assert.Throws&lt;ValidationException&gt;(() => opts.Validate());

  Fix B — Health check (structural, not behavioural — no live DB):

  11. SurrealDbClientOptions_EnableHealthChecks_DefaultTrue
      var opts = new SurrealDbClientOptions { ConnectionString = "surreal://localhost:8000",
          Namespace = "test", Database = "test" };
      Assert.True(opts.EnableHealthChecks);

  12. SurrealDbClientOptions_HealthCheckInterval_DefaultThirtySeconds
      var opts = new SurrealDbClientOptions { ConnectionString = "surreal://localhost:8000",
          Namespace = "test", Database = "test" };
      Assert.Equal(TimeSpan.FromSeconds(30), opts.HealthCheckInterval);
</testing>

<implementation_order>
  STEP 1 — Fix D: URI validation
    Edit SurrealDbClientOptions.cs: add _validSchemes + URI check in Validate().
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: existing tests still pass (none use invalid URIs).

  STEP 2 — Fix A: LRU cache
    Edit SurrealDbClientOptions.cs: add CacheMaxItems property.
    Edit MemoryQueryCache.cs: add maxItems constructor param, LinkedList LRU tracking.
    Edit SurrealDbClient.cs: pass _options.CacheMaxItems to MemoryQueryCache constructor.
    Run: dotnet build — 0 errors.
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: all existing tests pass.

  STEP 3 — Fix C: Command timeout
    Edit SurrealDbClient.cs: add CreateLinkedTimeout helper, apply to all methods.
    Run: dotnet build — 0 errors.
    Run: dotnet test — all pass.

  STEP 4 — Fix B: Health check timer
    Edit SurrealDbClient.cs: add _healthCheckTimer, start in ConnectAsync,
    dispose in DisposeAsync.
    Run: dotnet build — 0 errors.

  STEP 5 — Write tests
    Create tests/SurrealDB.Client.Tests.Unit/OperationalHardeningTests.cs
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: all pass, total >= 335 (323 + 12 new).

  STEP 6 — Full regression
    Run: dotnet build
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Assert: 0 failures.
</implementation_order>

<quality>
  - File-scoped namespaces
  - ConfigureAwait(false) in all library async paths
  - No Console.WriteLine
  - LRU lock granularity: _lruLock covers only LinkedList/Dictionary operations,
    never wraps ConcurrentDictionary calls (which are lock-free by design)
  - The health check timer callback is an async lambda — wrap entirely in try/catch
    to ensure no unhandled exception escapes the timer thread pool
  - Dispose the linked CTS (Fix C): always use 'using var' on both the timeout
    CTS and the linked CTS — never rely on GC to release WaitHandles
</quality>

<bootstrap>
  1. dotnet --version          → 9.x.x
  2. dotnet build              → 0 errors
  3. dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
                               → 323 passed, 0 failed
  Stop and diagnose if any step fails.
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code from the root of
> `C:\Projects\SurrealDB.Client`. Implements all four operational hardening
> fixes with zero follow-up questions.
