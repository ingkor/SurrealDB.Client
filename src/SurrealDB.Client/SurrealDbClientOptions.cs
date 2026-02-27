namespace SurrealDB.Client;

using Exceptions;

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
    /// SECURITY: Gets or sets whether to use HTTPS for HTTP connections.
    /// P1-3: HTTP certificate validation disabled by default.
    ///
    /// DEFAULT CHANGED: Now defaults to TRUE for security.
    /// Set to false only in development/testing environments where you control the network.
    /// </summary>
    public bool UseHttps { get; set; } = true;

    /// <summary>
    /// SECURITY: Gets or sets whether to verify SSL/TLS certificates.
    /// P1-3: Certificate validation can be disabled.
    ///
    /// WARNING: Disabling certificate validation exposes you to man-in-the-middle attacks.
    /// This should NEVER be disabled in production environments.
    ///
    /// This setting applies to both HTTP and WebSocket connections.
    /// Default: true (secure)
    ///
    /// To disable (NOT RECOMMENDED), you must explicitly acknowledge the security risk:
    ///   options.VerifyServerCertificate = false;
    ///   options.AcknowledgeCertificateValidationRisk = true;
    /// </summary>
    public bool VerifyServerCertificate { get; set; } = true;

    /// <summary>
    /// SECURITY: Explicit acknowledgment required to disable certificate validation.
    /// P1-3: Make certificate validation non-disableable without explicit consent.
    ///
    /// When VerifyServerCertificate is set to false, this property MUST also be set to true,
    /// or validation will fail. This forces developers to consciously acknowledge the risk.
    ///
    /// WARNING: Only use this in development/testing. NEVER in production.
    /// </summary>
    public bool AcknowledgeCertificateValidationRisk { get; set; } = false;

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

        // SECURITY FIX: P1-4 - Validate namespace and database names using strict allowlist
        // Only permit alphanumeric characters, underscore, and hyphen to prevent injection
        ValidateIdentifier(Namespace, "Namespace");
        ValidateIdentifier(Database, "Database");

        // SECURITY FIX: P1-3 - Enforce certificate validation or explicit risk acknowledgment
        if (!VerifyServerCertificate && !AcknowledgeCertificateValidationRisk)
        {
            throw new ValidationException(
                "Certificate validation is disabled, but risk has not been acknowledged. " +
                "This is a critical security vulnerability that exposes you to man-in-the-middle attacks. " +
                "If you understand the risks and are in a controlled development/testing environment, " +
                "set 'AcknowledgeCertificateValidationRisk = true'. " +
                "NEVER disable certificate validation in production.");
        }

        if (PoolSize < 1)
            throw new ValidationException("PoolSize must be at least 1.");

        if (ConnectionTimeout.TotalSeconds < 1)
            throw new ValidationException("ConnectionTimeout must be at least 1 second.");

        if (CommandTimeout.TotalSeconds < 1)
            throw new ValidationException("CommandTimeout must be at least 1 second.");

        if (MaxRetryAttempts < 0)
            throw new ValidationException("MaxRetryAttempts cannot be negative.");
    }

    /// <summary>
    /// SECURITY: Validates that an identifier contains only safe characters.
    /// P1-4: No input validation on namespace/database names.
    ///
    /// Uses strict allowlist validation to prevent injection attacks.
    /// Only permits: alphanumeric (a-z, A-Z, 0-9), underscore (_), hyphen (-).
    /// </summary>
    /// <param name="identifier">The identifier to validate.</param>
    /// <param name="parameterName">The parameter name for error messages.</param>
    /// <exception cref="ValidationException">Thrown if identifier contains invalid characters.</exception>
    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        foreach (var c in identifier)
        {
            bool isValid = char.IsLetterOrDigit(c) || c == '_' || c == '-';
            if (!isValid)
            {
                throw new ValidationException(
                    $"{parameterName} '{identifier}' contains invalid character '{c}'. " +
                    "Only alphanumeric characters, underscore (_), and hyphen (-) are permitted. " +
                    "This restriction prevents injection attacks.");
            }
        }
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
