using System.Reflection;
using Xunit;
using Moq;
using SurrealDB.Client.Connection;
using SurrealDB.Client.Exceptions;
using SurrealDB.Client.Protocol;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for resource management fixes (F2 and F3).
/// </summary>
public class ResourceManagementTests
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

    #region F2: Connection Leak on ConnectAsync Failure Tests

    [Fact]
    public async Task F2_ConnectAsync_WhenSendAsyncThrows_ReleasesConnection()
    {
        // Arrange — verify the acquire→fail→release pattern that ConnectAsync must follow.
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        mockAdapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(a => a.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated failure"));

        mockConnectionPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        bool releaseWasCalled = false;
        bool markedUnhealthy = false;
        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .Callback<IProtocolAdapter, bool>((_, healthy) =>
            {
                releaseWasCalled = true;
                markedUnhealthy = !healthy;
            })
            .Returns(Task.CompletedTask);

        // Act — simulate the acquire→use→fail→release pattern ConnectAsync implements.
        IProtocolAdapter? connection = null;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            connection = await mockConnectionPool.Object.AcquireAsync();
            try
            {
                await mockAdapter.Object.SendAsync("query", "USE NS test DB test;", null, default);
            }
            catch
            {
                await mockConnectionPool.Object.ReleaseAsync(connection, healthy: false);
                throw;
            }
        });

        // Assert
        Assert.True(releaseWasCalled, "ReleaseAsync should have been called to prevent connection leak");
        Assert.True(markedUnhealthy, "Connection should be marked as unhealthy on failure");
    }

    [Fact]
    public async Task F2_ConnectAsync_WhenConnectAsyncThrows_ReleasesConnection()
    {
        // Arrange — verify the acquire→connect-fail→release pattern.
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        mockAdapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConnectionException("Simulated connection failure"));

        mockConnectionPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        bool releaseWasCalled = false;
        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .Callback<IProtocolAdapter, bool>((_, __) => releaseWasCalled = true)
            .Returns(Task.CompletedTask);

        // Act — simulate acquire→connect(throws)→release pattern.
        IProtocolAdapter? connection = null;
        await Assert.ThrowsAsync<ConnectionException>(async () =>
        {
            connection = await mockConnectionPool.Object.AcquireAsync();
            try
            {
                await mockAdapter.Object.ConnectAsync();
            }
            catch
            {
                await mockConnectionPool.Object.ReleaseAsync(connection, healthy: false);
                throw;
            }
        });

        // Assert
        Assert.True(releaseWasCalled, "ReleaseAsync should have been called even when ConnectAsync fails");
    }

    [Fact]
    public async Task F2_ConnectAsync_WhenExceptionDuringCleanup_DoesNotThrow()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        mockAdapter.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConnectionException("Connection failed"));

        mockConnectionPool.Setup(p => p.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        // ReleaseAsync also throws
        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Release failed"));

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        // Act - Should only throw original ConnectionException, not release exception
        var exception = await Assert.ThrowsAsync<ConnectionException>(async () =>
        {
            var poolField = typeof(SurrealDbClient)
                .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
            poolField?.SetValue(client, mockConnectionPool.Object);

            var connection = await mockConnectionPool.Object.AcquireAsync();

            var currentConnectionField = typeof(SurrealDbClient)
                .GetField("_currentConnection", BindingFlags.NonPublic | BindingFlags.Instance);
            currentConnectionField?.SetValue(client, connection);

            await mockAdapter.Object.ConnectAsync();
        });

        // Assert - should get the original exception, not the cleanup exception
        Assert.Contains("Connection failed", exception.Message);

        // Cleanup
        await client.DisposeAsync();
    }

    #endregion

    #region F3: _currentConnection Never Released Tests

    [Fact]
    public async Task F3_DisconnectAsync_ReleasesConnectionToPool()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        bool releaseWasCalled = false;
        bool markedAsHealthy = false;

        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .Callback<IProtocolAdapter, bool>((conn, healthy) =>
            {
                releaseWasCalled = true;
                markedAsHealthy = healthy;
            })
            .Returns(Task.CompletedTask);

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        // Set up internal state using reflection
        var poolField = typeof(SurrealDbClient)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
        poolField?.SetValue(client, mockConnectionPool.Object);

        var currentConnectionField = typeof(SurrealDbClient)
            .GetField("_currentConnection", BindingFlags.NonPublic | BindingFlags.Instance);
        currentConnectionField?.SetValue(client, mockAdapter.Object);

        var isConnectedField = typeof(SurrealDbClient)
            .GetField("_isConnected", BindingFlags.NonPublic | BindingFlags.Instance);
        isConnectedField?.SetValue(client, true);

        // Act
        await client.DisconnectAsync();

        // Assert
        Assert.True(releaseWasCalled, "DisconnectAsync should release connection to pool");
        Assert.True(markedAsHealthy, "Connection should be marked as healthy during normal disconnect");
        Assert.False(client.IsConnected, "Client should no longer be connected");

        var currentConnection = currentConnectionField?.GetValue(client);
        Assert.Null(currentConnection);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task F3_DisconnectAsync_WhenReleaseThrows_SuppressesException()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Release failed"));

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        var poolField = typeof(SurrealDbClient)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
        poolField?.SetValue(client, mockConnectionPool.Object);

        var currentConnectionField = typeof(SurrealDbClient)
            .GetField("_currentConnection", BindingFlags.NonPublic | BindingFlags.Instance);
        currentConnectionField?.SetValue(client, mockAdapter.Object);

        // Act - should not throw
        await client.DisconnectAsync();

        // Assert
        Assert.False(client.IsConnected);

        // Cleanup
        await client.DisposeAsync();
    }

    [Fact]
    public async Task F3_DisposeAsync_ReleasesConnectionAndDisposesPool()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        bool releaseWasCalled = false;
        bool poolDisposeCalled = false;
        bool markedAsHealthy = false;

        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .Callback<IProtocolAdapter, bool>((conn, healthy) =>
            {
                releaseWasCalled = true;
                markedAsHealthy = healthy;
            })
            .Returns(Task.CompletedTask);

        mockConnectionPool.Setup(p => p.DisposeAsync())
            .Callback(() => poolDisposeCalled = true)
            .Returns(ValueTask.CompletedTask);

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        var poolField = typeof(SurrealDbClient)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
        poolField?.SetValue(client, mockConnectionPool.Object);

        var currentConnectionField = typeof(SurrealDbClient)
            .GetField("_currentConnection", BindingFlags.NonPublic | BindingFlags.Instance);
        currentConnectionField?.SetValue(client, mockAdapter.Object);

        // Act
        await client.DisposeAsync();

        // Assert
        Assert.True(releaseWasCalled, "DisposeAsync should release current connection");
        Assert.False(markedAsHealthy, "Connection should be marked as unhealthy during disposal");
        Assert.True(poolDisposeCalled, "DisposeAsync should dispose the connection pool");

        var currentConnection = currentConnectionField?.GetValue(client);
        Assert.Null(currentConnection);

        var pool = poolField?.GetValue(client);
        Assert.Null(pool);
    }

    [Fact]
    public async Task F3_DisposeAsync_WhenNoConnection_OnlyDisposesPool()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();

        bool poolDisposeCalled = false;

        mockConnectionPool.Setup(p => p.DisposeAsync())
            .Callback(() => poolDisposeCalled = true)
            .Returns(ValueTask.CompletedTask);

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        var poolField = typeof(SurrealDbClient)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
        poolField?.SetValue(client, mockConnectionPool.Object);

        // currentConnection is null

        // Act
        await client.DisposeAsync();

        // Assert
        Assert.True(poolDisposeCalled, "DisposeAsync should dispose the connection pool");

        // Verify ReleaseAsync was never called since there was no connection
        mockConnectionPool.Verify(
            p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task F3_DisposeAsync_MultipleCalls_OnlyExecutesOnce()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        int disposeCount = 0;

        mockConnectionPool.Setup(p => p.DisposeAsync())
            .Callback(() => disposeCount++)
            .Returns(ValueTask.CompletedTask);

        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        var poolField = typeof(SurrealDbClient)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
        poolField?.SetValue(client, mockConnectionPool.Object);

        var currentConnectionField = typeof(SurrealDbClient)
            .GetField("_currentConnection", BindingFlags.NonPublic | BindingFlags.Instance);
        currentConnectionField?.SetValue(client, mockAdapter.Object);

        // Act
        await client.DisposeAsync();
        await client.DisposeAsync();
        await client.DisposeAsync();

        // Assert
        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task F3_DisposeAsync_SuppressesExceptionsDuringCleanup()
    {
        // Arrange
        var mockConnectionPool = new Mock<IConnectionPool>();
        var mockAdapter = new Mock<IProtocolAdapter>();

        mockConnectionPool.Setup(p => p.ReleaseAsync(It.IsAny<IProtocolAdapter>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Release failed"));

        mockConnectionPool.Setup(p => p.DisposeAsync())
            .Throws(new InvalidOperationException("Dispose failed"));

        var options = CreateValidOptions();
        var client = new SurrealDbClient(options);

        var poolField = typeof(SurrealDbClient)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance);
        poolField?.SetValue(client, mockConnectionPool.Object);

        var currentConnectionField = typeof(SurrealDbClient)
            .GetField("_currentConnection", BindingFlags.NonPublic | BindingFlags.Instance);
        currentConnectionField?.SetValue(client, mockAdapter.Object);

        // Act - should not throw
        await client.DisposeAsync();

        // Assert - no exception thrown
        Assert.True(true);
    }

    #endregion
}
