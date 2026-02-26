namespace SurrealDB.Client;

using Authentication;
using Connection;
using Protocol;
using Serialization;

/// <summary>
/// Main SurrealDB client implementation.
/// </summary>
public class SurrealDbClient : ISurrealDbClient
{
    private readonly SurrealDbClientOptions _options;
    private readonly ISerializer _serializer;
    private IConnectionPool? _connectionPool;
    private IProtocolAdapter? _currentConnection;
    private AuthenticationSession? _authSession;
    private bool _isConnected;
    private bool _disposed;

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

        try
        {
            // Initialize connection pool
            _connectionPool = new ConnectionPool(
                _options,
                ct => ProtocolAdapterFactory.CreateAdapterAsync(_options, ct));

            await _connectionPool.InitializeAsync(cancellationToken);

            // Get a test connection to verify it works
            _currentConnection = await _connectionPool.AcquireAsync(cancellationToken);

            // Verify connection
            await _currentConnection.ConnectAsync(cancellationToken);

            // Set namespace and database context
            await _currentConnection.SendAsync(
                "query",
                "/sql",
                $"{{\"query\":\"USE NS {_options.Namespace} DB {_options.Database};\"}}",
                cancellationToken).ConfigureAwait(false);

            _isConnected = true;
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new ConnectionException($"Failed to connect to {_options.ConnectionString}", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        try
        {
            // TODO: Implement disconnection logic
            _isConnected = false;
            await Task.CompletedTask;
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

    public async Task<bool> DeleteAsync(string recordId, CancellationToken cancellationToken = default)
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
            return true;  // Idempotent: return true if DELETE was acknowledged
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
            await DisconnectAsync();
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

    #endregion
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
