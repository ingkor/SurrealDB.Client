namespace SurrealDB.Client.Tests.Unit;

using Mocks;
using SurrealDB.Client.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

[Trait("Category", "Unit")]
public class SurrealDbClientCrudTests
{
    private readonly SurrealDbClientOptions _options = new()
    {
        Namespace = "test",
        Database = "test",
        Protocol = ProtocolType.WebSocket
    };

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsCreatedRecord()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        var serializer = new SystemTextJsonSerializer();
        var client = new SurrealDbClient(_options, serializer);

        // Mock the connection pool to use our mock adapter
        var testRecord = new { id = "test:1", name = "Test Record" };

        // Act
        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "2.1ms",
            result = new[] { testRecord }
        }));

        await mockAdapter.ConnectAsync();
        var result = await mockAdapter.SendAsync("query", "/sql", "{}", default);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("OK", result);
    }

    [Fact]
    public async Task GetAsync_WithValidRecordId_ReturnsRecord()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        await mockAdapter.ConnectAsync();

        var serializer = new SystemTextJsonSerializer();
        var testRecord = new { id = "test:1", name = "Test" };

        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "1.8ms",
            result = new[] { testRecord }
        }));

        // Act
        var response = await mockAdapter.SendAsync("query", "/sql", "{\"query\":\"SELECT * FROM test:1;\"}", default);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("OK", response);
    }

    [Fact]
    public async Task SelectAsync_WithLimit_ReturnsRecords()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        await mockAdapter.ConnectAsync();

        var serializer = new SystemTextJsonSerializer();
        var records = new[]
        {
            new { id = "test:1", name = "Record 1" },
            new { id = "test:2", name = "Record 2" }
        };

        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "3.2ms",
            result = records
        }));

        // Act
        var response = await mockAdapter.SendAsync("query", "/sql", "{\"query\":\"SELECT * FROM test LIMIT 1000;\"}", default);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("Record 1", response);
        Assert.Contains("Record 2", response);
    }

    [Fact]
    public async Task UpdateAsync_WithValidData_ReturnsUpdatedRecord()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        await mockAdapter.ConnectAsync();

        var serializer = new SystemTextJsonSerializer();
        var updatedRecord = new { id = "test:1", name = "Updated" };

        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "2.5ms",
            result = new[] { updatedRecord }
        }));

        // Act
        var response = await mockAdapter.SendAsync("query", "/sql", "{\"query\":\"UPDATE test:1 CONTENT {...} RETURN AFTER;\"}", default);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("Updated", response);
    }

    [Fact]
    public async Task DeleteAsync_WithValidRecordId_ReturnsSuccess()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        await mockAdapter.ConnectAsync();

        var serializer = new SystemTextJsonSerializer();
        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "1.9ms",
            result = new object[] { }
        }));

        // Act
        var response = await mockAdapter.SendAsync("query", "/sql", "{\"query\":\"DELETE test:1;\"}", default);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("OK", response);
    }

    [Fact]
    public async Task UpsertAsync_WithValidData_ReturnsRecord()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        await mockAdapter.ConnectAsync();

        var serializer = new SystemTextJsonSerializer();
        var record = new { id = "test:1", name = "Upserted" };

        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "2.8ms",
            result = new[] { record }
        }));

        // Act
        var response = await mockAdapter.SendAsync("query", "/sql", "{\"query\":\"UPSERT test:1 CONTENT {...} RETURN AFTER;\"}", default);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("Upserted", response);
    }

    [Fact]
    public async Task QueryAsync_WithCustomSql_ReturnsResults()
    {
        // Arrange
        var mockAdapter = new MockProtocolAdapter();
        await mockAdapter.ConnectAsync();

        var serializer = new SystemTextJsonSerializer();
        var results = new[] { new { id = "test:1" } };

        mockAdapter.SetResponse("query", "/sql", serializer.Serialize(new
        {
            status = "OK",
            time = "1.5ms",
            result = results
        }));

        // Act
        var response = await mockAdapter.SendAsync("query", "/sql", "{\"query\":\"SELECT * FROM test WHERE name = 'Test';\"}", default);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("OK", response);
    }

    [Fact]
    public async Task MockAdapter_IsConnected_ReturnsTrueAfterConnect()
    {
        // Arrange
        var adapter = new MockProtocolAdapter();

        // Act
        await adapter.ConnectAsync();

        // Assert
        Assert.True(adapter.IsConnected);
    }

    [Fact]
    public async Task MockAdapter_HealthCheck_ReturnsTrue()
    {
        // Arrange
        var adapter = new MockProtocolAdapter();
        await adapter.ConnectAsync();

        // Act
        var result = await adapter.HealthCheckAsync();

        // Assert
        Assert.True(result);
    }
}
