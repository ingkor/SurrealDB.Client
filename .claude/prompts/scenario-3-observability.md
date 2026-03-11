# ✅ DONE — Scenario 3 — Observability (Logging + Distributed Tracing + Metrics)

```xml
<project>
  <name>SurrealDB.Client — Observability Layer</name>
  <description>
    Wire structured logging (ILogger&lt;T&gt;), distributed tracing (System.Diagnostics.ActivitySource),
    and pool/cache metrics into SurrealDB.Client. No new third-party packages required —
    everything uses BCL types (Microsoft.Extensions.Logging.Abstractions,
    System.Diagnostics.DiagnosticsSource) which are already transitively available in .NET 9.

    After this task:
    - Every public operation logs at appropriate levels with structured properties.
    - Every operation creates an Activity span that integrates transparently with
      OpenTelemetry (when consumers add an OTel exporter — the library never adds one itself).
    - Pool and cache statistics are exported via System.Diagnostics.Metrics (Meter API).
    - An ILoggerFactory can be passed via SurrealDbClientOptions or a new overload.
      The factory defaults to NullLoggerFactory when not provided.
  </description>
  <language>C# 13 / .NET 9</language>
  <package_manager>dotnet CLI</package_manager>
  <working_directory>C:\Projects\SurrealDB.Client</working_directory>
</project>

<scope>
  BUILD:
  1. Add Microsoft.Extensions.Logging.Abstractions to SurrealDB.Client.csproj.
     (DiagnosticsSource / System.Diagnostics.Metrics are in-box for net9.0.)
  2. Add optional ILoggerFactory? LoggerFactory property to SurrealDbClientOptions
     (default: null → library uses NullLoggerFactory.Instance internally).
  3. Create src/SurrealDB.Client/Diagnostics/SurrealDbActivitySource.cs:
     - Public static ActivitySource named "SurrealDB.Client" version "1.0.0".
     - Static helper StartOperation(string operationName) → Activity?.
  4. Create src/SurrealDB.Client/Diagnostics/SurrealDbMetrics.cs:
     - Internal static class. One Meter named "SurrealDB.Client".
     - Instruments: pool.acquired (Counter), pool.released (Counter),
       pool.exhausted (Counter), cache.hits (Counter), cache.misses (Counter),
       operation.duration (Histogram, milliseconds).
  5. Add ILogger&lt;SurrealDbClient&gt; field to SurrealDbClient, obtained from
     options.LoggerFactory ?? NullLoggerFactory.Instance in the constructor.
  6. Instrument every public async method in SurrealDbClient:
     - Log at Debug before calling the inner operation.
     - Log at Information on success (include duration).
     - Log at Warning on transient failure.
     - Log at Error on unhandled exception (include exception object).
     - Start an Activity span; set Status to Error on exception.
     - Record duration in the operation.duration Histogram.
  7. Instrument ConnectionPool: increment pool.acquired/released/exhausted counters.
  8. Instrument MemoryQueryCache.Get/Set: increment cache.hits/cache.misses counters.
  9. Expose public static ActivitySource SurrealDbActivitySource.Source so consumers
     can subscribe (required for OpenTelemetry AddSource("SurrealDB.Client")).
  10. Add unit tests in tests/SurrealDB.Client.Tests.Unit/ObservabilityTests.cs.

  DO NOT BUILD:
  - Do not add OpenTelemetry exporters (SDK, OTLP, Jaeger) to the library csproj.
    The library only instruments; consumers wire exporters.
  - Do not add Serilog or NLog dependencies.
  - Do not change public method signatures on ISurrealDbClient.
  - Do not log credential values (passwords, tokens). Log only operation type and
    record IDs. See edge cases.
</scope>

<constraints>
  - Target: net9.0
  - New package: Microsoft.Extensions.Logging.Abstractions 9.* only
  - ActivitySource and Meter are in System.Diagnostics.DiagnosticsSource (in-box, no package)
  - File-scoped namespaces
  - ConfigureAwait(false) on all awaited calls in library code
  - Log messages must use structured logging (message template with named properties),
    never string interpolation in the log call:
      CORRECT: _logger.LogDebug("Executing query {Table}", tableName)
      WRONG:   _logger.LogDebug($"Executing query {tableName}")
  - Activities must be started with ActivitySource.StartActivity, not new Activity(...)
  - All 323 existing unit tests must still pass
</constraints>

<architecture>
  src/SurrealDB.Client/
  ├── Diagnostics/
  │   ├── SurrealDbActivitySource.cs    ← NEW: ActivitySource + StartOperation helper
  │   └── SurrealDbMetrics.cs           ← NEW: Meter + instrument fields
  ├── Caching/
  │   └── MemoryQueryCache.cs           ← MODIFY: increment hit/miss counters
  ├── Connection/
  │   └── ConnectionPool.cs             ← MODIFY: increment pool counters
  ├── SurrealDbClient.cs                ← MODIFY: add _logger, instrument methods
  ├── SurrealDbClientOptions.cs         ← MODIFY: add LoggerFactory property
  └── SurrealDB.Client.csproj           ← MODIFY: add logging.abstractions ref

  tests/SurrealDB.Client.Tests.Unit/
  └── ObservabilityTests.cs             ← NEW: 8 unit tests
</architecture>

<models>
  <!-- SurrealDbActivitySource -->
  public static class SurrealDbActivitySource
  {
      public static readonly ActivitySource Source =
          new ActivitySource("SurrealDB.Client", "1.0.0");

      // Returns null if no listener is attached (standard ActivitySource behaviour)
      internal static Activity? StartOperation(string operationName)
          => Source.StartActivity(operationName, ActivityKind.Client);
  }

  <!-- SurrealDbMetrics (internal) -->
  internal static class SurrealDbMetrics
  {
      private static readonly Meter _meter = new("SurrealDB.Client", "1.0.0");

      internal static readonly Counter&lt;long&gt; PoolAcquired =
          _meter.CreateCounter&lt;long&gt;("surreal.pool.acquired", "connections");
      internal static readonly Counter&lt;long&gt; PoolReleased =
          _meter.CreateCounter&lt;long&gt;("surreal.pool.released", "connections");
      internal static readonly Counter&lt;long&gt; PoolExhausted =
          _meter.CreateCounter&lt;long&gt;("surreal.pool.exhausted", "connections");
      internal static readonly Counter&lt;long&gt; CacheHits =
          _meter.CreateCounter&lt;long&gt;("surreal.cache.hits", "requests");
      internal static readonly Counter&lt;long&gt; CacheMisses =
          _meter.CreateCounter&lt;long&gt;("surreal.cache.misses", "requests");
      internal static readonly Histogram&lt;double&gt; OperationDuration =
          _meter.CreateHistogram&lt;double&gt;("surreal.operation.duration", "ms");
  }

  <!-- SurrealDbClientOptions additions -->
  // Add property:
  public ILoggerFactory? LoggerFactory { get; set; } = null;
  // Validate() must NOT validate this field — null is valid (means NullLoggerFactory)
</models>

<algorithm>
  INSTRUMENTATION PATTERN (apply to each public async method in SurrealDbClient):

  public async Task&lt;T?&gt; GetAsync&lt;T&gt;(string recordId, CancellationToken ct = default)
  {
      ThrowIfDisposed();
      using var activity = SurrealDbActivitySource.StartOperation("surreal.get");
      activity?.SetTag("surreal.record_id", recordId);
      activity?.SetTag("db.system", "surrealdb");

      var sw = System.Diagnostics.Stopwatch.StartNew();
      try
      {
          _logger.LogDebug("Getting record {RecordId}", recordId);
          var result = await GetAsyncCore&lt;T&gt;(recordId, ct).ConfigureAwait(false);
          sw.Stop();
          _logger.LogDebug("Got record {RecordId} in {ElapsedMs}ms", recordId, sw.ElapsedMilliseconds);
          SurrealDbMetrics.OperationDuration.Record(sw.Elapsed.TotalMilliseconds,
              new KeyValuePair&lt;string, object?&gt;("operation", "get"));
          return result;
      }
      catch (Exception ex)
      {
          sw.Stop();
          activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
          if (ex is ITransientException)
              _logger.LogWarning(ex, "Transient error getting record {RecordId}", recordId);
          else
              _logger.LogError(ex, "Error getting record {RecordId}", recordId);
          SurrealDbMetrics.OperationDuration.Record(sw.Elapsed.TotalMilliseconds,
              new KeyValuePair&lt;string, object?&gt;("operation", "get"),
              new KeyValuePair&lt;string, object?&gt;("error", "true"));
          throw;
      }
  }

  Apply the same pattern to:
    ConnectAsync, DisconnectAsync, AuthenticateAsync (both overloads), LogoutAsync,
    QueryAsync (both overloads), CreateAsync (both overloads), GetAsync, SelectAsync,
    UpdateAsync, DeleteAsync, UpsertAsync, IsConnectedAsync, MigrateAsync, RollbackAsync.

  Operation name convention (surreal.{verb}):
    ConnectAsync        → "surreal.connect"
    DisconnectAsync     → "surreal.disconnect"
    AuthenticateAsync   → "surreal.authenticate"
    LogoutAsync         → "surreal.logout"
    QueryAsync          → "surreal.query"
    CreateAsync         → "surreal.create"
    GetAsync            → "surreal.get"
    SelectAsync         → "surreal.select"
    UpdateAsync         → "surreal.update"
    DeleteAsync         → "surreal.delete"
    UpsertAsync         → "surreal.upsert"
    IsConnectedAsync    → "surreal.health"
    MigrateAsync        → "surreal.migrate"
    RollbackAsync       → "surreal.rollback"

  TAGS to set on Activity (all optional — do not throw if activity is null):
    db.system = "surrealdb"
    surreal.table   (where applicable, e.g. CreateAsync table param)
    surreal.record_id (where applicable)
    surreal.operation = the verb above

  SECURITY — NEVER tag these values:
    - username, password, token (AuthenticateAsync params)
    - query string content (QueryAsync surrealQL) — could contain sensitive data
    - response data

  POOL INSTRUMENTATION in ConnectionPool.cs:
    AcquireAsync:  on success → SurrealDbMetrics.PoolAcquired.Add(1)
                   on timeout → SurrealDbMetrics.PoolExhausted.Add(1), then throw
    ReleaseAsync:  → SurrealDbMetrics.PoolReleased.Add(1)

  CACHE INSTRUMENTATION in MemoryQueryCache.cs:
    Get: hit path  → SurrealDbMetrics.CacheHits.Add(1)   (replace RecordHit())
         miss path → SurrealDbMetrics.CacheMisses.Add(1) (replace RecordMiss())
    Keep existing _hits/_misses fields + lock for GetStatistics() — they still power
    GetStatistics(). The Metrics calls are in addition, not a replacement.
</algorithm>

<edge_cases>
  1. No listener attached to ActivitySource → StartActivity returns null.
     Every activity?.SetTag / activity?.SetStatus call must use the null-conditional
     operator. Never call methods on a null Activity.

  2. ILoggerFactory not set (null) → use NullLoggerFactory.Instance.
     NullLoggerFactory produces NullLogger which discards all calls cheaply.
     No null checks needed on _logger itself.

  3. AuthenticateAsync — NEVER log username or password.
     Correct: _logger.LogDebug("Authenticating with username credentials")
     Wrong:   _logger.LogDebug("Authenticating as {User}", username)

  4. QueryAsync — NEVER log surrealQL string. It may contain sensitive predicates.
     Correct: _logger.LogDebug("Executing SurrealQL query")
     Wrong:   _logger.LogDebug("Executing {Query}", surrealQL)

  5. Exception in instrumentation code (e.g. Meter.CreateCounter throws):
     Must not surface to callers. Meter/ActivitySource creation happens in static
     initializers — if they fail, the static field is null. Wrap all metric
     recording calls with a null guard:
       SurrealDbMetrics.PoolAcquired?.Add(1)
     OR initialize Meter inside a try/catch and fall back to NullMeter.

  6. Stopwatch — always call sw.Stop() before any early return or exception path.
     Use try/finally to guarantee this.

  7. LoggerFactory supplied but CreateLogger throws — unlikely but possible.
     Catch in SurrealDbClient constructor, log to Console.Error (one-time only),
     and fall back to NullLoggerFactory.

  8. ConnectAsync creates a new pool on each call. If ConnectAsync is called twice,
     two sets of pool.acquired metrics may be emitted. This is correct — each
     pool acquisition is a real event.
</edge_cases>

<testing>
  File: tests/SurrealDB.Client.Tests.Unit/ObservabilityTests.cs
  Trait: [Trait("Category", "Unit")]

  Strategy: test SurrealDbActivitySource and SurrealDbMetrics in isolation.
  Do NOT mock SurrealDbClient internals — test the diagnostic infrastructure directly.

  TEST CASES:

  1. ActivitySource_HasCorrectName
     Assert.Equal("SurrealDB.Client", SurrealDbActivitySource.Source.Name)

  2. ActivitySource_StartOperation_ReturnsNullWhenNoListener
     // No ActivityListener attached → StartActivity returns null by default
     var activity = SurrealDbActivitySource.StartOperation("surreal.test");
     Assert.Null(activity);   // correct behaviour when no OTel listener

  3. ActivitySource_StartOperation_ReturnsActivityWhenListenerAttached
     Setup: ActivityListener attached to "SurrealDB.Client" source, Sample=always
     var activity = SurrealDbActivitySource.StartOperation("surreal.test");
     Assert.NotNull(activity);
     Assert.Equal("surreal.test", activity!.OperationName);
     Cleanup: listener.Dispose()

  4. Metrics_PoolCounters_AreCreated
     Assert.NotNull(SurrealDbMetrics.PoolAcquired)
     Assert.NotNull(SurrealDbMetrics.PoolReleased)
     Assert.NotNull(SurrealDbMetrics.PoolExhausted)

  5. Metrics_CacheCounters_AreCreated
     Assert.NotNull(SurrealDbMetrics.CacheHits)
     Assert.NotNull(SurrealDbMetrics.CacheMisses)

  6. Metrics_OperationDuration_IsHistogram
     Assert.NotNull(SurrealDbMetrics.OperationDuration)

  7. SurrealDbClient_AcceptsLoggerFactory_ViaOptions
     // Verify no exception when LoggerFactory is set
     var factory = LoggerFactory.Create(b => b.AddConsole());
     var opts = new SurrealDbClientOptions
     {
         ConnectionString = "surreal://localhost:8000",
         Namespace = "test", Database = "test",
         LoggerFactory = factory
     };
     var ex = Record.Exception(() => new SurrealDbClient(opts));
     Assert.Null(ex);

  8. SurrealDbClient_NullLoggerFactory_UsesNullLoggerByDefault
     var opts = new SurrealDbClientOptions
     {
         ConnectionString = "surreal://localhost:8000",
         Namespace = "test", Database = "test"
         // LoggerFactory not set
     };
     var ex = Record.Exception(() => new SurrealDbClient(opts));
     Assert.Null(ex);

  HELPERS:
  Add ActivityListener helper:
  private static ActivityListener CreateAlwaysSampleListener() => new()
  {
      ShouldListenTo = s => s.Name == "SurrealDB.Client",
      Sample = (ref ActivityCreationOptions&lt;ActivityContext&gt; _) => ActivitySamplingResult.AllData,
      ActivityStarted = _ => { },
      ActivityStopped = _ => { }
  };
  // Register with: ActivitySource.AddActivityListener(listener)
</testing>

<implementation_order>
  STEP 1 — Add logging package
    Edit SurrealDB.Client.csproj:
      &lt;PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.*" /&gt;
    Run: dotnet restore src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 2 — Create Diagnostics classes
    Create src/SurrealDB.Client/Diagnostics/SurrealDbActivitySource.cs
    Create src/SurrealDB.Client/Diagnostics/SurrealDbMetrics.cs
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 3 — Update SurrealDbClientOptions
    Add LoggerFactory property.
    Run: dotnet build — verify 0 errors.

  STEP 4 — Instrument SurrealDbClient
    Add _logger field, initialize from options.
    Apply instrumentation pattern to all public async methods.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 5 — Instrument ConnectionPool and MemoryQueryCache
    Add metric calls as per algorithm.
    Run: dotnet build — verify 0 errors.

  STEP 6 — Write tests
    Create tests/SurrealDB.Client.Tests.Unit/ObservabilityTests.cs
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: all pass, total >= 331 (323 + 8 new).

  STEP 7 — Full regression
    Run: dotnet build
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Assert: 0 failures.
</implementation_order>

<quality>
  - Structured log messages only (named properties, no interpolation in log calls)
  - Activity tags: use lowercase dot-separated names per OTel semantic conventions
  - Never log sensitive data: passwords, tokens, query strings, result payloads
  - ConfigureAwait(false) everywhere in library code
  - File-scoped namespaces
  - Meter and ActivitySource are singletons (static fields) — do NOT create new
    instances per client instance. This is critical: multiple clients share the
    same named source, which is how OTel works.
  - Stopwatch.StartNew() before try, Stop() in finally — never rely on sw.Elapsed
    after an exception without stopping
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
> `C:\Projects\SurrealDB.Client`. Complete observability layer without any
> follow-up questions.
