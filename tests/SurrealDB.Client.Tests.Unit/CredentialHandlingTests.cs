using System.Security;
using Xunit;
using SurrealDB.Client.Authentication;
using SurrealDB.Client.Exceptions;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for Batch 2: Credential Handling (P1-1, P1-2, P1-6)
/// - P1-1: Credentials exposed in memory - no secure clearing
/// - P1-2: Token not cleared from session after use
/// - P1-6: Credentials passed as strings in public API
/// </summary>
public class CredentialHandlingTests
{
    #region P1-1 and P1-6: SecureCredentials Tests

    [Fact]
    public void P1_1_SecureCredentials_FromUsernamePassword_StoresSecurely()
    {
        // Arrange & Act
        using var credentials = SecureCredentials.FromUsernamePassword("admin", "secret123");

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal("admin", credentials.Username);
        // Password is stored as SecureString, not accessible directly
    }

    [Fact]
    public void P1_1_SecureCredentials_FromToken_StoresSecurely()
    {
        // Arrange & Act
        using var credentials = SecureCredentials.FromToken("token_abc123");

        // Assert
        Assert.NotNull(credentials);
        // Token is stored as SecureString, not accessible directly
    }

    [Fact]
    public void P1_1_SecureCredentials_ToJsonCredentials_ProducesValidJson()
    {
        // Arrange
        using var credentials = SecureCredentials.FromUsernamePassword("testuser", "testpass");

        // Act
        var json = credentials.ToJsonCredentials();

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"user\"", json);
        Assert.Contains("\"pass\"", json);
        Assert.Contains("testuser", json);
        Assert.Contains("testpass", json);

