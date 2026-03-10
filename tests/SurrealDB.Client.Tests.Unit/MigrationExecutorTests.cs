namespace SurrealDB.Client.Tests.Unit;

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using SurrealDB.Client.Migrations;
using Xunit;

[Trait("Category", "Unit")]
public class MigrationExecutorTests
{
    private readonly List<string> _capturedSql = new();
    private readonly Mock<ISurrealDbClient> _mockClient;
    private readonly SurrealMigrationExecutor _executor;

    public MigrationExecutorTests()
    {
        _mockClient = new Mock<ISurrealDbClient>();
        _mockClient
            .Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>?, CancellationToken>((sql, _, _) => _capturedSql.Add(sql))
            .ReturnsAsync(new QueryResult());

        _executor = new SurrealMigrationExecutor(_mockClient.Object);
    }

    [Fact]
    public async Task CreateTable_GeneratesCorrectSurrealQL()
    {
        await _executor.CreateTableAsync("users");
        Assert.Contains(_capturedSql, s => s.Contains("DEFINE TABLE `users` SCHEMALESS"));
    }

    [Fact]
    public async Task DropTable_GeneratesCorrectSurrealQL()
    {
        await _executor.DropTableAsync("orders");
        Assert.Contains(_capturedSql, s => s.Contains("REMOVE TABLE `orders`"));
    }

    [Fact]
    public async Task AddColumn_BasicType_GeneratesCorrectSurrealQL()
    {
        await _executor.AddColumnAsync("users", "email", "string");
        Assert.Contains(_capturedSql, s =>
            s.Contains("DEFINE FIELD `email`") &&
            s.Contains("ON TABLE `users`") &&
            s.Contains("TYPE string"));
    }

    [Fact]
    public async Task AddColumn_WithDefault_IncludesDefaultClause()
    {
        await _executor.AddColumnAsync("users", "active", "bool", new Dictionary<string, object> { ["default"] = true });
        Assert.Contains(_capturedSql, s => s.Contains("DEFAULT"));
    }

    [Fact]
    public async Task CreateIndex_Unique_GeneratesUniqueKeyword()
    {
        await _executor.CreateIndexAsync("users", "idx_email", new[] { "email" }, unique: true);
        Assert.Contains(_capturedSql, s => s.Contains("UNIQUE"));
    }

    [Fact]
    public async Task CreateIndex_NonUnique_NoUniqueKeyword()
    {
        await _executor.CreateIndexAsync("users", "idx_name", new[] { "name" }, unique: false);
        Assert.DoesNotContain(_capturedSql, s => s.Contains("UNIQUE"));
    }

    [Fact]
    public void Escape_IdentifierWithBacktick_EscapesCorrectly()
    {
        var method = typeof(SurrealMigrationExecutor)
            .GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = (string)method!.Invoke(null, new object[] { "`bad`" })!;
        Assert.Equal("`\\`bad\\``", result);
    }
}
