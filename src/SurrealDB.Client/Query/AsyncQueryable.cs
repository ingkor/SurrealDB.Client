namespace SurrealDB.Client.Query;

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides async query execution for SurrealDB queries.
/// Enables ToListAsync, FirstOrDefaultAsync, etc.
/// </summary>
public static class AsyncQueryable
{
    /// <summary>
    /// Converts a SurrealDbQuery to a list asynchronously.
    /// </summary>
    public static async Task<List<T>> ToListAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is SurrealDbQuery<T> surrealQuery)
        {
            var provider = surrealQuery.Provider as SurrealDbQueryProvider;
            if (provider != null)
            {
                return await provider.ExecuteAsync<T>(surrealQuery.Expression, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Fallback to sync enumeration
        return await Task.FromResult(source.ToList()).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the first result asynchronously.
    /// </summary>
    public static async Task<T?> FirstOrDefaultAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var list = await ToListAsync(source, cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    /// <summary>
    /// Gets the first result or throws asynchronously.
    /// </summary>
    public static async Task<T> FirstAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = await FirstOrDefaultAsync(source, cancellationToken).ConfigureAwait(false);
        if (result == null)
            throw new InvalidOperationException("Sequence contains no elements");

        return result;
    }

    /// <summary>
    /// Counts results asynchronously.
    /// </summary>
    public static async Task<int> CountAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var list = await ToListAsync(source, cancellationToken).ConfigureAwait(false);
        return list.Count;
    }

    /// <summary>
    /// Checks if any results exist asynchronously.
    /// </summary>
    public static async Task<bool> AnyAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var list = await ToListAsync(source, cancellationToken).ConfigureAwait(false);
        return list.Count > 0;
    }
}
