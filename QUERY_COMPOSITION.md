# Query Composition: IQueryable Strategy

> Deep dive into SurrealDB.Client's query composition system using IQueryable<T>, deferred execution, and expression tree translation.

## Overview

SurrealDB.Client uses `IQueryable<T>` as the primary query API, enabling:

- **Composition**: Build queries step-by-step without execution
- **Deferred Execution**: Queries execute only when enumerated
- **Expression Translation**: C# expressions → SurrealQL
- **Reusability**: Pass queries between methods
- **Testability**: Mock IQueryable in unit tests

### The Case for IQueryable

While QueryBuilder is useful for raw query building, `IQueryable<T>` is the primary API because:

1. **Composability** - You can pass queries to methods without executing
2. **Type Safety** - Intellisense on entity properties
3. **Familiarity** - Developers know LINQ from EF Core
4. **Testability** - Easier to mock in unit tests
5. **Late Binding** - Query constructed at last moment

---

## Architecture

### QueryProvider Implementation

```csharp
public class SurrealDbQueryProvider : IQueryProvider
{
    private readonly ISurrealDbSession _session;
    private readonly IQueryCompiler _compiler;

    public IQueryable<S> CreateQuery<S>(Expression expression)
    {
        return new SurrealDbQuery<S>(this, expression);
    }

    public object Execute(Expression expression)
    {
        var compiled = _compiler.Compile(expression);
        return compiled.Execute(_session);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        var compiled = _compiler.Compile<TResult>(expression);
        return compiled.Execute(_session);
    }
}

public class SurrealDbQuery<T> : IQueryable<T>, IEnumerable<T>
{
    private readonly IQueryProvider _provider;
    private readonly Expression _expression;

    public SurrealDbQuery(IQueryProvider provider, Expression expression)
    {
        _provider = provider;
        _expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _provider;

    public IEnumerator<T> GetEnumerator()
    {
        // Execute at enumeration
        return _provider.Execute<IEnumerable<T>>(_expression).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

### Expression Tree Compilation

```
C# Code
  ↓
Expression Tree (abstract syntax)
  ↓
SurrealQL Query Compiler
  ↓
SurrealQL String
  ↓
Network Transmission
  ↓
Server Execution
  ↓
Results
```

Example:
```csharp
// C# Code
var query = session.Set<User>()
    .Where(u => u.Age >= 18)
    .OrderBy(u => u.Name)
    .Take(10);

// Expression Tree
MethodCall(
    MethodCall(
        MethodCall(
            Constant(session.Set<User>()),
            "Where",
            Expression.Lambda(u => u.Age >= 18)
        ),
        "OrderBy",
        Expression.Lambda(u => u.Name)
    ),
    "Take",
    Constant(10)
)

// Compiled SurrealQL
SELECT * FROM users
WHERE age >= 18
ORDER BY name
LIMIT 10
```

---

## Query Composition Patterns

### Pattern 1: Sequential Composition

```csharp
// Build query step by step
IQueryable<User> query = session.Set<User>();

if (filterByAge)
    query = query.Where(u => u.Age >= 18);

if (filterByStatus)
    query = query.Where(u => u.Status == "active");

if (needsOrdering)
    query = query.OrderBy(u => u.Name);

// Execute only at the end
var results = await query.ToListAsync();
// Single database round trip
```

### Pattern 2: Extension Methods

```csharp
// Domain-specific extensions
public static class UserQueryExtensions
{
    public static IQueryable<User> Active(this IQueryable<User> query)
    {
        return query.Where(u => u.Status == "active");
    }

    public static IQueryable<User> Adults(this IQueryable<User> query)
    {
        return query.Where(u => u.Age >= 18);
    }

    public static IQueryable<User> ByCity(this IQueryable<User> query, string city)
    {
        return query.Where(u => u.Address.City == city);
    }

    public static IQueryable<User> RecentlyCreated(this IQueryable<User> query, int days = 30)
    {
        var threshold = DateTime.UtcNow.AddDays(-days);
        return query.Where(u => u.CreatedAt > threshold);
    }
}

// Fluent, chainable API
var results = await session.Set<User>()
    .Active()
    .Adults()
    .ByCity("New York")
    .RecentlyCreated(7)
    .OrderBy(u => u.Name)
    .ToListAsync();
