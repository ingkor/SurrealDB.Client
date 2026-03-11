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
    private readonly int _maxItems;
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new();
    private readonly object _lruLock = new();

    public MemoryQueryCache(int maxItems = 0)
    {
        _maxItems = maxItems;
    }

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
                lock (_lruLock) { if (_lruNodes.Remove(key, out var n)) _lruOrder.Remove(n); }
                RecordMiss();
                return default;
            }

            // Move to front (most recently used)
            lock (_lruLock)
            {
                if (_lruNodes.TryGetValue(key, out var node))
                {
                    _lruOrder.Remove(node);
                    _lruNodes[key] = _lruOrder.AddFirst(key);
                }
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

        lock (_lruLock)
        {
            if (_lruNodes.TryGetValue(key, out var existingNode))
            {
                _lruOrder.Remove(existingNode);
            }
            else if (_maxItems > 0 && _cache.Count >= _maxItems)
            {
                var lruKey = _lruOrder.Last!.Value;
                _lruOrder.RemoveLast();
                _lruNodes.Remove(lruKey);
                _cache.TryRemove(lruKey, out _);
            }

            _lruNodes[key] = _lruOrder.AddFirst(key);
        }

        _cache.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Removes a cached result.
    /// </summary>
    public void Remove(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _cache.TryRemove(key, out _);
            lock (_lruLock) { if (_lruNodes.Remove(key, out var node)) _lruOrder.Remove(node); }
        }
    }

    /// <summary>
    /// Clears all cached results.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        lock (_lruLock) { _lruOrder.Clear(); _lruNodes.Clear(); }
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
        lock (_statsLock) { _hits++; }
        Diagnostics.SurrealDbMetrics.CacheHits.Add(1);
    }

    private void RecordMiss()
    {
        lock (_statsLock) { _misses++; }
        Diagnostics.SurrealDbMetrics.CacheMisses.Add(1);
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
