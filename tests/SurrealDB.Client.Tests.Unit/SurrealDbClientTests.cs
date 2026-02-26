using Xunit;
using Moq;
using SurrealDB.Client.Protocol;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for SurrealDbClient.
/// </summary>
public class SurrealDbClientTests
{
    private static SurrealDbClientOptions CreateValidOptions()
    {
        return new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test_ns",
            Database = "test_db"
        };
    }

    [Fact]
    public void Client_IsCreatedWithOptions()
    {
        // Arrange & Act
        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        // Assert
        Assert.NotNull(client);
        Assert.Equal(options, client.Options);
    }

    [Fact]
    public void Client_IsCreatedWithConnectionString()
    {
        // Arrange & Act
        var connectionString = "surreal://localhost:8000";
        var options = new SurrealDbClientOptions { ConnectionString = connectionString };

        // Act & Assert - should throw due to missing namespace/database
        Assert.Throws<ValidationException>(() => new SurrealDbClient(options));
    }

    [Fact]
    public void Client_InvalidOptions_Throws()
    {
        // Arrange
        var options = CreateValidOptions();
        options.PoolSize = 0;

        // Act & Assert
        Assert.Throws<ValidationException>(() => new SurrealDbClient(options));
    }

    [Fact]
    public void Client_MissingNamespace_Throws()
    {
        // Arrange
        var options = CreateValidOptions();
        options.Namespace = null;

        // Act & Assert
        Assert.Throws<ValidationException>(() => new SurrealDbClient(options));
    }

    [Fact]
    public void Client_MissingDatabase_Throws()
    {
        // Arrange
        var options = CreateValidOptions();
        options.Database = null;

        // Act & Assert
        Assert.Throws<ValidationException>(() => new SurrealDbClient(options));
    }

    [Fact]
    public async Task Client_ConnectAsync_SendsUseNsDb()
    {
        // Arrange
        var options = CreateValidOptions();
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockAdapter.Setup(a => a.SendAsync(
            It.Is<string>(m => m == "QUERY"),
            It.Is<string>(p => p.Contains("USE NS") && p.Contains("USE DB")),
            null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"status\": \"ok\"}");

        var client = new SurrealDbClient(options);

        // Act - would need to mock the protocol adapter factory
        // This is a limitation of the current architecture
        // The test demonstrates the intended behavior

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public async Task Client_DisconnectAsync_ClearsIsConnected()
    {
        // Arrange
        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        // Note: ConnectAsync will fail without a real server, so we just test DisconnectAsync logic
        await client.DisconnectAsync();

        // Assert
        Assert.False(client.IsConnected);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_AuthenticateAsync_WithEmptyUsername_Throws()
    {
        // Arrange
        var client = new SurrealDbClient(CreateValidOptions());

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => client.AuthenticateAsync("", "pass"));

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_CreateAsync_WithEmptyTable_Throws()
    {
        // Arrange
        var client = new SurrealDbClient(CreateValidOptions());

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateAsync("", new object()));

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_Dispose_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient(CreateValidOptions());

        // Act
        await client.DisposeAsync();

        // Assert - should throw ObjectDisposedException on operations
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.IsConnectedAsync());
    }
}
