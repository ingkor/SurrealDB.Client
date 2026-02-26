using Xunit;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for SurrealDbClient.
/// </summary>
public class SurrealDbClientTests
{
    [Fact]
    public void Client_IsCreatedWithOptions()
    {
        // Arrange & Act
        var options = new SurrealDbClientOptions { ConnectionString = "surreal://localhost:8000" };
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
        var client = new SurrealDbClient(connectionString);

        // Assert
        Assert.NotNull(client);
        Assert.NotNull(client.Options);
    }

    [Fact]
    public void Client_InvalidOptions_Throws()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 0 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => new SurrealDbClient(options));
    }

    [Fact]
    public async Task Client_ConnectAsync_SetsIsConnected()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");

        // Act
        await client.ConnectAsync();

        // Assert
        Assert.True(client.IsConnected);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_DisconnectAsync_ClearsIsConnected()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act
        await client.DisconnectAsync();

        // Assert
        Assert.False(client.IsConnected);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_AuthenticateAsync_WithUsername_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act & Assert (no exception)
        await client.AuthenticateAsync("user", "pass");

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_AuthenticateAsync_WithToken_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act & Assert (no exception)
        await client.AuthenticateAsync("token123");

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_AuthenticateAsync_WithEmptyUsername_Throws()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => client.AuthenticateAsync("", "pass"));

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_CreateAsync_WithValidData_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();
        var data = new { Name = "Test" };

        // Act
        var result = await client.CreateAsync("table", data);

        // Assert
        Assert.NotNull(result);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_CreateAsync_WithEmptyTable_Throws()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateAsync("", new object()));

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_Dispose_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act
        await client.DisposeAsync();

        // Assert - should throw ObjectDisposedException on operations
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.IsConnectedAsync());
    }

    [Fact]
    public async Task Client_BeginTransaction_ReturnsTransaction()
    {
        // Arrange
        var client = new SurrealDbClient("surreal://localhost:8000");
        await client.ConnectAsync();

        // Act
        var transaction = await client.BeginTransactionAsync();

        // Assert
        Assert.NotNull(transaction);

        // Cleanup
        await transaction.DisposeAsync();
        await client.DisposeAsync();
    }
}
