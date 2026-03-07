namespace SurrealDB.Client.Concurrency;

using System;

/// <summary>
/// Thrown when a concurrency conflict is detected during update.
/// Indicates that the entity was modified by another client.
/// </summary>
public class DbUpdateConcurrencyException : Exception
{
    /// <summary>
    /// Creates a new DbUpdateConcurrencyException.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public DbUpdateConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new DbUpdateConcurrencyException with default message.
    /// </summary>
    /// <param name="entityId">The ID of the entity that failed to update</param>
    /// <param name="expectedVersion">The expected concurrency token value</param>
    /// <param name="actualVersion">The actual concurrency token value</param>
    public static DbUpdateConcurrencyException Create(string entityId, object? expectedVersion, object? actualVersion)
    {
        return new DbUpdateConcurrencyException(
            $"Concurrency conflict for entity '{entityId}'. " +
            $"Expected version '{expectedVersion}' but found '{actualVersion}'. " +
            $"The entity was modified by another client.");
    }
}
