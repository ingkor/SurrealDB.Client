# Query Caching & Optimization

> Comprehensive caching strategies including query plan caching, result caching, and compiled queries - enterprise-grade performance.

## Multi-Level Caching Strategy

```
Level 1: Query Plan Cache (Automatic)
Level 2: Compiled Query Cache (Opt-in)
Level 3: Result Cache (Optional)
```

## Level 1: Query Plan Cache (Automatic)

The query compiler automatically caches expression tree compilation results.

### How It Works

```csharp
// First execution
var users1 = await session.Set<User>()
    .Where(u => u.Age >= 18)
    .ToListAsync();
// → Expression tree compiled to SurrealQL → Compiled plan cached

// Second execution (same query shape)
var users2 = await session.Set<User>()
    .Where(u => u.Age >= 18)
    .ToListAsync();
// → Reuses cached plan (no compilation overhead)

// Different parameter → Same plan
var users3 = await session.Set<User>()
    .Where(u => u.Age >= 25)  // Different parameter
    .ToListAsync();
// → Same compiled plan, different parameter
```

### Query Plan Key

Plans are cached by query shape, not parameter values:

```
Shape: Set<User>.Where(u => u.Age >= X)
Key: "SurrealDbQuery_Set_User_Where_Age_GreaterThan"

Any value for X reuses same plan:
  .Where(u => u.Age >= 18)  → Cache hit
  .Where(u => u.Age >= 25)  → Cache hit
  .Where(u => u.Age >= 100) → Cache hit
```

### Cache Configuration

```csharp
var options = new SurrealDbClientOptions
{
    QueryPlanCacheSize = 1000,        // Max cached plans
    QueryPlanCacheTtl = TimeSpan.FromHours(1),
    QueryPlanCacheEnabled = true
};

var client = new SurrealDbClient(options);
```

### Monitoring Cache Hit Rate

```csharp
var metrics = client.GetMetrics();
Console.WriteLine($"Cache hit rate: {metrics.QueryPlanCacheHitRate}%");
// Example: 98% (2% are new query shapes)
```

---

## Level 2: Compiled Queries

Pre-compile frequently-used queries for maximum performance.

### Basic Compiled Query

```csharp
// Define compiled query
private static readonly Func<ISurrealDbSession, string, Task<User>> GetUserByEmailCompiled =
    EF.CompileAsyncQuery(
        (ISurrealDbSession session, string email) =>
            session.Set<User>()
                .Where(u => u.Email == email)
                .FirstOrDefault()
    );

// Usage
var user = await GetUserByEmailCompiled(session, "john@example.com");
// No expression tree compilation - direct execution
```

### Complex Compiled Query

```csharp
private static readonly Func<ISurrealDbSession, string, int, Task<List<User>>>
    GetAdultsInCityCompiled = EF.CompileAsyncQuery(
        (ISurrealDbSession session, string city, int minAge) =>
            session.Set<User>()
                .Where(u => u.Address.City == city && u.Age >= minAge)
                .Include(u => u.Orders)
                .OrderBy(u => u.Name)
                .ToList()
    );

// Usage
var users = await GetAdultsInCityCompiled(session, "New York", 18);
```

### With Groups

```csharp
private static readonly Func<ISurrealDbSession, Task<Dictionary<string, int>>>
    UserCountByStatusCompiled = EF.CompileAsyncQuery(
        (ISurrealDbSession session) =>
            session.Set<User>()
                .GroupBy(u => u.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Status, x => x.Count)
    );
```

### Performance Benefit

```
Non-compiled:  SELECT → Expression tree → Compile → Execute → 150ms
Compiled:      Execute → 50ms
Savings:       67% faster
```

---

## Level 3: Result Caching

Cache query results with smart invalidation.

### Configuration

```csharp
services.AddSurrealDbClient(options =>
{
    options.ResultCachingEnabled = true;
    options.ResultCacheSize = 10000;  // Max cached results
    options.DefaultCacheDuration = TimeSpan.FromMinutes(5);
});
```

### Basic Result Caching

```csharp
var users = await session.Set<User>()
    .Cache(TimeSpan.FromMinutes(5))
    .Where(u => u.Status == "active")
    .ToListAsync();

// First call: Query server, cache result
// Next 5 minutes: Return cached result
// After 5 minutes: Query server again
```

### Explicit Cache Control

```csharp
// Cache indefinitely (until invalidation)
var result = await session.Set<User>()
    .Cache()  // No expiration
    .ToListAsync();

// No cache
var result = await session.Set<User>()
    .NoCache()
    .ToListAsync();

// Force refresh
var result = await session.Set<User>()
    .Cache(invalidateCache: true)
    .ToListAsync();
```

### Smart Cache Invalidation

Automatically invalidate caches when related entities change:

