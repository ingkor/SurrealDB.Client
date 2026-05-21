namespace SurrealDB.Client.Sample.Api.Endpoints;

public static class ResilienceEndpoints
{
    public static void MapResilienceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resilience")
            .WithTags("Resilience — Retry & Circuit Breaker");

        group.MapGet("/config", (ISurrealDbClient client) =>
        {
            var options = client.Options;
            var pipelineStatus = options.MaxRetryAttempts == 0
                ? "disabled (pipeline is Empty)"
                : $"enabled ({options.MaxRetryAttempts} retries, exponential backoff from {options.InitialRetryDelay.TotalMilliseconds}ms)";

            return Results.Ok(new
            {
                resilience = new
                {
                    status = pipelineStatus,
                    maxRetryAttempts = options.MaxRetryAttempts,
                    initialRetryDelay = options.InitialRetryDelay.ToString(),
                    enableAutoReconnect = options.EnableAutoReconnect
                },
                description = "The resilience pipeline uses Polly.Core 8.x with retry (exponential backoff) " +
                              "and circuit breaker. Only ITransientException subtypes (ConnectionException, TimeoutException) trigger retries.",
                configuration = new
                {
                    property = "SurrealDbClientOptions.MaxRetryAttempts",
                    zeroDisables = "Setting MaxRetryAttempts = 0 returns ResiliencePipeline.Empty (no wrapping)"
                }
            });
        })
        .WithName("ResilienceConfig")
        .WithSummary("Show retry & circuit breaker configuration")
        .WithDescription(
            "Displays the current resilience pipeline configuration from `SurrealDbClientOptions`. " +
            "The pipeline wraps all CRUD operations via `ExecuteWithResilienceAsync`. " +
            "Only `ITransientException` subtypes (ConnectionException, TimeoutException) trigger retries.");

        group.MapPost("/test", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            // Execute a simple query to exercise the resilience pipeline
            // On success, the pipeline is transparent; on transient failure, retries kick in
            try
            {
                var result = await client.QueryAsync("RETURN 'resilience-test-ok';", null, ct);
                return Results.Ok(new
                {
                    success = true,
                    result = result.Data,
                    note = "Query succeeded. If the server was temporarily unavailable, " +
                           "the resilience pipeline would have retried automatically."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    success = false,
                    exceptionType = ex.GetType().Name,
                    message = ex.Message,
                    note = "After exhausting all retry attempts, the exception propagates to the caller."
                }, statusCode: 503);
            }
        })
        .WithName("ResilienceTest")
        .WithSummary("Execute a query through the resilience pipeline")
        .WithDescription(
            "Runs `RETURN 'resilience-test-ok'` through the full resilience pipeline. " +
            "If the connection is healthy, it succeeds immediately. " +
            "If transient errors occur, the pipeline retries with exponential backoff. " +
            "Check structured logs (ILogger) for retry attempt details.");
    }
}
