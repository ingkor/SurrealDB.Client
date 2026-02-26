namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when a query execution error occurs.
/// </summary>
public class QueryException : SurrealDbException
{
    public QueryException(string message)
        : base(message)
    {
    }

    public QueryException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public QueryException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
