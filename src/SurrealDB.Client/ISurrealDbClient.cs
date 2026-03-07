namespace SurrealDB.Client;

using Session;

/// <summary>
/// Main interface for SurrealDB client operations.
/// </summary>
public interface ISurrealDbClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the current client options.
    /// </summary>
    SurrealDbClientOptions Options { get; }

    /// <summary>
    /// Gets whether the client is currently connected.
    /// </summary>
    bool IsConnected { get; }

    #region Sessions

    /// <summary>
    /// Creates a new session (Unit of Work) for managing entity changes.
    /// </summary>
    /// <returns>A new session for tracking and saving entity changes.</returns>
    ISurrealDbSession CreateSession();

    #endregion

    #region Connection Management

    /// <summary>
    /// Connects to the SurrealDB server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the connection is established.</returns>
    /// <exception cref="ConnectionException">Thrown if connection fails.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the SurrealDB server.
    /// </summary>
    /// <returns>A task that completes when the disconnection is complete.</returns>
    Task DisconnectAsync();

    /// <summary>
    /// Checks if the client is connected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connected, false otherwise.</returns>
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Authentication

    /// <summary>
    /// Authenticates with the database using credentials.
    /// SECURITY: For backward compatibility. Use AuthenticateAsync(SecureCredentials) for maximum security.
    /// </summary>
    /// <param name="username">Username.</param>
    /// <param name="password">Password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when authentication is complete.</returns>
    /// <exception cref="AuthenticationException">Thrown if authentication fails.</exception>
    Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates with the database using a token.
    /// SECURITY: For backward compatibility. Use AuthenticateAsync(SecureCredentials) for maximum security.
    /// </summary>
    /// <param name="token">Authentication token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when authentication is complete.</returns>
    /// <exception cref="AuthenticationException">Thrown if authentication fails.</exception>
    Task AuthenticateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out from the database and clears authentication session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when logout is complete.</returns>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Creates a new record.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="table">The table name.</param>
    /// <param name="data">The data to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created entity.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<T?> CreateAsync<T>(string table, T data, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Creates multiple new records.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="table">The table name.</param>
    /// <param name="data">The data to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created entities.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<IEnumerable<T?>> CreateAsync<T>(string table, IEnumerable<T> data, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets a record by ID.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="recordId">The record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entity or null if not found.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<T?> GetAsync<T>(string recordId, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Selects all records from a table.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="table">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entities.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<IEnumerable<T>> SelectAsync<T>(string table, int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a record.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="recordId">The record ID.</param>
    /// <param name="data">The updated data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated entity.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<T?> UpdateAsync<T>(string recordId, T data, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes a record.
    /// </summary>
    /// <param name="recordId">The record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the deletion is complete.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task DeleteAsync(string recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a record (upsert).
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="recordId">The record ID.</param>
    /// <param name="data">The data to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upserted entity.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<T?> UpsertAsync<T>(string recordId, T data, CancellationToken cancellationToken = default) where T : class;

    #endregion

    #region Query Execution

    /// <summary>
    /// Executes a raw SurrealQL query.
    /// </summary>
    /// <param name="surrealQL">The SurrealQL query string.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw query result.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<QueryResult> QueryAsync(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a raw SurrealQL query and returns typed results.
    /// </summary>
    /// <typeparam name="T">The result entity type.</typeparam>
    /// <param name="surrealQL">The SurrealQL query string.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query results.</returns>
    /// <exception cref="QueryException">Thrown if the query fails.</exception>
    Task<IEnumerable<T>> QueryAsync<T>(
        string surrealQL,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Transactions

    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A transaction object.</returns>
    /// <exception cref="QueryException">Thrown if the transaction cannot be started.</exception>
    Task<ISurrealDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Represents a transaction.
/// </summary>
public interface ISurrealDbTransaction : IAsyncDisposable
{
    /// <summary>
    /// Executes a query within the transaction.
    /// </summary>
    Task<QueryResult> QueryAsync(string surrealQL, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a typed query within the transaction.
    /// </summary>
    Task<IEnumerable<T>> QueryAsync<T>(string surrealQL, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a query.
/// </summary>
public class QueryResult
{
    /// <summary>
    /// Gets or sets the result data.
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Gets or sets the result status.
    /// </summary>
    public string Status { get; set; } = "OK";

    /// <summary>
    /// Gets or sets the execution time.
    /// </summary>
    public string? Time { get; set; }

    /// <summary>
    /// Gets or sets any error message.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets whether the query was successful.
    /// </summary>
    public bool IsSuccess => Status == "OK" && string.IsNullOrEmpty(Error);
}
