namespace SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when serialization/deserialization fails.
/// </summary>
public class SerializationException : SurrealDbException
{
    public SerializationException(string message)
        : base(message)
    {
    }

    public SerializationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public SerializationException(string message, string? errorCode, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
