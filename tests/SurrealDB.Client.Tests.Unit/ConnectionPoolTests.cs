using Xunit;
using SurrealDB.Client.Connection;
using SurrealDB.Client.Protocol;
using Moq;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for connection pool management.
/// </summary>
public class ConnectionPoolTests
{
    [Fact]
    public async Task ConnectionPool_Initialize_CreatesConnections()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);

        // Act
        await pool.InitializeAsync();

        // Assert
        Assert.True(pool.AvailableCount > 0);
        Assert.True(pool.CurrentSize > 0);
    }

    [Fact]
    public async Task ConnectionPool_Acquire_ReturnsConnection()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act
        var connection = await pool.AcquireAsync();

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(mockAdapter.Object, connection);
    }

    [Fact]
    public async Task ConnectionPool_Release_ReturnsConnectionToPool()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();
        var initialAvailable = pool.AvailableCount;

        // Act
        var connection = await pool.AcquireAsync();
        await pool.ReleaseAsync(connection, healthy: true);

        // Assert
        Assert.Equal(initialAvailable, pool.AvailableCount);
    }

    [Fact]
    public async Task ConnectionPool_Statistics_ReturnsAccurateData()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act
        var stats = pool.GetStatistics();

        // Assert
        Assert.True(stats.TotalConnections > 0);
        Assert.Equal(stats.TotalConnections, stats.AvailableConnections);
        Assert.Equal(0, stats.InUseConnections);
    }

    [Fact]
    public async Task ConnectionPool_Clear_RemovesAllConnections()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act
        await pool.ClearAsync();

        // Assert
        Assert.Equal(0, pool.CurrentSize);
        Assert.Equal(0, pool.AvailableCount);
    }

    [Fact]
    public async Task ConnectionPool_Dispose_CleansUpResources()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act
        await pool.DisposeAsync();

        // Assert - should throw ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.AcquireAsync());
    }
}
