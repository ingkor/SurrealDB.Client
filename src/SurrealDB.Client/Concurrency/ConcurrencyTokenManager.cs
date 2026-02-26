namespace SurrealDB.Client.Concurrency;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Manages concurrency token tracking for optimistic locking.
/// </summary>
public class ConcurrencyTokenManager
{
    /// <summary>
    /// Gets the concurrency token property for an entity type.
    /// </summary>
    /// <param name="entityType">The entity type</param>
    /// <returns>The concurrency token property, or null if not found</returns>
    public static PropertyInfo? GetConcurrencyTokenProperty(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType
            .GetProperties(BindingFlags.Public | BindingFlags.IgnoreCase)
            .FirstOrDefault(p => p.GetCustomAttribute<ConcurrencyTokenAttribute>() != null);
    }

    /// <summary>
    /// Gets all concurrency token properties for an entity type.
    /// </summary>
    public static IEnumerable<PropertyInfo> GetConcurrencyTokenProperties(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType
            .GetProperties(BindingFlags.Public | BindingFlags.IgnoreCase)
            .Where(p => p.GetCustomAttribute<ConcurrencyTokenAttribute>() != null);
    }

    /// <summary>
    /// Gets the concurrency token value from an entity instance.
    /// </summary>
    /// <param name="entity">The entity instance</param>
    /// <param name="property">The concurrency token property</param>
    /// <returns>The token value</returns>
    public static object? GetTokenValue(object entity, PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        return property.GetValue(entity);
    }

    /// <summary>
    /// Sets the concurrency token value on an entity instance.
    /// </summary>
    /// <param name="entity">The entity instance</param>
    /// <param name="property">The concurrency token property</param>
    /// <param name="value">The new token value</param>
    public static void SetTokenValue(object entity, PropertyInfo property, object? value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        if (property.CanWrite)
            property.SetValue(entity, value);
    }

    /// <summary>
    /// Checks if a concurrency conflict exists.
    /// </summary>
    /// <param name="expectedToken">The token value before the update</param>
    /// <param name="actualToken">The current token value from the database</param>
    /// <returns>True if tokens match (no conflict), false if they differ</returns>
    public static bool HasNoConflict(object? expectedToken, object? actualToken)
    {
        if (expectedToken == null && actualToken == null)
            return true;

        if (expectedToken == null || actualToken == null)
            return false;

        return expectedToken.Equals(actualToken);
    }

    /// <summary>
    /// Increments a numeric concurrency token (e.g., version number).
    /// </summary>
    /// <param name="currentValue">The current token value</param>
    /// <returns>The incremented value</returns>
    public static object IncrementToken(object? currentValue)
    {
        return currentValue switch
        {
            int i => i + 1,
            long l => l + 1,
            short s => s + 1,
            byte b => (byte)(b + 1),
            _ => currentValue ?? 1
        };
    }

    /// <summary>
    /// Generates a new timestamp token.
    /// </summary>
    /// <returns>The current UTC timestamp</returns>
    public static DateTime GenerateTimestampToken()
    {
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Generates a new GUID-based token.
    /// </summary>
    /// <returns>A new GUID</returns>
    public static Guid GenerateGuidToken()
    {
        return Guid.NewGuid();
    }
}