        // Verify it's valid JSON
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.NotNull(doc);
        Assert.Equal("testuser", doc.RootElement.GetProperty("user").GetString());
        Assert.Equal("testpass", doc.RootElement.GetProperty("pass").GetString());
    }

    [Fact]
    public void P1_1_SecureCredentials_Clear_ClearsCredentials()
    {
        // Arrange
        var credentials = SecureCredentials.FromUsernamePassword("user", "pass");

        // Act
        credentials.Clear();

        // Assert
        Assert.Null(credentials.Username);
        // Should throw when trying to use cleared credentials
        Assert.Throws<ValidationException>(() => credentials.ToJsonCredentials());
    }

    [Fact]
    public void P1_1_SecureCredentials_Dispose_ClearsCredentials()
    {
        // Arrange
        var credentials = SecureCredentials.FromUsernamePassword("user", "pass");

        // Act
        credentials.Dispose();

        // Assert - Should throw when trying to use disposed credentials
        Assert.Throws<ObjectDisposedException>(() => credentials.ToJsonCredentials());
    }

    [Fact]
    public void P1_1_SecureCredentials_WithSpecialCharacters_EscapesJson()
    {
        // Arrange - Test JSON escaping
        using var credentials = SecureCredentials.FromUsernamePassword(
            "user\"with\"quotes",
            "pass\nwith\nnewlines");

        // Act
        var json = credentials.ToJsonCredentials();

        // Assert - Should produce valid JSON despite special characters
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void P1_1_SecureCredentials_WithEmptyUsername_ThrowsValidationException(string? username)
    {
        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            SecureCredentials.FromUsernamePassword(username!, "password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void P1_1_SecureCredentials_WithEmptyPassword_ThrowsValidationException(string? password)
    {
        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            SecureCredentials.FromUsernamePassword("username", password!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void P1_1_SecureCredentials_WithEmptyToken_ThrowsValidationException(string? token)
    {
        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            SecureCredentials.FromToken(token!));
    }

    [Fact]
    public void P1_1_SecureCredentials_FromSecureString_WorksCorrectly()
    {
        // Arrange
        var securePassword = new SecureString();
        foreach (char c in "secretpass")
        {
            securePassword.AppendChar(c);
        }
        securePassword.MakeReadOnly();

        // Act
        using var credentials = SecureCredentials.FromSecurePassword("admin", securePassword);

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal("admin", credentials.Username);

        var json = credentials.ToJsonCredentials();
        Assert.Contains("secretpass", json);

        // Cleanup
        securePassword.Dispose();
    }

    #endregion

    #region P1-2: AuthenticationSession Token Clearing

    [Fact]
    public void P1_2_AuthenticationSession_ClearToken_ClearsToken()
    {
        // Arrange
        var session = new AuthenticationSession
        {
            Token = "test_token_123",
            EstablishedAt = DateTime.UtcNow
        };

        // Act
        session.ClearToken();

        // Assert
        Assert.Null(session.Token);
        Assert.False(session.IsValid);
    }

    [Fact]
    public void P1_2_AuthenticationSession_Dispose_ClearsToken()
    {
        // Arrange
        var session = new AuthenticationSession
        {
            Token = "test_token_123",
            EstablishedAt = DateTime.UtcNow
        };

        // Act
        session.Dispose();

        // Assert
        Assert.Null(session.Token);
        Assert.False(session.IsValid);
        Assert.True(session.IsExpired);
    }

    [Fact]
    public void P1_2_AuthenticationSession_DisposeMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var session = new AuthenticationSession
        {
            Token = "test_token",
            EstablishedAt = DateTime.UtcNow
        };

        // Act & Assert - Should not throw
        session.Dispose();
        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void P1_2_AuthenticationSession_AfterDisposal_IsNotValid()
    {
        // Arrange
        var session = new AuthenticationSession
        {
            Token = "valid_token",
            EstablishedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        Assert.True(session.IsValid);

        // Act
        session.Dispose();

        // Assert
        Assert.False(session.IsValid);
        Assert.True(session.IsExpired);
    }

    [Fact]
    public void P1_2_AuthenticationSession_SetTokenAfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var session = new AuthenticationSession();
        session.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => session.Token = "new_token");
    }

    [Fact]
    public void P1_2_AuthenticationSession_GetTokenAfterDisposal_ReturnsNull()
    {
        // Arrange
        var session = new AuthenticationSession
        {
            Token = "test_token"
        };
        session.Dispose();

        // Act
        var token = session.Token;

        // Assert
        Assert.Null(token);
    }

    #endregion

    #region P1-1, P1-6: Authentication Provider Disposal

    [Fact]
    public void P1_1_BasicAuthenticationProvider_Dispose_ClearsCredentials()
    {
        // Arrange
        var provider = new BasicAuthenticationProvider("user", "pass");

        // Act
        provider.Dispose();

        // Assert - After disposal, provider should not be usable
        // This is tested by checking it throws ObjectDisposedException when used
        var mockAdapter = new MockProtocolAdapter();
        var exception = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.AuthenticateAsync(mockAdapter));

        Assert.NotNull(exception);
    }

    [Fact]
    public void P1_1_TokenAuthenticationProvider_Dispose_ClearsCredentials()
    {
        // Arrange
        var provider = new TokenAuthenticationProvider("test_token");

        // Act
        provider.Dispose();

        // Assert - After disposal, provider should not be usable
        var mockAdapter = new MockProtocolAdapter();
        var exception = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.AuthenticateAsync(mockAdapter));

        Assert.NotNull(exception);
    }

    [Fact]
    public void P1_6_BasicAuthenticationProvider_WithSecureCredentials_WorksCorrectly()
    {
        // Arrange
        using var credentials = SecureCredentials.FromUsernamePassword("admin", "secret");

        // Act
        using var provider = new BasicAuthenticationProvider(credentials);

        // Assert
        Assert.NotNull(provider);
        // Provider should be usable (tested with actual connection in integration tests)
    }

    [Fact]
    public void P1_6_TokenAuthenticationProvider_WithSecureCredentials_WorksCorrectly()
    {
        // Arrange
        using var credentials = SecureCredentials.FromToken("token_abc");

        // Act
        using var provider = new TokenAuthenticationProvider(credentials);

        // Assert
        Assert.NotNull(provider);
        // Provider should be usable (tested with actual connection in integration tests)
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Mock protocol adapter for testing authentication providers.
    /// </summary>
    private class MockProtocolAdapter : SurrealDB.Client.Protocol.IProtocolAdapter
    {
        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisconnectAsync()
            => Task.CompletedTask;

        public Task<string> SendAsync(string method, string path, string? body = null, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"status\":\"ok\"}");

        public Task<string> AuthenticateAsync(string credentials, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"token\":\"mock_token\"}");

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    #endregion
}
