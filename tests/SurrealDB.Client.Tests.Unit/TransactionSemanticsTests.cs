namespace SurrealDB.Client.Tests.Unit;

using SurrealDB.Client.Session;
using Xunit;

[Trait("Category", "Unit")]
public class TransactionSemanticsTests
{
    private static SurrealDbSession CreateSession()
    {
        var client = new SurrealDbClient(new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        });
        return new SurrealDbSession(client);
    }

    private class TestEntity
    {
        public string? Id { get; set; }
    }

    [Fact]
    public async Task CommitAsync_WhenNoChanges_Succeeds()
    {
        var session = CreateSession();
        var txn = session.BeginTransaction();

        var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task CommitAsync_WhenHasUnsavedChanges_ThrowsInvalidOperationException()
    {
        var session = CreateSession();
        session.Add(new TestEntity { Id = "test:1" });
        var txn = session.BeginTransaction();

        var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("unsaved changes", ex!.Message);
    }

    [Fact]
    public async Task CommitAsync_AfterAddWithoutSave_ThrowsIfNotSavedAgain()
    {
        var session = CreateSession();
        var txn = session.BeginTransaction();
        session.Add(new TestEntity { Id = "test:2" });

        var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task RollbackAsync_CallsDiscard_LocalChangesCleared()
    {
        var session = CreateSession();
        session.Add(new TestEntity { Id = "test:3" });
        Assert.True(session.HasChanges);

        var txn = session.BeginTransaction();
        await txn.RollbackAsync();
        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task RollbackAsync_IsActiveBecomeFalse_AfterRollback()
    {
        var session = CreateSession();
        var txn = (SurrealDbSessionTransaction)session.BeginTransaction();
        await txn.RollbackAsync();
        Assert.False(txn.IsActive);
    }

    [Fact]
    public async Task CommitAsync_WhenNotActive_ThrowsInvalidOperationException()
    {
        var session = CreateSession();
        var txn = session.BeginTransaction();
        await txn.RollbackAsync();

        var ex = await Record.ExceptionAsync(() => txn.CommitAsync());
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("not active", ex!.Message);
    }
}
