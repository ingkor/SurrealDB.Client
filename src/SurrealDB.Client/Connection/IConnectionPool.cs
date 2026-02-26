namespace SurrealDB.Client.Connection;

using Protocol;

/// <summary>
/// Manages a pool of database connections.
/// </summary>
public interface IConnectionPool : IAsyncDisposable
{
    /// <summary>
    /// Gets the current pool size.
    /// </summary>
    int CurrentSize { get; }

    /// <summary>
    /// Gets the maximum pool size.
    /// </summary>
    int MaxSize { get; }

    /// <summary>
    /// Gets the number of available connections.
    /// </summary>
    int AvailableCount { get; }

    /// <summary>
    /// Initializes the connection pool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a connection from the pool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A connection.</returns>
    Task<IProtocolAdapter> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a connection back to the pool.
    /// </summary>
    /// <param name="connection">The connection to release.</param>
    /// <param name="healthy">Whether the connection is healthy.</param>
    Task ReleaseAsync(IProtocolAdapter connection, bool healthy = true);

    /// <summary>
    /// Clears all connections from the pool.
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Gets pool statistics.
    /// </summary>
    /// <returns>Pool statistics.</returns>
    PoolStatistics GetStatistics();
}

/// <summary>
/// Statistics for a connection pool.
/// </summary>
public class PoolStatistics
{
    /// <summary>
    /// Gets or sets the total number of connections.
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of available connections.
    /// </summary>
    public int AvailableConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of in-use connections.
    /// </summary>
    public int InUseConnections { get; set; }

    /// <summary>
    /// Gets or sets the total number of acquisitions.
    /// </summary>
    public long TotalAcquisitions { get; set; }

    /// <summary>
    /// Gets or sets the total number of releases.
    /// </summary>
    public long TotalReleases { get; set; }

    /// <summary>
    /// Gets or sets the number of failed health checks.
    /// </summary>
    public long FailedHealthChecks { get; set; }
}
