namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when a query execution error occurs.
/// </summary>
public class QueryException : SurrealDbException
{
    /// <summary>
    /// Initializes a new instance with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QueryException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QueryException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message, error code, and optional inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The database error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public QueryException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
