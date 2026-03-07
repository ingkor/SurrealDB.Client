namespace SurrealDB.Client.Serialization;

using System.Text.Json.Serialization;
using Exceptions;

/// <summary>
/// Represents the JSON-RPC response envelope from SurrealDB.
/// </summary>
internal class SurrealDbResponse<T>
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("result")]
    public List<T>? Result { get; set; }

    [JsonPropertyName("error")]
    public SurrealDbErrorInfo? Error { get; set; }

    public void EnsureSuccess()
    {
        if (Error != null)
            throw new QueryException($"SurrealDB error {Error.Code}: {Error.Message}");
    }
}

internal class SurrealDbErrorInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
