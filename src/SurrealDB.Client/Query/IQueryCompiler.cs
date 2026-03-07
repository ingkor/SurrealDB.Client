namespace SurrealDB.Client.Query;

using System.Linq.Expressions;

/// <summary>
/// Compiles expression trees to SurrealQL query strings.
/// </summary>
public interface IQueryCompiler
{
    /// <summary>
    /// Compiles an expression tree to a SurrealQL string.
    /// </summary>
    /// <param name="expression">The expression tree to compile.</param>
    /// <returns>The compiled SurrealQL string.</returns>
    string Compile(Expression expression);

    /// <summary>
    /// Compiles an expression tree and returns additional metadata.
    /// </summary>
    /// <param name="expression">The expression tree to compile.</param>
    /// <param name="tableName">The table being queried.</param>
    /// <returns>Compiled query info including SQL and parameters.</returns>
    CompiledQuery CompileDetailed(Expression expression, string? tableName = null);
}

/// <summary>
/// Detailed compilation result with metadata.
/// </summary>
public class CompiledQuery
{
    /// <summary>
    /// The generated SurrealQL query string.
    /// </summary>
    public string SurrealQL { get; set; } = string.Empty;

    /// <summary>
    /// Query parameters (e.g., for WHERE clauses).
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>
    /// The table name being queried.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Whether this is a scalar query (returns single value).
    /// </summary>
    public bool IsScalar { get; set; }

    /// <summary>
    /// Entity type being queried.
    /// </summary>
    public Type? EntityType { get; set; }
}
