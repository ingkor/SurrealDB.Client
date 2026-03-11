namespace SurrealDB.Client.Tests.Unit;

using System.Diagnostics;
using SurrealDB.Client.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

[Trait("Category", "Unit")]
public class ObservabilityTests
{
    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        Assert.Equal("SurrealDB.Client", SurrealDbActivitySource.Source.Name);
    }

    [Fact]
    public void ActivitySource_StartOperation_ReturnsNullWhenNoListener()
    {
        var activity = SurrealDbActivitySource.StartOperation("surreal.test");
        Assert.Null(activity);
    }

    [Fact]
    public void ActivitySource_StartOperation_ReturnsActivityWhenListenerAttached()
    {
        using var listener = CreateAlwaysSampleListener();
        ActivitySource.AddActivityListener(listener);

        using var activity = SurrealDbActivitySource.StartOperation("surreal.test");
        Assert.NotNull(activity);
        Assert.Equal("surreal.test", activity!.OperationName);
    }

    [Fact]
    public void Metrics_PoolCounters_AreCreated()
    {
        Assert.NotNull(SurrealDbMetrics.PoolAcquired);
        Assert.NotNull(SurrealDbMetrics.PoolReleased);
        Assert.NotNull(SurrealDbMetrics.PoolExhausted);
    }

    [Fact]
    public void Metrics_CacheCounters_AreCreated()
    {
        Assert.NotNull(SurrealDbMetrics.CacheHits);
        Assert.NotNull(SurrealDbMetrics.CacheMisses);
    }

    [Fact]
    public void Metrics_OperationDuration_IsHistogram()
    {
        Assert.NotNull(SurrealDbMetrics.OperationDuration);
    }

    [Fact]
    public void SurrealDbClient_AcceptsLoggerFactory_ViaOptions()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
        };
        var ex = Record.Exception(() => new SurrealDbClient(opts));
        Assert.Null(ex);
    }

    [Fact]
    public void SurrealDbClient_NullLoggerFactory_UsesNullLoggerByDefault()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        var ex = Record.Exception(() => new SurrealDbClient(opts));
        Assert.Null(ex);
    }

    private static ActivityListener CreateAlwaysSampleListener() => new()
    {
        ShouldListenTo = s => s.Name == "SurrealDB.Client",
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        ActivityStarted = _ => { },
        ActivityStopped = _ => { }
    };
}
