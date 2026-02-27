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
                throw new ConnectionException(
                    $"Failed to connect to SurrealDB: HTTP {response.StatusCode}");
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
                throw new QueryException(
                    $"Query failed with HTTP {response.StatusCode}: {errorContent}");
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
}