// Single query with all conditions
```

### Pattern 3: Method Extraction

```csharp
// Extract reusable query methods
public class UserRepository
{
    private readonly ISurrealDbSession _session;

    public IQueryable<User> GetBaseQuery()
    {
        return _session.Set<User>()
            .Where(u => !u.IsDeleted);
    }

    public IQueryable<User> GetActiveUsers()
    {
        return GetBaseQuery()
            .Where(u => u.Status == "active");
    }

    public async Task<User> GetUserByEmail(string email)
    {
        return await GetBaseQuery()
            .Where(u => u.Email == email)
            .FirstOrDefaultAsync();
    }

    public async Task<List<User>> GetUsersByRole(string role)
    {
        return await GetBaseQuery()
            .Where(u => u.Roles.Contains(role))
            .ToListAsync();
    }
}

// Usage
var activeUsers = await _repo.GetActiveUsers().ToListAsync();
var user = await _repo.GetUserByEmail("john@example.com");
var admins = await _repo.GetUsersByRole("admin");
```

### Pattern 4: Dynamic Filtering

```csharp
public async Task<List<User>> SearchUsers(SearchCriteria criteria)
{
    var query = _session.Set<User>();

    // Build query dynamically based on criteria
    if (!string.IsNullOrEmpty(criteria.Name))
        query = query.Where(u => u.Name.Contains(criteria.Name));

    if (!string.IsNullOrEmpty(criteria.Email))
        query = query.Where(u => u.Email == criteria.Email);

    if (criteria.MinAge.HasValue)
        query = query.Where(u => u.Age >= criteria.MinAge.Value);

    if (criteria.MaxAge.HasValue)
        query = query.Where(u => u.Age <= criteria.MaxAge.Value);

    if (criteria.Status != null)
        query = query.Where(u => u.Status == criteria.Status);

    // Apply sorting
    query = criteria.SortBy switch
    {
        "name" => query.OrderBy(u => u.Name),
        "age" => query.OrderBy(u => u.Age),
        "created" => query.OrderBy(u => u.CreatedAt),
        _ => query
    };

    // Apply pagination
    if (criteria.Skip.HasValue)
        query = query.Skip(criteria.Skip.Value);

    if (criteria.Take.HasValue)
        query = query.Take(criteria.Take.Value);

    // Single optimized query to server
    return await query.ToListAsync();
}
```

---

## Expression Translation

### Supported Operations

| LINQ | SurrealQL | Example |
|------|-----------|---------|
| `Where()` | `WHERE` | `.Where(u => u.Age > 18)` |
| `Select()` | `SELECT` | `.Select(u => new { u.Name, u.Email })` |
| `OrderBy()` | `ORDER BY` | `.OrderBy(u => u.Name)` |
| `OrderByDescending()` | `ORDER BY DESC` | `.OrderByDescending(u => u.CreatedAt)` |
| `Take()` | `LIMIT` | `.Take(10)` |
| `Skip()` | `OFFSET` | `.Skip(20)` |
| `Count()` | `count(*)` | `.Count()` |
| `Any()` | `EXISTS` | `.Any(u => u.Status == "active")` |
| `First()` | `LIMIT 1` | `.First()` |
| `FirstOrDefault()` | `LIMIT 1` | `.FirstOrDefault()` |
| `Single()` | `LIMIT 2` | `.Single()` |
| `Contains()` | `IN (...)` | `.Where(u => ids.Contains(u.Id))` |
| `Join()` | `RELATE` | `.Join(orders, u => u.Id, o => o.UserId)` |
| `GroupBy()` | `GROUP BY` | `.GroupBy(u => u.Status)` |
| `Distinct()` | `DISTINCT` | `.Distinct()` |

### Translation Examples

#### Example 1: Simple Filter

```csharp
var query = session.Set<User>()
    .Where(u => u.Age >= 18 && u.Status == "active");

// Translates to:
SELECT * FROM users
WHERE age >= 18 AND status = 'active'
```

#### Example 2: Nested Property Access

```csharp
var query = session.Set<User>()
    .Where(u => u.Address.City == "New York" && u.Address.Country == "USA");

// Translates to:
SELECT * FROM users
WHERE address.city = 'New York' AND address.country = 'USA'
```

#### Example 3: Collection Filtering

```csharp
var query = session.Set<User>()
    .Where(u => u.Orders.Count > 0)
    .Select(u => new { u.Name, OrderCount = u.Orders.Count });

