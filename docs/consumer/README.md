# SurrealDB.Client

A modern, production-grade .NET/C# client library for [SurrealDB](https://surrealdb.com/) with built-in connection pooling, typed exception handling, and flexible protocol support (HTTP and WebSocket).

[![NuGet](https://img.shields.io/nuget/v/SurrealDB.Client.svg)](https://www.nuget.org/packages/SurrealDB.Client)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](../../LICENSE)

## What is SurrealDB?

SurrealDB is a modern multi-model database for real-time data with SQL-style query language, supporting document, graph, and time-series data in one unified platform. This client provides a robust .NET interface for connecting to SurrealDB servers.

## Key Features

- **Flexible Protocol Support**: Choose HTTP or WebSocket at runtime
- **Connection Pooling**: Automatic connection management with health checking
- **Async/Await First**: Built for modern async .NET applications
- **Typed Exceptions**: Structured error hierarchy for better error handling
- **Multiple Serializers**: System.Text.Json, Newtonsoft.Json, or custom implementations
- **Production Ready**: Thread-safe with comprehensive error handling
- **SurrealDB 3.0+**: Full support for latest SurrealDB features

## System Requirements

- **.NET**: 8.0 or 9.0+
- **SurrealDB**: 3.0 or later
- **Connection**: Network access to SurrealDB instance (HTTP or WebSocket)

## Quick Installation

Install via NuGet:

```bash
dotnet add package SurrealDB.Client
```

Or via Package Manager:

```powershell
Install-Package SurrealDB.Client
```

## Quick Start

```csharp
using SurrealDB.Client;

// Create and connect client
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();

// Authenticate
await client.AuthenticateAsync(
    new UsernamePasswordAuth("user", "password")
);

// Execute raw query
var results = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE age >= $age",
    new { age = 18 }
);

// Cleanup
await client.DisconnectAsync();
```

## Getting Started

For installation, basic connection, and first queries:
- **[GETTING_STARTED.md](GETTING_STARTED.md)** - Step-by-step setup guide

## API Reference

For complete API documentation and options:
- **[API_REFERENCE.md](API_REFERENCE.md)** - Client API, connection options, error types

## Real-World Examples

For practical code examples and patterns:
- **[EXAMPLES.md](EXAMPLES.md)** - Connection patterns, queries, error handling, best practices

## Security

For security best practices and configuration:
- **[SECURITY.md](SECURITY.md)** - Secure connections, credential handling, security updates

## Version History

For release notes and breaking changes:
- **[CHANGELOG.md](CHANGELOG.md)** - Version history and upgrade guidance

## Basic Usage Pattern

The typical workflow is:

1. **Create client** with connection string
2. **Connect** to SurrealDB instance
3. **Authenticate** with credentials or token
4. **Execute queries** for your data
5. **Disconnect** when done

See [GETTING_STARTED.md](GETTING_STARTED.md) for a complete walkthrough.

## Connection Strings

Format: `surreal://[user:password@]host:port[?options]`

Examples:

```
surreal://localhost:8000
surreal://user:pass@localhost:8000
surreal://user:pass@surrealdb.example.com:443
```

## Protocol Selection

The client automatically selects the appropriate protocol:

- **HTTP**: For request/response queries
- **WebSocket**: For long-lived subscriptions and real-time features

## Error Handling

All database operations throw typed exceptions from the `SurrealDB.Client.Exceptions` namespace. Examples:

```csharp
try
{
    await client.QueryAsync<User>("...");
}
catch (AuthenticationException ex)
{
    Console.WriteLine($"Auth failed: {ex.Message}");
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

See [API_REFERENCE.md](API_REFERENCE.md) for complete exception reference.

## Dependency Injection

For ASP.NET Core applications:

```csharp
services.AddScoped<ISurrealDbClient>(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>()
        ["Database:ConnectionString"];
    return new SurrealDbClient(connectionString);
});
```

## Support and Resources

- **Official SurrealDB**: [surrealdb.com](https://surrealdb.com)
- **SurrealDB Docs**: [surrealdb.com/docs](https://surrealdb.com/docs)
- **Community**: [discord.gg/surrealdb](https://discord.gg/surrealdb)
- **Issues**: [GitHub Issues](https://github.com/your-org/SurrealDB.Client/issues)

## License

MIT License - see [LICENSE](../../LICENSE) for details

## Contributing

Contributions are welcome! Please see the main repository for guidelines.

---

**New to SurrealDB?** Start with [GETTING_STARTED.md](GETTING_STARTED.md)

**Looking for examples?** See [EXAMPLES.md](EXAMPLES.md)

**Need API details?** Check [API_REFERENCE.md](API_REFERENCE.md)
