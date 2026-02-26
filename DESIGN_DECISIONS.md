# Design Decisions

> Rationale behind key architectural choices in SurrealDB.Client, including alternatives considered and trade-offs accepted.

## Table of Contents

1. [Session-Based State Management](#session-based-state-management)
2. [IQueryable as Primary Query API](#iqueryable-as-primary-query-api)
3. [Protocol Abstraction](#protocol-abstraction)
4. [Automatic Change Tracking](#automatic-change-tracking)
5. [Optimistic Concurrency Tokens](#optimistic-concurrency-tokens)
6. [Multi-Serializer Support](#multi-serializer-support)
7. [Real-Time Subscriptions](#real-time-subscriptions)
8. [Exception Typing](#exception-typing)

---

## Session-Based State Management

### Decision
✅ Implement `ISurrealDbSession` following the Unit of Work pattern, similar to Entity Framework Core's DbContext.

### Rationale

**Problem Statement:**
Users need a way to:
- Manage a coherent set of changes within a bounded scope
- Automatically track which entities have been modified
- Commit all changes atomically
- Achieve efficient updates (only changed properties sent to server)

**Why Unit of Work (Session)?**

1. **Atomic Semantics**: All changes within a session are committed together
   ```csharp
   using var session = client.CreateSession();
   user.Email = "new@test.com";
   order.Amount = 150;
   await session.SaveChangesAsync();  // Both changes or neither
   ```

2. **Automatic Change Detection**: No manual state tracking required
   ```csharp
   var user = await session.FindAsync<User>("user:1");
   user.Email = "changed";  // Automatically marked as Modified
   // vs. requiring: session.Update(user) for every change
   ```

3. **Memory Efficiency**: Snapshots enable property-level updates
   ```csharp
   // With change tracking:
   await session.SaveChangesAsync();
   // → UPDATE users:1 SET email = 'new@test.com'

   // Without change tracking:
   await client.UpdateAsync("user:1", user);
   // → Sends entire 20KB object
   ```

4. **Familiarity**: EF Core developers recognize the pattern immediately

### Alternatives Considered

#### ❌ Alternative 1: Per-Operation Transactions
```csharp
// Each operation is independent transaction
var user = await client.GetAsync<User>("user:1");
user.Email = "new@test.com";
await client.UpdateAsync("user:1", user);  // No state tracking

// Problems:
// - No atomic multi-operation semantics
// - Must serialize entire object
// - More network calls for related operations
// - No change detection
```

**Verdict**: Rejected - Too inefficient and lack of atomicity

#### ❌ Alternative 2: Implicit DbContext-Like Singleton
```csharp
// Implicit lifetime management
client.Add(user);
client.Update(order);
await client.SaveChangesAsync();
// No explicit session/context

// Problems:
// - Hidden state (hard to debug)
// - Thread-safety issues
// - Ambiguous ownership
// - Difficult to test
// - Memory leaks from long-lived context
```

**Verdict**: Rejected - Hidden complexity outweighs convenience

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **More lines of code** | `using var session = ...` required | Clear documentation, templates |
| **Session lifetime management** | User must manage scope | Provide DI extensions |
| **Memory overhead from snapshots** | 1.5x per tracked entity | Guidance on session design |

### Implementation Notes

- Sessions should be short-lived (per-request, not per-application)
- DI extensions will manage session creation
- Clear documentation on session boundaries
- Memory profiling to validate snapshot overhead is acceptable

---

## IQueryable as Primary Query API

### Decision
✅ Implement `IQueryable<T>` as the primary query API, with QueryBuilder available as secondary API for complex SurrealQL.

### Rationale

**Problem Statement:**
Developers need to build queries that are:
- Composable across methods without execution
- Type-safe with IntelliSense
- Testable with mockable IQueryable
- Lazily evaluated (only execute when needed)

**Why IQueryable<T>?**

1. **Composability**: Pass queries to methods
   ```csharp
   // Without IQueryable: Terminal API
   var results = client
       .Query()
       .Select<User>()
       .From("users")
       .ExecuteAsync();  // Must execute immediately

   // With IQueryable: Composable
   IQueryable<User> query = session.Set<User>();
   query = ApplyFilters(query);  // Pass to method
   var results = await query.ToListAsync();  // Execute when ready
   ```

2. **Deferred Execution**: Optimize at last moment
   ```csharp
   var query = session.Set<User>()
       .Where(u => u.Status == "active")
       .OrderBy(u => u.Name);

   if (needsPagination)
       query = query.Skip(10).Take(20);  // Add BEFORE execution

   var results = await query.ToListAsync();  // Single optimized query
   ```

3. **Type Safety**: IntelliSense on entity properties
   ```csharp
   // With IQueryable:
   var query = session.Set<User>()
       .Where(u => u.Age >= 18);  // IntelliSense on User properties

   // With QueryBuilder:
   var query = client.Query().From("users")
       .Where("age >= 18");  // String literal, no IntelliSense
   ```

4. **Testability**: Easy to mock
   ```csharp
   var mockData = new List<User> { ... }.AsQueryable();
   var mockSession = new Mock<ISurrealDbSession>();
   mockSession.Setup(s => s.Set<User>()).Returns(mockData);

   // Test with mockSession
   ```

5. **Familiarity**: LINQ developers know this pattern
   ```csharp
   // Same as Entity Framework Core, LINQ to SQL, etc.
   var results = await session.Set<User>()
       .Where(u => u.Age >= 18)
       .OrderBy(u => u.Name)
       .ToListAsync();
   ```

### Alternatives Considered

#### ❌ Alternative 1: QueryBuilder Only
```csharp
var results = await client
    .Query()
    .Select<User>()
    .From("users")
    .Where("age >= $age")
    .Parameter("age", 18)
    .ExecuteAsync<User>();

// Problems:
// - Not composable across methods
// - Terminal operations (can't extend query)
// - No type safety (string-based predicates)
// - Harder to test
// - Unfamiliar to LINQ developers
```

**Verdict**: Rejected - Too limiting for real applications

#### ❌ Alternative 2: Expression Trees Without IQueryable
```csharp
var query = client.BuildQuery<User>()
    .Where(u => u.Age >= 18);  // Accepts Expression<Func<User, bool>>
// But doesn't return IQueryable<T>

// Problems:
// - Can't chain multiple operations
// - Can't use LINQ operators (OrderBy, Take, Skip, etc.)
// - Complex API design
// - Loses testability
```

**Verdict**: Rejected - Loses too much of IQueryable benefits

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Implementation Complexity** | Need expression tree compiler | Well-tested, proven pattern |
| **Expression Translation Bugs** | May not support all LINQ | Document supported operations |
| **Performance Overhead** | Expression compilation | Query plan caching, compiled queries |

### Implementation Notes

- Implement `IQueryProvider` for expression translation
- Cache compiled query plans
- Support compiled queries with `EF.CompileQuery<T>()`
- QueryBuilder remains available for raw SurrealQL:
  ```csharp
  var results = await client.QueryAsync<User>(
      "SELECT * FROM users WHERE age >= $age AND status IN $statuses",
      new { age = 18, statuses = new[] { "active", "pending" } }
  );
  ```

---

## Protocol Abstraction

### Decision
✅ Abstract protocol implementation to support both HTTP and WebSocket, with runtime selection.

### Rationale

**Problem Statement:**
Different operations have different requirements:
- **CRUD operations**: HTTP sufficient, simpler
- **Live subscriptions**: WebSocket required (persistent connection)
- **Deployment constraints**: Some environments only support HTTP

**Why Protocol Abstraction?**

1. **Flexibility**: Support multiple deployment scenarios
   ```csharp
   var options = useWebSocket
       ? new ClientOptions { Protocol = ProtocolType.WebSocket }
       : new ClientOptions { Protocol = ProtocolType.Http };
   ```

2. **Better Performance**: Choose optimal protocol per operation
   - HTTP: Better for one-off queries (connection overhead)
   - WebSocket: Better for subscriptions (persistent)

3. **Graceful Degradation**: Fall back to HTTP if WebSocket unavailable
   ```csharp
   var client = new SurrealDbClient(options)
   {
       PreferenceOrder = new[] { ProtocolType.WebSocket, ProtocolType.Http }
   };
   // Uses WebSocket if available, falls back to HTTP
   ```

4. **Testing**: Test each protocol independently
   ```csharp
   [Theory]
   [InlineData(ProtocolType.Http)]
   [InlineData(ProtocolType.WebSocket)]
   public async Task Query_Works(ProtocolType protocol) { ... }
   ```

### Alternatives Considered

#### ❌ Alternative 1: WebSocket Only
```csharp
// Always use WebSocket
var client = new SurrealDbClient("ws://localhost:8000");

// Problems:
// - Higher connection overhead for single queries
// - Connection management complexity
// - Doesn't work in all environments (proxies may block WSS)
// - Overkill for read-only operations
```

**Verdict**: Rejected - Worse performance for typical use cases

#### ❌ Alternative 2: HTTP Only
```csharp
// Always use HTTP
var client = new SurrealDbClient("http://localhost:8000");

// Problems:
// - Can't support live subscriptions
// - No persistent connection (latency)
// - Polling required for real-time features
// - Client can't receive server-initiated messages
```

**Verdict**: Rejected - Loses real-time capabilities

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Code Duplication** | Protocol adapters needed | Clear interface contracts |
| **Testing Complexity** | Test both protocols | Parameterized tests |
| **Connection Management** | Different per protocol | Adapter pattern isolates complexity |

### Implementation Notes

- `IProtocolAdapter` interface abstracts both protocols
- `HttpProtocolAdapter` for HTTP operations
- `WebSocketProtocolAdapter` for persistent connections
- Connection pooling per protocol
- Automatic protocol selection based on operation type

---

## Automatic Change Tracking

### Decision
✅ Implement snapshot-based change detection with automatic tracking of entity modifications.

### Rationale

**Problem Statement:**
Users should be able to:
- Modify entities without explicit "mark as modified" calls
- Know exactly which properties changed
- Send only changed properties to server (bandwidth efficiency)

**Why Snapshots?**

1. **Implicit State Changes**: No manual tracking required
   ```csharp
   user.Email = "new@test.com";  // Automatically detected as Modified
   // vs. requiring: session.Update(user)
   ```

2. **Property-Level Granularity**: Send only changed fields
   ```csharp
   // Snapshot: { Email: "old@test.com", Name: "John", Age: 30 }
   // Modified: user.Email = "new@test.com"
   // Generated: UPDATE users:1 SET Email = 'new@test.com'
   // NOT: UPDATE users:1 SET { Email: 'new@test.com', Name: 'John', Age: 30 }
   ```

3. **Efficiency**: Reduces bandwidth 5-50x for typical operations
   ```
   20-property object, 1 change:
   - Without tracking: 20KB
   - With tracking: 200B
   - Savings: 99%
   ```

4. **Proven Pattern**: EF Core uses this approach successfully

### Alternatives Considered

#### ❌ Alternative 1: No Change Tracking (Per-Operation)
```csharp
var user = await client.GetAsync<User>("user:1");
user.Email = "new@test.com";
await client.UpdateAsync("user:1", user);  // Send entire object

// Problems:
// - 10-50x more bandwidth
// - Server receives unchanged properties (confusing)
// - Can't distinguish intentional vs. accidental resets
// - Worse for batch operations
```

**Verdict**: Rejected - Too inefficient at scale

#### ❌ Alternative 2: Manual State Tracking
```csharp
var user = await client.GetAsync<User>("user:1");
user.Email = "new@test.com";
session.Update(user);  // Explicitly mark as modified

await session.SaveChangesAsync();

// Problems:
// - Boilerplate code (error-prone)
// - Still need snapshots to know what changed
// - Users likely to forget .Update() calls
// - Similar complexity to snapshot but less automatic
```

**Verdict**: Rejected - Boilerplate without benefits

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Memory Overhead** | Snapshots = ~1.5x object size | Guidance on session scope |
| **Complexity** | Snapshot comparison logic | Well-tested implementation |
| **Value Comparer Requirement** | Needed for complex types | Provide standard comparers |

### Implementation Notes

- Snapshots created when entity loaded or added
- Property-level comparison on SaveChanges
- Custom ValueComparers for complex types
- Clear documentation on snapshot overhead
- Guidance: Keep sessions short-lived

---

## Optimistic Concurrency Tokens

### Decision
✅ Implement optimistic concurrency control via version tokens instead of pessimistic locking.

### Rationale

**Problem Statement:**
In multi-user scenarios, concurrent modifications can cause data loss:
```csharp
User A: Load user
User B: Load user
User B: Update email
User B: Save
User A: Update phone
User A: Save  // B's changes lost!
```

**Why Optimistic Tokens?**

1. **Scalability**: No locks = better for distributed systems
   ```
   Pessimistic (locks):
   - Lock contention
   - Deadlock potential
   - Blocking operations

   Optimistic (tokens):
   - No locks
   - Higher concurrency
   - Scale to more users
   ```

2. **Typical Use Case**: Conflicts are rare
   ```
   Reality: 95%+ of updates succeed without conflict
   Pessimistic approach: All users pay 100% lock overhead
   Optimistic approach: Only conflicting users pay retry cost
   ```

3. **Simplicity**: Server manages version, client doesn't
   ```csharp
   [ConcurrencyToken]
   public long Version { get; set; }  // Auto-managed by server

   // vs. Complex transaction locking logic
   ```

4. **Application-Level Control**: Conflicts handled in code
   ```csharp
   try
   {
       await session.SaveChangesAsync();
   }
   catch (ConcurrencyException)
   {
       // Application decides: retry, merge, notify user, etc.
   }
   ```

### Alternatives Considered

#### ❌ Alternative 1: Pessimistic Locking
```csharp
using (await client.LockAsync("user:1"))
{
    var user = await session.FindAsync<User>("user:1");
    user.Email = "new@test.com";
    await session.SaveChangesAsync();
}  // Lock released

// Problems:
// - Scales poorly with many users
// - Deadlock potential
// - Blocking operations (poor concurrency)
// - Complex lock management
// - Typical use case (rare conflicts) pays full cost
```

**Verdict**: Rejected - Over-engineered for typical scenarios

#### ❌ Alternative 2: No Concurrency Control
```csharp
// No version tokens, no locks
var user1 = await client.GetAsync<User>("user:1");
var user2 = await client.GetAsync<User>("user:1");

user1.Email = "email1@test.com";
user2.Phone = "555-1234";

await client.UpdateAsync("user:1", user1);  // Succeeds
await client.UpdateAsync("user:1", user2);  // Overwrites user1's changes

// Problems:
// - Silent data loss
// - No conflict detection
// - Users unaware of overwrites
// - Unacceptable for production systems
```

**Verdict**: Rejected - Unacceptable data loss risk

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Retry Logic Required** | User must handle ConcurrencyException | Provide utilities, examples |
| **Conflict Window** | Race condition possible (rare) | Acceptable for typical apps |
| **Version Column Storage** | Requires database column | Standard pattern, minimal overhead |

### Implementation Notes

- Version token via `[ConcurrencyToken]` attribute
- Server auto-increments on each update
- `ConcurrencyException` thrown on version mismatch
- Provide retry utilities:
  ```csharp
  public static async Task<T> WithOptimisticRetry<T>(
      Func<Task<T>> operation,
      int maxRetries = 3)
  {
      for (int i = 0; i < maxRetries; i++)
      {
          try { return await operation(); }
          catch (ConcurrencyException) when (i < maxRetries - 1) { }
      }
      throw;
  }
  ```

---

## Multi-Serializer Support

### Decision
✅ Support multiple serializers (System.Text.Json, Newtonsoft.Json, custom) rather than forcing one.

### Rationale

**Problem Statement:**
Different applications have different serialization requirements:
- Performance-focused: System.Text.Json (fast, low memory)
- Compatibility: Newtonsoft.Json (more forgiving, richer features)
- Custom: Special type handling, domain-specific converters

**Why Multiple Serializers?**

1. **Flexibility**: Each application chooses what's best
   ```csharp
   // Performance-first
   var client = new SurrealDbClient(options)
   {
       Serializer = new SystemTextJsonSerializer()
   };

   // Compatibility
   var client = new SurrealDbClient(options)
   {
       Serializer = new NewtonsoftJsonSerializer()
   };

   // Custom
   var client = new SurrealDbClient(options)
   {
       Serializer = new DomainSpecificSerializer()
   };
   ```

2. **Pragmatism**: Accept different needs exist
   - Some projects already standardized on Newtonsoft.Json
   - Some want minimal dependencies (System.Text.Json only)
   - Some have special serialization requirements

3. **Migration**: Easier switching if needed
   ```csharp
   // Start with one, switch if needed
   client.Serializer = new AlternativeSerializer();
   ```

4. **Performance**: Let apps optimize their choice
   - System.Text.Json: ~2x faster than Newtonsoft.Json
   - Newtonsoft.Json: Better compatibility, more features
   - Custom: Domain-optimized

### Alternatives Considered

#### ❌ Alternative 1: System.Text.Json Only
```csharp
// Forced choice
var client = new SurrealDbClient(options);
// Uses only System.Text.Json

// Problems:
// - Projects already using Newtonsoft.Json forced to add dependency
// - Incompatible edge cases (rare but possible)
// - More friction for adoption
// - No flexibility for custom needs
```

**Verdict**: Rejected - Limits adoption

#### ❌ Alternative 2: Newtonsoft.Json Only
```csharp
// Forced choice
var client = new SurrealDbClient(options);
// Uses only Newtonsoft.Json

// Problems:
// - Adds large dependency (Newtonsoft.Json is heavy)
// - Slower than System.Text.Json
// - Projects wanting lightweight solution forced to use heavier option
// - Native .NET recommendation is System.Text.Json
```

**Verdict**: Rejected - Adds bloat

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Code Duplication** | Each serializer needs adapters | Interface contract isolates |
| **Testing Complexity** | Test each serializer | Parameterized test suite |
| **Documentation** | Must explain when to use each | Clear comparison table |

### Implementation Notes

- `ISerializer` interface defines contract
- Default: System.Text.Json
- Optional NuGet packages for Newtonsoft.Json support
- Custom serializers implement `ISerializer`
- Easy to add new serializers without core changes

---

## Real-Time Subscriptions

### Decision
✅ Make real-time subscriptions a first-class feature via live queries and change notifications.

### Rationale

**Problem Statement:**
Modern applications need real-time data:
- Collaborative apps need to show live changes
- Dashboards need live metrics
- Chat/notifications need instant updates

**Why First-Class Support?**

1. **Competitive Advantage**: Unlike EF Core, other ORMs
   ```csharp
   // SurrealDB.Client: Native support
   using var subscription = await client.SubscribeAsync<User>(
       q => q.Where(u => u.Status == "online")
   );

   // EF Core: No equivalent, must build polling
   ```

2. **Performance**: Efficient server-side filtering
   ```
   Server sends only changed records matching filter
   vs. polling: SELECT * every N seconds
   ```

3. **Simplicity**: Natural C# async/await
   ```csharp
   await foreach (var change in subscription.GetChangesAsync())
   {
       // React to real-time changes
   }
   ```

4. **Scale**: One persistent connection vs. many polling requests

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Connection Complexity** | WebSocket management | Robust reconnection logic |
| **Event Ordering** | Network delays possible | Document guarantees |
| **Backpressure Handling** | Slow consumers | Buffer management, guidance |

### Implementation Notes

- Live query subscriptions via WebSocket
- Composable query filters (same IQueryable API)
- Automatic reconnection
- Change notification structure (Create, Update, Delete)
- Example use cases in documentation

---

## Exception Typing

### Decision
✅ Implement typed exception hierarchy mirroring EntityFramework.Exceptions pattern instead of generic SurrealDbException.

### Rationale

**Problem Statement:**
Different errors need different handling:
```csharp
try
{
    await session.SaveChangesAsync();
}
catch (SurrealDbException ex)  // Too generic!
{
    // How do we know what went wrong?
    // Is it a duplicate email? Network timeout? Permission denied?
    // Can't determine recovery strategy
}
```

**Why Typed Exceptions?**

1. **Specificity**: Different errors → different handling
   ```csharp
   catch (UniqueConstraintException ex)
   {
       // Show user validation error
       ModelState.AddModelError("Email", "Email already in use");
   }
   catch (ConcurrencyException ex)
   {
       // Reload and retry
       await session.Entry(ex.Entity).ReloadAsync();
   }
   catch (ReferenceConstraintException ex)
   {
       // Can't delete due to dependencies
       logger.LogWarning($"Constraint violation: {ex.Message}");
   }
   ```

2. **Reduced String Parsing**: No brittle error message checking
   ```
   ❌ Bad: if (ex.Message.Contains("duplicate")) { ... }
   ✅ Good: catch (UniqueConstraintException) { ... }
   ```

3. **Database-Agnostic**: Same types regardless of underlying DB
   ```
   SQL Server → UniqueConstraintException
   PostgreSQL → UniqueConstraintException
   SQLite → UniqueConstraintException
   (Same code works for all)
   ```

4. **IntelliSense**: Discover exception types
   ```
   catch (|)  // IntelliSense shows available exception types
   ```

### Alternatives Considered

#### ❌ Alternative 1: Generic Exception
```csharp
catch (SurrealDbException ex)
{
    // Must parse error code/message to determine action
    if (ex.ErrorCode == "E_UNIQUE")
    {
        // Handle uniqueness
    }
    else if (ex.ErrorCode == "E_CONCURRENT")
    {
        // Handle concurrency
    }
    else if (ex.ErrorCode == "E_REFERENCE")
    {
        // Handle reference
    }
    // ... more string matching
}

// Problems:
// - Error-prone string matching
// - Fragile to error code changes
// - No IntelliSense discovery
// - Verbose error handling
```

**Verdict**: Rejected - Too boilerplate

### Trade-Offs Accepted

| Trade-Off | Impact | Mitigation |
|-----------|--------|-----------|
| **Exception Hierarchy Maintenance** | May add exceptions | Clear documentation |
| **Error Code Mapping** | Must update when DB adds errors | Well-tested mapper |
| **Database-Specific Details** | Lost in abstraction | Preserve in InnerException |

### Implementation Notes

- Base exception: `SurrealDbException`
- Constraint exceptions: `UniqueConstraintException`, `ReferenceConstraintException`, etc.
- Concurrency: `ConcurrencyException`
- Operational: `ConnectionException`, `TimeoutException`
- Authentication: `AuthenticationException`, `AuthorizationException`
- Original SurrealDB error in `InnerException` for debugging

---

## Summary Table

| Decision | Primary Benefit | Main Trade-Off | Alternative Rejected |
|----------|-----------------|----------------|----------------------|
| **Session/UoW** | Atomic multi-op semantics | Session lifecycle mgmt | Per-op transactions |
| **IQueryable** | Composability & testability | Expression compiler complexity | QueryBuilder-only |
| **Protocol Abstraction** | Flexibility & optimization | Code duplication | Single protocol |
| **Change Tracking** | Bandwidth efficiency (99%) | Memory overhead | Manual state tracking |
| **Optimistic Concurrency** | Better scalability | Retry logic required | Pessimistic locking |
| **Multi-Serializer** | Pragmatism & flexibility | Testing complexity | Forced serializer |
| **Real-Time First-Class** | Competitive advantage | Connection complexity | Polling-based |
| **Typed Exceptions** | Specific error handling | More exception types | Generic exceptions |

---

