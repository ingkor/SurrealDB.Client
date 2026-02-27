namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Base exception for all SurrealDB client errors.
/// </summary>
public abstract class SurrealDbException : Exception
{
    /// <summary>
    /// Gets the error code from the database.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Gets the additional details about the error.
    /// </summary>
    public Dictionary<string, object>? Details { get; set; }

    /// <summary>
    /// Initializes a new instance with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    protected SurrealDbException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    protected SurrealDbException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message, error code, and optional inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The database error code.</param>
    /// <param name="innerException">The inner exception.</param>
    protected SurrealDbException(string message, string? errorCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a detailed error message with error code and details.
    /// </summary>
    public override string ToString()
    {
        var message = base.ToString();
        if (!string.IsNullOrEmpty(ErrorCode))
            message += $"\nError Code: {ErrorCode}";

        if (Details?.Count > 0)
        {
            message += "\nDetails:";
            foreach (var (key, value) in Details)
                message += $"\n  {key}: {value}";
        }

        return message;
    }
}
