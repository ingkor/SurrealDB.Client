namespace SurrealDB.Client.Caching;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interface for caching compiled queries.
/// </summary>
public interface IQueryCache
{
    /// <summary>
    /// Gets a cached result by key.
    /// </summary>
    /// <typeparam name="T">The result type</typeparam>
    /// <param name="key">The cache key</param>
    /// <returns>Cached result, or null if not found</returns>
    T? Get<T>(string key);

    /// <summary>
    /// Sets a cached result.
    /// </summary>
    /// <typeparam name="T">The result type</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="expiration">Optional expiration time (default 5 minutes)</param>
    void Set<T>(string key, T value, TimeSpan? expiration = null);

    /// <summary>
    /// Removes a cached result.
    /// </summary>
    void Remove(string key);

    /// <summary>
    /// Clears all cached results.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Cache statistics.
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of items in cache.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Total cache hits.
    /// </summary>
    public long Hits { get; set; }

    /// <summary>
    /// Total cache misses.
    /// </summary>
    public long Misses { get; set; }

    /// <summary>
    /// Hit rate percentage.
    /// </summary>
    public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses) * 100;
}
