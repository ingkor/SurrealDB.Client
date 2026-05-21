namespace SurrealDB.Client.Protocol;

using System.Net.Http;

/// <summary>
/// Factory for creating protocol adapters based on client options.
/// </summary>
internal static class ProtocolAdapterFactory
{
    /// <summary>
    /// Creates a protocol adapter based on the configured protocol type.
    /// </summary>
    public static async Task<IProtocolAdapter> CreateAdapterAsync(
        SurrealDbClientOptions options,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(options.ConnectionString);

        return options.Protocol switch
        {
            ProtocolType.Http => CreateHttpAdapter(uri, options),
            ProtocolType.WebSocket => CreateWebSocketAdapter(uri, options),
            _ => throw new ArgumentException($"Unsupported protocol type: {options.Protocol}")
        };
    }

    /// <summary>
    /// Gets the appropriate HTTP scheme based on options.
    /// </summary>
    private static Uri GetUri(Uri baseUri, SurrealDbClientOptions options)
    {
        var scheme = options.UseHttps ? "https" : "http";
        return new UriBuilder(baseUri) { Scheme = scheme }.Uri;
    }

    /// <summary>
    /// SECURITY: Creates an HTTP protocol adapter with certificate validation.
    /// P1-3: Enforces certificate validation unless explicitly disabled with risk acknowledgment.
    /// </summary>
    private static IProtocolAdapter CreateHttpAdapter(Uri baseUri, SurrealDbClientOptions options)
    {
        var uri = GetUri(baseUri, options);

        var httpClientHandler = new HttpClientHandler();

        // SECURITY: P1-3 - Certificate validation is only disabled if explicitly acknowledged
        // This is validated in SurrealDbClientOptions.Validate()
        if (!options.VerifyServerCertificate)
        {
            // Developer has explicitly acknowledged the risk via AcknowledgeCertificateValidationRisk
#pragma warning disable S4830 // Server certificates should be verified
            httpClientHandler.ServerCertificateCustomValidationCallback =
                (_, _, _, _) => true;
#pragma warning restore S4830
        }
        // else: Use default certificate validation (secure)

        var httpClient = new HttpClient(httpClientHandler, disposeHandler: true);
        httpClient.Timeout = options.CommandTimeout;

        // Add NS/DB headers for SurrealDB HTTP API
        if (!string.IsNullOrEmpty(options.Namespace))
            httpClient.DefaultRequestHeaders.Add("surreal-ns", options.Namespace);
        if (!string.IsNullOrEmpty(options.Database))
            httpClient.DefaultRequestHeaders.Add("surreal-db", options.Database);

        return new HttpProtocolAdapter(uri, httpClient, options);
    }

    /// <summary>
    /// Creates a WebSocket protocol adapter.
    /// </summary>
    private static IProtocolAdapter CreateWebSocketAdapter(Uri baseUri, SurrealDbClientOptions options)
    {
        var uri = GetUri(baseUri, options);
        return new WebSocketProtocolAdapter(uri, options);
    }
}
