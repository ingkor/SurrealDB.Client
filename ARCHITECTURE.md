# SurrealDB.Client Architecture & Design

> Comprehensive architectural documentation including feature plan, EF Core comparison, and implementation roadmap.

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Architectural Overview](#architectural-overview)
3. [EF Core Comparison](#ef-core-comparison)
4. [Critical Architectural Decisions](#critical-architectural-decisions)
5. [Implementation Roadmap](#implementation-roadmap)
6. [Risk Assessment](#risk-assessment)

---

## Executive Summary

**SurrealDB.Client** is a production-grade .NET/C# client library for SurrealDB with modern database client patterns. This architecture combines lessons from Entity Framework Core with SurrealDB-specific optimizations.

### Architecture Grade: **B+ (Strong Foundation with Critical Gaps)**

**Strengths:**
- ✅ Protocol abstraction (HTTP/WebSocket)
- ✅ Real-time subscriptions (unique to SurrealDB)
- ✅ Explicit connection pooling
- ✅ Multi-serializer support
- ✅ Query builder DSL

**Critical Gaps:**
- ❌ Missing Unit of Work pattern
- ❌ No change tracking/snapshotting
- ❌ QueryBuilder not composable
- ❌ Missing optimistic concurrency tokens
- ❌ Incomplete error type hierarchy

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

### Direct Alignment: State Management Pattern

| Aspect | Entity Framework Core | SurrealDB.Client Plan | SurrealDB Recommendation |
|--------|----------------------|----------------------|--------------------------|
| Context | `DbContext` | Missing | **Introduce `ISurrealDbSession`** |
| State Tracking | Automatic snapshotting | Implicit in CRUD | **Implement ChangeTracker** |
| Transactions | `SaveChanges()` boundary | `ITransaction` separate | **Align SaveChangesAsync()** |
| Lazy Operations | Deferred in DbContext | Immediate per-operation | **Support both patterns** |
| Change Detection | Snapshot comparison | None | **Add snapshot comparison** |
| Efficiency | Partial updates (changed props only) | Full object serialization | **Implement property-level tracking** |

### Query API Comparison

| Feature | EF Core | SurrealDB Plan | Recommendation |
|---------|---------|---|---|
| API Style | LINQ (expression trees) | QueryBuilder DSL | **Support both via IQueryable** |
| Composition | `IQueryable<T>` | QueryBuilder (terminal) | **Make IQueryable primary** |
| Late Binding | Supported (until ToListAsync) | Immediate build | **Support deferred execution** |
| Testability | Can mock IQueryable | Must execute | **Testability improves with IQueryable** |
| Discoverability | IntelliSense on T | Limited | **IntelliSense on entities** |

### Concurrency Handling

| Pattern | EF Core | SurrealDB Plan | Gap |
|---------|---------|---|---|
| Optimistic Tokens | `[Timestamp]`, `[ConcurrencyCheck]` | Not specified | **Add concurrency token attributes** |
| Conflict Detection | `DbUpdateConcurrencyException` | Missing | **Implement typed exception** |
| RowVersion Support | Automatic increment | Not defined | **Support server-managed versions** |
| Retry Pattern | User-implemented | Not specified | **Provide retry utilities** |

### Error Handling

| Exception Type | EF Core | SurrealDB Plan | Status |
|---|---|---|---|
| `UniqueConstraintException` | ✅ EntityFramework.Exceptions | ❌ Missing | Need to add |
| `ConcurrencyException` | ✅ Handled | ❌ Missing | Need to add |
| `ReferenceConstraintException` | ✅ EntityFramework.Exceptions | ❌ Missing | Need to add |
| `CannotInsertNullException` | ✅ EntityFramework.Exceptions | ❌ Missing | Need to add |
| Generic `SurrealDbException` | ⚠️ Exists but not preferred | ✅ Base class | Keep as base |

### Unique to SurrealDB ✨

| Feature | EF Core | SurrealDB | Advantage |
|---------|---------|----------|-----------|
| Real-Time Subscriptions | ❌ None | ✅ First-class | **Competitive advantage** |
| Live Queries | ❌ None | ✅ First-class | **Competitive advantage** |
| Protocol Flexibility | ❌ Fixed per provider | ✅ HTTP/WebSocket | **Better flexibility** |
| Multi-Serializer | ⚠️ Single format | ✅ Multiple | **Pragmatism** |

---

## Critical Architectural Decisions

### 1. Session/Context Pattern (CRITICAL - Phase 1)

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

### 2. Change Tracker (CRITICAL - Phase 1)

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

### 3. Query Composition with IQueryable (CRITICAL - Phase 1)

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

### 4. Optimistic Concurrency Tokens (HIGH - Phase 1)

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

### 5. Typed Exception Hierarchy (HIGH - Phase 1)

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

### 6. Interceptors/Middleware (MEDIUM - Phase 1-2)

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

### Phase 1: Foundation (Weeks 1-4) - CRITICAL

**Must Complete Before Phase 2:**

1. ✅ Project structure & build system
2. ✅ Connection pooling & lifecycle
3. ✅ Authentication (token/credentials)
4. **🔴 ISurrealDbSession + ChangeTracker (ADDED)**
5. **🔴 IQueryable<T> composition (ADDED)**
6. **🔴 Optimistic concurrency tokens (ADDED)**
7. **🔴 Typed exception hierarchy (ADDED)**
8. ✅ CRUD operations
9. ✅ Basic query builder
10. ✅ Unit & integration tests
11. ✅ Initial documentation

**Effort**: 500-600 hours (↑ 100 hours for critical additions)

### Phase 2: Features (Weeks 5-8)

1. ✅ Advanced query operations
2. ✅ Serialization & type mapping
3. ✅ Real-time subscriptions (KEEP - competitive advantage)
4. ✅ Session management
5. ✅ Credential storage
6. ✅ Query logging
7. ✅ Performance metrics
8. **🔴 Include/Loading patterns (ADDED)**
9. **🔴 Eager loading API (ADDED)**

**Effort**: 350-450 hours

### Phase 3: Polish (Weeks 9-12)

1. ✅ Response streaming
2. ✅ Caching layer
3. ✅ Advanced error recovery
4. ✅ Middleware system
5. ✅ User guides & examples
6. ✅ Docker support
7. **🔴 Migration system (ADDED)**

**Effort**: 300-400 hours

### Phase 4+: Enterprise (Future)

1. Plugin system
2. Custom query extensions
3. Distributed caching
4. GraphQL support (if SurrealDB adds)

---

## Risk Assessment

### Critical Risks 🔴

#### 1. No Unit of Work Pattern (CRITICAL)
**Impact**: Users write inefficient code by default
- Every operation is a separate transaction
- No state consistency guarantees
- Change tracking impossible

**Severity**: 9/10 (Blocks Phase 1 completion)

**Mitigation**:
- ✅ Implement ISurrealDbSession immediately
- ✅ Make it primary API (not optional)
- ✅ Provide clear examples
- ✅ Documentation emphasizing session lifetime

#### 2. Non-Composable Queries (CRITICAL)
**Impact**: Reduces code reusability, testability
- Can't pass queries between methods
- N+1 queries hard to prevent
- Unit testing difficult

**Severity**: 8/10 (API ergonomics critical)

**Mitigation**:
- ✅ Implement IQueryable<T> in Phase 1
- ✅ Make it primary query API
- ✅ Provide LINQ provider implementation
- ✅ Cache compiled query plans

#### 3. Missing Change Tracking (CRITICAL)
**Impact**: Performance degradation, bandwidth waste
- Every update sends full object
- 10x+ more data than necessary
- Subscription network overhead

**Severity**: 8/10 (Performance blocker at scale)

**Mitigation**:
- ✅ Snapshot-based change detection in Phase 1
- ✅ Property-level granularity
- ✅ Benchmarks showing differential updates
- ✅ Document serialization overhead

### High Risks 🟡

#### 4. No Concurrency Model
**Impact**: Data corruption in multi-user scenarios
- Silent overwrites in concurrent updates
- No conflict detection
- Users forced to implement own locking

**Severity**: 7/10 (Data integrity risk)

**Mitigation**:
- ✅ Add optimistic tokens in Phase 1
- ✅ Provide conflict resolution examples
- ✅ Clear documentation on isolation levels
- ✅ Typed ConcurrencyException

#### 5. Incomplete Error Handling
**Impact**: Users catch broad exceptions, miss specifics
- Can't distinguish constraint violations from timeouts
- Recovery strategies unclear
- Error handling code brittle

**Severity**: 6/10 (DX and reliability)

**Mitigation**:
- ✅ Implement typed exception hierarchy in Phase 1
- ✅ Mirror EntityFramework.Exceptions pattern
- ✅ Provide error handling guide
- ✅ Document recovery strategies per error type

### Medium Risks 🟠

#### 6. Protocol Abstraction Complexity
**Impact**: Higher maintenance burden with HTTP/WebSocket duality
- Different semantics for each protocol
- Edge cases per protocol
- Testing complexity

**Severity**: 5/10 (Implementation concern, not user-facing)

**Mitigation**:
- ✅ Comprehensive protocol adapter tests
- ✅ Clear separation of concerns
- ✅ Fallback strategies documented
- ✅ Protocol selection guidance

#### 7. Real-Time Subscription Stability
**Impact**: Dropped subscriptions under load/network issues
- Backpressure not handled
- Reconnection logic complex
- Lost events possible

**Severity**: 5/10 (When subscriptions used)

**Mitigation**:
- ✅ Implement backpressure handling
- ✅ Auto-reconnection with exponential backoff
- ✅ Event buffering strategy
- ✅ Subscription health monitoring
- ✅ Document guarantees and limitations

---

## Comparison Matrix: SurrealDB vs EF Core

| Pattern | EF Core | SurrealDB Plan | Recommendation |
|---------|---------|---|---|
| **Context/Session** | DbContext | Missing | **Adopt ISurrealDbSession pattern** |
| **Change Tracking** | Automatic snapshots | None | **Implement ChangeTracker** |
| **Query API** | LINQ (IQueryable) | QueryBuilder | **Primary: IQueryable, Secondary: Builder** |
| **State Management** | 5-state model | Implicit | **Match 5-state model** |
| **Concurrency** | Optimistic tokens | Not defined | **Add [ConcurrencyToken] support** |
| **Exceptions** | Typed hierarchy | Generic | **Implement typed exceptions** |
| **Interceptors** | Decorator pattern | Middleware | **Align terminology and semantics** |
| **Loading** | Include/Lazy/Explicit | None | **Add Include() API** |
| **Transactions** | Auto + explicit | Explicit | **Support both patterns** |
| **Real-Time** | None | ✅ Subscriptions | **Maintain differentiation** |
| **Serialization** | Single format | Multiple | **Keep pragmatism** |

---

## Success Criteria

### Phase 1 Completion
- ✅ ISurrealDbSession API stable and documented
- ✅ ChangeTracker 90%+ accuracy
- ✅ IQueryable<T> composition working
- ✅ Concurrency tokens implemented
- ✅ Typed exception hierarchy complete
- ✅ Unit test coverage >85%
- ✅ Integration tests passing
- ✅ API documentation 100%

### Performance Targets
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

