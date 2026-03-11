namespace SurrealDB.Client.Tests.Unit;

using SurrealDB.Client.Caching;
using SurrealDB.Client.Exceptions;
using Xunit;

[Trait("Category", "Unit")]
public class OperationalHardeningTests
{
    // Fix A — LRU Cache

    [Fact]
    public void Cache_LRU_EvictsLeastRecentlyUsed_WhenAtCapacity()
    {
        var cache = new MemoryQueryCache(maxItems: 3);
        cache.Set("a", "A");
        cache.Set("b", "B");
        cache.Set("c", "C");
        _ = cache.Get<string>("a"); // access "a" → MRU
        cache.Set("d", "D");        // evict LRU → "b"

        Assert.Null(cache.Get<string>("b"));
        Assert.NotNull(cache.Get<string>("a"));
        Assert.NotNull(cache.Get<string>("c"));
        Assert.NotNull(cache.Get<string>("d"));
    }

    [Fact]
    public void Cache_LRU_ZeroMaxItems_NoEviction()
    {
        var cache = new MemoryQueryCache(maxItems: 0);
        for (var i = 0; i < 100; i++) cache.Set($"key{i}", i);
        Assert.Equal(100, cache.GetStatistics().ItemCount);
    }

    [Fact]
    public void Cache_LRU_UpdateExistingKey_DoesNotIncrementCount()
    {
        var cache = new MemoryQueryCache(maxItems: 3);
        cache.Set("a", "A1");
        cache.Set("b", "B");
        cache.Set("c", "C");
        cache.Set("a", "A2"); // update, not new entry
        Assert.Equal(3, cache.GetStatistics().ItemCount);
        Assert.Equal("A2", cache.Get<string>("a"));
    }

    [Fact]
    public void Cache_LRU_Clear_ResetsEvictionState()
    {
        var cache = new MemoryQueryCache(maxItems: 2);
        cache.Set("a", "A");
        cache.Set("b", "B");
        cache.Clear();
        cache.Set("c", "C");
        cache.Set("d", "D");
        Assert.Equal(2, cache.GetStatistics().ItemCount);
    }

    // Fix C — Command Timeout

    [Fact]
    public void SurrealDbClientOptions_CommandTimeout_DefaultIs120Seconds()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        Assert.Equal(TimeSpan.FromSeconds(120), opts.CommandTimeout);
    }

    // Fix D — URI Validation

    [Fact]
    public void Validate_ValidSurrealScheme_DoesNotThrow()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        var ex = Record.Exception(() => opts.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_ValidWssScheme_DoesNotThrow()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "wss://db.example.com:8000",
            Namespace = "test",
            Database = "test"
        };
        var ex = Record.Exception(() => opts.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_MalformedUri_ThrowsValidationException()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal//localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        Assert.Throws<ValidationException>(() => opts.Validate());
    }

    [Fact]
    public void Validate_UnsupportedScheme_ThrowsValidationException()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "ftp://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        var ex = Assert.Throws<ValidationException>(() => opts.Validate());
        Assert.Contains("ftp", ex.Message);
    }

    // Fix B — Health check defaults

    [Fact]
    public void SurrealDbClientOptions_EnableHealthChecks_DefaultTrue()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        Assert.True(opts.EnableHealthChecks);
    }

    [Fact]
    public void SurrealDbClientOptions_HealthCheckInterval_DefaultThirtySeconds()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        Assert.Equal(TimeSpan.FromSeconds(30), opts.HealthCheckInterval);
    }

    [Fact]
    public void SurrealDbClientOptions_CacheMaxItems_DefaultZero()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        Assert.Equal(0, opts.CacheMaxItems);
    }
}
