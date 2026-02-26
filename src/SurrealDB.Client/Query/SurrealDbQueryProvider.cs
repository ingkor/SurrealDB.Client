namespace SurrealDB.Client.Query;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using Session;

/// <summary>
/// IQueryProvider implementation for SurrealDB.
/// Translates LINQ expressions to SurrealQL and executes queries.
/// </summary>
public class SurrealDbQueryProvider : IQueryProvider
{
    private readonly ISurrealDbSession _session;
    private readonly SurrealDbClient _client;
    private readonly IQueryCompiler _compiler;
    private readonly string _table;

    /// <summary>
    /// Creates a new query provider.
    /// </summary>
    public SurrealDbQueryProvider(ISurrealDbSession session, SurrealDbClient client, IQueryCompiler compiler, string table)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _table = table ?? throw new ArgumentNullException(nameof(table));
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

        var compiled = _compiler.CompileDetailed(expression, _table);

        // For scalar queries, run synchronously (blocking)
        // In production, this should be async
        var task = _client.QueryAsync<dynamic>(compiled.SurrealQL);
        var result = task.GetAwaiter().GetResult();

        return result?.FirstOrDefault();
    }

    /// <summary>
    /// Executes a query (returns typed result).
    /// </summary>
    public TResult Execute<TResult>(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var compiled = _compiler.CompileDetailed(expression, _table);

        // Check if this is a scalar query
        if (compiled.IsScalar && typeof(TResult).IsGenericType &&
            typeof(TResult).GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            // Return enumerable for scalar results
            var task = _client.QueryAsync<dynamic>(compiled.SurrealQL);
            var results = task.GetAwaiter().GetResult() ?? new List<dynamic>();

            return (TResult)(object)results;
        }

        // Execute typed query
        var queryTask = _client.QueryAsync<TResult>(compiled.SurrealQL);
        var queryResults = queryTask.GetAwaiter().GetResult();

        if (typeof(TResult).IsGenericType &&
            typeof(TResult).GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return (TResult)(object)(queryResults ?? new List<TResult>());
        }

        // If requesting a single result, return first or default
        if (typeof(TResult).IsGenericType && typeof(TResult).GenericTypeArguments[0].IsClass)
        {
            var elementType = typeof(TResult).GenericTypeArguments[0];
            var list = (IEnumerable<TResult>?)queryResults;
            var first = list?.FirstOrDefault();

            return first!;
        }

        return queryResults ?? default!;
    }
}
