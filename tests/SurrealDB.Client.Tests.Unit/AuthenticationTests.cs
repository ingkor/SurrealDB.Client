using Xunit;
using Moq;
using SurrealDB.Client.Authentication;
using SurrealDB.Client.Protocol;
using SurrealDB.Client.Exceptions;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for authentication providers.
/// </summary>
public class AuthenticationTests
{
    [Fact]
    public void BasicAuthenticationProvider_ValidCredentials_CreatesSuccessfully()
    {
        // Arrange & Act
        var provider = new BasicAuthenticationProvider("testuser", "password123");

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void BasicAuthenticationProvider_EmptyUsername_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ValidationException>(() => new BasicAuthenticationProvider("", "password"));
    }

    [Fact]
    public void BasicAuthenticationProvider_NullUsername_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ValidationException>(() => new BasicAuthenticationProvider(null!, "password"));
    }

    [Fact]
    public void BasicAuthenticationProvider_EmptyPassword_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ValidationException>(() => new BasicAuthenticationProvider("user", ""));
    }

    [Fact]
    public async Task BasicAuthenticationProvider_ValidResponse_AuthenticatesSuccessfully()
    {
        // Arrange
        var provider = new BasicAuthenticationProvider("testuser", "password123");
        var mockAdapter = new Mock<IProtocolAdapter>();
        var validResponse = "{\"token\": \"valid_token_xyz\"}";
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validResponse);

        // Act
        await provider.AuthenticateAsync(mockAdapter.Object);

        // Assert
        mockAdapter.Verify(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BasicAuthenticationProvider_EmptyResponse_ThrowsAuthenticationException()
    {
        // Arrange
        var provider = new BasicAuthenticationProvider("testuser", "password123");
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(mockAdapter.Object));
    }

    [Fact]
    public async Task BasicAuthenticationProvider_NoTokenInResponse_ThrowsAuthenticationException()
    {
        // Arrange
        var provider = new BasicAuthenticationProvider("testuser", "password123");
        var mockAdapter = new Mock<IProtocolAdapter>();
        var invalidResponse = "{\"status\": \"ok\"}";
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidResponse);

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(mockAdapter.Object));
    }

    [Fact]
    public async Task BasicAuthenticationProvider_InvalidJsonResponse_ThrowsAuthenticationException()
    {
        // Arrange
        var provider = new BasicAuthenticationProvider("testuser", "password123");
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not valid json");

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(mockAdapter.Object));
    }

    [Fact]
    public void TokenAuthenticationProvider_ValidToken_CreatesSuccessfully()
    {
        // Arrange & Act
        var provider = new TokenAuthenticationProvider("valid_token_xyz");

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void TokenAuthenticationProvider_EmptyToken_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ValidationException>(() => new TokenAuthenticationProvider(""));
    }

    [Fact]
    public void TokenAuthenticationProvider_NullToken_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ValidationException>(() => new TokenAuthenticationProvider(null!));
    }

    [Fact]
    public async Task TokenAuthenticationProvider_ValidResponse_AuthenticatesSuccessfully()
    {
        // Arrange
        var provider = new TokenAuthenticationProvider("valid_token_xyz");
        var mockAdapter = new Mock<IProtocolAdapter>();
        var validResponse = "{\"status\": \"authenticated\"}";
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validResponse);

        // Act
        await provider.AuthenticateAsync(mockAdapter.Object);

        // Assert
        mockAdapter.Verify(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TokenAuthenticationProvider_EmptyResponse_ThrowsAuthenticationException()
    {
        // Arrange
        var provider = new TokenAuthenticationProvider("valid_token_xyz");
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(mockAdapter.Object));
    }

    [Fact]
    public async Task TokenAuthenticationProvider_InvalidJsonResponse_ThrowsAuthenticationException()
    {
        // Arrange
        var provider = new TokenAuthenticationProvider("valid_token_xyz");
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid json");

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(mockAdapter.Object));
    }

    [Fact]
    public void AuthenticationSession_ValidToken_IsValid()
    {
        // Arrange & Act
        var session = new AuthenticationSession
        {
            Token = "valid_token",
            EstablishedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Assert
        Assert.True(session.IsValid);
        Assert.False(session.IsExpired);
    }

    [Fact]
    public void AuthenticationSession_ExpiredToken_IsExpired()
    {
        // Arrange & Act
        var session = new AuthenticationSession
        {
            Token = "valid_token",
            EstablishedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1)
        };

        // Assert
        Assert.False(session.IsValid);
        Assert.True(session.IsExpired);
    }

    [Fact]
    public void AuthenticationSession_NoExpiration_IsValid()
    {
        // Arrange & Act
        var session = new AuthenticationSession
        {
            Token = "valid_token",
            EstablishedAt = DateTime.UtcNow,
            ExpiresAt = null
        };

        // Assert
        Assert.True(session.IsValid);
        Assert.False(session.IsExpired);
    }

    [Fact]
    public void AuthenticationSession_EmptyToken_IsNotValid()
    {
        // Arrange & Act
        var session = new AuthenticationSession
        {
            Token = "",
            EstablishedAt = DateTime.UtcNow,
            ExpiresAt = null
        };

        // Assert
        Assert.False(session.IsValid);
    }
}
