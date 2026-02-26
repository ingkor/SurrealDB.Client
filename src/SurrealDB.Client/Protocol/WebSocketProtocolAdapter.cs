namespace SurrealDB.Client.Protocol;

using System.Net.WebSockets;
using System.Text;

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
            var message = $"{{\"id\":{requestId},\"method\":\"{method}\",\"path\":\"{path}\",\"params\":{body ?? "{}"}}}";

            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CommandTimeout);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cts.Token);

            // Receive response
            var buffer = new byte[1024 * 4];
            var result = await _webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cts.Token);

            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
            return response;
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
            var message = $"{{\"id\":{requestId},\"method\":\"signin\",\"params\":[{credentials}]}}";

            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CommandTimeout);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cts.Token);

            var buffer = new byte[1024 * 4];
            var result = await _webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cts.Token);

            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);

            if (response.Contains("error", StringComparison.OrdinalIgnoreCase))
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
            var message = $"{{\"id\":{requestId},\"method\":\"ping\"}}";

            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cts.Token);

            var buffer = new byte[1024];
            var result = await _webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cts.Token);

            return result.MessageType == WebSocketMessageType.Text;
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WebSocketProtocolAdapter));
    }
}
