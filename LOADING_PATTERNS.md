# Loading Patterns: Include, Lazy, and Explicit Loading

> Comprehensive guide to related data loading strategies - a key EF Core pattern adapted for SurrealDB.Client to reach A-grade architecture.

## Overview

Three loading strategies for related data:

| Pattern | When | Pros | Cons |
|---------|------|------|------|
| **Eager** | Know you need related data | Single query, no N+1 | May load unnecessary data |
| **Lazy** | Related data conditionally needed | On-demand, clean code | N+1 problem, network overhead |
| **Explicit** | Fine-grained control | Flexible, efficient | More code, must be deliberate |

---

## 1. Eager Loading with Include()

Load related data as part of the initial query.

### Basic Include

```csharp
using var session = client.CreateSession();

var user = await session.Set<User>()
    .Include(u => u.Orders)
    .Include(u => u.Profile)
    .FirstAsync(u => u.Id == "user:1");

// Single query: SELECT * FROM users:1 WITH orders, profile
// user.Orders is loaded and ready
```

### Chaining with ThenInclude

```csharp
var users = await session.Set<User>()
    .Include(u => u.Orders)
        .ThenInclude(o => o.Items)
        .ThenInclude(i => i.Product)
    .ToListAsync();

// Multi-level relationships in single query:
// SELECT * FROM users WITH (
//   orders WITH (
//     items WITH product
//   )
// )
```

### Filtered Include

```csharp
var users = await session.Set<User>()
    .Include(u => u.Orders.Where(o => o.Status == "active"))
    .Include(u => u.Profile)
    .ToListAsync();

// Only load active orders, all profiles
```

### Implementation Details

```csharp
public interface IQueryable<T>
{
    // Single include
    IQueryable<T> Include<TProperty>(
        Expression<Func<T, TProperty>> navigationPropertyPath)
        where TProperty : class;

    // Collection include
    IQueryable<T> Include<TNavigationProperty>(
        Expression<Func<T, IEnumerable<TNavigationProperty>>> navigationPropertyPath)
        where TNavigationProperty : class;
}

public interface IIncludableQueryable<TEntity, TProperty> : IQueryable<TEntity>
{
    // Chain includes
    IIncludableQueryable<TEntity, TNewProperty> ThenInclude<TNewProperty>(
        Expression<Func<TProperty, TNewProperty>> navigationPropertyPath)
        where TNewProperty : class;
}
```

### Query Optimization

The query builder detects includes and generates optimized SurrealQL:

```
User with Orders relationship:
  SELECT * FROM users:1
  FETCH orders       -- Fetch related data

Multiple includes:
  SELECT * FROM users:1
  FETCH orders, profile

Nested includes (ThenInclude):
  SELECT * FROM users:1
  FETCH orders.items.product

Filtered includes:
  SELECT * FROM users:1
  FETCH orders[WHERE status = 'active']
```

---

## 2. Lazy Loading with Proxies

Related data loaded on property access.

### Enable Lazy Loading

```csharp
var options = new SurrealDbClientOptions
{
    UseLazyLoadingProxies = true
};

var client = new SurrealDbClient(options);
using var session = client.CreateSession();
```

### Usage

```csharp
var user = await session.FindAsync<User>("user:1");
// user.Orders NOT loaded yet

var orderCount = user.Orders.Count();
// Lazy load triggered: SELECT * FROM orders WHERE user_id = 'user:1'
```

### How It Works

Lazy loading uses **virtual proxies** - the framework intercepts property access:

```csharp
public class User
{
    public string Id { get; set; }
    public string Name { get; set; }

    // Must be virtual for lazy loading
    public virtual ICollection<Order> Orders { get; set; }
    public virtual Profile Profile { get; set; }
}

// Framework creates proxy:
// public class UserProxy : User
// {
//     private bool _ordersLoaded;
//     private ICollection<Order> _orders;
//
//     public override ICollection<Order> Orders
//     {
//         get
//         {
//             if (!_ordersLoaded)
//             {
//                 // Load from database
//                 _orders = LoadOrders();
//                 _ordersLoaded = true;
//             }
//             return _orders;
//         }
//     }
// }
```

### Pros & Cons

✅ **Pros:**
- Clean code (no Include calls)
- Load only what's accessed
- Feels natural

❌ **Cons:**
- N+1 query problem easy to trigger
- Network latency per access
- Hidden performance cost
- Requires virtual properties

