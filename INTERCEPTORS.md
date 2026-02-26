# Interceptors & Middleware System

> Advanced decorator pattern for comprehensive query, command, and transaction interception - enterprise-grade observability.

## Overview

Interceptors provide hooks into the client pipeline for:
- Query logging and modification
- Command execution tracking
- Soft deletes and auditing
- Multi-tenant filtering
- Performance profiling

## Architecture

```
Request
  ↓
QueryInterceptor.QueryExecuting()
  ↓
SaveChangesInterceptor.SavingChanges()
  ↓
CommandInterceptor.CommandExecuting()
  ↓
[Server]
  ↓
CommandInterceptor.CommandExecuted()
  ↓
SaveChangesInterceptor.SavedChanges()
  ↓
QueryInterceptor.QueryExecuted()
  ↓
Response
```

## IQueryInterceptor

Intercept query execution:

```csharp
public interface IQueryInterceptor
{
    InterceptionResult<QueryResult> QueryExecuting(
        QueryEventData eventData,
        InterceptionResult<QueryResult> result);

    ValueTask<InterceptionResult<QueryResult>> QueryExecutingAsync(
        QueryEventData eventData,
        InterceptionResult<QueryResult> result);

    InterceptionResult<QueryResult> QueryExecuted(
        QueryEventData eventData,
        QueryResult result);

    ValueTask<InterceptionResult<QueryResult>> QueryExecutedAsync(
        QueryEventData eventData,
        QueryResult result);
}

public class QueryEventData
{
    public string Query { get; }
    public Dictionary<string, object> Parameters { get; }
    public DateTime ExecutedAt { get; }
    public TimeSpan Duration { get; }
    public ISurrealDbSession Session { get; }
}
```

### Example: Query Logging

```csharp
public class QueryLoggingInterceptor : IQueryInterceptor
{
    private readonly ILogger<QueryLoggingInterceptor> _logger;

    public QueryLoggingInterceptor(ILogger<QueryLoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public ValueTask<InterceptionResult<QueryResult>> QueryExecutingAsync(
        QueryEventData eventData,
        InterceptionResult<QueryResult> result)
    {
        _logger.LogInformation(
            "Executing query: {Query} with parameters: {Parameters}",
            eventData.Query,
            eventData.Parameters);

        return default;
    }

    public ValueTask<InterceptionResult<QueryResult>> QueryExecutedAsync(
        QueryEventData eventData,
        QueryResult result)
    {
        _logger.LogInformation(
            "Query executed in {Duration}ms: {Query}",
            eventData.Duration.TotalMilliseconds,
            eventData.Query);

        return default;
    }
}
```

## ISaveChangesInterceptor

Intercept SaveChanges operations:

```csharp
public interface ISaveChangesInterceptor
{
    InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result);

    ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result);

    InterceptionResult<int> SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result);

    ValueTask<InterceptionResult<int>> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result);
}

public class DbContextEventData
{
    public ISurrealDbSession Session { get; }
    public IEnumerable<EntityEntry> ChangedEntities { get; }
    public DateTime ExecutedAt { get; }
}
```

### Example: Auditing Interceptor

```csharp
public class AuditingInterceptor : ISaveChangesInterceptor
{
    private readonly ILogger<AuditingInterceptor> _logger;
    private readonly IAuditService _auditService;

    public AuditingInterceptor(ILogger<AuditingInterceptor> logger, IAuditService auditService)
    {
        _logger = logger;
        _auditService = auditService;
    }

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var changes = eventData.ChangedEntities
            .Select(entry => new AuditEntry
            {
                EntityType = entry.Entity.GetType().Name,
                State = entry.State,
                Changes = GetPropertyChanges(entry),
                ChangedAt = DateTime.UtcNow,
                UserId = GetCurrentUserId()
            })
            .ToList();

        await _auditService.LogChangesAsync(changes);

        _logger.LogInformation("Audited {Count} entity changes", changes.Count);

        return result;
    }

    public ValueTask<InterceptionResult<int>> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Exception != null)
        {
            _logger.LogError("SaveChanges failed: {Exception}", eventData.Exception);
        }
        else
        {
            _logger.LogInformation("SaveChanges succeeded: {RowsAffected} rows", result.Result);
        }

        return new(result);
    }

    private Dictionary<string, object> GetPropertyChanges(EntityEntry entry)
    {
        return entry.Properties
            .Where(p => p.IsModified)
            .ToDictionary(
                p => p.Name,
                p => p.CurrentValue);
    }

    private string GetCurrentUserId() => /* Get from context */;
}
```

