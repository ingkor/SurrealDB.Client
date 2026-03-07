namespace SurrealDB.Client.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interceptor for SurrealDB operations.
/// Allows hooking into query execution, connection lifecycle, etc.
/// </summary>
public interface ISurrealDbInterceptor
{
    /// <summary>
    /// Called before query execution.
    /// </summary>
    Task OnQueryExecuting(QueryExecutingEventArgs args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after query execution.
    /// </summary>
    Task OnQueryExecuted(QueryExecutedEventArgs args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before connection opening.
    /// </summary>
    Task OnConnectionOpening(ConnectionOpeningEventArgs args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after connection opened.
    /// </summary>
    Task OnConnectionOpened(ConnectionOpenedEventArgs args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before SaveChangesAsync.
    /// </summary>
    Task OnSaveChangesExecuting(SaveChangesEventArgs args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after SaveChangesAsync.
    /// </summary>
    Task OnSaveChangesExecuted(SaveChangesEventArgs args, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event args for query execution.
/// </summary>
public class QueryExecutingEventArgs
{
    /// <summary>
    /// The SurrealQL query string.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// When the query started executing.
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Can be set to true to cancel execution.
    /// </summary>
    public bool IsCancelled { get; set; }
}

/// <summary>
/// Event args for query executed.
/// </summary>
public class QueryExecutedEventArgs
{
    /// <summary>
    /// The query that was executed.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Number of rows affected.
    /// </summary>
    public int RowsAffected { get; set; }

    /// <summary>
    /// Duration of execution.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Any exception that occurred.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Whether execution succeeded.
    /// </summary>
    public bool Success => Exception == null;
}

/// <summary>
/// Event args for connection opening.
/// </summary>
public class ConnectionOpeningEventArgs
{
    /// <summary>
    /// The connection string (with password masked).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Can be set to true to cancel connection.
    /// </summary>
    public bool IsCancelled { get; set; }
}

/// <summary>
/// Event args for connection opened.
/// </summary>
public class ConnectionOpenedEventArgs
{
    /// <summary>
    /// Time when connection opened.
    /// </summary>
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether opening succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Any exception that occurred.
    /// </summary>
    public Exception? Exception { get; set; }
}

/// <summary>
/// Event args for SaveChangesAsync.
/// </summary>
public class SaveChangesEventArgs
{
    /// <summary>
    /// Number of entities being saved.
    /// </summary>
    public int EntityCount { get; set; }

    /// <summary>
    /// Number of changes.
    /// </summary>
    public int ChangeCount { get; set; }

    /// <summary>
    /// When SaveChanges started.
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of SaveChanges.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Any exception that occurred.
    /// </summary>
    public Exception? Exception { get; set; }
}