// Translates to:
SELECT name, count(->orders) as OrderCount FROM users
WHERE count(->orders) > 0
```

#### Example 4: Complex Filtering with Multiple Conditions

```csharp
var query = session.Set<Order>()
    .Where(o => o.Status == "pending"
        && o.CreatedAt > DateTime.Now.AddDays(-30)
        && o.Amount > 100
        && (o.Priority == "high" || o.Priority == "urgent"))
    .OrderByDescending(o => o.Amount)
    .Take(100);

// Translates to:
SELECT * FROM orders
WHERE status = 'pending'
  AND created_at > <date>
  AND amount > 100
  AND (priority = 'high' OR priority = 'urgent')
ORDER BY amount DESC
LIMIT 100
```

---

## Advanced Patterns

### Query Caching

```csharp
public class CachedUserQueries
{
    private readonly ISurrealDbSession _session;
    private readonly IMemoryCache _cache;

    // Cache compiled queries
    private static readonly Func<ISurrealDbSession, string, Task<User>> GetByEmailQuery =
        EF.CompileAsyncQuery(
            (ISurrealDbSession session, string email) =>
                session.Set<User>()
                    .Where(u => u.Email == email)
                    .FirstOrDefault()
        );

    public async Task<User> GetByEmailAsync(string email)
    {
        var cacheKey = $"user:{email}";

        if (_cache.TryGetValue(cacheKey, out User cached))
            return cached;

        var user = await GetByEmailQuery(_session, email);

        if (user != null)
            _cache.Set(cacheKey, user, TimeSpan.FromHours(1));

        return user;
    }
}
```

### Specification Pattern

```csharp
public abstract class Specification<T> where T : class
{
    protected internal Expression<Func<T, bool>> Criteria { get; set; }
    protected internal List<Expression<Func<T, object>>> Includes { get; } = new();
    protected internal Expression<Func<T, object>> OrderBy { get; set; }
    protected internal bool IsPagingEnabled { get; set; }
    protected internal int Take { get; set; }
    protected internal int Skip { get; set; }

    public IQueryable<T> ApplySpecification(IQueryable<T> inputQuery)
    {
        var query = inputQuery;

        if (Criteria != null)
            query = query.Where(Criteria);

        query = Includes.Aggregate(query, (current, include) => current.Include(include));

        if (OrderBy != null)
            query = query.OrderBy(OrderBy);

        if (IsPagingEnabled)
        {
            query = query.Skip(Skip).Take(Take);
        }

        return query;
    }
}

// Concrete specification
public class ActiveUsersSpecification : Specification<User>
{
    public ActiveUsersSpecification()
    {
        Criteria = u => u.Status == "active" && !u.IsDeleted;
        Includes.Add(u => u.Orders);
        OrderBy = u => u.Name;
        IsPagingEnabled = true;
        Skip = 0;
        Take = 50;
    }
}

// Usage
var spec = new ActiveUsersSpecification();
var users = await spec.ApplySpecification(session.Set<User>())
    .ToListAsync();
```

### Projection Optimization

```csharp
// Load only needed fields
public class UserDTO
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

var query = session.Set<User>()
    .Where(u => u.Status == "active")
    .Select(u => new UserDTO
    {
        Id = u.Id,
        Name = u.Name,
        Email = u.Email
        // Don't select Address, Orders, etc.
    });

// Translates to:
SELECT id, name, email FROM users WHERE status = 'active'
// Not: SELECT * FROM users WHERE status = 'active'
```

---

## Performance Considerations

### 1. Query Caching

```csharp
// ✅ Good: Query plan cached after first use
var query1 = session.Set<User>().Where(u => u.Age > 18);
var query2 = session.Set<User>().Where(u => u.Age > 18);
// Same expression tree → cached plan reused

// ❌ Bad: Different query each time
for (int i = 0; i < 1000; i++)
{
    var age = i;  // Closure creates different expression
    var query = session.Set<User>().Where(u => u.Age > age);
    var results = await query.ToListAsync();  // No cache hit
}

// ✅ Better: Parameter extraction
var ages = new[] { 18, 21, 30, 40 };
foreach (var age in ages)
{
    var threshold = age;
    var query = session.Set<User>().Where(u => u.Age > threshold);
    var results = await query.ToListAsync();  // Cache hit
}
```

### 2. Projection Over Selection

```csharp
// ❌ Inefficient: Load entire objects, then project in memory
var result = await session.Set<User>()
    .Where(u => u.Status == "active")
    .ToListAsync()  // Load all columns
    .Select(u => new { u.Name, u.Email })  // Project in memory
    .ToList();

