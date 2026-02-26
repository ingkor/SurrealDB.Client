using Xunit;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for SurrealDbClientOptions.
/// </summary>
public class SurrealDbClientOptionsTests
{
    [Fact]
    public void Options_DefaultValues_AreValid()
    {
        // Arrange & Act
        var options = new SurrealDbClientOptions();

        // Assert
        Assert.NotNull(options.ConnectionString);
        Assert.Equal(ProtocolType.WebSocket, options.Protocol);
        Assert.Equal(10, options.PoolSize);
        Assert.True(options.EnableHealthChecks);
        Assert.True(options.EnableAutoReconnect);
    }

    [Fact]
    public void Options_Validate_WithValidOptions_Succeeds()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test_ns",
            Database = "test_db",
            PoolSize = 5
        };

        // Act & Assert (no exception)
        options.Validate();
    }

    [Fact]
    public void Options_Validate_WithEmptyConnectionString_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions { ConnectionString = "" };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithInvalidPoolSize_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 0 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithInvalidTimeout_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions { ConnectionTimeout = TimeSpan.Zero };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_CanSetNamespaceAndDatabase()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            Namespace = "mynamespace",
            Database = "mydb"
        };

        // Act & Assert
        Assert.Equal("mynamespace", options.Namespace);
        Assert.Equal("mydb", options.Database);
    }

    [Fact]
    public void ProtocolType_HasHttpAndWebSocket()
    {
        // Act & Assert
        Assert.Equal(0, (int)ProtocolType.Http);
        Assert.Equal(1, (int)ProtocolType.WebSocket);
    }

    [Fact]
    public void SerializerType_HasSystemTextJsonAndNewtonsoft()
    {
        // Act & Assert
        Assert.Equal(0, (int)SerializerType.SystemTextJson);
        Assert.Equal(1, (int)SerializerType.NewtonsoftJson);
    }

    [Fact]
    public void Options_Validate_WithoutNamespace_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Database = "test_db",
            Namespace = null
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithEmptyNamespace_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Database = "test_db",
            Namespace = ""
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithWhitespaceNamespace_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Database = "test_db",
            Namespace = "   "
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithoutDatabase_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test_ns",
            Database = null
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithEmptyDatabase_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test_ns",
            Database = ""
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Options_Validate_WithWhitespaceDatabase_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test_ns",
            Database = "   "
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }
}
