namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when a connection-related error occurs.
/// </summary>
public class ConnectionException : SurrealDbException
{
    public ConnectionException(string message)
        : base(message)
    {
    }

    public ConnectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public ConnectionException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
