namespace SurrealDB.Client;

using System.Text.Json;
using Authentication;
using Connection;
using Exceptions;
using Protocol;
using Serialization;

/// <summary>
/// F10 Fix: Protocol method constants to avoid magic strings.
/// </summary>
internal static class ProtocolMethods
{
    public const string Query = "QUERY";
    public const string SignIn = "signin";
    public const string Ping = "ping";
}

/// <summary>
/// Main SurrealDB client implementation.
/// </summary>
public class SurrealDbClient : ISurrealDbClient
{
    private readonly SurrealDbClientOptions _options;
    private readonly ISerializer _serializer;
    private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1); // F4: Prevent concurrent ConnectAsync
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

    public async Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isConnected || _currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var provider = new BasicAuthenticationProvider(username, password);
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
    }

    public async Task AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isConnected || _currentConnection == null)
            throw new ConnectionException("Not connected. Call ConnectAsync first.");

        try
        {
            var provider = new TokenAuthenticationProvider(token);
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
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // TODO: Implement logout
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new ConnectionException("Logout failed", ex);
        }
    }

    #endregion

    #region CRUD Operations

    public async Task<T> CreateAsync<T>(string table, T data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTable(table);

        try
        {
            // TODO: Implement CREATE operation
            await Task.CompletedTask;
            return data;
        }
        catch (Exception ex)
        {
            throw new QueryException($"Failed to create record in {table}", ex);
        }
    }

    public async Task<IEnumerable<T>> CreateAsync<T>(string table, IEnumerable<T> data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTable(table);

        try
        {
            // TODO: Implement bulk CREATE operation
            await Task.CompletedTask;
            return data;
        }
        catch (Exception ex)
        {
            throw new QueryException($"Failed to create records in {table}", ex);
        }
    }

    public async Task<T?> GetAsync<T>(string recordId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        try
        {
            // TODO: Implement GET operation
            await Task.CompletedTask;
            return default;
        }
        catch (Exception ex)
        {
            throw new QueryException($"Failed to get record {recordId}", ex);
        }
    }

    public async Task<IEnumerable<T>> SelectAsync<T>(string table, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTable(table);

        try
        {
            // TODO: Implement SELECT operation
            await Task.CompletedTask;
            return Enumerable.Empty<T>();
        }
        catch (Exception ex)
        {
            throw new QueryException($"Failed to select from {table}", ex);
        }
    }

    public async Task<T> UpdateAsync<T>(string recordId, T data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        try
        {
            // TODO: Implement UPDATE operation
            await Task.CompletedTask;
            return data;
        }
        catch (Exception ex)
        {
            throw new QueryException($"Failed to update record {recordId}", ex);
        }
    }

    public async Task DeleteAsync(string recordId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        try
        {
            // TODO: Implement DELETE operation
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new QueryException($"Failed to delete record {recordId}", ex);
        }
    }

    public async Task<T> UpsertAsync<T>(string recordId, T data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRecordId(recordId);

        try
        {
            // TODO: Implement UPSERT operation
            await Task.CompletedTask;
            return data;
        }
        catch (Exception ex)
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

        try
        {
            // TODO: Implement raw query execution
            await Task.CompletedTask;
            return new QueryResult { Status = "OK" };
        }
        catch (Exception ex)
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

        try
        {
            // TODO: Implement typed query execution
            await Task.CompletedTask;
            return Enumerable.Empty<T>();
        }
        catch (Exception ex)
        {
            throw new QueryException("Query execution failed", ex);
        }
    }

    #endregion

    #region Transactions

    public async Task<ISurrealDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // TODO: Implement transaction
            await Task.CompletedTask;
            return new SurrealDbTransaction(this);
        }
        catch (Exception ex)
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
}

/// <summary>
/// Transaction implementation.
/// </summary>
internal class SurrealDbTransaction : ISurrealDbTransaction
{
    private readonly SurrealDbClient _client;

    public SurrealDbTransaction(SurrealDbClient client)
    {
        _client = client;
    }

    public async Task<QueryResult> QueryAsync(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement transactional query execution
        await Task.CompletedTask;
        return new QueryResult { Status = "OK" };
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement transactional typed query execution
        await Task.CompletedTask;
        return Enumerable.Empty<T>();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement transaction commit
        await Task.CompletedTask;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement transaction rollback
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Cleanup if needed
        await Task.CompletedTask;
    }
}
