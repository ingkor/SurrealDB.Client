namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when data validation fails.
/// </summary>
public class ValidationException : SurrealDbException
{
    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public ValidationException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
