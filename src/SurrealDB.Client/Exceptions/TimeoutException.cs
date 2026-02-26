namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when an operation times out.
/// </summary>
public class TimeoutException : SurrealDbException
{
    public TimeoutException(string message)
        : base(message)
    {
    }

    public TimeoutException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public TimeoutException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
