namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when authentication fails.
/// </summary>
public class AuthenticationException : SurrealDbException
{
    public AuthenticationException(string message)
        : base(message)
    {
    }

    public AuthenticationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public AuthenticationException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
