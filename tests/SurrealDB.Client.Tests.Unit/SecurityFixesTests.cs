using System.Reflection;
using System.Text;
using Xunit;
using Moq;
using SurrealDB.Client.Exceptions;
using SurrealDB.Client.Protocol;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for security fixes (F1 and F6).
/// </summary>
public class SecurityFixesTests
{
    #region F1: JSON Injection Prevention Tests

    [Theory]
    [InlineData("test\"method")]
    [InlineData("method\\ninjection")]
    [InlineData("method\twith\ttabs")]
    [InlineData("method\"},{\"injected\":\"value")]
    [InlineData("method\u0000null")]
    [InlineData("method\\u0022escaped")]
    public async Task F1_SendAsync_WithSpecialCharactersInMethod_ProducesValidJson(string method)
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "ws://localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), options);

        // Use reflection to access private fields for testing
        var webSocketField = typeof(WebSocketProtocolAdapter)
            .GetField("_webSocket", BindingFlags.NonPublic | BindingFlags.Instance);

        var mockWebSocket = new MockClientWebSocket();
        webSocketField?.SetValue(adapter, mockWebSocket);

        // Act & Assert
        try
        {
            await adapter.SendAsync(method, "/test/path", null);
        }
        catch
        {
            // We expect this to fail since we're using a mock, but we can verify the message
        }

        // Verify that the sent message is valid JSON
        var sentMessage = mockWebSocket.LastSentMessage;
        Assert.NotNull(sentMessage);

        // Parse as JSON - this will throw if invalid
        var jsonDoc = System.Text.Json.JsonDocument.Parse(sentMessage);
        Assert.NotNull(jsonDoc);

