namespace SurrealDB.Client.Serialization;

/// <summary>
/// Provides JSON serialization/deserialization services.
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// Serializes an object to JSON string.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>JSON string.</returns>
    string Serialize(object? obj);

    /// <summary>
    /// Deserializes a JSON string to an object.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="json">JSON string.</param>
    /// <returns>Deserialized object.</returns>
    T? Deserialize<T>(string json);

    /// <summary>
    /// Deserializes a JSON string to an object of specified type.
    /// </summary>
    /// <param name="json">JSON string.</param>
    /// <param name="type">The target type.</param>
    /// <returns>Deserialized object.</returns>
    object? Deserialize(string json, Type type);
}