```csharp
// When User is updated, invalidate related caches
[CacheInvalidationTrigger("users:*")]
public class User
{
    public string Id { get; set; }
    public string Email { get; set; }
}

// All "users:*" caches invalidate on User update
var users = await session.Set<User>().Cache().ToListAsync();
// Invalidated when any user.SaveChangesAsync()
```

### Cache Tags

```csharp
var users = await session.Set<User>()
    .Where(u => u.Status == "active")
    .Cache(
        duration: TimeSpan.FromMinutes(10),
        tags: new[] { "users", "active-users" })
    .ToListAsync();

// Later: Invalidate specific cache tag
await client.InvalidateCacheAsync("active-users");
// Clears all caches tagged with "active-users"
```

### Distributed Caching

```csharp
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

services.AddSurrealDbClient(options =>
{
    options.UseDistributedCache<IDistributedCache>();
    options.DistributedCacheKeyPrefix = "surreal:";
});

// Now caching works across multiple instances
var user = await session.Set<User>()
    .Cache()
    .FirstAsync();  // Cached in Redis
```

---

## Advanced Caching Patterns

### Tiered Caching

```csharp
// Try memory cache first, then distributed cache
var user = await session.Set<User>()
    .Cache(
        provider: CacheProvider.Tiered,  // Memory + Distributed
        duration: TimeSpan.FromMinutes(5))
    .FirstAsync(u => u.Id == "user:1");
```

### Conditional Caching

```csharp
var users = await session.Set<User>()
    .ConditionalCache(
        shouldCache: (users) => users.Count < 1000,  // Only cache small result sets
        duration: TimeSpan.FromMinutes(5))
    .ToListAsync();
```

### Cache Warming

```csharp
// Pre-populate caches on startup
public class CacheWarmingHostedService : IHostedService
{
    private readonly ISurrealDbClient _client;

    public async Task StartAsync(CancellationToken ct)
    {
        using var session = _client.CreateSession();

        // Warm up frequently-accessed data
        await session.Set<User>()
            .Cache()
            .ToListAsync(ct);

        await session.Set<Order>()
            .Cache()
            .ToListAsync(ct);

        // Caches now populated for first requests
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

### Cache Invalidation Patterns

```csharp
// Time-based (TTL)
.Cache(TimeSpan.FromMinutes(5))

// Event-based
.Cache(tags: new[] { "users" })
// Invalidate when: await client.InvalidateCacheAsync("users")

// Dependency-based
.Cache(dependencies: new[] { typeof(User), typeof(Order) })
// Invalidate when those entities change

// Manual
var cacheToken = new CancellationTokenSource();
.Cache(cacheToken.Token)
// Invalidate when: cacheToken.Cancel()
```

---

## Query Optimization Strategies

### 1. Projection (Select Only Needed Columns)

```csharp
// ❌ Loads all columns
var users = await session.Set<User>().ToListAsync();

// ✅ Loads only needed columns
var userDtos = await session.Set<User>()
    .Select(u => new UserDto
    {
        Id = u.Id,
        Name = u.Name,
        Email = u.Email
    })
    .ToListAsync();
// Reduces bandwidth by 80%+
```

### 2. Filtering at Server

```csharp
// ❌ Client-side filtering
var users = await session.Set<User>().ToListAsync();
var adults = users.Where(u => u.Age >= 18).ToList();

// ✅ Server-side filtering
var adults = await session.Set<User>()
    .Where(u => u.Age >= 18)
    .ToListAsync();
// Reduces data transfer by 90%+
```

### 3. Pagination

```csharp
var pageSize = 20;
var page = 2;

var users = await session.Set<User>()
    .OrderBy(u => u.Name)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
// Load only page, not entire table
```

### 4. Distinct & Aggregates

```csharp
// Count without loading data
var count = await session.Set<User>()
    .CountAsync();

// Group and aggregate
var usersByStatus = await session.Set<User>()
    .GroupBy(u => u.Status)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToListAsync();
```

---

## Caching Best Practices

1. **Cache frequently-accessed, slowly-changing data**
   - User profiles ✅
   - Real-time data ❌

2. **Use appropriate TTLs**
   - Configuration: 1 hour
   - User data: 5 minutes
   - Real-time: No cache

3. **Invalidate strategically**
   - Don't invalidate everything (performance)
   - Use cache tags for targeted invalidation
   - Event-driven invalidation preferred

4. **Monitor cache effectiveness**
   ```csharp
   var metrics = client.GetMetrics();
   var hitRate = metrics.CacheHitRate;  // Should be 70-90%
   var size = metrics.CacheSize;
   var evictions = metrics.CacheEvictions;
   ```

5. **Distributed caching for multi-instance**
   - Use Redis or distributed cache
   - Share caches across servers
   - Consistent cache keys

6. **Test cache invalidation**
   - Verify caches clear correctly
   - Test with stale data scenarios
   - Performance regression tests

