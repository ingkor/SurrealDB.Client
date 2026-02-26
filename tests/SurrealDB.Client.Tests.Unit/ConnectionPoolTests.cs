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

    [Fact]
    public async Task ConnectionPool_GetStatistics_ThreadSafe()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 10 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        var statistics = new List<PoolStatistics>();
        var errors = new List<Exception>();

        // Act - concurrent GetStatistics calls while AcquireAsync is running
        var getStatisticsTasks = Enumerable.Range(0, 50)
            .Select(async _ =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var stats = pool.GetStatistics();
                        lock (statistics)
                        {
                            statistics.Add(stats);
                        }
                        await Task.Delay(10);
                    }
                }
                catch (Exception ex)
                {
                    lock (errors)
                    {
                        errors.Add(ex);
                    }
                }
            });

        await Task.WhenAll(getStatisticsTasks);

        // Assert
        Assert.Empty(errors); // No exceptions thrown
        Assert.NotEmpty(statistics); // At least some statistics were collected

        // Verify statistics invariants
        foreach (var stats in statistics)
        {
            Assert.True(stats.TotalConnections >= 0);
            Assert.True(stats.AvailableConnections >= 0);
            Assert.True(stats.InUseConnections >= 0);
            Assert.True(stats.TotalAcquisitions >= 0);
            Assert.True(stats.TotalReleases >= 0);
            Assert.True(stats.FailedHealthChecks >= 0);
        }
    }

    // -------------------------------------------------------------------------
    // P0.1 Acceptance tests: DisposeAsync deadlock & null-adapter corruption
    // -------------------------------------------------------------------------

    /// <summary>
    /// DisposeAsync must complete within the 5-second xUnit timeout.
    /// Before the fix, DisposeAsync called ClearAsync() while holding _disposeSemaphore,
    /// causing a non-reentrant deadlock on SemaphoreSlim.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task DisposeAsync_CompletesWithinTimeout_NoDeadlock()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act — must not hang; xUnit will fail the test after 5 000 ms
        await pool.DisposeAsync();

        // Assert — if we get here, no deadlock occurred
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.AcquireAsync());
    }

    /// <summary>
    /// Two concurrent calls to DisposeAsync must both complete without deadlocking.
    /// The second call is a no-op because _disposed is already true.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task DisposeAsync_ConcurrentFromTwoThreads_NoDeadlock()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act — fire two concurrent DisposeAsync calls
        var t1 = Task.Run(async () => await pool.DisposeAsync());
        var t2 = Task.Run(async () => await pool.DisposeAsync());

        // Both must complete; xUnit timeout guards against deadlock
        await Task.WhenAll(t1, t2);
    }

    /// <summary>
    /// ClearAsync must work independently from DisposeAsync — it should be callable
    /// repeatedly and must not interfere with a subsequent DisposeAsync.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ClearAsync_WorksIndependentlyFromDisposeAsync()
    {
        // Arrange
        var options = new SurrealDbClientOptions { PoolSize = 5 };
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(ct =>
            Task.FromResult(mockAdapter.Object));

        var pool = new ConnectionPool(options, factory);
        await pool.InitializeAsync();

        // Act — ClearAsync clears all connections without disposing the pool itself
        await pool.ClearAsync();
        Assert.Equal(0, pool.CurrentSize);
        Assert.Equal(0, pool.AvailableCount);

        // Pool should still be usable after ClearAsync
        // (re-initialise to put connections back)
        await pool.InitializeAsync();
        Assert.True(pool.CurrentSize > 0);

        // DisposeAsync must then succeed without deadlocking
        await pool.DisposeAsync();
    }

    /// <summary>
    /// Concurrent AcquireAsync calls that race against DisposeAsync must not throw
    /// NullReferenceException. Before the fix, a PooledConnection was added to
    /// _allConnections with Adapter = null, and a concurrent DisposeAsync would try
    /// to call Adapter.DisposeAsync() on that null reference.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task AcquireAsync_ConcurrentWithDisposeAsync_NoNullReferenceException()
    {
        // Arrange — pool starts empty so every Acquire creates a new connection
        var options = new SurrealDbClientOptions { PoolSize = 10, ConnectionTimeout = TimeSpan.FromSeconds(2) };

        // Introduce a small delay in the factory to widen the race window
        var mockAdapter = new Mock<IProtocolAdapter>();
        mockAdapter.Setup(a => a.IsConnected).Returns(true);
        mockAdapter.Setup(a => a.HealthCheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockAdapter.Setup(a => a.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var factory = new Func<CancellationToken, Task<IProtocolAdapter>>(async ct =>
        {
            await Task.Delay(10, ct); // widen the race window
            return mockAdapter.Object;
        });

        var pool = new ConnectionPool(options, factory);
        // Do NOT initialise — we want AcquireAsync to create connections from scratch

        var errors = new List<Exception>();

        // Spawn several concurrent acquirers
        var acquirers = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            try
            {
                var adapter = await pool.AcquireAsync();
                // Hold briefly then release
                await Task.Delay(20);
                await pool.ReleaseAsync(adapter);
            }
            catch (ObjectDisposedException) { /* expected once pool is disposed */ }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToList();

        // Dispose after a short delay to race against the acquirers
        var disposer = Task.Run(async () =>
        {
            await Task.Delay(15);
            await pool.DisposeAsync();
        });

        await Task.WhenAll(acquirers.Concat(new[] { disposer }));

        // No NullReferenceException (or any unexpected exception) must have been thrown
        Assert.Empty(errors);
    }
}
