namespace SurrealDB.Client.Tests.Unit;

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SurrealDB.Client.Security;
using SurrealDB.Client.Session;
using Xunit;

/// <summary>
/// Tests for audit attribute enforcement (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
/// via SurrealDbSession.ApplyAuditAttributes.
/// </summary>
[Trait("Category", "Unit")]
public class AuditAttributeTests
{
    // -------------------------------------------------------------------------
    // Test entity types
    // -------------------------------------------------------------------------

    private class FullAuditEntity
    {
        public string? Id { get; set; }
        [CreatedAt] public DateTime CreatedAt { get; set; }
        [UpdatedAt] public DateTime UpdatedAt { get; set; }
        [CreatedBy] public string? CreatedBy { get; set; }
        [UpdatedBy] public string? UpdatedBy { get; set; }
    }

    private class UpdatedAtOnlyEntity
    {
        public string? Id { get; set; }
        [UpdatedAt] public DateTime UpdatedAt { get; set; }
    }

    private class WrongTypeAuditEntity
    {
        public string? Id { get; set; }
        // [CreatedAt] on a string — should be silently skipped
        [CreatedAt] public string? CreatedAt { get; set; }
    }

    private class PlainEntity
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private class DateTimeOffsetEntity
    {
        public string? Id { get; set; }
        [CreatedAt] public DateTimeOffset CreatedAt { get; set; }
        [UpdatedAt] public DateTimeOffset UpdatedAt { get; set; }
    }

    // -------------------------------------------------------------------------
    // Helper: invoke ApplyAuditAttributes via reflection
    // -------------------------------------------------------------------------

    private static void InvokeApplyAuditAttributes(object session, object entity, bool isInsert)
    {
        var method = typeof(SurrealDbSession)
            .GetMethod("ApplyAuditAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(session, new object[] { entity, isInsert });
    }

    private static readonly SurrealDbClientOptions _testOptions = new()
    {
        ConnectionString = "surreal://localhost:8000",
        Namespace = "test",
        Database = "test"
    };

    private static SurrealDbSession CreateSession()
    {
        // SurrealDbSession is internal; InternalsVisibleTo allows direct instantiation.
        // A disconnected client is sufficient — ApplyAuditAttributes doesn't call the DB.
        var client = new SurrealDbClient(_testOptions);
        return new SurrealDbSession(client);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CreatedAt_SetOnInsert()
    {
        var session = CreateSession();
        var entity = new FullAuditEntity();
        var before = DateTime.UtcNow.AddSeconds(-1);

        InvokeApplyAuditAttributes(session, entity, isInsert: true);

        Assert.NotEqual(default, entity.CreatedAt);
        Assert.True(entity.CreatedAt >= before);
    }

    [Fact]
    public void UpdatedAt_SetOnInsert()
    {
        var session = CreateSession();
        var entity = new FullAuditEntity();
        var before = DateTime.UtcNow.AddSeconds(-1);

        InvokeApplyAuditAttributes(session, entity, isInsert: true);

        Assert.NotEqual(default, entity.UpdatedAt);
        Assert.True(entity.UpdatedAt >= before);
    }

    [Fact]
    public void UpdatedAt_SetOnUpdate_CreatedAt_NotChanged()
    {
        var session = CreateSession();
        var originalCreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = new FullAuditEntity { CreatedAt = originalCreatedAt };

        InvokeApplyAuditAttributes(session, entity, isInsert: false);

        // CreatedAt should NOT be touched on update
        Assert.Equal(originalCreatedAt, entity.CreatedAt);
        // UpdatedAt should be set
        Assert.NotEqual(default, entity.UpdatedAt);
    }

    [Fact]
    public void CreatedBy_SetWhenUserIdProvided()
    {
        var session = CreateSession();
        session.SetCurrentUser("users:alice");
        var entity = new FullAuditEntity();

        InvokeApplyAuditAttributes(session, entity, isInsert: true);

        Assert.Equal("users:alice", entity.CreatedBy);
    }

    [Fact]
    public void CreatedBy_NotSetWhenUserIdNull()
    {
        var session = CreateSession();
        session.SetCurrentUser(null);
        var entity = new FullAuditEntity { CreatedBy = "original" };

        InvokeApplyAuditAttributes(session, entity, isInsert: true);

        // CreatedBy should remain unchanged when userId is null
        Assert.Equal("original", entity.CreatedBy);
    }

    [Fact]
    public void WrongPropertyType_DoesNotThrow()
    {
        var session = CreateSession();
        var entity = new WrongTypeAuditEntity { CreatedAt = "unchanged" };

        var ex = Record.Exception(() =>
            InvokeApplyAuditAttributes(session, entity, isInsert: true));

        Assert.Null(ex);
        Assert.Equal("unchanged", entity.CreatedAt); // value unchanged
    }

    [Fact]
    public void NoAuditAttributes_NoSideEffects()
    {
        var session = CreateSession();
        var entity = new PlainEntity { Id = "test:1", Name = "Bob" };

        var ex = Record.Exception(() =>
            InvokeApplyAuditAttributes(session, entity, isInsert: true));

        Assert.Null(ex);
        Assert.Equal("Bob", entity.Name);
    }

    [Fact]
    public void DateTimeOffset_SetCorrectly()
    {
        var session = CreateSession();
        var entity = new DateTimeOffsetEntity();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        InvokeApplyAuditAttributes(session, entity, isInsert: true);

        Assert.NotEqual(default, entity.CreatedAt);
        Assert.True(entity.CreatedAt >= before);
        Assert.NotEqual(default, entity.UpdatedAt);
    }

    [Fact]
    public void SetCurrentUser_ClearsWhenNull()
    {
        var session = CreateSession();
        session.SetCurrentUser("users:bob");
        session.SetCurrentUser(null);

        var entity = new FullAuditEntity { CreatedBy = "unchanged" };
        InvokeApplyAuditAttributes(session, entity, isInsert: true);

        Assert.Equal("unchanged", entity.CreatedBy); // not overwritten when null
    }

    [Fact]
    public void ReflectionCache_SecondCallDoesNotThrow()
    {
        // Exercises cache hit path — same type called twice
        var session = CreateSession();
        var entity1 = new FullAuditEntity();
        var entity2 = new FullAuditEntity();

        InvokeApplyAuditAttributes(session, entity1, isInsert: true);
        var ex = Record.Exception(() =>
            InvokeApplyAuditAttributes(session, entity2, isInsert: true));

        Assert.Null(ex);
        Assert.NotEqual(default, entity2.CreatedAt);
    }
}
