namespace SurrealDB.Client.Protocol;

using System.Net.Http;
using System.Text;
using Exceptions;

/// <summary>
/// HTTP protocol adapter for SurrealDB communication.
/// </summary>
internal class HttpProtocolAdapter : IProtocolAdapter
{
    private readonly Uri _baseUri;
    private readonly HttpClient _httpClient;
    private readonly SurrealDbClientOptions _options;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public HttpProtocolAdapter(Uri baseUri, HttpClient httpClient, SurrealDbClientOptions options)
    {
        _baseUri = baseUri;
        _httpClient = httpClient;
        _options = options;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Verify connection with a simple query
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "query"))
            {
                Content = new StringContent("PING", Encoding.UTF8, "application/json")
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ConnectionTimeout);

            var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                // SECURITY: Sanitize error message to prevent information disclosure
                var sanitizedMessage = SanitizeErrorMessage(errorContent, response.StatusCode);
                throw new ConnectionException($"Failed to connect to SurrealDB: {sanitizedMessage}");
            }

            IsConnected = true;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Connection attempt timed out after {_options.ConnectionTimeout.TotalSeconds} seconds");
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException("Failed to connect to SurrealDB", ex);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new ConnectionException("Unexpected error during connection", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        try
        {
            IsConnected = false;
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new ConnectionException("Failed to disconnect", ex);
        }
    }

    public async Task<string> SendAsync(
        string method,
        string path,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new ConnectionException("Not connected to database");
        }

        try
        {
            var httpMethod = GetHttpMethod(method);
            var requestUri = new Uri(_baseUri, path);

            var request = new HttpRequestMessage(httpMethod, requestUri);

            if (!string.IsNullOrEmpty(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            // Apply timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CommandTimeout);

            var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                // SECURITY: Sanitize error message to prevent information disclosure
                var sanitizedMessage = SanitizeErrorMessage(errorContent, response.StatusCode);
                throw new QueryException(sanitizedMessage);
            }

            return await response.Content.ReadAsStringAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Query timed out after {_options.CommandTimeout.TotalSeconds} seconds");
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException("HTTP request failed", ex);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("Query execution failed", ex);
        }
    }

    public async Task<string> AuthenticateAsync(
        string credentials,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new ConnectionException("Not connected to database");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "signin"))
            {
                Content = new StringContent(credentials, Encoding.UTF8, "application/json")
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CommandTimeout);

            var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new AuthenticationException("Authentication failed");
            }

            return await response.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("Authentication failed", ex);
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, "health"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode && IsConnected;
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await DisconnectAsync();
        }
        catch
        {
            // Suppress errors during disposal
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HttpProtocolAdapter));
    }

    private static HttpMethod GetHttpMethod(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            _ => throw new ArgumentException($"Unsupported HTTP method: {method}")
        };
    }

    /// <summary>
    /// SECURITY: Sanitizes error messages to prevent information disclosure.
    /// P0-1: Error Message Exposure - Server error responses expose sensitive information.
    ///
    /// This method maps HTTP status codes to generic, safe error messages that don't
    /// expose internal details like stack traces, schema information, query details,
    /// or authentication information.
    /// </summary>
    /// <param name="errorContent">The original error content from the server (not used, logged internally only).</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>A sanitized, generic error message safe for external consumption.</returns>
    private static string SanitizeErrorMessage(string errorContent, System.Net.HttpStatusCode statusCode)
    {
        // TODO: Log full error details internally for debugging
        // For now, return generic message based on status code only
        return statusCode switch
        {
            System.Net.HttpStatusCode.BadRequest => "Invalid request format",
            System.Net.HttpStatusCode.Unauthorized => "Authentication failed",
            System.Net.HttpStatusCode.Forbidden => "Access denied",
            System.Net.HttpStatusCode.NotFound => "Resource not found",
            >= System.Net.HttpStatusCode.InternalServerError => "Server error occurred",
            _ => "Operation failed"
        };
    }
}
