namespace SurrealDB.Client.Diagnostics;

using System.Diagnostics.Metrics;

internal static class SurrealDbMetrics
{
    private static readonly Meter _meter = new("SurrealDB.Client", "1.0.0");

    internal static readonly Counter<long> PoolAcquired =
        _meter.CreateCounter<long>("surreal.pool.acquired", "connections");
    internal static readonly Counter<long> PoolReleased =
        _meter.CreateCounter<long>("surreal.pool.released", "connections");
    internal static readonly Counter<long> PoolExhausted =
        _meter.CreateCounter<long>("surreal.pool.exhausted", "connections");
    internal static readonly Counter<long> CacheHits =
        _meter.CreateCounter<long>("surreal.cache.hits", "requests");
    internal static readonly Counter<long> CacheMisses =
        _meter.CreateCounter<long>("surreal.cache.misses", "requests");
    internal static readonly Histogram<double> OperationDuration =
        _meter.CreateHistogram<double>("surreal.operation.duration", "ms");
}