### N+1 Query Problem Example

```csharp
// ❌ BAD: N+1 queries
var users = await session.Set<User>().ToListAsync();  // 1 query
foreach (var user in users)
{
    var orderCount = user.Orders.Count();  // N queries (one per user!)
}
// Total: 1 + N queries

// ✅ GOOD: Single query with eager loading
var users = await session.Set<User>()
    .Include(u => u.Orders)
    .ToListAsync();

foreach (var user in users)
{
    var orderCount = user.Orders.Count();  // No query
}
// Total: 1 query
```

---

## 3. Explicit Loading

Load related data on demand with explicit methods.

### Entry().Reference()

Load a single related entity:

```csharp
var user = await session.FindAsync<User>("user:1");

// Not loaded yet
var profile = user.Profile;  // null

// Explicitly load
await session.Entry(user)
    .Reference(u => u.Profile)
    .LoadAsync();

// Now loaded
var profile = user.Profile;  // Loaded from database
```

### Entry().Collection()

Load a collection of related entities:

```csharp
var user = await session.FindAsync<User>("user:1");

// Not loaded yet
var ordersCount = user.Orders?.Count ?? 0;  // 0

// Explicitly load
await session.Entry(user)
    .Collection(u => u.Orders)
    .LoadAsync();

// Now loaded
var ordersCount = user.Orders.Count;  // Actual count
```

### Filtered Explicit Loading

```csharp
// Load only active orders
await session.Entry(user)
    .Collection(u => u.Orders)
    .Query()
    .Where(o => o.Status == "active")
    .LoadAsync();

// user.Orders now contains only active orders
```

### Implementation

```csharp
public class EntityEntry<T>
{
    // Load a reference (single related entity)
    public ReferenceEntry<T, TProperty> Reference<TProperty>(
        Expression<Func<T, TProperty>> navigationPropertyPath);

    // Load a collection (multiple related entities)
    public CollectionEntry<T, TElement> Collection<TElement>(
        Expression<Func<T, IEnumerable<TElement>>> navigationPropertyPath)
        where TElement : class;
}

public class ReferenceEntry<T, TProperty>
{
    public async Task LoadAsync(CancellationToken ct = default);
    public IQueryable<TProperty> Query();
}

public class CollectionEntry<T, TElement>
{
    public async Task LoadAsync(CancellationToken ct = default);
    public IQueryable<TElement> Query();  // Filter before loading
}
```

---

## Loading Pattern Selection Guide

### Use Eager Loading (Include) When:

✅ You know you'll need the related data
✅ Related data is frequently accessed
✅ Relationship is 1:1 or small collections
✅ You want to avoid N+1 queries

```csharp
// Getting user with all details
var user = await session.Set<User>()
    .Include(u => u.Profile)
    .Include(u => u.Orders)
    .Include(u => u.Preferences)
    .FirstAsync(u => u.Id == "user:1");
```

### Use Lazy Loading When:

✅ Related data is conditionally needed
✅ Code simplicity matters more than performance
✅ N+1 is unlikely (small result sets)
✅ Using dependency injection for convenience

```csharp
// Web request where related data might not be needed
var user = await session.FindAsync<User>("user:1");

if (user.RequiresApproval)
{
    var approvers = user.Approvers;  // Lazy load only if needed
}
```

### Use Explicit Loading When:

✅ Need fine-grained control over what loads
✅ Want to filter related data before loading
✅ Different code paths need different related data
✅ Combining multiple loading strategies

```csharp
var user = await session.FindAsync<User>("user:1");

// Load paginated orders
var page = 1;
var pageSize = 10;

await session.Entry(user)
    .Collection(u => u.Orders)
    .Query()
    .OrderByDescending(o => o.CreatedAt)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .LoadAsync();
```

---

## Performance Considerations

### Including Too Much Data

❌ **Bad**: Over-fetching

```csharp
var users = await session.Set<User>()
    .Include(u => u.Orders)
    .Include(u => u.Orders)
        .ThenInclude(o => o.Items)
        .ThenInclude(i => i.Product)
    .Include(u => u.Orders)
        .ThenInclude(o => o.Payments)
    .Include(u => u.Profile)
    .Include(u => u.Addresses)
    .ToListAsync();

// Loads massive result set, much of it unused
```

✅ **Good**: Only what's needed

```csharp
var users = await session.Set<User>()
    .Include(u => u.Profile)
    .ToListAsync();

// Load orders separately only if needed
```

