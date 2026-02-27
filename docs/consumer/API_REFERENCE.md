# API Reference

Complete reference documentation for SurrealDB.Client API.

## Table of Contents

1. [SurrealDbClient](#surrealdbclient)
2. [Connection Options](#connection-options)
3. [Authentication](#authentication)
4. [Query Methods](#query-methods)
5. [Exception Hierarchy](#exception-hierarchy)
6. [Thread Safety](#thread-safety)
7. [Version Requirements](#version-requirements)

## SurrealDbClient

The main entry point for connecting to and querying SurrealDB.

### Class Definition

```csharp
public class SurrealDbClient : ISurrealDbClient
{
    public SurrealDbClient(SurrealDbClientOptions options, ISerializer? serializer = null);
    public SurrealDbClient(string connectionString);

    public SurrealDbClientOptions Options { get; }
    public bool IsConnected { get; }
}
```

### Connection Methods

#### ConnectAsync

```csharp
public Task ConnectAsync(CancellationToken cancellationToken = default)
```

Establishes a connection to the SurrealDB instance.

**Parameters**:
- `cancellationToken` - Cancellation token for async operation

**Exceptions**:
- `ConnectionException` - Failed to establish connection
- `InvalidOperationException` - Already connected

**Example**:
```csharp
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();
```

#### DisconnectAsync

```csharp
public Task DisconnectAsync()
```

Closes the connection to SurrealDB and releases resources.

**Exceptions**:
- `InvalidOperationException` - Not connected

**Example**:
```csharp
await client.DisconnectAsync();
```

### Authentication Methods

#### AuthenticateAsync

```csharp
public Task AuthenticateAsync(
    IAuthenticationProvider auth,
    CancellationToken cancellationToken = default)
```

Authenticates with the SurrealDB instance.

**Parameters**:
- `auth` - Authentication provider (UsernamePasswordAuth or TokenAuth)
- `cancellationToken` - Cancellation token for async operation

**Exceptions**:
- `AuthenticationException` - Authentication failed
- `InvalidOperationException` - Not connected

**Example**:
```csharp
await client.AuthenticateAsync(
    new UsernamePasswordAuth("user", "password")
);
```

#### LogoutAsync

```csharp
public Task LogoutAsync()
```

Logs out from the current session.

**Exceptions**:
- `InvalidOperationException` - Not authenticated

**Example**:
```csharp
await client.LogoutAsync();
```

### Query Methods

#### QueryAsync

```csharp
public Task<List<T>> QueryAsync<T>(
    string query,
    object? parameters = null,
    CancellationToken cancellationToken = default)
```

Executes a SurrealQL query and returns typed results.

**Generic Parameters**:
- `T` - The type to deserialize results into

**Parameters**:
- `query` - SurrealQL query string
- `parameters` - Query parameters (optional)
- `cancellationToken` - Cancellation token

**Returns**: List of deserialized objects, or null if no results

**Exceptions**:
- `QueryException` - Invalid query or execution error
- `SerializationException` - Failed to deserialize results
- `InvalidOperationException` - Not connected

**Examples**:
```csharp
// Simple query
var users = await client.QueryAsync<User>(
    "SELECT * FROM users"
);

// With parameters
var users = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE age >= $minAge ORDER BY age",
    new { minAge = 18 }
);

// With cancellation
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var users = await client.QueryAsync<User>("SELECT * FROM users", null, cts.Token);
```

#### ExecuteAsync

```csharp
public Task ExecuteAsync(
    string query,
    object? parameters = null,
    CancellationToken cancellationToken = default)
```

Executes a query without returning results (for INSERT, UPDATE, DELETE).

**Parameters**:
- `query` - SurrealQL statement
- `parameters` - Query parameters (optional)
- `cancellationToken` - Cancellation token

**Exceptions**:
- `QueryException` - Invalid query or execution error
- `InvalidOperationException` - Not connected

**Example**:
```csharp
await client.ExecuteAsync(
    "DELETE FROM users WHERE status = 'inactive'"
);
```

## Connection Options

Configure client behavior via `SurrealDbClientOptions`.

```csharp
public class SurrealDbClientOptions
{
    public string ConnectionString { get; set; }
    public int PoolSize { get; set; } = 10;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public string Protocol { get; set; } = "http";
    public ISerializer? Serializer { get; set; }
}
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | string | Required | Connection string in format `surreal://[user:pass@]host:port` |
| `PoolSize` | int | 10 | Number of connections to maintain in pool |
| `Timeout` | TimeSpan | 30s | Default timeout for operations |
| `Protocol` | string | "http" | Protocol to use: "http" or "ws" |
| `Serializer` | ISerializer | System.Text.Json | Custom serializer implementation |

### Example

```csharp
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://user:pass@localhost:8000",
    PoolSize = 20,
    Timeout = TimeSpan.FromSeconds(60),
    Protocol = "http"
};

var client = new SurrealDbClient(options);
```

## Authentication

### IAuthenticationProvider

```csharp
public interface IAuthenticationProvider
{
    Task AuthenticateAsync(
        ISurrealDbClient client,
        CancellationToken cancellationToken = default);
}
```

### UsernamePasswordAuth

```csharp
public class UsernamePasswordAuth : IAuthenticationProvider
{
    public string Username { get; set; }
    public string Password { get; set; }
}
```

**Example**:
```csharp
var auth = new UsernamePasswordAuth
{
    Username = "user",
    Password = "password"
};

await client.AuthenticateAsync(auth);
```

### TokenAuth

```csharp
public class TokenAuth : IAuthenticationProvider
{
    public string Token { get; set; }
}
```

**Example**:
```csharp
var auth = new TokenAuth
{
    Token = "eyJ0eXAiOiJKV1QiLCJhbGc..."
};

await client.AuthenticateAsync(auth);
```

## Query Methods

### Common SurrealQL Patterns

#### SELECT

```csharp
// Get all records
var users = await client.QueryAsync<User>("SELECT * FROM users");

// Get with filtering
var users = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE status = 'active'"
);

// Get with sorting and limit
var users = await client.QueryAsync<User>(
    "SELECT * FROM users ORDER BY created_at DESC LIMIT 10"
);

// Get specific fields
var users = await client.QueryAsync<UserSummary>(
    "SELECT id, name, email FROM users"
);
```

#### CREATE

```csharp
var user = await client.QueryAsync<User>(
    "CREATE users CONTENT $data RETURN *",
    new { data = new { name = "John", email = "john@example.com" } }
);
```

#### UPDATE

```csharp
await client.ExecuteAsync(
    "UPDATE users:john SET status = 'inactive'"
);
```

#### DELETE

```csharp
await client.ExecuteAsync("DELETE FROM users WHERE status = 'inactive'");
```

#### UPSERT

```csharp
await client.ExecuteAsync(
    "UPSERT users:john CONTENT $data",
    new { data = new { name = "John", email = "john@example.com" } }
);
```

## Exception Hierarchy

All exceptions inherit from `SurrealDbException` in the `SurrealDB.Client.Exceptions` namespace.

```
SurrealDbException (base class)
├── ConnectionException
│   └── Timeout during connection operations
├── AuthenticationException
│   └── Authentication failed
├── QueryException
│   └── Invalid query or execution error
├── SerializationException
│   └── Failed to serialize/deserialize
└── ValidationException
    └── Invalid input parameters
```

### Exception Handling

```csharp
try
{
    var result = await client.QueryAsync<User>("SELECT * FROM users");
}
catch (ConnectionException ex)
{
    Console.WriteLine($"Connection error: {ex.Message}");
    // Handle connection issues
}
catch (AuthenticationException ex)
{
    Console.WriteLine($"Auth error: {ex.Message}");
    // Handle authentication issues
}
catch (QueryException ex)
{
    Console.WriteLine($"Query error: {ex.Message}");
    // Handle query execution errors
}
catch (SerializationException ex)
{
    Console.WriteLine($"Serialization error: {ex.Message}");
    // Handle deserialization errors
}
catch (SurrealDbException ex)
{
    Console.WriteLine($"Database error: {ex.Message}");
    // Handle unexpected errors
}
```

## Thread Safety

### Client Thread Safety

The `SurrealDbClient` is **thread-safe** for concurrent operations:

- Multiple threads can call query methods concurrently
- Connection pooling is thread-safe
- Authentication state is thread-safe

**Safe pattern**:
```csharp
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();

// Safe: Multiple threads can query concurrently
var tasks = Enumerable.Range(1, 10)
    .Select(_ => client.QueryAsync<User>("SELECT * FROM users"))
    .ToList();

await Task.WhenAll(tasks);
```

**Unsafe pattern**:
```csharp
// AVOID: Creating new client per thread
var tasks = Enumerable.Range(1, 10)
    .Select(async _ =>
    {
        var newClient = new SurrealDbClient("surreal://localhost:8000");
        await newClient.ConnectAsync();
        return await newClient.QueryAsync<User>("SELECT * FROM users");
    })
    .ToList();
```

### Connection Pool Thread Safety

The connection pool is fully thread-safe:
- Handles concurrent acquisition/release
- Automatic health checking
- No locks required in user code

## Version Requirements

### Client Library

- **.NET**: 8.0 or 9.0 (or later)
- **C#**: 10.0 or later

### SurrealDB Server

- **Required**: 3.0 or later
- **Not supported**: SurrealDB versions before 3.0

Attempting to connect to SurrealDB < 3.0 will result in a `ConnectionException`.

### NuGet Package

Latest version recommended. Check [releases](https://www.nuget.org/packages/SurrealDB.Client) for updates.

## Protocol Support

### Supported Protocols

- **HTTP**: For general queries and operations
- **WebSocket**: For real-time features (future releases)

### Protocol Selection

The client automatically selects the appropriate protocol based on:
1. Connection string scheme (surreal:// uses automatic selection)
2. Operation type (subscriptions prefer WebSocket)
3. Configuration options

### Performance Considerations

- **HTTP**: Stateless, good for request/response patterns, lower latency per request
- **WebSocket**: Connection-oriented, good for subscriptions, persistent connections

## Dependency Injection

### ASP.NET Core Integration

```csharp
// Startup.cs or Program.cs
services.AddScoped<ISurrealDbClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["Database:ConnectionString"];
    return new SurrealDbClient(connectionString);
});

// In controller or service
public class UserService
{
    private readonly ISurrealDbClient _client;

    public UserService(ISurrealDbClient client)
    {
        _client = client;
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await _client.QueryAsync<User>("SELECT * FROM users");
    }
}
```

## Resource Cleanup

### Proper Disposal

Always disconnect when finished:

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

### Using Statement (Recommended)

```csharp
await using var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();
// ... use client ...
// Automatically disconnects
```

## Related Documentation

- [Getting Started](GETTING_STARTED.md) - Installation and first steps
- [Examples](EXAMPLES.md) - Real-world code samples
- [Security](SECURITY.md) - Security best practices
- [SurrealDB Docs](https://surrealdb.com/docs) - SurrealQL reference
