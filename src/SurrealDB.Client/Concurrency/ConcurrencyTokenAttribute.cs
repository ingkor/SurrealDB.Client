namespace SurrealDB.Client.Concurrency;

using System;

/// <summary>
/// Marks a property as a concurrency token for optimistic locking.
/// Used for detecting concurrent modifications.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConcurrencyTokenAttribute : Attribute
{
    /// <summary>
    /// Creates a new ConcurrencyTokenAttribute.
    /// </summary>
    /// <param name="isRowVersion">Whether this is an auto-generated row version</param>
    public ConcurrencyTokenAttribute(bool isRowVersion = false)
    {
        IsRowVersion = isRowVersion;
    }

    /// <summary>
    /// Gets whether this is an auto-generated row version.
    /// </summary>
    public bool IsRowVersion { get; }
}
