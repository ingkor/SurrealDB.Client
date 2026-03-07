namespace SurrealDB.Client.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
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

    // Test model
    private class TestUser
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
    }
}
