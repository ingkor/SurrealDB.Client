namespace SurrealDB.Client.Sample.Api.Endpoints;

using SurrealDB.Client.Diagnostics;

public static class ObservabilityEndpoints
{
    public static void MapObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/observability")
            .WithTags("Observability — Traces, Metrics, Logging");

        group.MapGet("/config", (ISurrealDbClient client) =>
        {
            var options = client.Options;

            return Results.Ok(new
            {
                tracing = new
                {
                    activitySourceName = SurrealDbActivitySource.Source.Name,
                    activitySourceVersion = SurrealDbActivitySource.Source.Version,
                    activityKind = "Client",
                    spanNamePattern = "surreal.{OperationName}",
                    tags = new[] { "db.system=surrealdb" }
                },
                logging = new
                {
                    loggerFactory = options.LoggerFactory != null ? "configured" : "null (NullLoggerFactory)",
                    structuredFields = new[] { "operationName", "durationMs", "exceptionType" }
                },
                description = "Every CRUD operation is wrapped in ExecuteWithResilienceAsync which creates " +
                              "an Activity span (distributed trace), records OperationDuration histogram, " +
                              "and emits structured log entries via ILogger<SurrealDbClient>."
            });
        })
        .WithName("ObservabilityConfig")
        .WithSummary("ActivitySource & ILogger configuration")
        .WithDescription(
            "Shows the tracing and logging configuration. " +
            "The `SurrealDbActivitySource.Source` (name: 'SurrealDB.Client') creates Activity spans " +
            "for every operation. Configure `SurrealDbClientOptions.LoggerFactory` to receive structured logs.");

        group.MapGet("/metrics", () =>
        {
            return Results.Ok(new
            {
                meterName = "SurrealDB.Client",
                meterVersion = "1.0.0",
                instruments = new[]
                {
                    new { name = "surreal.pool.acquired", type = "Counter<long>", unit = "connections", description = "Connection pool acquisitions" },
                    new { name = "surreal.pool.released", type = "Counter<long>", unit = "connections", description = "Connection pool releases" },
                    new { name = "surreal.pool.exhausted", type = "Counter<long>", unit = "connections", description = "Pool exhaustion events" },
                    new { name = "surreal.cache.hits", type = "Counter<long>", unit = "requests", description = "Query cache hits" },
                    new { name = "surreal.cache.misses", type = "Counter<long>", unit = "requests", description = "Query cache misses" },
                    new { name = "surreal.operation.duration", type = "Histogram<double>", unit = "ms", description = "Operation duration in milliseconds" }
                },
                usage = "Use OpenTelemetry .AddMeter(\"SurrealDB.Client\") to export metrics to Prometheus, OTLP, etc. " +
                        "Use .AddSource(\"SurrealDB.Client\") to export traces."
            });
        })
        .WithName("ObservabilityMetrics")
        .WithSummary("Meter instruments — counters & histograms")
        .WithDescription(
            "Lists all `System.Diagnostics.Metrics` instruments defined in `SurrealDbMetrics`. " +
            "Export via OpenTelemetry: `builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(\"SurrealDB.Client\"))`.");
    }
}
