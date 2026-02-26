# DataLoader Pattern: Batch Loading & Query Optimization

> Batch data loading to eliminate N+1 queries and optimize request processing.

## DataLoader Basics

```csharp
// Create loader with batch function
var userLoader = new DataLoader<string, User>(async (ids, ct) =>
{
    // Batch load all requested IDs in single query
    var users = await session.Set<User>()
        .Where(u => ids.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id, ct);

    // Return in same order as requested IDs
    return ids.Select(id => users.TryGetValue(id, out var user) ? user : null).ToList();
});

// Load individual items - automatically batched
var user1 = await userLoader.LoadAsync("user:1");
var user2 = await userLoader.LoadAsync("user:2");
var user3 = await userLoader.LoadAsync("user:3");
// All 3 loaded in single query
```

## Using DataLoader in GraphQL-like Scenarios

```csharp
public class OrderResolver
{
    private readonly DataLoader<string, User> _userLoader;

    public async Task<User> GetUserAsync(Order order)
    {
        return await _userLoader.LoadAsync(order.UserId);
    }
}

// N+1 eliminated: 1 query instead of N queries
var orders = await session.Set<Order>().ToListAsync();
foreach (var order in orders)
{
    var user = await resolver.GetUserAsync(order);  // Batched
}
```

## Caching in DataLoader

```csharp
var cachedLoader = new DataLoader<string, User>(
    batchLoadFn: async (ids, ct) => { /* ... */ },
    options: new DataLoaderOptions { Cache = true });

// First call: loads from database
var user1 = await cachedLoader.LoadAsync("user:1");

// Second call: loads from cache (same request)
var user2 = await cachedLoader.LoadAsync("user:1");  // From cache
```

## See full DATALOADER.md in repository
