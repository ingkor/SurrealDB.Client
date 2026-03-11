# ✅ DONE — Scenario 2 — Resilience Layer

```xml
<project>
  <name>SurrealDB.Client — Resilience Layer</name>
  <description>
    Add a Polly-backed resilience pipeline to SurrealDB.Client. Every public async
    operation on SurrealDbClient must execute through a configurable retry +
    circuit-breaker pipeline. No public API signatures change. The existing
    MaxRetryAttempts and InitialRetryDelay options in SurrealDbClientOptions are
    wired up for the first time.
  </description>
  <language>C# 13 / .NET 9</language>
  <package_manager>dotnet CLI</package_manager>
  <working_directory>C:\Projects\SurrealDB.Client</working_directory>
</project>

<scope>
  BUILD:
  1. Add Polly.Core (latest stable, currently 8.x) to
     src/SurrealDB.Client/SurrealDB.Client.csproj.
  2. Create src/SurrealDB.Client/Exceptions/ITransientException.cs —
     marker interface with no members.
  3. Implement ITransientException on ConnectionException and TimeoutException.
     ValidationException and AuthenticationException must NOT implement it.
  4. Create src/SurrealDB.Client/Resilience/ResiliencePipelineFactory.cs that
     builds a Polly ResiliencePipeline&lt;object?&gt; from SurrealDbClientOptions.
  5. Add a private _pipeline field (ResiliencePipeline&lt;object?&gt;) to SurrealDbClient,
     built once in the constructor.
  6. Wrap the inner body of every public async method in SurrealDbClient through
     the pipeline using ExecuteAsync. The 9 methods are:
       ConnectAsync, DisconnectAsync, AuthenticateAsync, LogoutAsync,
       QueryAsync, CreateAsync, UpdateAsync, DeleteAsync, GetAsync,
       SelectAsync, IsConnectedAsync, MigrateAsync, RollbackAsync.
     ConnectAsync is exempt — retrying a connect that creates a new pool on
     each call would be dangerous. Skip it.
  7. Create tests/SurrealDB.Client.Tests.Unit/ResilienceTests.cs with 6 named
     tests (see &lt;testing&gt;).

  DO NOT BUILD:
  - Do not change any method signature on ISurrealDbClient or SurrealDbClient.
  - Do not add retry to ConnectAsync (exempt, see above).
  - Do not add ILogger or OpenTelemetry (that is Scenario 3).
  - Do not add Polly to any project other than SurrealDB.Client.
  - Do not introduce Polly's Microsoft.Extensions.Http.Resilience package —
    use Polly.Core only (no HttpClient dependency).
  - Do not change SurrealDbClientOptions public properties or defaults.
</scope>

<constraints>
  - Target framework: net9.0 (set in Directory.Build.props — do not override in csproj)
  - Polly.Core version: 8.* (use wildcard in PackageReference; do NOT pin patch)
  - Namespace: SurrealDB.Client.Resilience for the factory; SurrealDB.Client.Exceptions for the interface
  - File-scoped namespaces (no braced namespace blocks)
  - All async helpers must accept and forward CancellationToken
  - Pipeline instance is created once per SurrealDbClient instance in the constructor
  - The pipeline must be a ResiliencePipeline&lt;object?&gt; (generic form) so it can
    wrap both void-returning and value-returning methods uniformly
  - HARD RULE: ISurrealDbClient interface must compile unchanged after this task
  - HARD RULE: All existing 323 unit tests must still pass after this task
</constraints>

<architecture>
src/SurrealDB.Client/
├── Exceptions/
│   ├── ITransientException.cs          ← NEW: empty marker interface
│   ├── ConnectionException.cs          ← MODIFY: add ITransientException
│   ├── TimeoutException.cs             ← MODIFY: add ITransientException
│   ├── QueryException.cs               ← no change (not transient by default)
│   ├── AuthenticationException.cs      ← no change (permanent)
│   ├── ValidationException.cs          ← no change (permanent)
│   ├── SerializationException.cs       ← no change
│   └── SurrealDbException.cs           ← no change
├── Resilience/
│   └── ResiliencePipelineFactory.cs    ← NEW: builds Polly pipeline
├── SurrealDbClient.cs                  ← MODIFY: add _pipeline, wrap methods
└── SurrealDB.Client.csproj             ← MODIFY: add Polly.Core PackageReference

tests/SurrealDB.Client.Tests.Unit/
└── ResilienceTests.cs                  ← NEW: 6 unit tests
</architecture>

<models>
  <!-- ITransientException: marker, no members -->
  namespace SurrealDB.Client.Exceptions;
  public interface ITransientException { }

  <!-- ConnectionException after change -->
  public class ConnectionException : SurrealDbException, ITransientException { ... }

  <!-- TimeoutException after change -->
  public class TimeoutException : SurrealDbException, ITransientException { ... }

  <!-- ResiliencePipelineOptions (internal, used only inside the factory) -->
  // No new public model needed — reads from SurrealDbClientOptions directly:
  //   options.MaxRetryAttempts  (int, default 3)
  //   options.InitialRetryDelay (TimeSpan, default TimeSpan.FromSeconds(1))
</models>

<algorithm>
  PIPELINE CONSTRUCTION (ResiliencePipelineFactory.Create):

  Input: SurrealDbClientOptions options
  Output: ResiliencePipeline&lt;object?&gt;

  Steps:
  1. If options.MaxRetryAttempts == 0 → return ResiliencePipeline&lt;object?&gt;.Empty
     (skip building retry/CB entirely — this is how retry is disabled)

  2. Build retry strategy:
     - MaxRetryAttempts = options.MaxRetryAttempts
     - Delay type: Exponential
     - Base delay: options.InitialRetryDelay
     - Jitter: enabled (UseJitter = true)
     - ShouldHandle predicate: retry ONLY when exception is ITransientException
       i.e.: args.Outcome.Exception is ITransientException
     - Formula for attempt N (0-indexed): delay = InitialRetryDelay * 2^N + jitter
       (Polly handles this automatically when DelayGenerator is not set and
        BackoffType = Exponential is used)

  3. Build circuit-breaker strategy:
     - FailureRatio: not used — use consecutive failures mode
     - MinimumThroughput: 5 (SamplingDuration 30s window)
     - BreakDuration: TimeSpan.FromSeconds(30)
     - ShouldHandle: same predicate as retry — ITransientException only
     - When circuit opens: Polly throws BrokenCircuitException automatically

  NOTE: BrokenCircuitException is itself an exception, not ITransientException,
  so it will NOT be retried by the retry layer. Stack order in pipeline:
    outer → retry → circuit-breaker → inner operation
  This means retry sees BrokenCircuitException from CB and stops retrying.

  WRAPPING PATTERN inside SurrealDbClient:

  // Before (example — QueryAsync):
  public async Task&lt;QueryResult&lt;T&gt;&gt; QueryAsync&lt;T&gt;(string query, ...) {
      ThrowIfDisposed();
      // ... existing implementation ...
  }

  // After:
  public async Task&lt;QueryResult&lt;T&gt;&gt; QueryAsync&lt;T&gt;(string query, ...) {
      ThrowIfDisposed();
      return (QueryResult&lt;T&gt;)await _pipeline.ExecuteAsync(
          async ct =&gt; (object?)await QueryAsyncCore&lt;T&gt;(query, ..., ct),
          cancellationToken).ConfigureAwait(false);
  }

  private async Task&lt;QueryResult&lt;T&gt;&gt; QueryAsyncCore&lt;T&gt;(...) {
      // move existing body here
  }

  Apply this extract-to-Core pattern for every wrapped method.
  Name pattern: {MethodName}Core (e.g. AuthenticateAsyncCore, CreateAsyncCore).

  For void-returning methods (DisconnectAsync, LogoutAsync, DeleteAsync):
  await _pipeline.ExecuteAsync(
      async ct =&gt; { await VoidMethodCore(..., ct); return (object?)null; },
      cancellationToken).ConfigureAwait(false);
</algorithm>

<edge_cases>
  1. MaxRetryAttempts = 0 → pipeline is ResiliencePipeline.Empty → zero retries,
     zero circuit-breaker overhead, method executes exactly once.

  2. Permanent exception (ValidationException, AuthenticationException) thrown by
     inner operation → ShouldHandle returns false → exception propagates immediately
     without any retry attempt. Assert retryCount == 0 in test.

  3. Transient exception thrown MaxRetryAttempts times → all retries exhausted →
     the last exception propagates to caller. Assert retryCount == MaxRetryAttempts.

  4. Circuit breaker open → Polly throws BrokenCircuitException before calling
     inner operation → retry layer sees BrokenCircuitException which is not
     ITransientException → propagates immediately. Inner operation is never called
     after CB opens.

  5. CancellationToken cancelled mid-retry → Polly respects cancellation and throws
     OperationCanceledException without further retries. Do not catch or suppress it.

  6. ConnectAsync is NOT wrapped → it must remain unwrapped because each call
     creates a new ConnectionPool. Retrying ConnectAsync would create multiple pools.
     ConnectAsync already has its own SemaphoreSlim guard (_connectLock).

  7. Exception thrown in a wrapped method that is NOT a SurrealDbException and NOT
     ITransientException (e.g. ArgumentNullException) → ShouldHandle returns false
     → propagates immediately.

  8. MigrateAsync and RollbackAsync are wrapped. If a migration step fails with a
     ConnectionException (transient), the entire MigrateAsync is retried from the
     start. This is acceptable because migrations are idempotent by design (they
     check history table before applying).
</edge_cases>

<testing>
  File: tests/SurrealDB.Client.Tests.Unit/ResilienceTests.cs
  Trait: [Trait("Category", "Unit")]
  Framework: xUnit + Moq (already referenced in test project)

  The tests exercise ResiliencePipelineFactory in isolation using a delegate
  counter — no real SurrealDbClient needed for most tests.

  TEST CASES (all must have exactly these names):

  1. Pipeline_WhenMaxRetryAttemptsIsZero_ExecutesExactlyOnce
     Setup: options = { MaxRetryAttempts = 0, InitialRetryDelay = 1s }
            callCount = 0
            delegate: callCount++; throw new ConnectionException("fail");
     Act: pipeline.ExecuteAsync(delegate) wrapped in try/catch
     Assert: callCount == 1 (exactly once, no retries)
             caught exception is ConnectionException

  2. Pipeline_TransientException_RetriesUpToMaxAttempts
     Setup: options = { MaxRetryAttempts = 3, InitialRetryDelay = 1ms }
            callCount = 0
            delegate: callCount++; throw new ConnectionException("fail");
     Act: pipeline.ExecuteAsync(delegate) wrapped in try/catch
     Assert: callCount == 4 (1 initial + 3 retries)
             caught exception is ConnectionException

     NOTE: set InitialRetryDelay = TimeSpan.FromMilliseconds(1) to keep test fast.

  3. Pipeline_PermanentException_DoesNotRetry
     Setup: options = { MaxRetryAttempts = 3, InitialRetryDelay = 1ms }
            callCount = 0
            delegate: callCount++; throw new AuthenticationException("bad creds");
     Act: pipeline.ExecuteAsync(delegate) wrapped in try/catch
     Assert: callCount == 1 (no retries for permanent exception)
             caught exception is AuthenticationException

  4. Pipeline_TransientException_SucceedsOnSecondAttempt
     Setup: options = { MaxRetryAttempts = 3, InitialRetryDelay = 1ms }
            callCount = 0
            delegate: callCount++;
                      if (callCount == 1) throw new ConnectionException("transient");
                      return (object?)42;
     Act: result = await pipeline.ExecuteAsync(delegate)
     Assert: callCount == 2
             result is not null

  5. Pipeline_CircuitBreaker_OpensAfterThreshold
     Setup: options = { MaxRetryAttempts = 0, InitialRetryDelay = 1ms }
            (use MaxRetryAttempts=0 so retries don't interfere; CB still active)

     NOTE: With MaxRetryAttempts=0 the factory returns ResiliencePipeline.Empty
           which has NO circuit breaker. Adjust: create a factory overload or
           test the CB in isolation by constructing the pipeline directly:

     var pipeline = new ResiliencePipelineBuilder&lt;object?&gt;()
         .AddCircuitBreaker(new CircuitBreakerStrategyOptions&lt;object?&gt;
         {
             ShouldHandle = new PredicateBuilder&lt;object?&gt;()
                 .Handle&lt;ITransientException&gt;(),
             MinimumThroughput = 5,
             SamplingDuration = TimeSpan.FromSeconds(30),
             BreakDuration = TimeSpan.FromSeconds(30),
         })
         .Build();

     callCount = 0
     Trigger 5 failures (loop): try { await pipeline.ExecuteAsync(...) } catch {}
     6th call:
     Act: await pipeline.ExecuteAsync(ct =&gt; { callCount++; return ...; })
          wrapped in try/catch
     Assert: 6th call throws BrokenCircuitException (from Polly.CircuitBreaker namespace)
             callCount == 0 (inner delegate never invoked on 6th call)

  6. Pipeline_ValidationException_PropagatesImmediately
     Setup: options = { MaxRetryAttempts = 3, InitialRetayDelay = 1ms }
            callCount = 0
            delegate: callCount++; throw new ValidationException("bad input");
     Act: pipeline.ExecuteAsync(delegate) wrapped in try/catch
     Assert: callCount == 1
             caught exception is ValidationException

  HELPERS:
  - Create a private static ResiliencePipeline&lt;object?&gt; BuildPipeline(int retries, int delayMs = 1)
    that calls ResiliencePipelineFactory.Create(new SurrealDbClientOptions {
        MaxRetryAttempts = retries,
        InitialRetryDelay = TimeSpan.FromMilliseconds(delayMs)
    })
  - ResiliencePipelineFactory must be internal with [InternalsVisibleTo] already
    granting access to the test project.
</testing>

<implementation_order>
  Execute steps in this exact order. Run the verification command between steps.

  STEP 1 — Add Polly dependency
    Edit src/SurrealDB.Client/SurrealDB.Client.csproj:
    Add inside the existing &lt;ItemGroup&gt; (or a new one):
      &lt;PackageReference Include="Polly.Core" Version="8.*" /&gt;
    Run: dotnet restore src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: no errors.

  STEP 2 — Create ITransientException
    Create src/SurrealDB.Client/Exceptions/ITransientException.cs.
    Content: file-scoped namespace, empty public interface.

  STEP 3 — Tag transient exceptions
    Edit ConnectionException.cs: add ITransientException to class declaration.
    Edit TimeoutException.cs: add ITransientException to class declaration.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 4 — Create ResiliencePipelineFactory
    Create src/SurrealDB.Client/Resilience/ResiliencePipelineFactory.cs.
    Class must be: internal static class ResiliencePipelineFactory
    One public static method: Create(SurrealDbClientOptions) → ResiliencePipeline&lt;object?&gt;
    Implement algorithm from &lt;algorithm&gt; section.
    Usings needed: Polly, Polly.CircuitBreaker, Polly.Retry, SurrealDB.Client.Exceptions
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 5 — Wire pipeline into SurrealDbClient
    Add field: private readonly ResiliencePipeline&lt;object?&gt; _pipeline;
    Add to constructor (after _cache initialization):
      _pipeline = ResiliencePipelineFactory.Create(_options);
    Add using: using SurrealDB.Client.Resilience;
    For each method listed in &lt;scope&gt; (excluding ConnectAsync):
      a. Extract existing body into a private {MethodName}Core method.
      b. Replace original body with the pipeline.ExecuteAsync wrapper.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors, 0 warnings.

  STEP 6 — Write tests
    Create tests/SurrealDB.Client.Tests.Unit/ResilienceTests.cs.
    Implement all 6 named tests from &lt;testing&gt;.
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: all tests pass (total count must be &gt;= 329 — 323 existing + 6 new).

  STEP 7 — Full regression
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Assert: 0 failures.
    Run: dotnet build
    Assert: entire solution builds cleanly.
</implementation_order>

<quality>
  - File-scoped namespaces everywhere (namespace Foo.Bar; not namespace Foo.Bar { })
  - No regions added (existing regions in SurrealDbClient.cs may remain)
  - No XML doc comments added to new files (library already has them; don't duplicate)
  - No Console.WriteLine — this is a library
  - The ResiliencePipelineFactory.Create method must have a unit-testable signature:
    it must NOT read from static globals or environment variables
  - ConfigureAwait(false) on every awaited call inside library code
  - Polly's ExecuteAsync already handles CancellationToken propagation — always
    pass the caller's CancellationToken as the second argument to ExecuteAsync
  - The Core methods must be private (not internal, not protected)
  - Do not use .Result or .Wait() anywhere
</quality>

<bootstrap>
  Before writing any code, verify the environment:

  1. dotnet --version
     Expected: 9.x.x

  2. dotnet build
     Expected: Build succeeded, 0 Error(s)

  3. dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
     Expected: 323 passed, 0 failed

  If any of these fail, do not proceed — diagnose and fix first.
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code from the root of
> `C:\Projects\SurrealDB.Client`. It contains everything needed to implement
> the resilience layer without any follow-up questions.
