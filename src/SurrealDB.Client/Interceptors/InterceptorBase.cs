namespace SurrealDB.Client.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Base class for implementing interceptors.
/// Provides default no-op implementations.
/// </summary>
public abstract class InterceptorBase : ISurrealDbInterceptor
{
    /// <summary>
    /// Called before query execution.
    /// </summary>
    public virtual Task OnQueryExecuting(QueryExecutingEventArgs args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after query execution.
    /// </summary>
    public virtual Task OnQueryExecuted(QueryExecutedEventArgs args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called before connection opening.
    /// </summary>
    public virtual Task OnConnectionOpening(ConnectionOpeningEventArgs args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after connection opened.
    /// </summary>
    public virtual Task OnConnectionOpened(ConnectionOpenedEventArgs args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called before SaveChangesAsync.
    /// </summary>
    public virtual Task OnSaveChangesExecuting(SaveChangesEventArgs args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after SaveChangesAsync.
    /// </summary>
    public virtual Task OnSaveChangesExecuted(SaveChangesEventArgs args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Logging interceptor for diagnostics.
/// </summary>
public class LoggingInterceptor : InterceptorBase
{
    private readonly Action<string>? _log;

    public LoggingInterceptor(Action<string>? log = null)
    {
        _log = log;
    }

    public override Task OnQueryExecuting(QueryExecutingEventArgs args, CancellationToken cancellationToken = default)
    {
        _log?.Invoke($"[Query] Executing: {args.Query}");
        return Task.CompletedTask;
    }

    public override Task OnQueryExecuted(QueryExecutedEventArgs args, CancellationToken cancellationToken = default)
    {
        if (args.Success)
        {
            _log?.Invoke($"[Query] Completed in {args.Duration.TotalMilliseconds}ms: {args.RowsAffected} rows");
        }
        else
        {
            _log?.Invoke($"[Query] Failed: {args.Exception?.Message}");
        }

        return Task.CompletedTask;
    }

    public override Task OnConnectionOpened(ConnectionOpenedEventArgs args, CancellationToken cancellationToken = default)
    {
        if (args.Success)
        {
            _log?.Invoke($"[Connection] Opened at {args.OpenedAt:O}");
        }
        else
        {
            _log?.Invoke($"[Connection] Failed: {args.Exception?.Message}");
        }

        return Task.CompletedTask;
    }

    public override Task OnSaveChangesExecuted(SaveChangesEventArgs args, CancellationToken cancellationToken = default)
    {
        if (args.Success)
        {
            _log?.Invoke($"[SaveChanges] Committed {args.ChangeCount} changes in {args.Duration.TotalMilliseconds}ms");
        }
        else
        {
            _log?.Invoke($"[SaveChanges] Failed: {args.Exception?.Message}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Performance interceptor for benchmarking.
/// </summary>
public class PerformanceInterceptor : InterceptorBase
{
    private readonly Action<string>? _log;
    private readonly int _slowQueryThresholdMs;

    public PerformanceInterceptor(int slowQueryThresholdMs = 1000, Action<string>? log = null)
    {
        _slowQueryThresholdMs = slowQueryThresholdMs;
        _log = log;
    }

    public override Task OnQueryExecuted(QueryExecutedEventArgs args, CancellationToken cancellationToken = default)
    {
        if (args.Duration.TotalMilliseconds > _slowQueryThresholdMs)
        {
            _log?.Invoke($"[Slow Query] {args.Duration.TotalMilliseconds}ms: {args.Query}");
        }

        return Task.CompletedTask;
    }
}
