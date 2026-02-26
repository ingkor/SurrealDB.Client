namespace SurrealDB.Client.DataLoading;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Batches and deduplicates data loading requests to prevent N+1 query problems.
/// </summary>
/// <typeparam name="TKey">The key type</typeparam>
/// <typeparam name="TValue">The value type</typeparam>
public class DataLoader<TKey, TValue> where TKey : notnull
{
    private readonly Func<IEnumerable<TKey>, CancellationToken, Task<Dictionary<TKey, TValue>>> _batchLoader;
    private readonly Dictionary<TKey, TValue> _cache = new();
    private readonly Dictionary<TKey, TaskCompletionSource<TValue>> _pending = new();
    private bool _isScheduled;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new DataLoader.
    /// </summary>
    /// <param name="batchLoader">Function that loads multiple values by keys</param>
    public DataLoader(Func<IEnumerable<TKey>, CancellationToken, Task<Dictionary<TKey, TValue>>> batchLoader)
    {
        _batchLoader = batchLoader ?? throw new ArgumentNullException(nameof(batchLoader));
    }

    /// <summary>
    /// Loads a value by key (batched and deduplicated).
    /// </summary>
    public Task<TValue> LoadAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_lock)
        {
            // Return cached value
            if (_cache.TryGetValue(key, out var cached))
                return Task.FromResult(cached);

            // Return pending task if already requested
            if (_pending.TryGetValue(key, out var pending))
                return pending.Task;

            // Create new pending task
            var tcs = new TaskCompletionSource<TValue>();
            _pending[key] = tcs;

            // Schedule batch load if not already scheduled
            if (!_isScheduled)
            {
                _isScheduled = true;
#pragma warning disable CS4014
                Task.Run(() => FlushAsync(cancellationToken));
#pragma warning restore CS4014
            }

            return tcs.Task;
        }
    }

    /// <summary>
    /// Loads multiple values (batched).
    /// </summary>
    public async Task<IEnumerable<TValue>> LoadManyAsync(
        IEnumerable<TKey> keys,
        CancellationToken cancellationToken = default)
    {
        var tasks = keys.Select(k => LoadAsync(k, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _pending.Clear();
        }
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public (int Cached, int Pending) GetStats()
    {
        lock (_lock)
        {
            return (_cache.Count, _pending.Count);
        }
    }

    /// <summary>
    /// Flushes pending requests by calling the batch loader.
    /// </summary>
    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        Dictionary<TKey, TaskCompletionSource<TValue>> toLoad;

        lock (_lock)
        {
            toLoad = new Dictionary<TKey, TaskCompletionSource<TValue>>(_pending);
            _pending.Clear();
            _isScheduled = false;
        }

        if (toLoad.Count == 0)
            return;

        try
        {
            var results = await _batchLoader(toLoad.Keys, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                foreach (var kvp in toLoad)
                {
                    if (results.TryGetValue(kvp.Key, out var value))
                    {
                        _cache[kvp.Key] = value;
                        kvp.Value.SetResult(value);
                    }
                    else
                    {
                        kvp.Value.SetException(new KeyNotFoundException($"Key not found: {kvp.Key}"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                foreach (var kvp in toLoad.Values)
                {
                    kvp.SetException(ex);
                }
            }
        }
    }
}

/// <summary>
/// Factory for creating DataLoaders.
/// </summary>
public class DataLoaderFactory
{
    /// <summary>
    /// Creates a DataLoader for loading entities by ID.
    /// </summary>
    public static DataLoader<string, T> CreateIdLoader<T>(
        Func<IEnumerable<string>, CancellationToken, Task<Dictionary<string, T>>> loader)
        where T : class
    {
        return new DataLoader<string, T>(loader);
    }

    /// <summary>
    /// Creates a DataLoader with custom key type.
    /// </summary>
    public static DataLoader<TKey, TValue> Create<TKey, TValue>(
        Func<IEnumerable<TKey>, CancellationToken, Task<Dictionary<TKey, TValue>>> loader)
        where TKey : notnull
    {
        return new DataLoader<TKey, TValue>(loader);
    }
}
