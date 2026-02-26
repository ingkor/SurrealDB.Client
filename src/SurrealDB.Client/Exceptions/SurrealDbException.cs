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

    protected SurrealDbException(string message) : base(message)
    {
    }

    protected SurrealDbException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

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
