namespace SurrealDB.Client.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using SurrealDB.Client.Query;
using Xunit;

[Trait("Category", "Unit")]
public class QueryCompilerTests
{
    private readonly SurrealQueryCompiler _compiler = new();

    [Fact]
    public void Compile_SimpleQuery_GeneratesBasicSelect()
    {
        // Arrange
        var query = new List<TestUser>().AsQueryable();

        // Act
        var result = _compiler.Compile(query.Expression);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("SELECT", result);
    }

    [Fact]
    public void CompileDetailed_WithTableName_IncludesFromClause()
    {
        // Arrange
        var query = new List<TestUser>().AsQueryable();

        // Act
        var result = _compiler.CompileDetailed(query.Expression, "users");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("users", result.TableName);
        Assert.Contains("FROM users", result.SurrealQL);
    }

    [Fact]
    public void CompileDetailed_ReturnsCompiledQuery()
    {
        // Arrange
        var query = new List<TestUser>().AsQueryable();

        // Act
        var result = _compiler.CompileDetailed(query.Expression, "users");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.SurrealQL);
        Assert.NotNull(result.Parameters);
        Assert.False(result.IsScalar);
    }

    [Fact]
    public void Compile_NullExpression_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _compiler.Compile(null!));
    }

    [Fact]
    public void CompileDetailed_NullExpression_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _compiler.CompileDetailed(null!, "users"));
    }

    // -------------------------------------------------------------------------
    // SELECT projection tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Compile_SinglePropertyProjection_GeneratesSelectColumn()
    {
        var query = new List<TestUser>().AsQueryable()
            .Select(u => u.Name);

        var result = _compiler.Compile(query.Expression);

        Assert.Contains("SELECT name", result);
        Assert.DoesNotContain("SELECT *", result);
    }

    [Fact]
    public void Compile_AnonymousTypeProjection_GeneratesColumnList()
    {
        var query = new List<TestUser>().AsQueryable()
            .Select(u => new { u.Name, u.Email });

        var result = _compiler.Compile(query.Expression);

        Assert.Contains("SELECT name, email", result);
    }

    [Fact]
    public void Compile_AnonymousTypeWithAlias_GeneratesAsClauses()
    {
        var query = new List<TestUser>().AsQueryable()
            .Select(u => new { FullName = u.Name, u.Email });

        var result = _compiler.Compile(query.Expression);

        Assert.Contains("name AS full_name", result);
        Assert.Contains("email", result);
    }

    [Fact]
    public void Compile_AliasMatchesSource_NoAsClauses()
    {
        var query = new List<TestUser>().AsQueryable()
            .Select(u => new { u.Name });

        var result = _compiler.Compile(query.Expression);

        Assert.Contains("SELECT name", result);
        Assert.DoesNotContain(" AS ", result);
    }

    [Fact]
    public void Compile_UnrecognizedProjection_FallsBackToSelectStar()
    {
        // u.Name.Length is a property of a property — not a direct member of param
        var query = new List<TestUser>().AsQueryable()
            .Select(u => u.Name!.Length);

        var result = _compiler.Compile(query.Expression);

        Assert.Contains("SELECT *", result);
    }

    [Fact]
    public void Compile_ProjectionCombinedWithWhere_GeneratesBothClauses()
    {
        var query = new List<TestUser>().AsQueryable()
            .Where(u => u.Age > 18)
            .Select(u => new { u.Name, u.Email });

        var result = _compiler.CompileDetailed(query.Expression, "users");

        Assert.Contains("SELECT name, email", result.SurrealQL);
        Assert.Contains("WHERE", result.SurrealQL);
    }

    [Fact]
    public void CompileDetailed_TableNameFromQueryMetadata()
    {
        var fakeProvider = new FakeQueryProvider();
        var query = new SurrealDbQuery<TestUser>(fakeProvider, tableName: "users");

        var result = _compiler.CompileDetailed(query.Expression);

        Assert.Equal("users", result.TableName);
        Assert.Contains("FROM users", result.SurrealQL);
    }

    [Fact]
    public void CompileDetailed_ExplicitTableNameOverridesMetadata()
    {
        var fakeProvider = new FakeQueryProvider();
        var query = new SurrealDbQuery<TestUser>(fakeProvider, tableName: "users");

        var result = _compiler.CompileDetailed(query.Expression, tableName: "accounts");

        Assert.Equal("accounts", result.TableName);
        Assert.Contains("FROM accounts", result.SurrealQL);
    }

    [Fact]
    public void CompileDetailed_NoTableName_NoFromClause()
    {
        var query = new List<TestUser>().AsQueryable();

        var result = _compiler.CompileDetailed(query.Expression, tableName: null);

        Assert.DoesNotContain("FROM", result.SurrealQL);
    }

    // -------------------------------------------------------------------------
    // Test model + helpers
    // -------------------------------------------------------------------------

    private class TestUser
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
    }

    /// <summary>Minimal IQueryProvider for constructing SurrealDbQuery in tests.</summary>
    private sealed class FakeQueryProvider : IQueryProvider
    {
        public IQueryable CreateQuery(System.Linq.Expressions.Expression expression) => throw new NotImplementedException();
        public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression) => throw new NotImplementedException();
        public object? Execute(System.Linq.Expressions.Expression expression) => throw new NotImplementedException();
        public TResult Execute<TResult>(System.Linq.Expressions.Expression expression) => throw new NotImplementedException();
    }
}
