namespace SurrealDB.Client.Protocol;

using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Exceptions;

/// <summary>
/// F10 Fix: Protocol method constants to avoid magic strings.
/// </summary>
internal static class ProtocolMethods
{
    public const string Query = "QUERY";
    public const string SignIn = "signin";
    public const string Ping = "ping";
}

/// <summary>
/// WebSocket protocol adapter for SurrealDB communication.
/// </summary>
internal class WebSocketProtocolAdapter : IProtocolAdapter
{
    private readonly Uri _baseUri;
    private readonly SurrealDbClientOptions _options;
    private ClientWebSocket? _webSocket;
    private bool _disposed;
    private int _requestId = 0;

    /// <summary>
    /// Maximum allowed response size (50 MB). Prevents OOM attacks from oversized messages.
    /// </summary>
    private const int MaxResponseSizeBytes = 50 * 1024 * 1024;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public WebSocketProtocolAdapter(Uri baseUri, SurrealDbClientOptions options)
    {
        _baseUri = baseUri;
        _options = options;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _webSocket = new ClientWebSocket();

            var wsUri = new UriBuilder(_baseUri)
            {
                Scheme = _baseUri.Scheme == "https" ? "wss" : "ws"
            }.Uri;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ConnectionTimeout);

            await _webSocket.ConnectAsync(wsUri, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"WebSocket connection timed out after {_options.ConnectionTimeout.TotalSeconds} seconds");
        }
        catch (WebSocketException ex)
        {
            throw new ConnectionException("Failed to establish WebSocket connection", ex);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new ConnectionException("Unexpected error during WebSocket connection", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        if (_webSocket != null && IsConnected)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing connection",
                    CancellationToken.None);
            }
            catch
            {
                // Suppress errors during disconnection
            }
        }
    }

    public async Task<string> SendAsync(
        string method,
        string path,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_webSocket == null || !IsConnected)
        {
            throw new ConnectionException("WebSocket is not connected");
        }

        try
        {
            // For WebSocket, we'll send as a JSON-RPC style message
            var requestId = Interlocked.Increment(ref _requestId);
            // F1 Fix: Use JsonSerializer.Serialize to prevent JSON injection
            var message = $"{{\"id\":{requestId},\"method\":{JsonSerializer.Serialize(method)},\"path\":{JsonSerializer.Serialize(path)},\"params\":{body ?? "{}"}}}";

            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CommandTimeout);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cts.Token);

            // Receive and accumulate all WebSocket frames until EndOfMessage
            return await ReceiveFullMessageAsync(_webSocket, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"WebSocket operation timed out after {_options.CommandTimeout.TotalSeconds} seconds");
        }
        catch (WebSocketException ex)
        {
            throw new ConnectionException("WebSocket communication error", ex);
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new QueryException("WebSocket query execution failed", ex);
        }
    }

    public async Task<string> AuthenticateAsync(
        string credentials,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_webSocket == null || !IsConnected)
        {
            throw new ConnectionException("WebSocket is not connected");
        }

        try
        {
            var requestId = Interlocked.Increment(ref _requestId);
            // F1 Fix: Use JsonSerializer.Serialize to prevent JSON injection
            // F10 Fix: Use constant for method name
            var message = $"{{\"id\":{requestId},\"method\":{JsonSerializer.Serialize(ProtocolMethods.SignIn)},\"params\":[{credentials}]}}";

            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CommandTimeout);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cts.Token);

            // Receive and accumulate all WebSocket frames until EndOfMessage
            var response = await ReceiveFullMessageAsync(_webSocket, cts.Token);

            // F6 Fix: Parse JSON and check for root-level error property instead of string search
            if (HasRootLevelError(response))
            {
                throw new AuthenticationException("Authentication failed");
            }

            return response;
        }
        catch (Exception ex) when (!(ex is SurrealDbException))
        {
            throw new AuthenticationException("WebSocket authentication failed", ex);
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            if (_webSocket?.State != WebSocketState.Open)
                return false;

            var requestId = Interlocked.Increment(ref _requestId);
            // F10 Fix: Use constant for method name
            var message = $"{{\"id\":{requestId},\"method\":{JsonSerializer.Serialize(ProtocolMethods.Ping)}}}";

            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cts.Token);

            // Use the same receive path as all other methods to handle multi-frame responses
            // If server sends multi-frame pong, single-frame receive would leave stale data
            var response = await ReceiveFullMessageAsync(_webSocket, cts.Token);
            return !string.IsNullOrEmpty(response);
        }
        catch
        {
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
            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await DisconnectAsync();
                }

                _webSocket.Dispose();
            }
        }
        catch
        {
            // Suppress errors during disposal
        }
    }

    /// <summary>
    /// Receives a complete logical WebSocket message by accumulating frames until
    /// <see cref="WebSocketReceiveResult.EndOfMessage"/> is <see langword="true"/>.
    /// Uses <see cref="ArrayPool{T}"/> for the per-frame read buffer and a
    /// <see cref="MemoryStream"/> to accumulate data across frames. Enforces a 50 MB
    /// size cap to guard against OOM attacks.
    /// </summary>
    /// <param name="webSocket">The WebSocket to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full decoded UTF-8 response string.</returns>
    /// <exception cref="ConnectionException">
    /// Thrown when the accumulated message exceeds <see cref="MaxResponseSizeBytes"/>.
    /// </exception>
    internal static async Task<string> ReceiveFullMessageAsync(
        WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        // Rent a 16 KB frame buffer from the shared pool.
        var frameBuffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            // Pre-size to 16 KB to avoid multiple reallocations for typical messages
            using var ms = new MemoryStream(16 * 1024);

            WebSocketReceiveResult result;
            do
            {
                result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(frameBuffer),
                    cancellationToken);

                if (ms.Length + result.Count > MaxResponseSizeBytes)
                {
                    throw new ConnectionException(
                        $"WebSocket response exceeded the maximum allowed size of {MaxResponseSizeBytes / (1024 * 1024)} MB. " +
                        "Possible OOM attack or unexpectedly large payload.");
                }

                ms.Write(frameBuffer, 0, result.Count);

            } while (!result.EndOfMessage);

            // Decode directly from the underlying buffer to avoid an extra allocation
            // that ms.ToArray() would introduce.
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
        finally
        {
            // ALWAYS return the rented buffer — even on exception, cancellation, or
            // size-limit violation — to prevent ArrayPool leaks.
            // Clear sensitive data (auth tokens, user data) from the buffer.
            ArrayPool<byte>.Shared.Return(frameBuffer, clearArray: true);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WebSocketProtocolAdapter));
    }

    /// <summary>
    /// F6 Fix: Checks if a JSON response has a root-level "error" property.
    /// This prevents false positives from string-based error detection.
    /// </summary>
    /// <param name="jsonResponse">The JSON response string.</param>
    /// <returns>True if the response has a root-level error property; otherwise, false.</returns>
    private static bool HasRootLevelError(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            return doc.RootElement.TryGetProperty("error", out _);
        }
        catch
        {
            // If JSON parsing fails, assume no error (or let the caller handle invalid JSON)
            return false;
        }
    }
}
