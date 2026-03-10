namespace SurrealDB.Client;

using System.Reflection;
using System.Text.Json;
using Authentication;
using Caching;
using Connection;
using Exceptions;
using Interceptors;
using Migrations;
using Plugins;
using Protocol;
using Serialization;
using Session;
using Validation;

/// <summary>
/// Main SurrealDB client implementation.
/// </summary>
public class SurrealDbClient : ISurrealDbClient
{
    /// <summary>
    /// F10 Fix: Protocol method constants to avoid magic strings.
    /// </summary>
    private static class ProtocolMethods
    {
        public const string Query = "QUERY";
        public const string SignIn = "signin";
        public const string Ping = "ping";
    }
    private readonly SurrealDbClientOptions _options;
    private readonly ISerializer _serializer;
    private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1); // F4: Prevent concurrent ConnectAsync
    private readonly List<ISurrealDbInterceptor> _interceptors = new();
    private readonly IQueryCache _cache;
    private readonly PluginManager _pluginManager = new();
    private IConnectionPool? _connectionPool;
    private IProtocolAdapter? _currentConnection;
    private AuthenticationSession? _authSession;
    // F8 Fix: Add volatile modifier to flag fields for thread-safety
    private volatile bool _isConnected;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SurrealDbClient class.
    /// </summary>
    /// <param name="options">Client options.</param>
    /// <param name="serializer">Custom serializer (optional).</param>
    public SurrealDbClient(SurrealDbClientOptions options, ISerializer? serializer = null)
    {
        options.Validate();
        _options = options;
        _serializer = serializer ?? new SystemTextJsonSerializer();
        _cache = new MemoryQueryCache();
    }

    /// <summary>
    /// Initializes a new instance of the SurrealDbClient class.
    /// </summary>
    /// <param name="connectionString">Connection string.</param>
    public SurrealDbClient(string connectionString)
        : this(new SurrealDbClientOptions { ConnectionString = connectionString })
    {
    }

    public SurrealDbClientOptions Options => _options;

    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets the query cache.
    /// </summary>
    public IQueryCache QueryCache => _cache;

    /// <summary>
    /// Gets the plugin manager.
    /// </summary>
    public PluginManager Plugins => _pluginManager;

    /// <summary>
    /// Registers an interceptor.
    /// </summary>
    public void AddInterceptor(ISurrealDbInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _interceptors.Add(interceptor);
    }

    /// <summary>
    /// Removes an interceptor.
    /// </summary>
    public void RemoveInterceptor(ISurrealDbInterceptor interceptor)
    {
        _interceptors.Remove(interceptor);
    }

    /// <summary>
    /// Gets all registered interceptors.
    /// </summary>
    public IEnumerable<ISurrealDbInterceptor> GetInterceptors() => _interceptors.AsReadOnly();

    #region Sessions

    /// <summary>
    /// Creates a new session for managing entity changes with automatic change tracking.
    /// </summary>
    /// <returns>A new ISurrealDbSession for the unit of work pattern.</returns>
    public ISurrealDbSession CreateSession()
    {
        ThrowIfDisposed();
        if (!_isConnected)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        return new SurrealDbSession(this);
    }

    #endregion

    #region Connection Management

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // F4 Fix: Use semaphore to prevent concurrent ConnectAsync calls
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            // F4 Fix: Check if already connected
            if (_isConnected && _connectionPool != null)
            {
                return; // Already connected, nothing to do
            }

            IProtocolAdapter? connection = null;
            try
            {
                // Initialize connection pool
                _connectionPool = new ConnectionPool(
                    _options,
                    ct => ProtocolAdapterFactory.CreateAdapterAsync(_options, ct));

                await _connectionPool.InitializeAsync(cancellationToken);

                // Get a test connection to verify it works
                connection = await _connectionPool.AcquireAsync(cancellationToken);
                _currentConnection = connection;

                // Verify connection
                await _currentConnection.ConnectAsync(cancellationToken);

                // Set namespace and database for this connection
                // F10 Fix: Use constant for QUERY method
                var useNsDbStatement = $"USE NS {EscapeIdentifier(_options.Namespace)} DB {EscapeIdentifier(_options.Database)};";
                var response = await _currentConnection.SendAsync(ProtocolMethods.Query, useNsDbStatement, null, cancellationToken);

                if (string.IsNullOrEmpty(response))
                    throw new ConnectionException("Failed to set namespace and database: empty response");

                // F6 Fix: Validate response for errors using proper JSON parsing
                if (HasRootLevelError(response))
                    throw new ConnectionException($"Failed to set namespace and database: {response}");

                _isConnected = true;
            }
            catch (Exception ex) when (!(ex is SurrealDbException))
            {
                // F2 Fix: Release connection on failure to prevent connection leak
                if (connection != null && _connectionPool != null)
                {
                    try
                    {
                        await _connectionPool.ReleaseAsync(connection, healthy: false);
                    }
                    catch
                    {
                        // Suppress errors during cleanup
                    }
                    _currentConnection = null;
                }

                throw new ConnectionException($"Failed to connect to {_options.ConnectionString}", ex);
            }
            catch
            {
                // F2 Fix: Also handle SurrealDbException cases
                if (connection != null && _connectionPool != null)
                {
                    try
                    {
                        await _connectionPool.ReleaseAsync(connection, healthy: false);
                    }
                    catch
                    {
                        // Suppress errors during cleanup
                    }
                    _currentConnection = null;
                }

                throw;
            }
        }
        finally
        {
            // F4 Fix: Always release the lock
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        try
        {
            // F3 Fix: Release current connection back to pool
            if (_currentConnection != null && _connectionPool != null)
            {
                try
                {
                    await _connectionPool.ReleaseAsync(_currentConnection, healthy: true);
                }
                catch
                {
                    // Suppress errors during disconnection
                }
                _currentConnection = null;
            }

            _isConnected = false;
        }
        catch (Exception ex)
        {
            throw new ConnectionException("Failed to disconnect", ex);
        }
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // TODO: Implement health check
            return _isConnected;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Authentication

    /// <summary>
    /// SECURITY: Authenticates with username and password.
    /// P1-6: Credentials passed as strings in public API.
    ///
    /// Note: This method accepts plain strings for backward compatibility,
    /// but internally uses SecureCredentials for secure handling.
    /// For maximum security, use AuthenticateAsync(SecureCredentials).
    /// </summary>
    public async Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Validate inputs before checking connection state
        if (string.IsNullOrEmpty(username))
            throw new ValidationException("Username cannot be empty.");
        if (string.IsNullOrEmpty(password))
            throw new ValidationException("Password cannot be empty.");

        if (!_isConnected || _currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        // SECURITY: Dispose previous session if it exists
        _authSession?.Dispose();

        BasicAuthenticationProvider? provider = null;
        try
        {
            provider = new BasicAuthenticationProvider(username, password);
            await provider.AuthenticateAsync(_currentConnection, cancellationToken);

            _authSession = new AuthenticationSession
            {
                EstablishedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Basic authentication failed", ex);
        }
        finally
        {
            // SECURITY: Always dispose the provider to clear credentials
            provider?.Dispose();
        }
    }

    /// <summary>
    /// SECURITY: Authenticates with a token.
    /// P1-6: Credentials passed as strings in public API.
    ///
    /// Note: This method accepts plain string for backward compatibility,
    /// but internally uses SecureCredentials for secure handling.
    /// For maximum security, use AuthenticateAsync(SecureCredentials).
    /// </summary>
    public async Task AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isConnected || _currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        // SECURITY: Dispose previous session if it exists
        _authSession?.Dispose();

        TokenAuthenticationProvider? provider = null;
        try
        {
            provider = new TokenAuthenticationProvider(token);
            await provider.AuthenticateAsync(_currentConnection, cancellationToken);

            _authSession = new AuthenticationSession
            {
                Token = token,
                EstablishedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Token authentication failed", ex);
        }
        finally
        {
            // SECURITY: Always dispose the provider to clear credentials
            provider?.Dispose();
        }
    }

    /// <summary>
    /// SECURITY: Authenticates using secure credentials.
    /// P1-6: New secure API that accepts SecureCredentials instead of plain strings.
    ///
    /// This is the preferred method for authentication as it uses SecureString
    /// for credential storage and automatic cleanup.
    /// </summary>
    /// <param name="credentials">Secure credentials object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AuthenticateAsync(SecureCredentials credentials, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isConnected || _currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        if (credentials == null)
            throw new ArgumentNullException(nameof(credentials));

        // SECURITY: Dispose previous session if it exists
        _authSession?.Dispose();

        IAuthenticationProvider? provider = null;
        try
        {
            // Determine provider type based on credentials
            if (credentials.Username != null)
            {
                provider = new BasicAuthenticationProvider(credentials);
            }
            else
            {
                provider = new TokenAuthenticationProvider(credentials);
            }

            await provider.AuthenticateAsync(_currentConnection, cancellationToken);

            _authSession = new AuthenticationSession
            {
                Token = credentials.GetTokenString(),
                EstablishedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Authentication failed", ex);
        }
        finally
        {
            // SECURITY: Always dispose the provider to clear credentials
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// SECURITY: Logs out and clears the authentication session.
    /// P1-2: Token not cleared from session after use.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // SECURITY: Dispose and clear the authentication session
            if (_authSession != null)
            {
                _authSession.Dispose();
                _authSession = null;
            }

            // TODO: Send logout command to server
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new ConnectionException("Logout failed", ex);
        }
    }

    #endregion

    #region CRUD Operations

    public async Task<T?> CreateAsync<T>(string table, T data, CancellationToken cancellationToken = default)
        where T : class
    {
        ThrowIfDisposed();
        ValidateTable(table);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var json = _serializer.Serialize(data);
            var query = $"CREATE {table} CONTENT {json} RETURN AFTER;";
            var response = await _currentConnection.SendAsync("query", "/sql", $"{{\"query\":\"{query}\"}}", cancellationToken).ConfigureAwait(false);

            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result?.FirstOrDefault();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to create record in {table}", ex);
        }
    }

    public async Task<IEnumerable<T?>> CreateAsync<T>(string table, IEnumerable<T> data, CancellationToken cancellationToken = default)
        where T : class
    {
        ThrowIfDisposed();
        ValidateTable(table);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var results = new List<T?>();
            foreach (var item in data)
            {
                var result = await CreateAsync(table, item, cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }
            return results;
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to create records in {table}", ex);
        }
    }

    public async Task<T?> GetAsync<T>(string recordId, CancellationToken cancellationToken = default)
        where T : class
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var query = $"SELECT * FROM {recordId};";
            var response = await _currentConnection.SendAsync("query", "/sql", $"{{\"query\":\"{query}\"}}", cancellationToken).ConfigureAwait(false);

            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result?.FirstOrDefault();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to get record {recordId}", ex);
        }
    }

    public async Task<IEnumerable<T>> SelectAsync<T>(string table, int limit = 1000, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTable(table);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var query = $"SELECT * FROM {table} LIMIT {limit};";
            var response = await _currentConnection.SendAsync("query", "/sql", $"{{\"query\":\"{query}\"}}", cancellationToken).ConfigureAwait(false);

            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result ?? Enumerable.Empty<T>();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to select from {table}", ex);
        }
    }

    /// <summary>
    /// Updates a record in the database with the provided data.
    /// </summary>
    /// <remarks>
    /// <strong>Concurrency Warning:</strong> This operation uses last-write-wins semantics.
    /// If multiple clients update the same record concurrently, the last update will overwrite
    /// all previous updates without conflict detection. For optimistic concurrency control,
    /// consider using a version field and conditional updates (planned for Phase 2).
    /// </remarks>
    public async Task<T?> UpdateAsync<T>(string recordId, T data, CancellationToken cancellationToken = default)
        where T : class
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var json = _serializer.Serialize(data);
            var query = $"UPDATE {recordId} CONTENT {json} RETURN AFTER;";
            var response = await _currentConnection.SendAsync("query", "/sql", $"{{\"query\":\"{query}\"}}", cancellationToken).ConfigureAwait(false);

            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result?.FirstOrDefault();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to update record {recordId}", ex);
        }
    }

    public async Task DeleteAsync(string recordId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var query = $"DELETE {recordId};";
            var response = await _currentConnection.SendAsync("query", "/sql", $"{{\"query\":\"{query}\"}}", cancellationToken).ConfigureAwait(false);
            var envelope = _serializer.Deserialize<SurrealDbResponse<object>>(response);
            envelope?.EnsureSuccess();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to delete record {recordId}", ex);
        }
    }

    /// <summary>
    /// Creates a record if it doesn't exist, or updates it if it does (SurrealDB 3.0+ only).
    /// </summary>
    public async Task<T?> UpsertAsync<T>(string recordId, T data, CancellationToken cancellationToken = default)
        where T : class
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var json = _serializer.Serialize(data);
            var query = $"UPSERT {recordId} CONTENT {json} RETURN AFTER;";
            var response = await _currentConnection.SendAsync("query", "/sql", $"{{\"query\":\"{query}\"}}", cancellationToken).ConfigureAwait(false);

            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result?.FirstOrDefault();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException($"Failed to upsert record {recordId}", ex);
        }
    }

    #endregion

    #region Query Execution

    public async Task<QueryResult> QueryAsync(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateQuery(surrealQL);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            // Build parameterized query if parameters provided
            var body = parameters != null
                ? _serializer.Serialize(new { query = surrealQL, vars = parameters })
                : _serializer.Serialize(new { query = surrealQL });

            var response = await _currentConnection.SendAsync("query", "/sql", body, cancellationToken).ConfigureAwait(false);
            var result = _serializer.Deserialize<QueryResult>(response) ?? new QueryResult { Status = "OK" };
            return result;
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Query execution failed", ex);
        }
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateQuery(surrealQL);

        if (_currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            // Build parameterized query if parameters provided
            var body = parameters != null
                ? _serializer.Serialize(new { query = surrealQL, vars = parameters })
                : _serializer.Serialize(new { query = surrealQL });

            var response = await _currentConnection.SendAsync("query", "/sql", body, cancellationToken).ConfigureAwait(false);
            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result ?? Enumerable.Empty<T>();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Query execution failed", ex);
        }
    }

    #endregion

    #region Transactions

    public async Task<ISurrealDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connectionPool == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            // Acquire a dedicated connection for the transaction
            var connection = await _connectionPool.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // Send BEGIN to start transaction
            await connection.SendAsync("query", "/sql", "{\"query\":\"BEGIN;\"}", cancellationToken).ConfigureAwait(false);

            return new SurrealDbTransaction(_serializer, connection, _connectionPool);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Failed to begin transaction", ex);
        }
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // SECURITY: P1-2 - Dispose and clear authentication session
            if (_authSession != null)
            {
                try
                {
                    _authSession.Dispose();
                }
                catch
                {
                    // Suppress errors during disposal
                }
                _authSession = null;
            }

            // F3 Fix: Release current connection if still held
            if (_currentConnection != null && _connectionPool != null)
            {
                try
                {
                    await _connectionPool.ReleaseAsync(_currentConnection, healthy: false);
                }
                catch
                {
                    // Suppress errors during disposal
                }
                _currentConnection = null;
            }

            // F3 Fix: Dispose connection pool to release all resources
            if (_connectionPool != null)
            {
                try
                {
                    await _connectionPool.DisposeAsync();
                }
                catch
                {
                    // Suppress errors during disposal
                }
                _connectionPool = null;
            }

            // F4 Fix: Dispose semaphore
            try
            {
                _connectLock.Dispose();
            }
            catch
            {
                // Suppress errors during disposal
            }

            _isConnected = false;
        }
        catch
        {
            // Suppress errors during disposal
        }
    }

    #endregion

    #region Private Methods

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SurrealDbClient));
    }

    /// <summary>
    /// SECURITY FIX: Escapes SurrealDB identifiers using strict allowlist validation.
    /// Vuln 3: SurrealQL Injection via weak identifier escaping.
    ///
    /// Only permits: alphanumeric characters (a-z, A-Z, 0-9), underscore (_), and hyphen (-).
    /// This prevents injection attacks by rejecting all potentially dangerous characters.
    /// </summary>
    /// <param name="identifier">The identifier to escape.</param>
    /// <returns>The validated identifier.</returns>
    /// <exception cref="ValidationException">Thrown if identifier contains invalid characters.</exception>
    private static string EscapeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ValidationException("Identifier cannot be empty.");

        // SECURITY: Use strict allowlist - only permit safe characters
        // Allowed: alphanumeric (a-z, A-Z, 0-9), underscore (_), hyphen (-)
        // Reject: backticks, quotes, semicolons, spaces, control characters, etc.
        foreach (var c in identifier)
        {
            bool isValid = char.IsLetterOrDigit(c) || c == '_' || c == '-';
            if (!isValid)
            {
                throw new ValidationException(
                    $"Identifier '{identifier}' contains invalid character '{c}'. " +
                    "Only alphanumeric characters, underscore (_), and hyphen (-) are permitted. " +
                    "This restriction prevents SurrealQL injection attacks.");
            }
        }

        // No escaping needed - identifier contains only safe characters
        return identifier;
    }

    private static void ValidateTable(string table)
    {
        if (string.IsNullOrWhiteSpace(table))
            throw new ValidationException("Table name cannot be empty.");
    }

    private static void ValidateRecordId(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            throw new ValidationException("Record ID cannot be empty.");
    }

    private static void ValidateQuery(string surrealQL)
    {
        if (string.IsNullOrWhiteSpace(surrealQL))
            throw new ValidationException("Query cannot be empty.");
    }

    /// <summary>
    /// F6 Fix: Checks if a JSON response has a root-level "error" property.
    /// This prevents false positives from string-based error detection.
    /// </summary>
    /// <param name="jsonResponse">The JSON response string.</param>
    /// <returns>True if the response has a root-level error property; otherwise, false.</returns>
    private static bool HasRootLevelError(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            return doc.RootElement.TryGetProperty("error", out _);
        }
        catch
        {
            // If JSON parsing fails, assume no error (or let the caller handle invalid JSON)
            return false;
        }
    }

    #endregion

    // -------------------------------------------------------------------------
    // Migrations
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task MigrateAsync(Assembly migrationsAssembly, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_isConnected)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");
        var runner = new SurrealMigrationRunner(this);
        await runner.MigrateAsync(migrationsAssembly, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RollbackAsync(string migrationName, Assembly migrationsAssembly, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_isConnected)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");
        var runner = new SurrealMigrationRunner(this);
        await runner.RollbackAsync(migrationName, migrationsAssembly, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Transaction implementation that holds a dedicated pool connection for the transaction lifetime.
/// </summary>
internal class SurrealDbTransaction : ISurrealDbTransaction
{
    private readonly ISerializer _serializer;
    private readonly IProtocolAdapter _connection;
    private readonly IConnectionPool _connectionPool;
    private bool _disposed;
    private bool _committed;

    public SurrealDbTransaction(
        ISerializer serializer,
        IProtocolAdapter connection,
        IConnectionPool connectionPool)
    {
        _serializer = serializer;
        _connection = connection;
        _connectionPool = connectionPool;
    }

    public async Task<QueryResult> QueryAsync(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var body = parameters != null
                ? _serializer.Serialize(new { query = surrealQL, vars = parameters })
                : _serializer.Serialize(new { query = surrealQL });

            var response = await _connection.SendAsync("query", "/sql", body, cancellationToken).ConfigureAwait(false);
            return _serializer.Deserialize<QueryResult>(response) ?? new QueryResult { Status = "OK" };
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Transaction query execution failed", ex);
        }
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var body = parameters != null
                ? _serializer.Serialize(new { query = surrealQL, vars = parameters })
                : _serializer.Serialize(new { query = surrealQL });

            var response = await _connection.SendAsync("query", "/sql", body, cancellationToken).ConfigureAwait(false);
            var envelope = _serializer.Deserialize<SurrealDbResponse<T>>(response);
            envelope?.EnsureSuccess();
            return envelope?.Result ?? Enumerable.Empty<T>();
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Transaction query execution failed", ex);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await _connection.SendAsync("query", "/sql", "{\"query\":\"COMMIT;\"}", cancellationToken).ConfigureAwait(false);
            _committed = true;
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Transaction commit failed", ex);
        }
        finally
        {
            // Release connection back to pool after commit
            await _connectionPool.ReleaseAsync(_connection, true).ConfigureAwait(false);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await _connection.SendAsync("query", "/sql", "{\"query\":\"ROLLBACK;\"}", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Transaction rollback failed", ex);
        }
        finally
        {
            // Release connection back to pool after rollback
            await _connectionPool.ReleaseAsync(_connection, true).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;

            if (!_committed)
            {
                try
                {
                    // Rollback if not explicitly committed
                    await RollbackAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Suppress rollback errors during disposal
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SurrealDbTransaction));
    }
}
