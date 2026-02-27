# Getting Started with SurrealDB.Client

This guide walks you through installation, connecting to SurrealDB, and running your first queries.

## Prerequisites

- .NET 8.0 or 9.0 SDK installed
- Access to a running SurrealDB instance (version 3.0 or later)
- Basic C# and async/await knowledge

## Installation

### Step 1: Create a Project

```bash
dotnet new console -n SurrealDbExample
cd SurrealDbExample
```

### Step 2: Add NuGet Package

```bash
dotnet add package SurrealDB.Client
```

### Step 3: Verify Installation

Check that the package appears in your `.csproj` file:

```xml
<ItemGroup>
    <PackageReference Include="SurrealDB.Client" Version="*" />
</ItemGroup>
```

## Basic Connection

### Step 1: Create the Client

```csharp
using SurrealDB.Client;

// Create client pointing to SurrealDB instance
var client = new SurrealDbClient("surreal://localhost:8000");
```

### Step 2: Connect

```csharp
try
{
    await client.ConnectAsync();
    Console.WriteLine("Connected to SurrealDB");
}
catch (ConnectionException ex)
{
    Console.WriteLine($"Failed to connect: {ex.Message}");
}
```

### Step 3: Authenticate

```csharp
try
{
    // Authenticate with username and password
    await client.AuthenticateAsync(
        new UsernamePasswordAuth("user", "password")
    );
    Console.WriteLine("Authenticated");
}
catch (AuthenticationException ex)
{
    Console.WriteLine($"Authentication failed: {ex.Message}");
}
```

### Step 4: Disconnect

```csharp
await client.DisconnectAsync();
Console.WriteLine("Disconnected");
```

## Complete Basic Example

```csharp
using SurrealDB.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var client = new SurrealDbClient("surreal://localhost:8000");

        try
        {
            // Connect
            await client.ConnectAsync();
            Console.WriteLine("Connected");

            // Authenticate
            await client.AuthenticateAsync(
                new UsernamePasswordAuth("user", "password")
            );
            Console.WriteLine("Authenticated");

            // Execute a query
            var result = await client.QueryAsync<User>(
                "SELECT * FROM users LIMIT 1"
            );
            Console.WriteLine($"Query returned: {result?.Count} records");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            await client.DisconnectAsync();
            Console.WriteLine("Disconnected");
        }
    }
}

class User
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

## Connection Options

### Connection String Format

```
surreal://[user:password@]host:port[?options]
```

### Examples

```csharp
// Local instance, no auth
var client1 = new SurrealDbClient("surreal://localhost:8000");

// With credentials
var client2 = new SurrealDbClient("surreal://user:pass@localhost:8000");

// Remote instance
var client3 = new SurrealDbClient("surreal://user:pass@db.example.com:443");
```

### Advanced Options

Use `SurrealDbClientOptions` for detailed configuration:

```csharp
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://localhost:8000",
    PoolSize = 10,                    // Connection pool size
    Timeout = TimeSpan.FromSeconds(30),
    Protocol = "http",               // "http" or "ws"
};

var client = new SurrealDbClient(options);
```

## Your First Query

### Define a Model

```csharp
class Product
{
    public string Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}
```

### Execute a Simple Query

```csharp
// Get all products
var products = await client.QueryAsync<Product>(
    "SELECT * FROM products"
);

foreach (var product in products ?? new List<Product>())
{
    Console.WriteLine($"{product.Name}: ${product.Price}");
}
```

### Query with Parameters

```csharp
// Get products over a certain price
var minPrice = 100;
var products = await client.QueryAsync<Product>(
    "SELECT * FROM products WHERE price > $minPrice",
    new { minPrice = minPrice }
);
```

### Query with Filtering and Sorting

```csharp
var products = await client.QueryAsync<Product>(
    "SELECT * FROM products WHERE available = true ORDER BY price ASC LIMIT 10"
);
```

## Common Patterns

### Using Statement (Recommended)

The client implements `IAsyncDisposable`, so use `await using`:

```csharp
await using var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();
// ... use client ...
// Automatically disconnects when scope ends
```

### Simple Try-Catch

```csharp
try
{
    var result = await client.QueryAsync<User>("...");
}
catch (QueryException ex)
{
    Console.WriteLine($"Query error: {ex.Message}");
}
catch (SurrealDbException ex)
{
    Console.WriteLine($"Database error: {ex.Message}");
}
```

### Connection Pooling

The client automatically manages a connection pool. No special configuration needed for most applications:

```csharp
var client = new SurrealDbClient("surreal://localhost:8000");
// Pool is automatically created on first ConnectAsync()
// Pool is automatically cleaned up on DisconnectAsync()
```

## Troubleshooting

### Connection Refused

**Error**: "Unable to connect to SurrealDB"

**Solution**:
1. Verify SurrealDB is running: `surrealdb --version`
2. Check connection string host/port
3. Verify network connectivity

```bash
# Start SurrealDB locally
surreal start --user root --pass root
```

### Authentication Failed

**Error**: "Authentication failed"

**Solution**:
1. Verify username and password are correct
2. Ensure user exists in SurrealDB
3. Check user permissions

```surrealql
-- In SurrealDB console, create a test user
DEFINE USER user ON ROOT PASSWORD 'password' ROLES OWNER;
```

### Query Returns Empty

**Error**: No results from query

**Solution**:
1. Verify namespace and database exist
2. Check table exists: `SELECT * FROM sys.db.tables`
3. Check data exists: `SELECT COUNT(*) FROM your_table`

### Timeout

**Error**: "Operation timed out"

**Solution**:
1. Increase timeout: `options.Timeout = TimeSpan.FromSeconds(60)`
2. Check SurrealDB performance
3. Verify network latency

## Next Steps

1. **Explore Examples**: See [EXAMPLES.md](EXAMPLES.md) for common patterns
2. **Read API Reference**: Check [API_REFERENCE.md](API_REFERENCE.md) for complete API
3. **Security Best Practices**: Review [SECURITY.md](SECURITY.md)
4. **SurrealDB Docs**: Visit [surrealdb.com/docs](https://surrealdb.com/docs)

## Common Tasks

### Check if Connected

```csharp
if (client.IsConnected)
{
    Console.WriteLine("Client is connected");
}
```

### Handle Connection Errors

```csharp
try
{
    await client.ConnectAsync();
}
catch (ConnectionException ex)
{
    Console.WriteLine($"Connection error: {ex.Message}");
    // Handle error
}
```

### Clean Resource Cleanup

```csharp
try
{
    var client = new SurrealDbClient("surreal://localhost:8000");
    await client.ConnectAsync();
    // ... use client ...
}
finally
{
    await client.DisconnectAsync();
}
```

## Tips

- Always use async/await patterns for database operations
- Use connection strings for sensitive data (not hardcoded credentials)
- Implement retry logic for network failures
- Use parameterized queries to prevent injection attacks
- Configure connection pool size based on application load

## Getting Help

- Check [EXAMPLES.md](EXAMPLES.md) for code samples
- Review [API_REFERENCE.md](API_REFERENCE.md) for complete documentation
- Visit [surrealdb.com/docs](https://surrealdb.com/docs) for SurrealDB-specific questions
- Report issues on GitHub
