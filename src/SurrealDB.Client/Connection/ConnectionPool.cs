namespace SurrealDB.Client.Connection;

using System.Collections.Concurrent;
using Protocol;

/// <summary>
/// Manages a pool of database connections with health checking.
/// </summary>
internal class ConnectionPool : IConnectionPool
{
    private static readonly TimeSpan HealthCheckGracePeriod = TimeSpan.FromSeconds(30);

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
    private int _availableCount;

    public int CurrentSize => _allConnections.Count;
    public int MaxSize => _options.PoolSize;
    public int AvailableCount => _availableCount;

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
                Interlocked.Increment(ref _availableCount);
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
                Interlocked.Decrement(ref _availableCount);

                // Skip health check for recently-used connections (grace period optimization)
                var timeSinceLastUse = DateTime.UtcNow - pooledConnection.LastUsedAt;
                if (timeSinceLastUse < HealthCheckGracePeriod)
                {
                    // Connection was recently used and is likely still healthy
                    pooledConnection.InUse = true;
                    pooledConnection.LastUsedAt = DateTime.UtcNow;
                    pooledConnection.UsageCount++;
                    Interlocked.Increment(ref _totalAcquisitions);
                    return pooledConnection.Adapter;
                }

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

            // Check pool capacity before creating a new adapter
            bool canCreate = false;
            lock (_allConnections)
            {
                if (_allConnections.Count < _options.PoolSize)
                {
                    canCreate = true;
                }
            }

            if (!canCreate)
            {
                throw new ConnectionException("Connection pool exhausted and cannot create new connections");
            }

            // Create the adapter BEFORE adding to _allConnections to prevent null-adapter
            // corruption if DisposeAsync runs concurrently between those two steps.
            var adapter = await _connectionFactory(cancellationToken);

            pooledConnection = new PooledConnection
            {
                Id = Guid.NewGuid(),
                Adapter = adapter,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                InUse = true,
                UsageCount = 1
            };

            lock (_allConnections)
            {
                _allConnections.Add(pooledConnection);
            }

            Interlocked.Increment(ref _totalAcquisitions);
            return pooledConnection.Adapter;
        }
        catch (TimeoutException ex)
        {
            _acquireSemaphore.Release();
            throw new ConnectionException("Failed to acquire connection within timeout period", ex);
        }
        catch (Exception)
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
            Interlocked.Increment(ref _availableCount);
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
            await ClearConnectionsAsync();
        }
        finally
        {
            _disposeSemaphore.Release();
        }
    }

    /// <summary>
    /// Drains and disposes all connections. Must only be called while <c>_disposeSemaphore</c>
    /// is already held (or during disposal before the semaphore is released) to avoid
    /// re-entrant acquisition that would deadlock.
    /// </summary>
    private async Task ClearConnectionsAsync()
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

    public PoolStatistics GetStatistics()
    {
        lock (_allConnections)
        {
            return new PoolStatistics
            {
                TotalConnections = _allConnections.Count,
                AvailableConnections = _availableConnections.Count,
                InUseConnections = _allConnections.Count(c => c.InUse),
                TotalAcquisitions = Interlocked.Read(ref _totalAcquisitions),
                TotalReleases = Interlocked.Read(ref _totalReleases),
                FailedHealthChecks = Interlocked.Read(ref _failedHealthChecks)
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _disposeSemaphore.WaitAsync();
        try
        {
            // Call the shared drain logic directly — NOT ClearAsync(), which would
            // attempt to acquire _disposeSemaphore again and deadlock (non-reentrant).
            await ClearConnectionsAsync();
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
