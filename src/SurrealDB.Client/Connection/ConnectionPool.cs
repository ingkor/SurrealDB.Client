namespace SurrealDB.Client.Connection;

using System.Collections.Concurrent;
using Protocol;

/// <summary>
/// Manages a pool of database connections with health checking.
/// </summary>
internal class ConnectionPool : IConnectionPool
{
    private readonly SurrealDbClientOptions _options;
    private readonly Func<CancellationToken, Task<IProtocolAdapter>> _connectionFactory;
    private readonly ConcurrentBag<PooledConnection> _availableConnections;
    private readonly HashSet<PooledConnection> _allConnections;
    private readonly SemaphoreSlim _acquireSemaphore;
    private readonly SemaphoreSlim _disposeSemaphore;
    private bool _disposed;
    private long _totalAcquisitions;
    private long _totalReleases;
    private long _failedHealthChecks;

    public int CurrentSize => _allConnections.Count;
    public int MaxSize => _options.PoolSize;
    public int AvailableCount => _availableConnections.Count;

    public ConnectionPool(
        SurrealDbClientOptions options,
        Func<CancellationToken, Task<IProtocolAdapter>> connectionFactory)
    {
        _options = options;
        _connectionFactory = connectionFactory;
        _availableConnections = new ConcurrentBag<PooledConnection>();
        _allConnections = new HashSet<PooledConnection>();
        _acquireSemaphore = new SemaphoreSlim(options.PoolSize);
        _disposeSemaphore = new SemaphoreSlim(1, 1);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Create initial pool connections
        var initialSize = Math.Min(2, _options.PoolSize);  // Start with 2 connections

        for (int i = 0; i < initialSize; i++)
        {
            try
            {
                var adapter = await _connectionFactory(cancellationToken);
                var pooled = new PooledConnection
                {
                    Id = Guid.NewGuid(),
                    Adapter = adapter,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    InUse = false,
                    UsageCount = 0
                };

                _availableConnections.Add(pooled);
                lock (_allConnections)
                {
                    _allConnections.Add(pooled);
                }
            }
            catch (Exception ex)
            {
                throw new ConnectionException(
                    $"Failed to initialize connection pool: {ex.Message}", ex);
            }
        }
    }

    public async Task<IProtocolAdapter> AcquireAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Wait for a semaphore slot (throttle if pool is full)
        await _acquireSemaphore.WaitAsync(_options.ConnectionTimeout, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PooledConnection? pooledConnection = null;

            // Try to get existing connection
            while (_availableConnections.TryTake(out pooledConnection))
            {
                // Check if connection is still healthy
                if (await pooledConnection.Adapter.HealthCheckAsync(cancellationToken))
                {
                    pooledConnection.InUse = true;
                    pooledConnection.LastUsedAt = DateTime.UtcNow;
                    pooledConnection.UsageCount++;
                    Interlocked.Increment(ref _totalAcquisitions);
                    return pooledConnection.Adapter;
                }

                // Connection is dead, dispose it
                Interlocked.Increment(ref _failedHealthChecks);
                await DisposeConnectionAsync(pooledConnection);
                pooledConnection = null;
            }

            // Create new connection if under limit
            lock (_allConnections)
            {
                if (_allConnections.Count < _options.PoolSize)
                {
                    pooledConnection = new PooledConnection
                    {
                        Id = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow,
                        LastUsedAt = DateTime.UtcNow,
                        InUse = true,
                        UsageCount = 1
                    };

                    _allConnections.Add(pooledConnection);
                }
            }

            if (pooledConnection == null)
            {
                throw new ConnectionException("Connection pool exhausted and cannot create new connections");
            }

            // Create the actual adapter
            pooledConnection.Adapter = await _connectionFactory(cancellationToken);
            Interlocked.Increment(ref _totalAcquisitions);
            return pooledConnection.Adapter;
        }
        catch (TimeoutException ex)
        {
            _acquireSemaphore.Release();
            throw new ConnectionException("Failed to acquire connection within timeout period", ex);
        }
        catch (Exception ex)
        {
            _acquireSemaphore.Release();
            throw;
        }
    }

    public async Task ReleaseAsync(IProtocolAdapter connection, bool healthy = true)
    {
        ThrowIfDisposed();

        PooledConnection? pooledConnection = null;

        lock (_allConnections)
        {
            pooledConnection = _allConnections.FirstOrDefault(c => c.Adapter == connection);
        }

        if (pooledConnection == null)
        {
            // Connection not from this pool, just dispose it
            await connection.DisposeAsync();
            _acquireSemaphore.Release();
            return;
        }

        pooledConnection.InUse = false;
        Interlocked.Increment(ref _totalReleases);

        if (healthy)
        {
            // Return to pool
            _availableConnections.Add(pooledConnection);
        }
        else
        {
            // Discard unhealthy connection
            await DisposeConnectionAsync(pooledConnection);
        }

        _acquireSemaphore.Release();
    }

    public async Task ClearAsync()
    {
        ThrowIfDisposed();

        await _disposeSemaphore.WaitAsync();
        try
        {
            while (_availableConnections.TryTake(out var pooled))
            {
                await DisposeConnectionAsync(pooled);
            }

            lock (_allConnections)
            {
                _allConnections.Clear();
            }
        }
        finally
        {
            _disposeSemaphore.Release();
        }
    }

    public PoolStatistics GetStatistics()
    {
        // Snapshot under lock, compute outside to minimize lock duration
        PooledConnection[] snapshot;
        int totalCount;
        lock (_allConnections)
        {
            totalCount = _allConnections.Count;
            snapshot = _allConnections.ToArray();
        }

        // Compute InUse count outside the lock
        int inUseCount = 0;
        foreach (var conn in snapshot)
        {
            if (conn.InUse)
                inUseCount++;
        }

        return new PoolStatistics
        {
            TotalConnections = totalCount,
            AvailableConnections = _availableConnections.Count,
            InUseConnections = inUseCount,
            TotalAcquisitions = Interlocked.Read(ref _totalAcquisitions),
            TotalReleases = Interlocked.Read(ref _totalReleases),
            FailedHealthChecks = Interlocked.Read(ref _failedHealthChecks)
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _disposeSemaphore.WaitAsync();
        try
        {
            await ClearAsync();
        }
        finally
        {
            _disposeSemaphore.Release();
            _disposeSemaphore.Dispose();
            _acquireSemaphore.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConnectionPool));
    }

    private static async Task DisposeConnectionAsync(PooledConnection pooled)
    {
        try
        {
            await pooled.Adapter.DisposeAsync();
        }
        catch
        {
            // Suppress errors during disposal
        }
    }
}
