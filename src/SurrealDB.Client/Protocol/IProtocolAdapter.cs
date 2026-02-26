namespace SurrealDB.Client.Protocol;

/// <summary>
/// Represents a protocol adapter for communicating with SurrealDB.
/// </summary>
public interface IProtocolAdapter : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the adapter is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a request and receives a response.
    /// </summary>
    /// <param name="method">HTTP method or operation type.</param>
    /// <param name="path">Request path or operation.</param>
    /// <param name="body">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response data.</returns>
    Task<string> SendAsync(
        string method,
        string path,
        string? body = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an authentication request.
    /// </summary>
    Task<string> AuthenticateAsync(
        string credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the connection is healthy.
    /// </summary>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a connection in the pool.
/// </summary>
public class PooledConnection
{
    /// <summary>
    /// Gets or sets the unique identifier for this connection.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the protocol adapter.
    /// </summary>
    public IProtocolAdapter Adapter { get; set; } = null!;

    /// <summary>
    /// Gets or sets when the connection was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the connection was last used.
    /// </summary>
    public DateTime LastUsedAt { get; set; }

    /// <summary>
    /// Gets or sets whether the connection is in use.
    /// </summary>
    public bool InUse { get; set; }

    /// <summary>
    /// Gets or sets the number of times this connection has been used.
    /// </summary>
    public int UsageCount { get; set; }
}