        // Verify the method field exists and has the correct value
        var methodProperty = jsonDoc.RootElement.GetProperty("method");
        Assert.Equal(method, methodProperty.GetString());
    }

    [Theory]
    [InlineData("/path\"injection")]
    [InlineData("/path\\nwith\\nnewlines")]
    [InlineData("/path\u0022unicode")]
    [InlineData("/NS `test` DB `test`;")]
    [InlineData("/path\"},{\"sql\":\"DROP DATABASE")]
    public async Task F1_SendAsync_WithSpecialCharactersInPath_ProducesValidJson(string path)
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "ws://localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), options);

        var webSocketField = typeof(WebSocketProtocolAdapter)
            .GetField("_webSocket", BindingFlags.NonPublic | BindingFlags.Instance);

        var mockWebSocket = new MockClientWebSocket();
        webSocketField?.SetValue(adapter, mockWebSocket);

        // Act & Assert
        try
        {
            await adapter.SendAsync("TEST", path, null);
        }
        catch
        {
            // Expected to fail with mock
        }

        // Verify valid JSON
        var sentMessage = mockWebSocket.LastSentMessage;
        Assert.NotNull(sentMessage);

        var jsonDoc = System.Text.Json.JsonDocument.Parse(sentMessage);
        var pathProperty = jsonDoc.RootElement.GetProperty("path");
        Assert.Equal(path, pathProperty.GetString());
    }

    [Theory]
    [InlineData("test\"namespace", "test\"database")]
    [InlineData("test\\namespace", "test\\database")]
    [InlineData("test\nnamesp", "test\ndb")]
    [InlineData("`test`", "`db`")]
    public async Task F1_AuthenticateAsync_WithSpecialCharactersInMethod_ProducesValidJson(string ns, string db)
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "ws://localhost:8000",
            Namespace = ns,
            Database = db
        };

        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), options);

        var webSocketField = typeof(WebSocketProtocolAdapter)
            .GetField("_webSocket", BindingFlags.NonPublic | BindingFlags.Instance);

        var mockWebSocket = new MockClientWebSocket();
        webSocketField?.SetValue(adapter, mockWebSocket);

        // Act & Assert
        try
        {
            await adapter.AuthenticateAsync("{\"user\":\"test\",\"pass\":\"test\"}", CancellationToken.None);
        }
        catch
        {
            // Expected to fail with mock
        }

        // Verify valid JSON
        var sentMessage = mockWebSocket.LastSentMessage;
        Assert.NotNull(sentMessage);

        var jsonDoc = System.Text.Json.JsonDocument.Parse(sentMessage);
        var methodProperty = jsonDoc.RootElement.GetProperty("method");
        Assert.Equal("signin", methodProperty.GetString());
    }

    #endregion

    #region F6: Fragile Error Detection Tests

    [Fact]
    public async Task F6_AuthenticateAsync_WithErrorInFieldValue_DoesNotThrowFalsePositive()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "ws://localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), options);

        var webSocketField = typeof(WebSocketProtocolAdapter)
            .GetField("_webSocket", BindingFlags.NonPublic | BindingFlags.Instance);

        // Response with "error" in a field value, but no root-level error property
        var responseJson = "{\"id\":1,\"result\":{\"message\":\"This is not an error, just a test\"}}";
        var mockWebSocket = new MockClientWebSocket(responseJson);
        webSocketField?.SetValue(adapter, mockWebSocket);

        // Act - should NOT throw because "error" is in a field value, not a root property
        var result = await adapter.AuthenticateAsync("{\"user\":\"test\",\"pass\":\"test\"}", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("result", result);
    }

    [Fact]
    public async Task F6_AuthenticateAsync_WithRootLevelError_ThrowsAuthenticationException()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "ws://localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), options);

        var webSocketField = typeof(WebSocketProtocolAdapter)
            .GetField("_webSocket", BindingFlags.NonPublic | BindingFlags.Instance);

        // Response with actual root-level error property
        var responseJson = "{\"id\":1,\"error\":{\"code\":-32000,\"message\":\"Authentication failed\"}}";
        var mockWebSocket = new MockClientWebSocket(responseJson);
        webSocketField?.SetValue(adapter, mockWebSocket);

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            adapter.AuthenticateAsync("{\"user\":\"test\",\"pass\":\"test\"}", CancellationToken.None));
    }

    [Fact]
    public void F6_HasRootLevelError_WithRootError_ReturnsTrue()
    {
        // Arrange
        var json = "{\"error\":{\"code\":123,\"message\":\"test error\"}}";

        // Act - use reflection to call private method
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { json })!;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void F6_HasRootLevelError_WithErrorInNestedField_ReturnsFalse()
    {
        // Arrange
        var json = "{\"result\":{\"status\":\"ok\",\"data\":{\"error\":\"This is just a field value\"}}}";

        // Act
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { json })!;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void F6_HasRootLevelError_WithInvalidJson_ReturnsFalse()
    {
        // Arrange
        var invalidJson = "this is not json";

        // Act
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { invalidJson })!;

        // Assert - should return false and not throw
        Assert.False(result);
    }

    [Fact]
    public void F6_HasRootLevelError_WithEmptyJson_ReturnsFalse()
    {
        // Arrange
        var json = "{}";

        // Act
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { json })!;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void F6_HasRootLevelError_WithStringContainingError_ReturnsFalse()
    {
        // Arrange - "error" appears in string value but not as property
        var json = "{\"message\":\"This message contains the word error\"}";

        // Act
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { json })!;

        // Assert
        Assert.False(result);
    }

    #endregion
}

/// <summary>
/// Mock ClientWebSocket for testing without actual network connections.
/// </summary>
internal class MockClientWebSocket : System.Net.WebSockets.ClientWebSocket
{
    private readonly string? _responseMessage;
    private bool _responseSent;

    public string? LastSentMessage { get; private set; }

    public MockClientWebSocket(string? responseMessage = null)
    {
        _responseMessage = responseMessage;
    }

    public new System.Net.WebSockets.WebSocketState State => System.Net.WebSockets.WebSocketState.Open;

    public new Task SendAsync(ArraySegment<byte> buffer, System.Net.WebSockets.WebSocketMessageType messageType,
        bool endOfMessage, CancellationToken cancellationToken)
    {
        LastSentMessage = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
        return Task.CompletedTask;
    }

    public new Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_responseSent || _responseMessage == null)
        {
            throw new InvalidOperationException("No response configured");
        }

        _responseSent = true;
        var responseBytes = Encoding.UTF8.GetBytes(_responseMessage);
        Array.Copy(responseBytes, 0, buffer.Array!, buffer.Offset, Math.Min(responseBytes.Length, buffer.Count));

        return Task.FromResult(new System.Net.WebSockets.WebSocketReceiveResult(
            responseBytes.Length,
            System.Net.WebSockets.WebSocketMessageType.Text,
            true));
    }
}
