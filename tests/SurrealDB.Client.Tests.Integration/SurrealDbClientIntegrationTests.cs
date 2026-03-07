namespace SurrealDB.Client.Tests.Integration;

using Aspire.Hosting.Testing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Integration tests using .NET Aspire for container orchestration.
/// Tests automatically spin up SurrealDB container and clean up resources.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class SurrealDbClientIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private SurrealDbClient? _client;

    private const string Username = "admin";
    private const string Password = "password";
    private const string Namespace = "test";
    private const string Database = "integration";

    public async Task InitializeAsync()
    {
        // Start the Aspire application (which includes SurrealDB container)
        _app = await DistributedApplicationTestingExtensions
            .BuildAndStartAsync(typeof(global::SurrealDB.Client.AppHost.Program).Assembly);

        // Get the SurrealDB resource from Aspire
        var appModel = _app.Services.GetRequiredService<DistributedApplicationModel>();
        var surrealDbResource = appModel.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == "surrealdb");

        if (surrealDbResource == null)
        {
            throw new InvalidOperationException(
                "Failed to find surrealdb resource in Aspire application. " +
                "Ensure AppHost project defines the SurrealDB container.");
        }

        // Get the endpoint for SurrealDB
        var endpoint = surrealDbResource.Annotations
            .OfType<EndpointAnnotation>()
            .FirstOrDefault(e => e.Scheme == "http");

        if (endpoint == null)
        {
            throw new InvalidOperationException("SurrealDB HTTP endpoint not found in Aspire configuration.");
        }

        var connectionString = $"surreal://localhost:{endpoint.Port}";

        // Initialize client
        var options = new SurrealDbClientOptions
        {
            ConnectionString = connectionString,
            Protocol = ProtocolType.WebSocket,
            Namespace = Namespace,
            Database = Database,
            ConnectionTimeout = TimeSpan.FromSeconds(15)
        };

        _client = new SurrealDbClient(options);

        try
        {
            await _client.ConnectAsync();
            await _client.AuthenticateAsync(Username, Password);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to SurrealDB. Ensure Aspire AppHost is properly configured.",
                ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            try
            {
                await _client.DisposeAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        if (_app != null)
        {
            await _app.StopAsync();
        }
    }

    [Fact]
    public async Task FullCrudLifecycle_CreateReadUpdateDelete_SucceedsEndToEnd()
    {
        Assert.NotNull(_client);

        // Arrange
        var table = "users";
        var testUser = new TestUser { Name = "Integration Test User", Email = "test@example.com" };

        // Act & Assert: CREATE
        var created = await _client!.CreateAsync(table, testUser);
        Assert.NotNull(created);
        Assert.NotNull(created.Id);
        var createdId = created.Id!;

        // Act & Assert: GET (READ)
        var retrieved = await _client.GetAsync<TestUser>(createdId);
        Assert.NotNull(retrieved);
        Assert.Equal(testUser.Name, retrieved.Name);
        Assert.Equal(testUser.Email, retrieved.Email);

        // Act & Assert: UPDATE
        var updated = new TestUser { Name = "Updated Name", Email = "updated@example.com" };
        var updateResult = await _client.UpdateAsync(createdId, updated);
        Assert.NotNull(updateResult);
        Assert.Equal("Updated Name", updateResult.Name);

        // Act & Assert: SELECT
        var allUsers = await _client.SelectAsync<TestUser>(table);
        Assert.NotEmpty(allUsers);
        Assert.True(allUsers.Any(u => u.Name == "Updated Name"));

        // Act & Assert: DELETE
        var deleted = await _client.DeleteAsync(createdId);
        Assert.True(deleted);

        // Verify deletion
        var afterDelete = await _client.GetAsync<TestUser>(createdId);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task SelectAsync_WithMultipleRecords_ReturnsList()
    {
        Assert.NotNull(_client);

        // Arrange
        var table = "products";
        var products = new[]
        {
            new TestProduct { Name = "Product 1", Price = 10.00m },
            new TestProduct { Name = "Product 2", Price = 20.00m },
            new TestProduct { Name = "Product 3", Price = 30.00m }
        };

        // Act: Create multiple records
        foreach (var product in products)
        {
            await _client!.CreateAsync(table, product);
        }

        // Act: Select all
        var results = await _client!.SelectAsync<TestProduct>(table, limit: 100);

        // Assert
        Assert.NotEmpty(results);
        Assert.True(results.Count() >= products.Length);
    }

    [Fact]
    public async Task UpsertAsync_CreatesIfNotExists_UpdatesIfExists()
    {
        Assert.NotNull(_client);

        // Arrange
        var recordId = "orders:integration-test-1";
        var order = new TestOrder { OrderNumber = "ORD-001", Total = 100.00m };

        // Act: First upsert (create)
        var created = await _client!.UpsertAsync(recordId, order);
        Assert.NotNull(created);
        Assert.Equal("ORD-001", created.OrderNumber);

        // Act: Second upsert (update)
        var updated = new TestOrder { OrderNumber = "ORD-001-UPDATED", Total = 150.00m };
        var result = await _client.UpsertAsync(recordId, updated);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ORD-001-UPDATED", result.OrderNumber);
        Assert.Equal(150.00m, result.Total);
    }

    [Fact]
    public async Task QueryAsync_WithCustomSql_ReturnsTypedResults()
    {
        Assert.NotNull(_client);

        // Arrange
        var table = "items";
        await _client!.CreateAsync(table, new TestItem { Description = "Test Item 1" });
        await _client.CreateAsync(table, new TestItem { Description = "Test Item 2" });

        // Act
        var query = $"SELECT * FROM {table};";
        var results = await _client.QueryAsync<TestItem>(query);

        // Assert
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task TransactionAsync_CommitSucceeds_UpdatesDatabase()
    {
        Assert.NotNull(_client);

        // Arrange
        var table = "transactions";
        var item = new TestItem { Description = "Transaction Test" };

        // Act
        using var txn = await _client!.BeginTransactionAsync();
        var result = await txn.QueryAsync<TestItem>($"CREATE {table} CONTENT {{\"Description\":\"Transaction Test\"}} RETURN AFTER;");
        await txn.CommitAsync();

        // Assert
        Assert.NotEmpty(result);
    }

    // Test data models
    private class TestUser
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    private class TestProduct
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
    }

    private class TestOrder
    {
        public string? Id { get; set; }
        public string? OrderNumber { get; set; }
        public decimal Total { get; set; }
    }

    private class TestItem
    {
        public string? Id { get; set; }
        public string? Description { get; set; }
    }
}
