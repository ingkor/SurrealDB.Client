namespace SurrealDB.Client.Caching;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// In-memory implementation of IQueryCache.
/// Stores cached results with expiration support.
/// </summary>
public class MemoryQueryCache : IQueryCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private long _hits;
    private long _misses;
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(5);
    private readonly object _statsLock = new();

    /// <summary>
    /// Gets a cached result.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
            return default;

        if (_cache.TryGetValue(key, out var entry))
        {
            // Check expiration
            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt)
            {
                _cache.TryRemove(key, out _);
                RecordMiss();
                return default;
            }

            RecordHit();
            return (T?)entry.Value;
        }

        RecordMiss();
        return default;
    }

    /// <summary>
    /// Sets a cached result.
    /// </summary>
    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        if (string.IsNullOrEmpty(key))
            return;

        var expiresAt = expiration.HasValue
            ? DateTime.UtcNow.Add(expiration.Value)
            : DateTime.UtcNow.Add(_defaultExpiration);

        var entry = new CacheEntry
        {
            Value = value,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };

        _cache.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Removes a cached result.
    /// </summary>
    public void Remove(string key)
    {
        if (!string.IsNullOrEmpty(key))
            _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Clears all cached results.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new CacheStatistics
            {
                ItemCount = _cache.Count,
                Hits = _hits,
                Misses = _misses
            };
        }
    }

    /// <summary>
    /// Records a cache hit.
    /// </summary>
    private void RecordHit()
    {
        lock (_statsLock)
        {
            _hits++;
        }
    }

    /// <summary>
    /// Records a cache miss.
    /// </summary>
    private void RecordMiss()
    {
        lock (_statsLock)
        {
            _misses++;
        }
    }

    /// <summary>
    /// Internal cache entry.
    /// </summary>
    private class CacheEntry
    {
        public object? Value { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
