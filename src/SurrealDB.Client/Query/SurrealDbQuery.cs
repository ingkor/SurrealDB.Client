namespace SurrealDB.Client.Query;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

/// <summary>
/// Internal interface that exposes query metadata (table name) to the expression compiler.
/// </summary>
internal interface ISurrealDbQueryMetadata
{
    string? TableName { get; }
}

/// <summary>
/// IQueryable<T> implementation for SurrealDB queries.
/// Enables LINQ-style query composition with deferred execution.
/// </summary>
public class SurrealDbQuery<T> : IQueryable<T>, IEnumerable<T>, ISurrealDbQueryMetadata
{
    private readonly IQueryProvider _provider;
    private readonly Expression _expression;
    private readonly string? _tableName;

    /// <summary>
    /// Creates a new SurrealDB query with an optional table name.
    /// </summary>
    public SurrealDbQuery(IQueryProvider provider, string? tableName = null)
        : this(provider, null!, tableName)
    {
        _expression = Expression.Constant(this);
    }

    /// <summary>
    /// Creates a new SurrealDB query with a specific expression.
    /// </summary>
    public SurrealDbQuery(IQueryProvider provider, Expression expression, string? tableName = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _expression = expression ?? Expression.Constant(this);
        _tableName = tableName;
    }

    /// <inheritdoc/>
    string? ISurrealDbQueryMetadata.TableName => _tableName;

    /// <summary>
    /// Gets the element type.
    /// </summary>
    public Type ElementType => typeof(T);

    /// <summary>
    /// Gets the expression tree.
    /// </summary>
    public Expression Expression => _expression;

    /// <summary>
    /// Gets the query provider.
    /// </summary>
    public IQueryProvider Provider => _provider;

    /// <summary>
    /// Executes the query and returns results.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        var result = _provider.Execute<IEnumerable<T>>(_expression);
        return result?.GetEnumerator() ?? Enumerable.Empty<T>().GetEnumerator();
    }

    /// <summary>
    /// Executes the query and returns results (non-generic).
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Converts query to list (forces execution).
    /// </summary>
    public List<T> ToList()
    {
        return ((IEnumerable<T>)this).ToList();
    }

    /// <summary>
    /// Converts query to array (forces execution).
    /// </summary>
    public T[] ToArray()
    {
        return ((IEnumerable<T>)this).ToArray();
    }

    /// <summary>
    /// Gets the first result (forces execution).
    /// </summary>
    public T? FirstOrDefault()
    {
        return ((IEnumerable<T>)this).FirstOrDefault();
    }

    /// <summary>
    /// Gets the first result or throws (forces execution).
    /// </summary>
    public T First()
    {
        return ((IEnumerable<T>)this).First();
    }

    /// <summary>
    /// Checks if any results match (forces execution).
    /// </summary>
    public bool Any()
    {
        return ((IEnumerable<T>)this).Any();
    }

    /// <summary>
    /// Counts results (forces execution).
    /// </summary>
    public int Count()
    {
        return ((IEnumerable<T>)this).Count();
    }
}