## ICommandInterceptor

Intercept low-level database commands:

```csharp
public interface ICommandInterceptor
{
    InterceptionResult<DbCommand> CommandCreating(
        CommandCreatingEventData eventData,
        InterceptionResult<DbCommand> result);

    InterceptionResult<DbCommand> CommandExecuting(
        CommandExecutingEventData eventData,
        InterceptionResult<DbCommand> result);

    ValueTask<InterceptionResult<DbDataReader>> CommandExecutedAsync(
        CommandExecutedEventData eventData,
        InterceptionResult<DbDataReader> result);
}
```

## Advanced Interceptor Patterns

### Soft Deletes

```csharp
public class SoftDeleteInterceptor : IQueryInterceptor
{
    public InterceptionResult<QueryResult> QueryExecuting(
        QueryEventData eventData,
        InterceptionResult<QueryResult> result)
    {
        // Inject WHERE clause for soft deletes
        if (ShouldFilterSoftDeletes(eventData.Query))
        {
            var modifiedQuery = eventData.Query + " AND deleted_at IS NULL";
            // Return modified query
        }

        return result;
    }

    public ValueTask<InterceptionResult<QueryResult>> QueryExecutedAsync(
        QueryEventData eventData,
        QueryResult result)
    {
        return default;
    }

    private bool ShouldFilterSoftDeletes(string query) =>
        query.Contains("FROM") && !query.Contains("SHOW_DELETED");
}
```

### Multi-Tenant Filtering

```csharp
public class MultiTenantInterceptor : IQueryInterceptor
{
    private readonly ITenantContext _tenantContext;

    public MultiTenantInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public InterceptionResult<QueryResult> QueryExecuting(
        QueryEventData eventData,
        InterceptionResult<QueryResult> result)
    {
        var tenantId = _tenantContext.CurrentTenant?.Id;

        if (tenantId != null)
        {
            // Add tenant filter
            var modifiedQuery = ModifyQueryForTenant(eventData.Query, tenantId);
            eventData.Parameters["tenant_id"] = tenantId;
            // Return modified with updated parameters
        }

        return result;
    }

    private string ModifyQueryForTenant(string query, string tenantId)
    {
        // Inject tenant_id filter
        return query.Replace("WHERE", "WHERE tenant_id = $tenant_id AND");
    }
}
```

### Performance Profiling

```csharp
public class PerformanceProfilingInterceptor : IQueryInterceptor
{
    private readonly ILogger<PerformanceProfilingInterceptor> _logger;

    public InterceptionResult<QueryResult> QueryExecuting(
        QueryEventData eventData,
        InterceptionResult<QueryResult> result)
    {
        // Start timer in eventData
        return result;
    }

    public ValueTask<InterceptionResult<QueryResult>> QueryExecutedAsync(
        QueryEventData eventData,
        QueryResult result)
    {
        var duration = eventData.Duration;
        var threshold = TimeSpan.FromMilliseconds(100);

        if (duration > threshold)
        {
            _logger.LogWarning(
                "Slow query ({Duration}ms): {Query}",
                duration.TotalMilliseconds,
                eventData.Query);
        }

        return new(result);
    }
}
```

## Registration & Configuration

```csharp
services.AddSurrealDbClient(options =>
{
    // Add interceptors
    options.AddInterceptor<QueryLoggingInterceptor>();
    options.AddInterceptor<AuditingInterceptor>();
    options.AddInterceptor<SoftDeleteInterceptor>();
    options.AddInterceptor<MultiTenantInterceptor>();
    options.AddInterceptor<PerformanceProfilingInterceptor>();
});

// Interceptors execute in registration order
```

## Interceptor Control Flow

```csharp
public interface IInterceptionResult<T>
{
    T Result { get; }
    bool IsSuppressed { get; }
}

// Suppress execution (return cached/default result)
public InterceptionResult<QueryResult> SuppressQueryExecution(QueryResult cachedResult)
{
    return new InterceptionResult<QueryResult>(result: cachedResult, suppress: true);
}

// Modify and continue
public InterceptionResult<QueryResult> ModifyQueryAndContinue(string newQuery)
{
    // Continue with modified query
    return new InterceptionResult<QueryResult>(suppress: false);
}
```

## Best Practices

1. **Keep interceptors stateless** - Instantiated once
2. **Short execution** - Don't block pipeline
3. **Order matters** - Register in correct sequence
4. **Handle exceptions** - Don't break pipeline
5. **Use dependency injection** - Access services
6. **Log strategically** - Don't over-log
7. **Cache when appropriate** - Reuse results

