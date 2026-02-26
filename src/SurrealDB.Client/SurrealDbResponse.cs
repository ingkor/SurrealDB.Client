namespace SurrealDB.Client;

using System.Text.Json.Serialization;
using Exceptions;

/// <summary>
/// Represents the envelope structure returned by SurrealDB for all query responses.
/// SurrealDB wraps results in: { "status": "OK", "time": "1.5ms", "result": [...] }
/// </summary>
/// <typeparam name="T">The type of items contained in the result array.</typeparam>
public class SurrealDbResponse<T>
{
    /// <summary>
    /// The response status from SurrealDB. "OK" indicates success; any other value indicates an error.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The time taken by SurrealDB to execute the query (e.g., "1.5ms").
    /// </summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// The result array containing the deserialized records returned by SurrealDB.
    /// </summary>
    [JsonPropertyName("result")]
    public T[] Result { get; set; } = Array.Empty<T>();

    /// <summary>
    /// Returns true if the response status is "OK".
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates that the response status is "OK" and throws a <see cref="QueryException"/>
    /// if the status indicates an error.
    /// </summary>
    /// <exception cref="QueryException">Thrown when Status is not "OK".</exception>
    public void EnsureSuccess()
    {
        if (!IsSuccess)
        {
            throw new QueryException(
                $"SurrealDB returned error status '{Status}'. Time: {Time}");
        }
    }

    /// <summary>
    /// Validates this response and returns the result array. Throws if the status is not "OK".
    /// </summary>
    /// <returns>The result array of type <typeparamref name="T"/>[].</returns>
    /// <exception cref="QueryException">Thrown when Status is not "OK".</exception>
    public T[] GetResultOrThrow()
    {
        EnsureSuccess();
        return Result;
    }
}
