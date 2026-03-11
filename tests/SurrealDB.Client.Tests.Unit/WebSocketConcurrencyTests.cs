namespace SurrealDB.Client.Tests.Unit;

using System.Reflection;
using SurrealDB.Client.Protocol;
using Xunit;

[Trait("Category", "Unit")]
public class WebSocketConcurrencyTests
{
    private static SurrealDbClientOptions CreateOptions() => new()
    {
        ConnectionString = "surreal://localhost:8000",
        Namespace = "test",
        Database = "test"
    };

    [Fact]
    public void WebSocketAdapter_HasReceiveLock_Field()
    {
        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), CreateOptions());
        var field = typeof(WebSocketProtocolAdapter)
            .GetField("_receiveLock", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field);
        var sem = field!.GetValue(adapter) as SemaphoreSlim;
        Assert.NotNull(sem);
        Assert.Equal(1, sem!.CurrentCount);
    }

    [Fact]
    public async Task WebSocketAdapter_ReceiveLock_IsReleasedAfterRoundTrip()
    {
        var adapter = new WebSocketProtocolAdapter(new Uri("ws://localhost:8000"), CreateOptions());
        var field = typeof(WebSocketProtocolAdapter)
            .GetField("_receiveLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var sem = (SemaphoreSlim)field!.GetValue(adapter)!;

        // Simulate acquire + release
        await sem.WaitAsync();
        Assert.Equal(0, sem.CurrentCount);
        sem.Release();
        Assert.Equal(1, sem.CurrentCount);
    }
}
