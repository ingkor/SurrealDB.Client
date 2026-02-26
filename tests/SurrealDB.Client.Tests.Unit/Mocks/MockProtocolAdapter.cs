namespace SurrealDB.Client.Tests.Unit.Mocks;

using SurrealDB.Client.Protocol;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Mock implementation of IProtocolAdapter for unit testing without a live server.
/// </summary>
internal class MockProtocolAdapter : IProtocolAdapter
{
    private readonly Dictionary<string, string> _responses = new();
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public MockProtocolAdapter()
    {
        InitializeDefaultResponses();
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<string> SendAsync(
        string method,
        string path,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Generate a response key based on method and path
        var key = $"{method}:{path}";

        if (_responses.TryGetValue(key, out var response))
        {
            return Task.FromResult(response);
        }

        // Default response for unknown requests
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "1.5ms",
            result = new object[] { }
        }));
    }

    public Task<string> AuthenticateAsync(
        string credentials,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "1.2ms",
            result = new { token = "mock-token" }
        }));
    }

    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IsConnected);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            IsConnected = false;
        }
        return default;
    }

    /// <summary>
    /// Sets a canned response for a specific method and path combination.
    /// </summary>
    public void SetResponse(string method, string path, string response)
    {
        var key = $"{method}:{path}";
        _responses[key] = response;
    }

    /// <summary>
    /// Clears all responses and resets to defaults.
    /// </summary>
    public void ResetResponses()
    {
        _responses.Clear();
        InitializeDefaultResponses();
    }

    private void InitializeDefaultResponses()
    {
        // CREATE response
        SetResponse("query", "/sql", JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "2.1ms",
            result = new[] { new { id = "test:1", name = "Test" } }
        }));

        // GET response (found)
        SetResponse("query", "/get", JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "1.8ms",
            result = new[] { new { id = "test:1", name = "Test" } }
        }));

        // SELECT response
        SetResponse("query", "/select", JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "3.2ms",
            result = new[]
            {
                new { id = "test:1", name = "Test 1" },
                new { id = "test:2", name = "Test 2" }
            }
        }));

        // UPDATE response
        SetResponse("query", "/update", JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "2.5ms",
            result = new[] { new { id = "test:1", name = "Updated" } }
        }));

        // DELETE response
        SetResponse("query", "/delete", JsonSerializer.Serialize(new
        {
            status = "OK",
            time = "1.9ms",
            result = new object[] { }
        }));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MockProtocolAdapter));
    }
}
