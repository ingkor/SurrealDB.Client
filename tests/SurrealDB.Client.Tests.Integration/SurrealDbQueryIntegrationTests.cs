namespace SurrealDB.Client.Tests.Integration;

using Aspire.Hosting.Testing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Integration tests for LINQ query compilation and execution.
/// Uses Aspire container orchestration for real SurrealDB testing.
/// </summary>
[Trait("Category", "Integration")]
public class SurrealDbQueryIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private SurrealDbClient? _client;
    private ISurrealDbSession? _session;

    private const string Username = "admin";
    private const string Password = "password";
    private const string Namespace = "test";
    private const string Database = "queries";

    public async Task InitializeAsync()
    {
        // Start Aspire application
        _app = await DistributedApplicationTestingExtensions
            .BuildAndStartAsync(typeof(global::SurrealDB.Client.AppHost.Program).Assembly);

        // Get endpoint
        var appModel = _app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = appModel.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == "surrealdb");

        if (resource == null)
            throw new InvalidOperationException("SurrealDB resource not found");

        var endpoint = resource.Annotations
            .OfType<EndpointAnnotation>()
            .FirstOrDefault(e => e.Scheme == "http");

        if (endpoint == null)
            throw new InvalidOperationException("HTTP endpoint not found");

        var options = new SurrealDbClientOptions
        {
            ConnectionString = $"surreal://localhost:{endpoint.Port}",
            Protocol = ProtocolType.WebSocket,
            Namespace = Namespace,
            Database = Database,
            ConnectionTimeout = TimeSpan.FromSeconds(15)
        };

        _client = new SurrealDbClient(options);
        await _client.ConnectAsync();
        await _client.AuthenticateAsync(Username, Password);
        _session = _client.CreateSession();

        // Seed test data
        await SeedTestData();
    }

    private async Task SeedTestData()
    {
        if (_client == null)
            return;

        // Create test users
        var users = new[]
        {
            new TestUser { Name = "Alice", Age = 25, Email = "alice@example.com" },
            new TestUser { Name = "Bob", Age = 30, Email = "bob@example.com" },
            new TestUser { Name = "Charlie", Age = 35, Email = "charlie@example.com" },
            new TestUser { Name = "David", Age = 20, Email = "david@example.com" },
            new TestUser { Name = "Eve", Age = 28, Email = "eve@example.com" }
        };

        foreach (var user in users)
        {
            await _client.CreateAsync("users", user);
        }
    }

    public async Task DisposeAsync()
    {
        if (_session != null)
            await _session.DisposeAsync();

        if (_client != null)
            await _client.DisposeAsync();

        if (_app != null)
            await _app.StopAsync();
    }

    [Fact]
    public async Task Query_SimpleSelect_ReturnsAllResults()
    {
        Assert.NotNull(_session);

        // Act
        var query = _session!.Set<TestUser>("users");
        var results = query.ToList();

        // Assert
        Assert.NotEmpty(results);
        Assert.True(results.Count >= 5);
    }

    [Fact]
    public async Task Query_WithWhere_FiltersResults()
    {
        Assert.NotNull(_session);

        // Act
        var query = _session!.Set<TestUser>("users")
            .Where(u => u.Age >= 30);

        var results = query.ToList();

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, u => Assert.True(u.Age >= 30));
    }

    [Fact]
    public async Task Query_WithOrderBy_SortsResults()
    {
        Assert.NotNull(_session);

        // Act
        var query = _session!.Set<TestUser>("users")
            .OrderBy(u => u.Age);

        var results = query.ToList();

        // Assert
        Assert.NotEmpty(results);
        // Results should be sorted by age (ascending)
        for (int i = 0; i < results.Count - 1; i++)
        {
            Assert.True(results[i].Age <= results[i + 1].Age);
        }
    }

    [Fact]
    public async Task Query_WithOrderByDescending_ReversesSortOrder()
    {
        Assert.NotNull(_session);

        // Act
        var query = _session!.Set<TestUser>("users")
            .OrderByDescending(u => u.Age);

        var results = query.ToList();

        // Assert
        Assert.NotEmpty(results);
        // Results should be sorted by age (descending)
        for (int i = 0; i < results.Count - 1; i++)
        {
            Assert.True(results[i].Age >= results[i + 1].Age);
        }
    }

    [Fact]
    public async Task Query_WithTake_LimitsResults()
    {
        Assert.NotNull(_session);

        // Act
        var query = _session!.Set<TestUser>("users")
            .Take(2);

        var results = query.ToList();

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Query_WithSkip_SkipsResults()
    {
        Assert.NotNull(_session);

        // Act - Get first 2
        var first = _session!.Set<TestUser>("users")
            .OrderBy(u => u.Name)
            .Take(2)
            .ToList();

        // Skip first 2
        var skipped = _session.Set<TestUser>("users")
            .OrderBy(u => u.Name)
            .Skip(2)
            .Take(2)
            .ToList();

        // Assert
        Assert.NotEmpty(first);
        Assert.NotEmpty(skipped);
        Assert.DoesNotContain(skipped.First(), first);
    }

    [Fact]
    public async Task Query_WithWhereAndOrderBy_CombinesFiltersAndSort()
    {
        Assert.NotNull(_session);

        // Act
        var query = _session!.Set<TestUser>("users")
            .Where(u => u.Age >= 25)
            .OrderBy(u => u.Name);

        var results = query.ToList();

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, u => Assert.True(u.Age >= 25));

        // Verify sorting
        for (int i = 0; i < results.Count - 1; i++)
        {
            Assert.True(string.CompareOrdinal(results[i].Name, results[i + 1].Name) <= 0);
        }
    }

    [Fact]
    public async Task Query_Count_ReturnsCorrectCount()
    {
        Assert.NotNull(_session);

        // Act
        var count = _session!.Set<TestUser>("users")
            .Where(u => u.Age >= 25)
            .Count();

        // Assert
        Assert.True(count > 0);
    }

    [Fact]
    public async Task Query_FirstOrDefault_ReturnsFirstResult()
    {
        Assert.NotNull(_session);

        // Act
        var result = _session!.Set<TestUser>("users")
            .OrderBy(u => u.Name)
            .FirstOrDefault();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Query_Any_ReturnsTrueWhenMatching()
    {
        Assert.NotNull(_session);

        // Act
        var anyOldUsers = _session!.Set<TestUser>("users")
            .Where(u => u.Age >= 30)
            .Any();

        // Assert
        Assert.True(anyOldUsers);
    }

    // Test model
    private class TestUser
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
    }
}
