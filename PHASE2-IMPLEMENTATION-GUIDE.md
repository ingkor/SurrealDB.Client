# Phase 2B+ Implementation Guide

**Status:** Phase 2A COMPLETE, Phase 2B IN PROGRESS
**Completed:** ISurrealDbSession, ChangeTracker, Entity State Management
**Remaining:** ~190+ hours across Phases 2B-4

---

## Phase 2B: Query Composition (50+ hours)

### 2.4 QueryProvider - STARTED ✅

**Status:** Skeleton implemented
**Files:**
- `src/SurrealDB.Client/Query/SurrealDbQueryProvider.cs` - IQueryProvider impl
- `src/SurrealDB.Client/Query/SurrealDbQuery.cs` - IQueryable<T> impl
- `src/SurrealDB.Client/Query/IQueryCompiler.cs` - Compiler interface

**What's Done:**
- IQueryProvider interface implementation
- SurrealDbQuery<T> implementing IQueryable<T>
- IQueryCompiler interface definition
- Basic method signatures

**What's Needed:**
1. **QueryCompiler implementation** (20+ hours)
   - Expression tree visitor pattern
   - Method call translation (Where, OrderBy, Select, Take, Skip)
   - Comparison operator translation
   - Logical operator handling (&&, ||, !)
   - Property accessor resolution
   - Projection handling

2. **Connection to Session** (5 hours)
   - Wire Set<T>(table) to return SurrealDbQuery<T>
   - Implement actual query execution
   - Result materialization

3. **Tests** (10 hours)
   - Expression compilation tests
   - Query execution tests
   - Complex composition tests

### 2.5 ExpressionCompiler - TO DO

**Effort:** 20+ hours

**Key Methods:**
```csharp
public class SurrealQueryCompiler : IQueryCompiler
{
    public string Compile(Expression expression)
    {
        // Translate Expression Tree to SurrealQL
        // e.g., session.Set<User>()
        //       .Where(u => u.Age >= 18)
        //       .OrderBy(u => u.Name)
        //
        // Becomes:
        // SELECT * FROM users
        // WHERE age >= 18
        // ORDER BY name
    }

    public CompiledQuery CompileDetailed(Expression expression, string? tableName)
    {
        // Return compiled info with metadata
    }
}
```

**Supported Operators (Priority):**
1. **Tier 1 (Essential):**
   - Where() with comparison operators (==, !=, >, <, >=, <=)
   - OrderBy()/OrderByDescending()
   - Select() projections
   - Take()/Skip() pagination

2. **Tier 2 (Important):**
   - Any()/All() predicates
   - Contains() for IN clauses
   - String operations (StartsWith, EndsWith, Contains)
   - Logical operators (&&, ||, !)

3. **Tier 3 (Advanced):**
   - GroupBy() with aggregations
   - Joins via Select (Include pattern)
   - Nested conditions
   - Method chaining

### 2.6 Loading Patterns - TO DO

**Effort:** 12+ hours

**Patterns:**
```csharp
// Include (eager load)
var users = session.Set<User>()
    .Include(u => u.Posts)
    .ThenInclude(p => p.Comments)
    .ToListAsync();

// Lazy (deferred)
var user = await session.FindAsync<User>("user:1");
var posts = user.Posts; // Load on access

// Explicit (separate query)
await session.LoadAsync(user, u => u.Posts);
```

---

## Phase 2C: Advanced Features (40+ hours)

### 2.7 Interceptors - TO DO

**Effort:** 10+ hours

**Interface:**
```csharp
public interface ISurrealDbInterceptor
{
    void OnQueryExecuting(QueryExecutingEventArgs args);
    void OnQueryExecuted(QueryExecutedEventArgs args);
    void OnConnectionOpening(ConnectionOpeningEventArgs args);
    void OnConnectionOpened(ConnectionOpenedEventArgs args);
}
```

**Built-in Interceptors:**
- LoggingInterceptor
- PerformanceInterceptor
- CacheInvalidationInterceptor

### 2.8 Caching - TO DO

**Effort:** 14+ hours

**Three-Level Cache:**
1. **Result Cache** - By primary key
2. **Query Cache** - Expression → SQL mapping
3. **Compiled Plan Cache** - SQL → execution plan

**Invalidation:**
- On SaveChangesAsync()
- TTL-based expiration
- Manual cache clearing

### 2.9 Diagnostics - TO DO

**Effort:** 10+ hours

**Features:**
- Query execution timing
- Network latency metrics
- Cache hit/miss rates
- Memory allocation tracking
- ILogger integration
- Prometheus metrics export

---

## Phase 3: Production Features (55+ hours)

### 3.1 Optimistic Concurrency - TO DO

**Effort:** 8+ hours

**Attributes:**
```csharp
public class User
{
    public string Id { get; set; }

    [ConcurrencyToken]
    public int Version { get; set; }

    public string Name { get; set; }
}
```

**Detection:**
- Server-managed timestamps
- Custom version tokens
- Conflict handling with DbUpdateConcurrencyException

