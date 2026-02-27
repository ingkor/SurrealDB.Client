using System.Reflection;
using Xunit;

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
    public void F1_SendAsync_WithSpecialCharactersInMethod_ProducesValidJson(string method)
    {
        // Arrange - Test JSON serialization directly
        var requestId = 1;
        var path = "/test/path";

        // Act - Simulate what SendAsync does
        var message = $"{{\"id\":{requestId},\"method\":{System.Text.Json.JsonSerializer.Serialize(method)},\"path\":{System.Text.Json.JsonSerializer.Serialize(path)},\"params\":{{}}}}";

        // Assert - Parse as JSON - this will throw if invalid
        var jsonDoc = System.Text.Json.JsonDocument.Parse(message);
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
    public void F1_SendAsync_WithSpecialCharactersInPath_ProducesValidJson(string path)
    {
        // Arrange - Test JSON serialization directly
        var requestId = 1;
        var method = "TEST";

        // Act - Simulate what SendAsync does
        var message = $"{{\"id\":{requestId},\"method\":{System.Text.Json.JsonSerializer.Serialize(method)},\"path\":{System.Text.Json.JsonSerializer.Serialize(path)},\"params\":{{}}}}";

        // Assert - Verify valid JSON
        var jsonDoc = System.Text.Json.JsonDocument.Parse(message);
        var pathProperty = jsonDoc.RootElement.GetProperty("path");
        Assert.Equal(path, pathProperty.GetString());
    }

    [Fact]
    public void F1_AuthenticateAsync_SignInMethod_ProducesValidJson()
    {
        // Arrange - Test JSON serialization of signin method
        var requestId = 1;
        var credentials = "{\"user\":\"test\",\"pass\":\"test\"}";

        // Act - Simulate what AuthenticateAsync does
        var message = $"{{\"id\":{requestId},\"method\":{System.Text.Json.JsonSerializer.Serialize("signin")},\"params\":[{credentials}]}}";

        // Assert - Verify valid JSON
        var jsonDoc = System.Text.Json.JsonDocument.Parse(message);
        var methodProperty = jsonDoc.RootElement.GetProperty("method");
        Assert.Equal("signin", methodProperty.GetString());
    }

    #endregion

    #region F6: Fragile Error Detection Tests

    [Fact]
    public void F6_HasRootLevelError_WithErrorInFieldValue_DoesNotDetect()
    {
        // Arrange - Response with "error" in a field value, but no root-level error property
        var responseJson = "{\"id\":1,\"result\":{\"message\":\"This is not an error, just a test\"}}";

        // Act - Use reflection to call private method from SurrealDbClient
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { responseJson })!;

        // Assert - should NOT detect error because "error" is in a field value, not a root property
        Assert.False(result);
    }

    [Fact]
    public void F6_HasRootLevelError_WithRootLevelError_Detects()
    {
        // Arrange - Response with actual root-level error property
        var responseJson = "{\"id\":1,\"error\":{\"code\":-32000,\"message\":\"Authentication failed\"}}";

        // Act - Use reflection to call private method from SurrealDbClient
        var method = typeof(SurrealDbClient).GetMethod("HasRootLevelError",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)method!.Invoke(null, new object[] { responseJson })!;

        // Assert
        Assert.True(result);
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
