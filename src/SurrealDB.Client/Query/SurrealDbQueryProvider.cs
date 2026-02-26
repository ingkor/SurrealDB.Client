namespace SurrealDB.Client.Query;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Session;

/// <summary>
/// IQueryProvider implementation for SurrealDB.
/// Translates LINQ expressions to SurrealQL and executes queries.
/// </summary>
public class SurrealDbQueryProvider : IQueryProvider
{
    private readonly ISurrealDbSession _session;
    private readonly IQueryCompiler _compiler;

    /// <summary>
    /// Creates a new query provider.
    /// </summary>
    public SurrealDbQueryProvider(ISurrealDbSession session, IQueryCompiler compiler)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    /// <summary>
    /// Creates a new queryable from an expression.
    /// </summary>
    public IQueryable<S> CreateQuery<S>(Expression expression) where S : class
    {
        ArgumentNullException.ThrowIfNull(expression);
        return new SurrealDbQuery<S>(this, expression);
    }

    /// <summary>
    /// Creates a new queryable from an expression (non-generic).
    /// </summary>
    public IQueryable CreateQuery(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var elementType = expression.Type.GetGenericArguments()[0];
        var queryableType = typeof(SurrealDbQuery<>).MakeGenericType(elementType);

        return (IQueryable)Activator.CreateInstance(queryableType, this, expression)!;
    }

    /// <summary>
    /// Executes a scalar query (returns single value).
    /// </summary>
    public object? Execute(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var surrealQL = _compiler.Compile(expression);
        // TODO: Execute scalar query
        return null;
    }

    /// <summary>
    /// Executes a query (returns typed result).
    /// </summary>
    public TResult Execute<TResult>(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var surrealQL = _compiler.Compile(expression);

        // TODO: Execute query and return TResult
        // For now, return default
        return default!;
    }
}
