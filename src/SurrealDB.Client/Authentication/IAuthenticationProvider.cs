namespace SurrealDB.Client.Authentication;

using Protocol;

/// <summary>
/// Provides authentication for SurrealDB connections.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Authenticates using the provided protocol adapter.
    /// </summary>
    /// <param name="adapter">The protocol adapter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AuthenticateAsync(IProtocolAdapter adapter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Username/password authentication provider.
/// </summary>
public class BasicAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _username;
    private readonly string _password;

    /// <summary>
    /// Initializes a new instance of BasicAuthenticationProvider.
    /// </summary>
    /// <param name="username">Username.</param>
    /// <param name="password">Password.</param>
    public BasicAuthenticationProvider(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ValidationException("Username cannot be empty.");

        if (string.IsNullOrWhiteSpace(password))
            throw new ValidationException("Password cannot be empty.");

        _username = username;
        _password = password;
    }

    public async Task AuthenticateAsync(
        IProtocolAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = System.Text.Json.JsonSerializer.Serialize(
                new { user = _username, pass = _password });

            var response = await adapter.AuthenticateAsync(credentials, cancellationToken);

            if (string.IsNullOrEmpty(response))
                throw new AuthenticationException("Empty response from authentication.");

            // TODO: Parse response and validate token
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Basic authentication failed", ex);
        }
    }
}

/// <summary>
/// Token-based authentication provider.
/// </summary>
public class TokenAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _token;

    /// <summary>
    /// Initializes a new instance of TokenAuthenticationProvider.
    /// </summary>
    /// <param name="token">Authentication token.</param>
    public TokenAuthenticationProvider(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ValidationException("Token cannot be empty.");

        _token = token;
    }

    public async Task AuthenticateAsync(
        IProtocolAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = System.Text.Json.JsonSerializer.Serialize(
                new { token = _token });

            var response = await adapter.AuthenticateAsync(credentials, cancellationToken);

            if (string.IsNullOrEmpty(response))
                throw new AuthenticationException("Empty response from token authentication.");

            // TODO: Validate response
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Token authentication failed", ex);
        }
    }
}

/// <summary>
/// Session management for authenticated connections.
/// </summary>
public class AuthenticationSession
{
    /// <summary>
    /// Gets or sets the authentication token.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets when the session was established.
    /// </summary>
    public DateTime EstablishedAt { get; set; }

    /// <summary>
    /// Gets or sets when the token expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Gets whether the session is valid.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(Token) &&
                           (ExpiresAt == null || DateTime.UtcNow < ExpiresAt);

    /// <summary>
    /// Gets whether the session is expired.
    /// </summary>
    public bool IsExpired => ExpiresAt != null && DateTime.UtcNow >= ExpiresAt;
}