### 3.2 Migrations - TO DO

**Effort:** 20+ hours

**Pattern:**
```csharp
public class CreateUsersTable : Migration
{
    protected override void Up()
    {
        CreateTable("users")
            .AddColumn<string>("id", opt => opt.IsPrimaryKey())
            .AddColumn<string>("name")
            .AddColumn<string>("email");
    }

    protected override void Down()
    {
        DropTable("users");
    }
}
```

**Features:**
- Version tracking
- Rollback support
- Field operations (add, drop, rename)
- Index creation
- Constraint management

### 3.3 Security - TO DO

**Effort:** 16+ hours

**Features:**
- Row-level security (RLS) filters
- Field-level encryption
- Audit trails (CreatedBy, UpdatedBy, CreatedAt)
- User context injection
- Role-based access

### 3.4 DataLoader - TO DO

**Effort:** 12+ hours

**Pattern:**
```csharp
var loader = new DataLoader<string, User>(async ids =>
{
    return await session.Set<User>()
        .Where(u => ids.Contains(u.Id))
        .ToListAsync();
});

var user = await loader.LoadAsync("user:1");
```

**Features:**
- Batch loading
- Deduplication
- Identity map integration
- N+1 prevention

---

## Phase 4: Enterprise Features (45+ hours)

### 4.1 Plugins - TO DO

**Effort:** 10+ hours

**Interface:**
```csharp
public interface ISurrealDbPlugin
{
    void OnInitialize(SurrealDbClient client);
    void OnSessionCreated(ISurrealDbSession session);
}
```

### 4.2 Event Sourcing - TO DO

**Effort:** 24+ hours

**Pattern:**
```csharp
public class UserCreatedEvent : IDomainEvent
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Store events, project state, replay history
```

**Features:**
- Event table storage
- Event publishing on SaveChangesAsync()
- Event replay/projection
- Snapshot creation
- CQRS pattern support

---

## Implementation Roadmap

### Week 1-2 (Phase 2B - QueryProvider)
- [ ] SurrealQL Compiler - Expression tree translation
- [ ] Connection to Session
- [ ] Basic Where/OrderBy/Select/Take/Skip
- [ ] Tests and validation

### Week 3 (Phase 2C - Caching & Interceptors)
- [ ] Three-level caching system
- [ ] Interceptor pipeline
- [ ] Built-in interceptors

### Week 4 (Phase 3A - Concurrency & Migrations)
- [ ] Optimistic concurrency tokens
- [ ] Migration framework
- [ ] Version tracking

### Week 5 (Phase 3B - Security & DataLoader)
- [ ] RLS filters
- [ ] Field encryption
- [ ] DataLoader implementation

### Week 6-7 (Phase 4 - Plugins & Event Sourcing)
- [ ] Plugin system
- [ ] Event store
- [ ] Snapshot creation

---

## Critical Path for MVP

**Minimum to ship:**
1. ✅ Phase 2A: Session & State Management
2. ⏳ Phase 2B: QueryProvider & Expression Compilation
3. Phase 2C: Caching (recommended)
4. Phase 3.1: Optimistic Concurrency (optional)

**Optional for initial release:**
- Phase 3.2: Migrations (can use raw SQL initially)
- Phase 3.3: Security (implement gradually)
- Phase 3.4: DataLoader (nice-to-have)
- Phase 4: Plugins & Event Sourcing (future)

---

## How to Continue

### Next Step: Implement SurrealQL Compiler

**File to create:** `src/SurrealDB.Client/Query/SurrealQueryCompiler.cs`

**Template:**
```csharp
public class SurrealQueryCompiler : IQueryCompiler
{
    public string Compile(Expression expression)
    {
        var visitor = new SurrealQLExpressionVisitor();
        visitor.Visit(expression);
        return visitor.GetSQL();
    }

    public CompiledQuery CompileDetailed(Expression expression, string? tableName = null)
    {
        var sql = Compile(expression);
        return new CompiledQuery
        {
            SurrealQL = sql,
            TableName = tableName,
            // Extract parameters, scalar flag, etc.
        };
    }
}

internal class SurrealQLExpressionVisitor : ExpressionVisitor
{
    private StringBuilder _sql = new();
    private Dictionary<string, object?> _parameters = new();

    // Override VisitMethodCall to handle Where, OrderBy, Select, etc.
    // Override VisitBinary for comparisons (==, >, <, etc.)
    // Override VisitMember for property access
}
```

### References
- Expression Trees: https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/expression-trees/
- IQueryProvider: https://docs.microsoft.com/en-us/dotnet/api/system.linq.iqueryprovider
- LINQ to SQL implementation for reference

---

## Testing Strategy

### Unit Tests
- Expression compilation correctness
- SurrealQL generation
- Parameter extraction

### Integration Tests
- Full query composition
- Actual database queries
- State tracking with queries

---

**Total Work Remaining: ~190 hours**
**Recommended: Focus on Phase 2B for MVP completion**
