# SurrealDB.Client Architecture & Design

> Architectural documentation including design overview, EF Core comparison, and key decisions.

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Architectural Overview](#architectural-overview)
3. [EF Core Comparison](#ef-core-comparison)
4. [Critical Architectural Decisions](#critical-architectural-decisions)
5. [Implementation Roadmap](#implementation-roadmap)

---

## Executive Summary

**SurrealDB.Client** is a production-grade .NET/C# client library for SurrealDB with modern database client patterns. This architecture combines lessons from Entity Framework Core with SurrealDB-specific optimizations.

**Core Capabilities:**
- ✅ Protocol abstraction (HTTP/WebSocket)
- ✅ Real-time subscriptions (unique to SurrealDB)
- ✅ Explicit connection pooling
- ✅ Multi-serializer support
- ✅ ISurrealDbSession (Unit of Work)
- ✅ Automatic change tracking with snapshots
- ✅ IQueryable<T> composable queries
- ✅ Optimistic concurrency tokens
- ✅ Typed exception hierarchy

---

## Architectural Overview

### Core Components

```
┌─────────────────────────────────────────────────────┐
│         ISurrealDbSession (Context)                 │
│  ┌──────────────────────────────────────────────┐   │
│  │ ChangeTracker                                │   │
│  │ ├─ EntityEntry tracking                      │   │
│  │ ├─ Snapshot comparison                       │   │
│  │ └─ State management (Added/Modified/Deleted) │   │
│  └──────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────┐   │
│  │ IQueryable<T> Set<T>(table)                  │   │
│  │ (Composable query API)                       │   │
│  └──────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────┐   │
│  │ SaveChangesAsync()                           │   │
│  │ (Atomic transaction scope)                   │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
         ▼                           ▼
    ┌─────────────┐         ┌──────────────────┐
    │ ConnectionPool      │ Protocol         │
    │ (Pooling)      │ │ (HTTP/WebSocket) │
    └─────────────┘         └──────────────────┘
         ▼                           ▼
    ┌─────────────────────────────────────────┐
    │    SurrealDB Server                     │
    └─────────────────────────────────────────┘
```

### Design Layers

| Layer | Components | Responsibility |
|-------|-----------|-----------------|
| **Session** | ISurrealDbSession, ChangeTracker | State management, transaction scope |
| **Query** | IQueryable<T>, QueryBuilder | Query composition and execution |
| **Protocol** | ConnectionManager, HTTP/WebSocket adapters | Connection & communication |
| **Serialization** | TypeMapper, ValueConverters | CLR ↔ SurrealDB type conversion |
| **Resilience** | CircuitBreaker, RetryPolicy, ConnectionRecovery | Error recovery and stability |
| **Diagnostics** | Logging, Metrics, Interceptors | Observability and debugging |

---

## EF Core Comparison

### State Management Comparison

| Aspect | Entity Framework Core | SurrealDB.Client |
|--------|----------------------|------------------|
| Context | `DbContext` | `ISurrealDbSession` |
| State Tracking | Automatic snapshotting | `ChangeTracker` with snapshot comparison |
| Transactions | `SaveChanges()` boundary | `SaveChangesAsync()` |
| Lazy Operations | Deferred in DbContext | Both deferred and immediate supported |
| Change Detection | Snapshot comparison | Snapshot comparison |
| Efficiency | Partial updates (changed props only) | Property-level change tracking |

### Query API Comparison

| Feature | EF Core | SurrealDB.Client |
|---------|---------|------------------|
| API Style | LINQ (expression trees) | Both `IQueryable<T>` and QueryBuilder DSL |
| Composition | `IQueryable<T>` | `IQueryable<T>` primary |
| Late Binding | Supported (until ToListAsync) | Supported (deferred execution) |
| Testability | Can mock IQueryable | IQueryable mockable |
| Discoverability | IntelliSense on T | IntelliSense on entities |

### Concurrency Handling

| Pattern | EF Core | SurrealDB.Client |
|---------|---------|------------------|
| Optimistic Tokens | `[Timestamp]`, `[ConcurrencyCheck]` | `[ConcurrencyToken]` attribute |
| Conflict Detection | `DbUpdateConcurrencyException` | `ConcurrencyException` |
| RowVersion Support | Automatic increment | Server-managed versions supported |
| Retry Pattern | User-implemented | Retry utilities provided |

### Error Handling

| Exception Type | EF Core | SurrealDB.Client |
|---|---|---|
| `UniqueConstraintException` | ✅ EntityFramework.Exceptions | ✅ Implemented |
| `ConcurrencyException` | ✅ Handled | ✅ Implemented |
| `ReferenceConstraintException` | ✅ EntityFramework.Exceptions | ✅ Implemented |
| `CannotInsertNullException` | ✅ EntityFramework.Exceptions | ✅ Implemented |
| Generic `SurrealDbException` | ⚠️ Exists but not preferred | ✅ Base class |

### Unique to SurrealDB ✨

| Feature | EF Core | SurrealDB | Advantage |
|---------|---------|----------|-----------|
| Real-Time Subscriptions | ❌ None | ✅ First-class | **Competitive advantage** |
| Live Queries | ❌ None | ✅ First-class | **Competitive advantage** |
| Protocol Flexibility | ❌ Fixed per provider | ✅ HTTP/WebSocket | **Better flexibility** |
| Multi-Serializer | ⚠️ Single format | ✅ Multiple | **Pragmatism** |

---

## Critical Architectural Decisions

### 1. Session/Context Pattern

**Decision**: Introduce `ISurrealDbSession` (analogous to DbContext)

**Rationale**:
- **State Management**: Enable automatic change tracking within session scope
- **Atomic Operations**: All changes within session → single SaveChangesAsync()
- **Unit of Work**: Clear semantic boundary for transaction scope
- **EF Core Alignment**: Proven pattern from most successful .NET ORM

**Design**:
```csharp
public interface ISurrealDbSession : IAsyncDisposable
{
    // State tracking
    ChangeTracker ChangeTracker { get; }

    // Query composition
    IQueryable<T> Set<T>(string table);
    Task<T> FindAsync<T>(string id, CancellationToken ct = default);

    // Change registration (optional explicit API)
    void Add<T>(T entity) where T : class;
    void Update<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;

    // Transaction scope
    Task SaveChangesAsync(CancellationToken ct = default);
    ITransaction BeginTransaction(IsolationLevel level = IsolationLevel.ReadCommitted);

    // Diagnostics
    DatabaseFacade Database { get; }
}
```

**Usage Pattern**:
```csharp
// Short-lived session scope (similar to DbContext)
using var session = client.CreateSession();

// Track changes within scope
var user = await session.FindAsync<User>("users:1");
user.Email = "new@example.com";
user.UpdatedAt = DateTime.UtcNow;

var newUser = new User { Name = "Jane", Email = "jane@example.com" };
session.Add(newUser);

// Single atomic save
await session.SaveChangesAsync();
// Equivalent to single transaction with multiple operations
```

**Benefits**:
- ✅ Automatic change detection → efficient updates
- ✅ Implicit transaction scope → cleaner code
- ✅ State consistency guarantees
- ✅ Familiar to EF Core developers

### 2. Change Tracker

**Decision**: Implement automatic snapshot-based change detection

**Rationale**:
- **Performance**: Only changed properties sent to server
- **Efficiency**: Avoid full-object serialization on every update
- **Correctness**: Track original vs. modified state
- **Feature Parity**: EF Core's proven pattern

**Design**:
```csharp
public class ChangeTracker
{
    // Get tracking entry for entity
    public EntityEntry<T> Entry<T>(T entity) where T : class;

    // Current tracking state
    public EntityState GetState(object entity);
    public IEnumerable<EntityEntry> TrackedEntities { get; }

    // Diagnostics
    public IEnumerable<EntityChange> GetChanges();
}

public class EntityEntry<T> where T : class
{
    public T Entity { get; }
    public EntityState State { get; }
    public PropertyEntry[] Properties { get; }

    // Original values captured at tracking time
    public object GetOriginalValue(string propertyName);
    public IEnumerable<string> GetModifiedProperties();

    // Reload from database
    public Task ReloadAsync(CancellationToken ct = default);
}

public enum EntityState
{
    Detached = 0,
    Unchanged = 1,
    Deleted = 2,
    Modified = 3,
    Added = 4
}
```

**Implementation Strategy**:
1. On `Set<T>()` query result: Create snapshot of loaded entity
2. Before `SaveChangesAsync()`: Compare current vs. snapshot
3. Generate differential UPDATE (only changed properties)
4. Send to SurrealDB server

**Example - Efficient Update**:
```csharp
using var session = client.CreateSession();

var user = await session.Set<User>()
    .FirstAsync();  // Snapshot: { Id: "user:1", Name: "John", Email: "old@test.com" }

user.Email = "new@test.com";  // Only this property changed

// Instead of:
//   UPDATE users:1 SET { Name: "John", Email: "new@test.com", ... }
//
// Sends:
//   UPDATE users:1 SET Email = "new@test.com"

await session.SaveChangesAsync();
```

### 3. Query Composition with IQueryable

**Decision**: Implement `IQueryable<T>` to enable query composition

**Rationale**:
- **Composability**: Pass queries between methods without execution
- **Late Binding**: Build complex queries step-by-step
- **Testability**: Mock queries in unit tests
- **Reusability**: Domain-specific query extensions

**Design**:
```csharp
// Primary API returns IQueryable<T>, not QueryBuilder
public interface ISurrealDbSession
{
    IQueryable<T> Set<T>(string table);
    IQueryable<T> FromQuery(string surrealQL);
}

// Domain-specific query extensions
public static class UserQueryExtensions
{
    public static IQueryable<User> Active(this IQueryable<User> query)
        => query.Where(u => u.Status == "active");

    public static IQueryable<User> ByEmail(this IQueryable<User> query, string email)
        => query.Where(u => u.Email == email);

    public static IQueryable<User> WithOrders(this IQueryable<User> query)
        => query.Include(u => u.Orders);  // Eager load
}

// Composable usage
public async Task<User> GetActiveUserWithOrdersAsync(string email)
{
    using var session = client.CreateSession();

    var query = session.Set<User>()
        .Active()                    // IQueryable<User>
        .ByEmail(email)             // Still IQueryable<User>
        .WithOrders();              // Still not executed

    return await query.FirstOrDefaultAsync();  // Single round-trip
}
```

**Implementation**:
- Implement `IQueryProvider` to translate IQueryable to SurrealQL
- Cache compiled query plans (like EF Core does)
- Deferred execution until `ToListAsync()`, `FirstAsync()`, etc.

### 4. Optimistic Concurrency Tokens

**Decision**: Support optimistic locking via version tokens

**Rationale**:
- **Conflict Detection**: Detect concurrent modifications
- **Data Integrity**: Prevent silent overwrites in multi-user scenarios
- **Performance**: No pessimistic locks (scale better)
- **EF Core Alignment**: Proven pattern

**Design**:
```csharp
// Attribute-based configuration
public class User
{
    public string Id { get; set; }

    [ConcurrencyToken]  // Or [Timestamp] for auto-managed versions
    public long Version { get; set; }

    public string Email { get; set; }
}

// Configuration via fluent API
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>()
        .Property(u => u.Version)
        .IsConcurrencyToken()
        .IsRowVersion();  // Server auto-increments
}

// Usage
try
{
    user.Email = "new@example.com";
    await session.SaveChangesAsync();  // Includes Version in WHERE clause
}
catch (ConcurrencyException ex)
{
    // Handle conflict - reload and retry
    await session.Entry(user).ReloadAsync();
    user.Email = "new@example.com";
    await session.SaveChangesAsync();
}
```

**Server Integration**:
- SurrealDB `<thing>` syntax or custom version column
- Auto-increment on each update
- Conflict detection: Version mismatch → UPDATE affects 0 rows → Exception

### 5. Typed Exception Hierarchy

**Decision**: Implement database-agnostic typed exceptions

**Rationale**:
- **Developer Experience**: Catch specific exceptions, not generic ones
- **Error Handling**: Different recovery strategies per error type
- **Maintainability**: Less brittle than string parsing
- **EF Core Pattern**: Proven via EntityFramework.Exceptions

**Design**:
```csharp
// Base exception
public abstract class SurrealDbException : Exception
{
    public string ErrorCode { get; }
    public object Details { get; }
}

// Constraint violations
public class UniqueConstraintException : SurrealDbException { }
public class ReferenceConstraintException : SurrealDbException { }
public class CannotInsertNullException : SurrealDbException { }
public class CheckConstraintException : SurrealDbException { }

// Concurrency conflicts
public class ConcurrencyException : SurrealDbException
{
    public IEnumerable<string> AffectedProperties { get; }
}

// Connection/protocol errors
public class ConnectionException : SurrealDbException { }
public class TimeoutException : SurrealDbException { }

// Authentication/authorization
public class AuthenticationException : SurrealDbException { }
public class AuthorizationException : SurrealDbException { }

// Query/transaction errors
public class QueryException : SurrealDbException
{
    public string Query { get; }
    public Dictionary<string, object> Parameters { get; }
}

public class TransactionException : SurrealDbException
{
    public TransactionStatus Status { get; }
}

// Usage
try
{
    await session.SaveChangesAsync();
}
catch (UniqueConstraintException ex)
{
    // Handle duplicate - show user error message
    logger.LogWarning($"Duplicate email: {ex.Details}");
}
catch (ConcurrencyException ex)
{
    // Reload and retry
    await session.Entry(user).ReloadAsync();
}
catch (SurrealDbException ex)
{
    logger.LogError($"Database error: {ex.Message}");
}
```

**Implementation**:
- Intercept SurrealDB error responses
- Map error codes/messages to specific exception types
- Preserve original SurrealDB error in InnerException

### 6. Interceptors/Middleware

**Decision**: Implement decorator pattern for operation interception

**Design** (adapted from EF Core):
```csharp
// Query execution interception
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

// SaveChanges interception
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
}

// Usage
public class AuditingInterceptor : ISaveChangesInterceptor
{
    private readonly ILogger<AuditingInterceptor> _logger;

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var session = eventData.Context as ISurrealDbSession;
        var changes = session.ChangeTracker.GetChanges();

        _logger.LogInformation("Saving {count} changes", changes.Count());

        return new(result);
    }
}

// Configuration
services.AddSurrealDbClient(options =>
{
    options.AddInterceptor<AuditingInterceptor>();
});
```

---

## Implementation Roadmap

### Phase 1: Foundation

1. ✅ Project structure & build system
2. ✅ Connection pooling & lifecycle
3. ✅ Authentication (token/credentials)
4. ✅ ISurrealDbSession + ChangeTracker
5. ✅ IQueryable<T> composition
6. ✅ Optimistic concurrency tokens
7. ✅ Typed exception hierarchy
8. ✅ CRUD operations
9. ✅ Basic query builder
10. ✅ Unit & integration tests
11. ✅ Initial documentation

### Phase 2: Features

1. ✅ Advanced query operations
2. ✅ Serialization & type mapping
3. ✅ Real-time subscriptions
4. ✅ Session management
5. ✅ Credential storage
6. ✅ Query logging
7. ✅ Performance metrics
8. ✅ Include/Loading patterns
9. ✅ Eager loading API

### Phase 3: Polish

1. ✅ Response streaming
2. ✅ Caching layer
3. ✅ Advanced error recovery
4. ✅ Middleware system
5. ✅ User guides & examples
6. ✅ Docker support
7. ✅ Migration system

### Phase 4+: Enterprise (Future)

1. Plugin system
2. Custom query extensions
3. Distributed caching
4. GraphQL support (if SurrealDB adds)

---

## Comparison Matrix: SurrealDB.Client vs EF Core

| Pattern | EF Core | SurrealDB.Client |
|---------|---------|-----------------|
| **Context/Session** | `DbContext` | `ISurrealDbSession` |
| **Change Tracking** | Automatic snapshots | `ChangeTracker` with snapshots |
| **Query API** | LINQ (`IQueryable<T>`) | `IQueryable<T>` primary, QueryBuilder secondary |
| **State Management** | 5-state model | 5-state model (Detached/Added/Unchanged/Modified/Deleted) |
| **Concurrency** | Optimistic tokens | `[ConcurrencyToken]` attribute |
| **Exceptions** | Typed hierarchy | Typed hierarchy |
| **Interceptors** | Decorator pattern | Decorator pattern |
| **Loading** | Include/Lazy/Explicit | Include/Lazy/Explicit |
| **Transactions** | Auto + explicit | Both supported |
| **Real-Time** | None | ✅ First-class subscriptions |
| **Serialization** | Single format | Multiple (System.Text.Json, Newtonsoft, custom) |

---

## Performance Targets
- Connection pool setup: <1s
- Authentication: <500ms
- Simple query: <50ms
- Typical update (with change tracking): <100ms
- Batch operation (100 records): <200ms
- Update with only 1 property changed vs. 10 properties: 5x less bandwidth

### User Experience
- Feature parity with EF Core on core patterns
- Clear migration path for EF Core developers
- Intuitive APIs without surprising behaviors
- Comprehensive error messages
- Rich IntelliSense support

---

## References

- [Entity Framework Core Architecture](https://learn.microsoft.com/en-us/ef/core/)
- [EF Core DbContext](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [EF Core Change Tracking](https://learn.microsoft.com/en-us/ef/core/change-tracking/)
- [EF Core Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [SurrealDB Documentation](https://surrealdb.com/docs)
- [SurrealQL Query Language](https://surrealdb.com/docs/surrealql)

