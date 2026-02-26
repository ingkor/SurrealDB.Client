namespace SurrealDB.Client;

/// <summary>
/// Configuration options for SurrealDbClient.
/// </summary>
public class SurrealDbClientOptions
{
    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "surreal://localhost:8000";

    /// <summary>
    /// Gets or sets the protocol type (HTTP or WebSocket).
    /// </summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.WebSocket;

    /// <summary>
    /// Gets or sets the connection pool size.
    /// </summary>
    public int PoolSize { get; set; } = 10;

    /// <summary>
    /// Gets or sets the connection timeout.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the query command timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Gets or sets the maximum retry attempts for failed connections.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial retry delay.
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets whether to enable connection health checks.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Gets or sets the health check interval.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether to enable automatic reconnection.
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the namespace for the connection.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Gets or sets the database name for the connection.
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// Gets or sets whether to use HTTPS for HTTP connections.
    /// </summary>
    public bool UseHttps { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to verify SSL certificates (HTTP only).
    /// </summary>
    public bool VerifyServerCertificate { get; set; } = true;

    /// <summary>
    /// Gets or sets the serializer to use for JSON serialization.
    /// </summary>
    public SerializerType SerializerType { get; set; } = SerializerType.SystemTextJson;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ValidationException">Thrown if options are invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ValidationException("ConnectionString cannot be empty.");

        if (string.IsNullOrWhiteSpace(Namespace))
            throw new ValidationException("Namespace is required and cannot be empty.");

        if (string.IsNullOrWhiteSpace(Database))
            throw new ValidationException("Database is required and cannot be empty.");

        if (PoolSize < 1)
            throw new ValidationException("PoolSize must be at least 1.");

        if (ConnectionTimeout.TotalSeconds < 1)
            throw new ValidationException("ConnectionTimeout must be at least 1 second.");

        if (CommandTimeout.TotalSeconds < 1)
            throw new ValidationException("CommandTimeout must be at least 1 second.");

        if (MaxRetryAttempts < 0)
            throw new ValidationException("MaxRetryAttempts cannot be negative.");
    }
}

/// <summary>
/// Protocol types for SurrealDB connections.
/// </summary>
public enum ProtocolType
{
    /// <summary>
    /// HTTP protocol.
    /// </summary>
    Http = 0,

    /// <summary>
    /// WebSocket protocol.
    /// </summary>
    WebSocket = 1
}

/// <summary>
/// Serializer types for JSON serialization.
/// </summary>
public enum SerializerType
{
    /// <summary>
    /// System.Text.Json serializer.
    /// </summary>
    SystemTextJson = 0,

    /// <summary>
    /// Newtonsoft.Json serializer.
    /// </summary>
    NewtonsoftJson = 1
}
