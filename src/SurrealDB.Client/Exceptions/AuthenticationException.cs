namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when authentication fails.
/// </summary>
public class AuthenticationException : SurrealDbException
{
    /// <summary>
    /// Initializes a new instance with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AuthenticationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message, error code, and optional inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The database error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public AuthenticationException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
