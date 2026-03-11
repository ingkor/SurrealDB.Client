namespace SurrealDB.Client.Resilience;

using Exceptions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

internal static class ResiliencePipelineFactory
{
    public static ResiliencePipeline<object?> Create(SurrealDbClientOptions options)
    {
        if (options.MaxRetryAttempts == 0)
            return ResiliencePipeline<object?>.Empty;

        return new ResiliencePipelineBuilder<object?>()
            .AddRetry(new RetryStrategyOptions<object?>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = options.InitialRetryDelay,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<object?>()
                    .Handle<Exception>(ex => ex is ITransientException),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<object?>
            {
                ShouldHandle = new PredicateBuilder<object?>()
                    .Handle<Exception>(ex => ex is ITransientException),
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .Build();
    }
}
