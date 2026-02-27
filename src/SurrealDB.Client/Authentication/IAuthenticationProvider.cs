namespace SurrealDB.Client.Authentication;

using Exceptions;
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
/// SECURITY: Username/password authentication provider with secure credential handling.
/// P1-1, P1-6: Credentials exposed in memory - no secure clearing.
///
/// Uses SecureCredentials for encrypted password storage.
/// </summary>
public class BasicAuthenticationProvider : IAuthenticationProvider, IDisposable
{
    private SecureCredentials? _credentials;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of BasicAuthenticationProvider.
    /// SECURITY: Stores credentials securely using SecureString internally.
    /// </summary>
    /// <param name="username">Username.</param>
    /// <param name="password">Password (will be encrypted in memory).</param>
    public BasicAuthenticationProvider(string username, string password)
    {
        _credentials = SecureCredentials.FromUsernamePassword(username, password);
    }

    /// <summary>
    /// Initializes with pre-secured credentials (advanced usage).
    /// </summary>
    /// <param name="credentials">Secure credentials object.</param>
    public BasicAuthenticationProvider(SecureCredentials credentials)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public async Task AuthenticateAsync(
        IProtocolAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _credentials == null)
            throw new ObjectDisposedException(nameof(BasicAuthenticationProvider));

        try
        {
            // SECURITY: Get credentials JSON, use immediately, then it goes out of scope
            var credentialsJson = _credentials.ToJsonCredentials();

            var response = await adapter.AuthenticateAsync(credentialsJson, cancellationToken);

            // The credentialsJson string is now out of scope and eligible for GC
            // We cannot force clear it as strings are immutable, but we minimize exposure time

            if (string.IsNullOrEmpty(response))
                throw new AuthenticationException("Empty response from authentication.");

            // Validate response is valid JSON
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                // Response should contain a token
                if (!root.TryGetProperty("token", out var tokenElement))
                    throw new AuthenticationException("No token in authentication response.");

                var token = tokenElement.GetString();
                if (string.IsNullOrEmpty(token))
                    throw new AuthenticationException("Empty token in authentication response.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new AuthenticationException("Invalid JSON in authentication response", ex);
            }
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Basic authentication failed", ex);
        }
    }

    /// <summary>
    /// SECURITY: Disposes and clears credentials from memory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _credentials?.Dispose();
        _credentials = null;
        _disposed = true;
    }
}

/// <summary>
/// SECURITY: Token-based authentication provider with secure token handling.
/// P1-1, P1-6: Token stored as plain string - cannot be securely cleared from memory.
///
/// Uses SecureCredentials for encrypted token storage.
/// </summary>
public class TokenAuthenticationProvider : IAuthenticationProvider, IDisposable
{
    private SecureCredentials? _credentials;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of TokenAuthenticationProvider.
    /// SECURITY: Stores token securely using SecureString internally.
    /// </summary>
    /// <param name="token">Authentication token (will be encrypted in memory).</param>
    public TokenAuthenticationProvider(string token)
    {
        _credentials = SecureCredentials.FromToken(token);
    }

    /// <summary>
    /// Initializes with pre-secured credentials (advanced usage).
    /// </summary>
    /// <param name="credentials">Secure credentials object.</param>
    public TokenAuthenticationProvider(SecureCredentials credentials)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public async Task AuthenticateAsync(
        IProtocolAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _credentials == null)
            throw new ObjectDisposedException(nameof(TokenAuthenticationProvider));

        try
        {
            // SECURITY: Get credentials JSON, use immediately, then it goes out of scope
            var credentialsJson = _credentials.ToJsonCredentials();

            var response = await adapter.AuthenticateAsync(credentialsJson, cancellationToken);

            // The credentialsJson string is now out of scope and eligible for GC

            if (string.IsNullOrEmpty(response))
                throw new AuthenticationException("Empty response from token authentication.");

            // Validate response is valid JSON
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                // Check for error field first
                if (root.TryGetProperty("error", out var errorProp))
                    throw new AuthenticationException($"Token auth failed: {errorProp.GetString()}");

                // Response should indicate success (common pattern: { "status": "ok" } or similar)
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    // Accept any successful JSON response as valid for token auth
                    // The token was already provided by the client
                    return;
                }

                throw new AuthenticationException("Invalid response format from token authentication.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new AuthenticationException("Invalid JSON in authentication response", ex);
            }
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Token authentication failed", ex);
        }
    }

    /// <summary>
    /// SECURITY: Disposes and clears token from memory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _credentials?.Dispose();
        _credentials = null;
        _disposed = true;
    }
}

/// <summary>
/// SECURITY: Session management for authenticated connections with secure token handling.
/// P1-2: Token not cleared from session after use.
///
/// Implements IDisposable to ensure tokens are cleared when session ends.
/// </summary>
public class AuthenticationSession : IDisposable
{
    private string? _token;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the authentication token.
    /// SECURITY: Token is cleared on disposal to prevent memory exposure.
    /// </summary>
    public string? Token
    {
        get => _disposed ? null : _token;
        set
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AuthenticationSession));
            _token = value;
        }
    }

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
    public bool IsValid => !_disposed &&
                           !string.IsNullOrEmpty(_token) &&
                           (ExpiresAt == null || DateTime.UtcNow < ExpiresAt);

    /// <summary>
    /// Gets whether the session is expired.
    /// </summary>
    public bool IsExpired => _disposed ||
                             (ExpiresAt != null && DateTime.UtcNow >= ExpiresAt);

    /// <summary>
    /// SECURITY: Explicitly clears the authentication token from memory.
    /// P1-2: Token not cleared from session after use.
    ///
    /// This should be called on logout or when the session is no longer needed.
    /// </summary>
    public void ClearToken()
    {
        if (_token != null)
        {
            // Best effort to clear string from memory
            // .NET strings are immutable, but we null the reference
            _token = null;
        }
    }

    /// <summary>
    /// SECURITY: Disposes the session and clears sensitive data.
    /// P1-2: Implements IDisposable for proper resource cleanup.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        ClearToken();
        _disposed = true;
    }
}
