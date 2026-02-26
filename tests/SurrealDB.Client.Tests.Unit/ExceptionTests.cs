using Xunit;
using SurrealDB.Client.Exceptions;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for SurrealDB exception types.
/// </summary>
public class ExceptionTests
{
    [Fact]
    public void SurrealDbException_WithErrorCode_IncludesErrorCodeInString()
    {
        // Arrange
        var exception = new ConnectionException("Test error", "CONNECTION_FAILED");

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("CONNECTION_FAILED", result);
        Assert.Contains("Test error", result);
    }

    [Fact]
    public void SurrealDbException_WithDetails_IncludesDetailsInString()
    {
        // Arrange
        var exception = new QueryException("Test error", "QUERY_FAILED");
        exception.Details = new Dictionary<string, object>
        {
            { "line", 1 },
            { "column", 10 }
        };

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("line", result);
        Assert.Contains("column", result);
    }

    [Fact]
    public void ConnectionException_IsCreated()
    {
        // Arrange & Act
        var exception = new ConnectionException("Connection failed");

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<ConnectionException>(exception);
    }

    [Fact]
    public void AuthenticationException_IsCreated()
    {
        // Arrange & Act
        var exception = new AuthenticationException("Auth failed");

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<AuthenticationException>(exception);
    }

    [Fact]
    public void QueryException_IsCreated()
    {
        // Arrange & Act
        var exception = new QueryException("Query failed");

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<QueryException>(exception);
    }

    [Fact]
    public void SerializationException_IsCreated()
    {
        // Arrange & Act
        var exception = new SerializationException("Serialization failed");

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<SerializationException>(exception);
    }

    [Fact]
    public void ValidationException_IsCreated()
    {
        // Arrange & Act
        var exception = new ValidationException("Validation failed");

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<ValidationException>(exception);
    }

    [Fact]
    public void TimeoutException_IsCreated()
    {
        // Arrange & Act
        var exception = new TimeoutException("Operation timed out");

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<TimeoutException>(exception);
    }
}
