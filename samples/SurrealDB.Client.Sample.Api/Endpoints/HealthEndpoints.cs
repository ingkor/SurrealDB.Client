namespace SurrealDB.Client.Sample.Api.Endpoints;

using SurrealDB.Client.Caching;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/health")
            .WithTags("Health — Connection & Pool Status");

        group.MapGet("/connection", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            var isConnected = await client.IsConnectedAsync(ct);

            return Results.Ok(new
            {
                isConnected,
                isConnectedSync = client.IsConnected,
                description = "IsConnectedAsync sends a health-check ping to the server. " +
                              "IsConnected returns the cached connection state (may be stale)."
            });
        })
        .WithName("HealthConnection")
        .WithSummary("IsConnectedAsync — live connection check")
        .WithDescription(
            "Checks the live connection status via `client.IsConnectedAsync()`. " +
            "This sends an actual ping to the SurrealDB server, unlike `client.IsConnected` " +
            "which returns the locally cached state.");

        group.MapGet("/config", (ISurrealDbClient client) =>
        {
            var options = client.Options;

            return Results.Ok(new
            {
                healthChecks = new
                {
                    enabled = options.EnableHealthChecks,
                    interval = options.HealthCheckInterval.ToString()
                },
                timeouts = new
                {
                    connectionTimeout = options.ConnectionTimeout.ToString(),
                    commandTimeout = options.CommandTimeout.ToString()
                },
                pool = new
                {
                    size = options.PoolSize,
                    autoReconnect = options.EnableAutoReconnect
                },
                description = "EnableHealthChecks starts a background timer that pings SurrealDB " +
                              "at the configured interval. CommandTimeout wraps each operation " +
                              "with a CancellationTokenSource.CreateLinkedTokenSource timeout."
            });
        })
        .WithName("HealthConfig")
        .WithSummary("Health check & timeout configuration")
        .WithDescription(
            "Shows connection health, timeout, and pool settings from `SurrealDbClientOptions`. " +
            "When `EnableHealthChecks = true`, a background timer periodically calls the server.");

        group.MapGet("/cache", (ISurrealDbClient client) =>
        {
            var concreteClient = (SurrealDbClient)client;
            var cache = concreteClient.QueryCache;
            var stats = cache.GetStatistics();
            var options = client.Options;

            return Results.Ok(new
            {
                cacheMaxItems = options.CacheMaxItems == 0 ? "unlimited" : options.CacheMaxItems.ToString(),
                statistics = stats,
                description = "CacheMaxItems controls LRU eviction in MemoryQueryCache. " +
                              "When capacity is reached, the least recently used entry is evicted. " +
                              "Set to 0 for unlimited (backward compatible default)."
            });
        })
        .WithName("HealthCache")
        .WithSummary("LRU cache stats & capacity")
        .WithDescription(
            "Shows the query cache configuration and hit/miss statistics. " +
            "`CacheMaxItems` enables LRU eviction via a LinkedList + Dictionary tracking strategy. " +
            "Cache hits/misses are also recorded as `SurrealDbMetrics.CacheHits` / `CacheMisses` counters.");
    }
}
