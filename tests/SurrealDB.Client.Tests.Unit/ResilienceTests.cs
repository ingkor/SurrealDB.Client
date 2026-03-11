namespace SurrealDB.Client.Tests.Unit;

using Polly.CircuitBreaker;
using SurrealDB.Client.Exceptions;
using SurrealDB.Client.Resilience;
using Polly;
using Xunit;

[Trait("Category", "Unit")]
public class ResilienceTests
{
    private static ResiliencePipeline<object?> BuildPipeline(int retries, int delayMs = 1)
        => ResiliencePipelineFactory.Create(new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            MaxRetryAttempts = retries,
            InitialRetryDelay = TimeSpan.FromMilliseconds(delayMs)
        });

    private static ValueTask<object?> Throw(Exception ex) => throw ex;

    [Fact]
    public async Task Pipeline_WhenMaxRetryAttemptsIsZero_ExecutesExactlyOnce()
    {
        var pipeline = BuildPipeline(0);
        var callCount = 0;

        var ex = await Record.ExceptionAsync(async () =>
            await pipeline.ExecuteAsync<object?>(ct =>
            {
                callCount++;
                return Throw(new ConnectionException("fail"));
            }, CancellationToken.None));

        Assert.Equal(1, callCount);
        Assert.IsType<ConnectionException>(ex);
    }

    [Fact]
    public async Task Pipeline_TransientException_RetriesUpToMaxAttempts()
    {
        var pipeline = BuildPipeline(3);
        var callCount = 0;

        var ex = await Record.ExceptionAsync(async () =>
            await pipeline.ExecuteAsync<object?>(ct =>
            {
                callCount++;
                return Throw(new ConnectionException("fail"));
            }, CancellationToken.None));

        Assert.Equal(4, callCount); // 1 initial + 3 retries
        Assert.IsType<ConnectionException>(ex);
    }

    [Fact]
    public async Task Pipeline_PermanentException_DoesNotRetry()
    {
        var pipeline = BuildPipeline(3);
        var callCount = 0;

        var ex = await Record.ExceptionAsync(async () =>
            await pipeline.ExecuteAsync<object?>(ct =>
            {
                callCount++;
                return Throw(new AuthenticationException("bad creds"));
            }, CancellationToken.None));

        Assert.Equal(1, callCount);
        Assert.IsType<AuthenticationException>(ex);
    }

    [Fact]
    public async Task Pipeline_TransientException_SucceedsOnSecondAttempt()
    {
        var pipeline = BuildPipeline(3);
        var callCount = 0;

        var result = await pipeline.ExecuteAsync<object?>(ct =>
        {
            callCount++;
            if (callCount == 1) return Throw(new ConnectionException("transient"));
            return ValueTask.FromResult((object?)42);
        }, CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Pipeline_CircuitBreaker_OpensAfterThreshold()
    {
        var pipeline = new ResiliencePipelineBuilder<object?>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<object?>
            {
                ShouldHandle = new PredicateBuilder<object?>()
                    .Handle<Exception>(ex => ex is ITransientException),
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .Build();

        // Trip the circuit breaker with 5 failures
        for (var i = 0; i < 5; i++)
        {
            try
            {
                await pipeline.ExecuteAsync<object?>(ct =>
                    Throw(new ConnectionException("fail")), CancellationToken.None);
            }
            catch { }
        }

        // 6th call should be rejected by circuit breaker
        var delegateCalled = false;
        var ex = await Record.ExceptionAsync(async () =>
            await pipeline.ExecuteAsync<object?>(ct =>
            {
                delegateCalled = true;
                return ValueTask.FromResult((object?)null);
            }, CancellationToken.None));

        Assert.False(delegateCalled);
        Assert.IsAssignableFrom<BrokenCircuitException>(ex);
    }

    [Fact]
    public async Task Pipeline_ValidationException_PropagatesImmediately()
    {
        var pipeline = BuildPipeline(3);
        var callCount = 0;

        var ex = await Record.ExceptionAsync(async () =>
            await pipeline.ExecuteAsync<object?>(ct =>
            {
                callCount++;
                return Throw(new ValidationException("bad input"));
            }, CancellationToken.None));

        Assert.Equal(1, callCount);
        Assert.IsType<ValidationException>(ex);
    }
}
