# Examples

Real-world code examples and common patterns for SurrealDB.Client.

## Table of Contents

1. [Connection Patterns](#connection-patterns)
2. [Query Patterns](#query-patterns)
3. [Error Handling](#error-handling)
4. [Resource Management](#resource-management)
5. [Best Practices](#best-practices)

## Connection Patterns

### Basic Connection

```csharp
using SurrealDB.Client;

var client = new SurrealDbClient("surreal://localhost:8000");
try
{
    await client.ConnectAsync();
    Console.WriteLine("Connected");
}
finally
{
    await client.DisconnectAsync();
}
```

### Connection with Authentication

```csharp
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();

// Username and password auth
await client.AuthenticateAsync(
    new UsernamePasswordAuth { Username = "user", Password = "password" }
);

Console.WriteLine("Authenticated");
```

### Connection with Token

```csharp
var token = "eyJ0eXAiOiJKV1QiLCJhbGc...";
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();

await client.AuthenticateAsync(new TokenAuth { Token = token });
```

### Connection with Custom Options

```csharp
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://user:pass@localhost:8000",
    PoolSize = 20,
    Timeout = TimeSpan.FromSeconds(60),
    Protocol = "http"
};

var client = new SurrealDbClient(options);
await client.ConnectAsync();
```

### Connection with Timeout

```csharp
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://localhost:8000",
    Timeout = TimeSpan.FromSeconds(10)
};

var client = new SurrealDbClient(options);

try
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await client.ConnectAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Connection timeout");
}
```

## Query Patterns

### Simple SELECT

```csharp
class User
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
}

// Get all users
var users = await client.QueryAsync<User>("SELECT * FROM users");

foreach (var user in users ?? new List<User>())
{
    Console.WriteLine($"{user.Name} ({user.Email})");
}
```

### SELECT with Filtering

```csharp
// Get active users over 18
var adults = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE age >= $minAge AND status = $status",
    new { minAge = 18, status = "active" }
);
```

### SELECT with Sorting

```csharp
// Get top 10 youngest users
var youngest = await client.QueryAsync<User>(
    "SELECT * FROM users ORDER BY age ASC LIMIT 10"
);

// Get top 10 newest users
var newest = await client.QueryAsync<User>(
    "SELECT * FROM users ORDER BY created_at DESC LIMIT 10"
);
```

### SELECT with Aggregation

```csharp
class UserStats
{
    public int Count { get; set; }
    public double AverageAge { get; set; }
    public int MaxAge { get; set; }
    public int MinAge { get; set; }
}

var stats = await client.QueryAsync<UserStats>(
    "SELECT COUNT() as count, AVG(age) as average_age, MAX(age) as max_age, MIN(age) as min_age FROM users"
);
```

### SELECT with JOIN

```csharp
class UserWithOrders
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<Order> Orders { get; set; }
}

class Order
{
    public string Id { get; set; }
    public decimal Amount { get; set; }
}

var usersWithOrders = await client.QueryAsync<UserWithOrders>(
    @"SELECT *, ->purchased->order as orders FROM users"
);
```

### CREATE New Record

```csharp
class Product
{
    public string Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string Category { get; set; }
}

// Create with auto-generated ID
var product = await client.QueryAsync<Product>(
    "CREATE products CONTENT $data RETURN *",
    new { data = new { name = "Laptop", price = 999.99, category = "Electronics" } }
);

Console.WriteLine($"Created product: {product?.FirstOrDefault()?.Id}");
```

### CREATE with Specific ID

```csharp
var product = await client.QueryAsync<Product>(
    "CREATE products:p123 CONTENT $data RETURN *",
    new { data = new { name = "Mouse", price = 29.99, category = "Electronics" } }
);
```

### UPDATE Record

```csharp
// Update specific fields
await client.ExecuteAsync(
    "UPDATE users:u1 SET name = $name, email = $email",
    new { name = "John Updated", email = "john.new@example.com" }
);

// Update with timestamp
await client.ExecuteAsync(
    "UPDATE users:u1 SET status = 'active', updated_at = time::now()"
);

// Update all matching records
await client.ExecuteAsync(
    "UPDATE users SET status = 'inactive' WHERE last_login < time::now() - 30d"
);
```

### DELETE Record

```csharp
// Delete specific record
await client.ExecuteAsync("DELETE FROM users WHERE id = 'u1'");

// Delete all matching
await client.ExecuteAsync("DELETE FROM users WHERE status = 'inactive'");

// Delete all
await client.ExecuteAsync("DELETE FROM users");
```

### UPSERT (Insert or Update)

```csharp
// Create or update
var result = await client.QueryAsync<Product>(
    "UPSERT products:p123 CONTENT $data RETURN *",
    new { data = new { name = "Keyboard", price = 79.99 } }
);
```

### Batch Operations

```csharp
// Insert multiple records at once
var products = new[]
{
    new { name = "Product 1", price = 100 },
    new { name = "Product 2", price = 200 },
    new { name = "Product 3", price = 300 }
};

foreach (var p in products)
{
    await client.ExecuteAsync(
        "CREATE products CONTENT $data",
        new { data = p }
    );
}
```

## Error Handling

### Basic Error Handling

```csharp
try
{
    var users = await client.QueryAsync<User>("SELECT * FROM users");
}
catch (QueryException ex)
{
    Console.WriteLine($"Query failed: {ex.Message}");
    // Handle query error
}
catch (SerializationException ex)
{
    Console.WriteLine($"Deserialization failed: {ex.Message}");
    // Handle deserialization error
}
catch (SurrealDbException ex)
{
    Console.WriteLine($"Database error: {ex.Message}");
    // Handle unexpected error
}
```

### Connection Error Handling

```csharp
try
{
    await client.ConnectAsync();
}
catch (ConnectionException ex)
{
    Console.WriteLine($"Failed to connect: {ex.Message}");
    // Implement retry logic or fallback
}
```

### Authentication Error Handling

```csharp
try
{
    await client.AuthenticateAsync(
        new UsernamePasswordAuth { Username = "user", Password = "password" }
    );
}
catch (AuthenticationException ex)
{
    Console.WriteLine($"Authentication failed: {ex.Message}");
    // Handle auth failure (invalid credentials, etc.)
}
```

### Timeout Handling

```csharp
try
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var users = await client.QueryAsync<User>(
        "SELECT * FROM users",
        null,
        cts.Token
    );
}
catch (OperationCanceledException)
{
    Console.WriteLine("Query timeout");
    // Handle timeout
}
```

### Retry Logic

```csharp
async Task<T> QueryWithRetry<T>(
    ISurrealDbClient client,
    string query,
    object parameters,
    int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await client.QueryAsync<T>(query, parameters) ?? new();
        }
        catch (ConnectionException ex) when (attempt < maxRetries)
        {
            Console.WriteLine($"Attempt {attempt} failed, retrying...");
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
        }
    }

    throw new InvalidOperationException("Max retries exceeded");
}

// Usage
var users = await QueryWithRetry<User>(client, "SELECT * FROM users", null);
```

## Resource Management

### Using Statement (Recommended)

```csharp
await using var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();

var users = await client.QueryAsync<User>("SELECT * FROM users");

// Automatically disconnects when leaving scope
```

### Try-Finally Pattern

```csharp
var client = new SurrealDbClient("surreal://localhost:8000");
try
{
    await client.ConnectAsync();
    var users = await client.QueryAsync<User>("SELECT * FROM users");
}
finally
{
    await client.DisconnectAsync();
}
```

### Async Lifetime Management

```csharp
public class DatabaseService : IAsyncDisposable
{
    private readonly ISurrealDbClient _client;

    public DatabaseService(ISurrealDbClient client)
    {
        _client = client;
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await _client.QueryAsync<User>("SELECT * FROM users");
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisconnectAsync();
    }
}

// Usage
await using var service = new DatabaseService(client);
var users = await service.GetUsersAsync();
```

## Best Practices

### 1. Single Client Instance

```csharp
// Good: Reuse single client
public class UserRepository
{
    private readonly ISurrealDbClient _client;

    public UserRepository(ISurrealDbClient client)
    {
        _client = client; // Injected, shared
    }

    public async Task<List<User>> GetAllAsync() =>
        await _client.QueryAsync<User>("SELECT * FROM users");

    public async Task<User> GetByIdAsync(string id) =>
        (await _client.QueryAsync<User>(
            "SELECT * FROM users WHERE id = $id",
            new { id }
        ))?.FirstOrDefault();
}
```

### 2. Parameterized Queries

```csharp
// Good: Parameterized (prevents injection)
var users = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE age >= $minAge AND status = $status",
    new { minAge = 18, status = "active" }
);

// Avoid: String concatenation (vulnerable)
var query = $"SELECT * FROM users WHERE age >= {age} AND status = '{status}'";
```

### 3. Async/Await Patterns

```csharp
// Good: Async all the way
async Task<List<User>> GetActiveUsersAsync() =>
    await client.QueryAsync<User>(
        "SELECT * FROM users WHERE status = 'active'"
    );

// Call with await
var activeUsers = await GetActiveUsersAsync();

// Avoid: Blocking
var users = client.QueryAsync<User>("...").Result; // Don't do this
```

### 4. Connection Configuration

```csharp
// Good: Configuration from appsettings
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISurrealDbClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var options = new SurrealDbClientOptions
            {
                ConnectionString = config["Database:ConnectionString"],
                PoolSize = int.Parse(config["Database:PoolSize"] ?? "10"),
                Timeout = TimeSpan.FromSeconds(
                    int.Parse(config["Database:TimeoutSeconds"] ?? "30")
                )
            };
            return new SurrealDbClient(options);
        });
    }
}

// appsettings.json
{
    "Database": {
        "ConnectionString": "surreal://user:pass@localhost:8000",
        "PoolSize": 20,
        "TimeoutSeconds": 60
    }
}
```

### 5. Proper Type Models

```csharp
// Good: Properly typed models
public class User
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Use strongly typed results
var users = await client.QueryAsync<User>("SELECT * FROM users");
foreach (var user in users)
{
    Console.WriteLine($"{user.Name} - {user.Email}");
}
```

### 6. Error Logging

```csharp
using Microsoft.Extensions.Logging;

public class UserService
{
    private readonly ISurrealDbClient _client;
    private readonly ILogger<UserService> _logger;

    public async Task<List<User>> GetUsersAsync()
    {
        try
        {
            return await _client.QueryAsync<User>("SELECT * FROM users");
        }
        catch (QueryException ex)
        {
            _logger.LogError(ex, "Failed to fetch users");
            throw;
        }
    }
}
```

### 7. Null Safety

```csharp
// Handle null results
var users = await client.QueryAsync<User>("SELECT * FROM users");

if (users?.Any() == true)
{
    foreach (var user in users)
    {
        Console.WriteLine(user.Name);
    }
}
else
{
    Console.WriteLine("No users found");
}
```

## Related Documentation

- [API Reference](API_REFERENCE.md) - Complete API documentation
- [Getting Started](GETTING_STARTED.md) - Installation and first steps
- [Security](SECURITY.md) - Security best practices
- [SurrealDB Docs](https://surrealdb.com/docs) - SurrealQL reference
