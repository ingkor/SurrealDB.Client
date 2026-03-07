namespace SurrealDB.Client.Sample.Api.Endpoints;

using Models;
using SurrealDB.Client;
using SurrealDB.Client.Caching;
using SurrealDB.Client.Concurrency;
using SurrealDB.Client.EventSourcing;
using SurrealDB.Client.Interceptors;
using SurrealDB.Client.Plugins;
using SurrealDB.Client.Query;

/// <summary>
/// Demonstrates: Interceptors, Plugins, IQueryCache, ConcurrencyTokenAttribute,
/// InMemoryEventStore, SurrealQueryCompiler
/// </summary>
public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/demo")
            .WithTags("Demo — Enterprise Features");

        // Feature: ISurrealDbInterceptor — hook into query execution pipeline
        group.MapGet("/interceptors", (ISurrealDbClient client) =>
        {
            // Show registered interceptors
            var concreteClient = (SurrealDbClient)client;
            var interceptors = concreteClient.GetInterceptors().ToList();

            return Results.Ok(new
            {
                registeredCount = interceptors.Count,
                interceptors = interceptors.Select(i => i.GetType().Name),
                description = "Interceptors hook into the query pipeline via ISurrealDbInterceptor. " +
                              "Implement OnBeforeQueryAsync / OnAfterQueryAsync to add logging, metrics, caching, or auth.",
                example = new
                {
                    usage = "client.AddInterceptor(new LoggingInterceptor())",
                    baseClass = "InterceptorBase (override OnBeforeQueryAsync / OnAfterQueryAsync)"
                }
            });
        })
        .WithName("DemoInterceptors")
        .WithSummary("ISurrealDbInterceptor — query pipeline hooks")
        .WithDescription("Shows the interceptor pipeline. Implement `InterceptorBase` to add cross-cutting concerns (logging, metrics, audit trails) to every query.");

        // Feature: PluginManager / ISurrealDbPlugin
        group.MapGet("/plugins", (ISurrealDbClient client) =>
        {
            var pluginManager = ((SurrealDbClient)client).Plugins;
            var plugins = pluginManager.GetPlugins().ToList();

            return Results.Ok(new
            {
                registeredCount = plugins.Count,
                plugins = plugins.Select(p => new { p.Name, p.Version }),
                description = "The PluginManager allows loading ISurrealDbPlugin instances that extend client behaviour.",
                example = new
                {
                    usage = "client.Plugins.RegisterPlugin(new AuditPlugin())",
                    interface_ = "ISurrealDbPlugin { string Name; string Version; Task InitializeAsync(ISurrealDbClient); }"
                }
            });
        })
        .WithName("DemoPlugins")
        .WithSummary("ISurrealDbPlugin — extensible plugin system")
        .WithDescription("Shows registered plugins via `client.Plugins`. Use `PluginManager.RegisterPlugin()` to add custom behaviour without modifying the client.");

        // Feature: IQueryCache — cache query results in memory
        group.MapGet("/cache", (ISurrealDbClient client) =>
        {
            var cache = ((SurrealDbClient)client).QueryCache;

            // Manually set a cached value to demonstrate the API
            var cacheKey = "demo:products:all";
            var cachedData = new[] { new Product { Id = "products:demo", Name = "Cached Product", Price = 9.99m } };
            cache.Set(cacheKey, cachedData, TimeSpan.FromMinutes(5));

            var hit = cache.Get<Product[]>(cacheKey);
            var stats = cache.GetStatistics();

            return Results.Ok(new
            {
                cacheKey,
                cachedValue = hit,
                statistics = stats,
                description = "IQueryCache provides transparent result caching. " +
                              "The SurrealDbQueryProvider automatically caches IQueryable results.",
                usage = new
                {
                    set = "cache.SetAsync(key, value, ttl)",
                    get = "cache.GetAsync<T>(key)",
                    invalidate = "cache.InvalidateAsync(key)"
                }
            });
        })
        .WithName("DemoCache")
        .WithSummary("IQueryCache — transparent result caching")
        .WithDescription("Demonstrates the in-memory query cache (`MemoryQueryCache`). The `SurrealDbQueryProvider` automatically caches `IQueryable` results to reduce round-trips.");

        // Feature: ConcurrencyTokenAttribute — optimistic concurrency
        group.MapGet("/concurrency", () =>
        {
            // Show what the attribute looks like and how it works
            var productProps = typeof(ProductWithVersion)
                .GetProperties()
                .Select(p => new
                {
                    property = p.Name,
                    isConcurrencyToken = p.GetCustomAttributes(typeof(ConcurrencyTokenAttribute), false).Any(),
                    type = p.PropertyType.Name
                });

            return Results.Ok(new
            {
                entity = nameof(ProductWithVersion),
                properties = productProps,
                description = "Mark a property with [ConcurrencyToken] to enable optimistic concurrency. " +
                              "ConcurrencyTokenManager reads the token before UPDATE and checks it hasn't changed.",
                howItWorks = new[]
                {
                    "1. Load entity → server sends current Version",
                    "2. Client modifies entity",
                    "3. UpdateAsync checks: WHERE version = $originalVersion",
                    "4. If version changed → throws DbUpdateConcurrencyException",
                    "5. Caller retries with fresh data"
                }
            });
        })
        .WithName("DemoConcurrency")
        .WithSummary("[ConcurrencyToken] — optimistic concurrency control")
        .WithDescription("Shows how `[ConcurrencyToken]` enables optimistic concurrency via `ConcurrencyTokenManager`. Prevents lost-update anomalies in concurrent scenarios.");

        // Feature: InMemoryEventStore — event sourcing
        group.MapGet("/event-sourcing", async (IEventStore eventStore, CancellationToken ct) =>
        {
            // Append some demo events
            var aggregateId = $"order:{Guid.NewGuid():N}";

            await eventStore.AppendEventAsync(new EntityCreatedEvent
            {
                AggregateId = aggregateId,
                EntityType = "Order",
                UserId = "users:demo",
                InitialData = new Dictionary<string, object> { ["status"] = "Pending", ["total"] = 99.99m }
            }, ct);

            await eventStore.AppendEventAsync(new EntityUpdatedEvent
            {
                AggregateId = aggregateId,
                UserId = "users:demo",
                Changes = new Dictionary<string, object> { ["status"] = "Processing" }
            }, ct);

            // Replay the full event history for this aggregate
            var history = await eventStore.GetEventsAsync(aggregateId, ct);
            var allEvents = await eventStore.GetAllEventsAsync(ct);

            return Results.Ok(new
            {
                aggregateId,
                eventHistory = history.Select(e => new
                {
                    e.EventId,
                    e.EventType,
                    e.AggregateId,
                    e.OccurredAt,
                    e.Version,
                    e.UserId
                }),
                totalEventsInStore = allEvents.Count(),
                description = "InMemoryEventStore persists domain events per aggregate. " +
                              "Use GetEventsAsync(aggregateId) to replay an entity's full history."
            });
        })
        .WithName("DemoEventSourcing")
        .WithSummary("InMemoryEventStore — event sourcing & replay")
        .WithDescription("Demonstrates the event sourcing pattern using `InMemoryEventStore`. Appends `EntityCreatedEvent` and `EntityUpdatedEvent`, then replays the full aggregate history via `GetEventsAsync(aggregateId)`.");
    }
}

// Example entity with concurrency token — for the demo endpoint
file class ProductWithVersion
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }

    [ConcurrencyToken]
    public int Version { get; set; }
}
