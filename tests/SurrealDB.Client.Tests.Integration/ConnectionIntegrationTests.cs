using Xunit;

namespace SurrealDB.Client.Tests.Integration;

/// <summary>
/// Integration tests for connection management.
///
/// These tests require a running SurrealDB instance.
/// To run: docker run --rm -p 8000:8000 surrealdb/surrealdb:latest start
/// </summary>
public class ConnectionIntegrationTests
{
    private const string ConnectionString = "surreal://localhost:8000";

    [Fact(Skip = "Requires running SurrealDB instance")]
    public async Task Connect_ToLocalSurrealDB_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient(ConnectionString);

        // Act
        await client.ConnectAsync();

        // Assert
        Assert.True(client.IsConnected);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact(Skip = "Requires running SurrealDB instance")]
    public async Task Authenticate_WithValidCredentials_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient(ConnectionString);
        await client.ConnectAsync();

        // Act
        await client.AuthenticateAsync("root", "root");

        // Assert
        Assert.True(client.IsConnected);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact(Skip = "Requires running SurrealDB instance")]
    public async Task CreateRecord_WithValidData_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient(ConnectionString);
        await client.ConnectAsync();
        await client.AuthenticateAsync("root", "root");

        var record = new { Name = "Test User", Email = "test@example.com" };

        // Act
        var result = await client.CreateAsync("users", record);

        // Assert
        Assert.NotNull(result);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact(Skip = "Requires running SurrealDB instance")]
    public async Task SelectRecords_FromTable_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient(ConnectionString);
        await client.ConnectAsync();
        await client.AuthenticateAsync("root", "root");

        // Act
        var results = await client.SelectAsync<dynamic>("users");

        // Assert
        Assert.NotNull(results);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact(Skip = "Requires running SurrealDB instance")]
    public async Task ExecuteRawQuery_WithValidSurrealQL_Succeeds()
    {
        // Arrange
        var client = new SurrealDbClient(ConnectionString);
        await client.ConnectAsync();
        await client.AuthenticateAsync("root", "root");

        // Act
        var result = await client.QueryAsync("SELECT * FROM users LIMIT 10");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);

        // Cleanup
        await client.DisposeAsync();
    }
}