// ✅ Efficient: Project in server, load only needed columns
var result = await session.Set<User>()
    .Where(u => u.Status == "active")
    .Select(u => new { u.Name, u.Email })  // Project at server
    .ToListAsync();
```

### 3. N+1 Query Problem

```csharp
// ❌ N+1 Problem: One query per order
var users = await session.Set<User>().ToListAsync();
foreach (var user in users)
{
    var orders = await session.Set<Order>()
        .Where(o => o.UserId == user.Id)
        .ToListAsync();  // N additional queries!
}

// ✅ Solution: Include related data
var users = await session.Set<User>()
    .Include(u => u.Orders)
    .ToListAsync();
// Single query with all data

// ✅ Alternative: Join
var usersWithOrders = await session.Set<User>()
    .Join(
        session.Set<Order>(),
        u => u.Id,
        o => o.UserId,
        (u, o) => new { User = u, Order = o }
    )
    .ToListAsync();
```

---

## Testing IQueryable

### Mock IQueryable<T>

```csharp
[Fact]
public async Task GetActiveUsers_ReturnsOnlyActiveUsers()
{
    // Arrange
    var data = new List<User>
    {
        new() { Id = "1", Name = "Alice", Status = "active" },
        new() { Id = "2", Name = "Bob", Status = "inactive" },
        new() { Id = "3", Name = "Charlie", Status = "active" }
    };

    var mockSession = new Mock<ISurrealDbSession>();
    mockSession
        .Setup(s => s.Set<User>("users"))
        .Returns(data.AsQueryable());

    var repository = new UserRepository(mockSession.Object);

    // Act
    var result = await repository.GetActiveUsersAsync();

    // Assert
    Assert.Equal(2, result.Count);
    Assert.All(result, u => Assert.Equal("active", u.Status));
}
```

### Real Query Testing

```csharp
[Collection("Database")]
public class UserQueryTests : IAsyncLifetime
{
    private ISurrealDbSession _session;

    public async Task InitializeAsync()
    {
        var client = new SurrealDbClient("ws://localhost:8000");
        _session = client.CreateSession();
    }

    [Fact]
    public async Task ComplexQuery_ExecutesCorrectly()
    {
        // Arrange - Set up test data
        var users = new[]
        {
            new User { Id = "1", Name = "Alice", Age = 25, Status = "active" },
            new User { Id = "2", Name = "Bob", Age = 17, Status = "active" },
            new User { Id = "3", Name = "Charlie", Age = 30, Status = "inactive" }
        };

        foreach (var user in users)
            _session.Add(user);

        await _session.SaveChangesAsync();

        // Act
        var results = await _session.Set<User>()
            .Where(u => u.Age >= 18 && u.Status == "active")
            .OrderBy(u => u.Name)
            .ToListAsync();

        // Assert
        Assert.Equal(1, results.Count);
        Assert.Equal("Alice", results[0].Name);
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
    }
}
```

---

## Migration from QueryBuilder

For users familiar with the QueryBuilder API:

```csharp
// Old: QueryBuilder (terminal API)
var results = await client
    .Query()
    .Select<User>()
    .From("users")
    .Where("age >= $age")
    .Parameter("age", 18)
    .ExecuteAsync<User>();

// New: IQueryable (composable API)
var results = await session.Set<User>()
    .Where(u => u.Age >= 18)
    .ToListAsync();

// QueryBuilder still available for complex SurrealQL:
var results = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE age >= $age AND status IN $statuses",
    new { age = 18, statuses = new[] { "active", "pending" } }
);
```

---

## Best Practices

1. **Use IQueryable as primary API** - More composable than QueryBuilder
2. **Defer execution** - Build complete query before calling ToListAsync()
3. **Project early** - Select only needed columns at server
4. **Use Include for relationships** - Avoid N+1 queries
5. **Extract reusable queries** - Use extension methods or repository methods
6. **Test with real database** - Expression translation is complex
7. **Monitor generated queries** - Log SurrealQL for optimization
8. **Cache query plans** - For frequently-used queries
9. **Use ValueComparers** - For complex type filtering
10. **Avoid closure issues** - Use explicit variables for parameters