### Collection Size Management

```csharp
// ❌ Bad: Loading 10,000 orders per user
var users = await session.Set<User>()
    .Include(u => u.Orders)  // Could be huge!
    .ToListAsync();

// ✅ Good: Paginate collections
var users = await session.Set<User>().ToListAsync();

foreach (var user in users)
{
    var recentOrders = await session.Entry(user)
        .Collection(u => u.Orders)
        .Query()
        .OrderByDescending(o => o.CreatedAt)
        .Take(5)
        .ToListAsync();
}

// Or better: Use projection
var userOrders = await session.Set<User>()
    .Select(u => new
    {
        User = u,
        RecentOrders = u.Orders.OrderByDescending(o => o.CreatedAt).Take(5)
    })
    .ToListAsync();
```

### Query Complexity

Include depth impacts query performance:

```
Level 1:  User -> Profile                      Fast
Level 2:  User -> Orders -> Items              Moderate
Level 3:  User -> Orders -> Items -> Products  Slower
Level 4+: Multiple deep paths                  Potentially Slow
```

Recommendation: Keep eager loading paths to 2-3 levels max

---

## Advanced Patterns

### Conditional Include

```csharp
var needsOrders = context.Request.Query["includeOrders"] == "true";

var query = session.Set<User>();

if (needsOrders)
    query = query.Include(u => u.Orders);

var user = await query.FirstAsync();
```

### Multiple Include Strategies

```csharp
// Strategy 1: Load all relationships
var userFull = await GetUserFullAsync(userId);

// Strategy 2: Load minimal data
var userBasic = await GetUserBasicAsync(userId);

// Strategy 3: Custom loading
var userCustom = await GetUserWithSpecificDataAsync(userId);

private Task<User> GetUserFullAsync(string id) =>
    session.Set<User>()
        .Include(u => u.Profile)
        .Include(u => u.Orders)
        .Include(u => u.Addresses)
        .FirstAsync(u => u.Id == id);

private Task<User> GetUserBasicAsync(string id) =>
    session.Set<User>()
        .FirstAsync(u => u.Id == id);

private async Task<User> GetUserWithSpecificDataAsync(string id)
{
    var user = await session.FindAsync<User>(id);

    await session.Entry(user)
        .Collection(u => u.Orders)
        .Query()
        .Where(o => o.Status == "active")
        .LoadAsync();

    return user;
}
```

### Projection as Alternative

Instead of Include, project only needed fields:

```csharp
// Instead of:
var users = await session.Set<User>()
    .Include(u => u.Orders)
    .ToListAsync();

// Use projection:
var userDtos = await session.Set<User>()
    .Select(u => new UserDto
    {
        Id = u.Id,
        Name = u.Name,
        OrderCount = u.Orders.Count(),
        RecentOrder = u.Orders.OrderByDescending(o => o.CreatedAt).FirstOrDefault()
    })
    .ToListAsync();

// Loads only needed columns, filtered at server
```

---

## Best Practices

1. **Default to Eager Loading** - Most performant for typical queries
2. **Avoid N+1 Queries** - Use includes or explicit loading in loops
3. **Project When Possible** - Select only needed fields
4. **Lazy Load Carefully** - Understand when it triggers
5. **Explicit Load for Control** - Fine-grained loading strategies
6. **Monitor Query Plans** - Log generated SurrealQL
7. **Document Expectations** - Clarify when related data loads
8. **Test with Real Data** - Development data may hide N+1 problems

---

## Testing Loading Patterns

```csharp
[Fact]
public async Task Include_LoadsRelatedData_InSingleQuery()
{
    var user = await session.Set<User>()
        .Include(u => u.Orders)
        .FirstAsync();

    Assert.NotNull(user.Orders);
    Assert.NotEmpty(user.Orders);
}

[Fact]
public async Task ExplicitLoad_LoadsOnlyWhenCalled()
{
    var user = await session.FindAsync<User>("user:1");
    Assert.Null(user.Orders);  // Not loaded

    await session.Entry(user)
        .Collection(u => u.Orders)
        .LoadAsync();

    Assert.NotNull(user.Orders);  // Now loaded
}

[Fact]
public async Task LazyLoad_TriggersOnAccess()
{
    var user = await session.FindAsync<User>("user:1");
    var count = user.Orders.Count;  // Triggers load
    Assert.True(count >= 0);
}
```

