namespace SurrealDB.Client.Query;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caching;
using Interceptors;
using Session;

/// <summary>
/// IQueryProvider implementation for SurrealDB.
/// Translates LINQ expressions to SurrealQL and executes queries.
/// Integrates caching and interceptors for performance and observability.
/// </summary>
public class SurrealDbQueryProvider : IQueryProvider
{
    private readonly ISurrealDbSession _session;
    private readonly SurrealDbClient _client;
    private readonly IQueryCompiler _compiler;
    private readonly string _table;
    private readonly IQueryCache? _cache;
    private readonly List<ISurrealDbInterceptor> _interceptors;

    /// <summary>
    /// Creates a new query provider.
    /// </summary>
    public SurrealDbQueryProvider(
        ISurrealDbSession session,
        SurrealDbClient client,
        IQueryCompiler compiler,
        string table,
        IQueryCache? cache = null,
        IEnumerable<ISurrealDbInterceptor>? interceptors = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _cache = cache;
        _interceptors = interceptors?.ToList() ?? new List<ISurrealDbInterceptor>();
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

    /// <summary>
    /// Executes a query asynchronously and returns typed results.
    /// Integrates caching and interceptors for performance and observability.
    /// </summary>
    public async Task<List<T>> ExecuteAsync<T>(
        Expression expression,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(expression);

        var compiled = _compiler.CompileDetailed(expression, _table);
        var cacheKey = GenerateCacheKey(compiled.SurrealQL);

        // Check cache first
        if (_cache != null)
        {
            var cached = _cache.Get<List<T>>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        var startTime = DateTime.UtcNow;
        var executingArgs = new QueryExecutingEventArgs { Query = compiled.SurrealQL };

        try
        {
            // Call OnQueryExecuting interceptors
            foreach (var interceptor in _interceptors)
            {
                await interceptor.OnQueryExecuting(executingArgs, cancellationToken).ConfigureAwait(false);

                if (executingArgs.IsCancelled)
                {
                    throw new OperationCanceledException("Query execution cancelled by interceptor");
                }
            }

            // Execute query
            var results = await _client.QueryAsync<T>(compiled.SurrealQL, cancellationToken)
                .ConfigureAwait(false);

            var resultList = results?.ToList() ?? new List<T>();

            // Cache result
            if (_cache != null)
            {
                _cache.Set(cacheKey, resultList, TimeSpan.FromMinutes(5));
            }

            // Call OnQueryExecuted interceptors
            var duration = DateTime.UtcNow - startTime;
            var executedArgs = new QueryExecutedEventArgs
            {
                Query = compiled.SurrealQL,
                RowsAffected = resultList.Count,
                Duration = duration,
                Exception = null
            };

            foreach (var interceptor in _interceptors)
            {
                await interceptor.OnQueryExecuted(executedArgs, cancellationToken).ConfigureAwait(false);
            }

            return resultList;
        }
        catch (Exception ex)
        {
            // Call OnQueryExecuted with exception
            var failedDuration = DateTime.UtcNow - startTime;
            var failedArgs = new QueryExecutedEventArgs
            {
                Query = compiled.SurrealQL,
                RowsAffected = 0,
                Duration = failedDuration,
                Exception = ex
            };

            foreach (var interceptor in _interceptors)
            {
                try
                {
                    await interceptor.OnQueryExecuted(failedArgs, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore interceptor errors during failure handling
                }
            }

            throw new QueryException($"Failed to execute query: {compiled.SurrealQL}", ex);
        }
    }

    /// <summary>
    /// Generates a cache key for a query.
    /// </summary>
    private static string GenerateCacheKey(string query)
    {
        using var hasher = System.Security.Cryptography.SHA256.Create();
        var hash = hasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes(query));
        return "query:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
