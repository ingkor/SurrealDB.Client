namespace SurrealDB.Client.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;
using Exceptions;

/// <summary>
/// System.Text.Json serializer implementation.
/// </summary>
public class SystemTextJsonSerializer : ISerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of SystemTextJsonSerializer.
    /// </summary>
    /// <param name="options">Custom JSON serializer options (optional).</param>
    public SystemTextJsonSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? DefaultOptions;
    }

    public string Serialize(object? obj)
    {
        try
        {
            return JsonSerializer.Serialize(obj, _options);
        }
        catch (Exception ex)
        {
            throw new SerializationException($"Serialization failed: {ex.Message}", ex);
        }
    }

    public T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        catch (JsonException ex)
        {
            throw new SerializationException(
                $"Deserialization to {typeof(T).Name} failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new SerializationException(
                $"Unexpected error during deserialization: {ex.Message}", ex);
        }
    }

    public object? Deserialize(string json, Type type)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, type, _options);
        }
        catch (JsonException ex)
        {
            throw new SerializationException(
                $"Deserialization to {type.Name} failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new SerializationException(
                $"Unexpected error during deserialization: {ex.Message}", ex);
        }
    }
}
