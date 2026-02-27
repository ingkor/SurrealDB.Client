# Changelog

All notable changes to SurrealDB.Client are documented here.

## Version History

### [1.0.0] - 2026-03-15 (Planned)

First production release of SurrealDB.Client.

#### New Features

- Complete CRUD API (Create, Read, Update, Delete, Upsert)
- Connection pooling with automatic health checking
- HTTP and WebSocket protocol support with automatic selection
- Flexible authentication (Username/Password and Token-based)
- Comprehensive exception hierarchy for error handling
- Multiple serializer support (System.Text.Json, Newtonsoft.Json, custom)
- Parameterized query support to prevent injection attacks
- Thread-safe concurrent operations
- Async/await-first API design
- Detailed logging and diagnostics

#### Critical Fixes (P0)

**DisposeAsync Deadlock (P0.1)**
- Fixed: Deadlock in ConnectionPool.DisposeAsync when disposing multiple connections concurrently
- Impact: All applications must upgrade to avoid hanging on shutdown
- Details: Semaphore was not properly released when exceptions occurred

**GetStatistics Data Race (P0.2)**
- Fixed: Race condition in ConnectionPool.GetStatistics() on _allConnections field
- Impact: Concurrent calls to GetStatistics could crash monitoring systems
- Details: Added proper locking to prevent concurrent modification

**WebSocket Response Truncation (P0.3)**
- Fixed: WebSocketProtocolAdapter silently truncated responses larger than 4 KB
- Impact: Queries returning large result sets failed silently with partial data
- Details: Refactored message buffering to handle frames >4 KB correctly

#### Foundation Improvements (P0.4-P0.12)

- Namespace and Database validation (required parameters)
- Proper USE NS / USE DB initialization in ConnectAsync
- Response deserialization with SurrealDbResponse<T>
- Mock adapter for unit testing without database
- Performance optimizations for connection pool
- Query parameter validation
- Improved error messages with actionable guidance

#### Requirements

- **.NET:** 8.0 or 9.0
- **SurrealDB:** 3.0 or later
- **Breaking Change:** Namespace and Database are now required parameters

#### Upgrade Notes

Upgrading from earlier beta versions:

1. Review your connection string - ensure Namespace and Database are specified
2. Update any error handling code to use new exception types
3. Test HTTPS connections if migrating from HTTP
4. Verify serializer configuration if using custom serializers

### [0.9.0-beta] - 2026-02-26

Beta release with core infrastructure ready.

#### Features

- Connection pool implementation with lifecycle management
- HTTP protocol adapter with request/response handling
- WebSocket protocol adapter (has known issues - see below)
- Authentication scaffolding (stubs for actual implementation)
- Exception type hierarchy
- Configuration options for connection and pooling

#### Known Issues

- DisposeAsync deadlock (see P0.1 in 1.0.0 fixes)
- GetStatistics race condition (see P0.2 in 1.0.0 fixes)
- WebSocket response truncation >4 KB (see P0.3 in 1.0.0 fixes)
- CRUD operations are stubs (not implemented)
- Session and change tracking not yet implemented

#### Limitations

- SurrealDB < 3.0 not supported
- Real-time subscriptions not yet implemented
- No query composition API (IQueryable support)
- No transaction support
- Limited diagnostic capabilities

---

## Breaking Changes

### 1.0.0: Namespace and Database Parameters

**What Changed**: Namespace and Database are now required parameters in connection options.

**Before**:
```csharp
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();
// Would use default namespace/database
```

**After**:
```csharp
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://localhost:8000",
    Namespace = "my_ns",
    Database = "my_db"
};
var client = new SurrealDbClient(options);
await client.ConnectAsync();
```

**Reason**: Ensures consistent behavior and prevents accidental use of wrong database.

**Migration Path**: Add Namespace and Database to your connection options.

### 1.0.0: Exception Types Changed

**What Changed**: More specific exception types for different error scenarios.

**Before**:
```csharp
catch (Exception ex)
{
    // Generic exception handling
}
```

**After**:
```csharp
catch (AuthenticationException ex) { /* Handle auth errors */ }
catch (QueryException ex) { /* Handle query errors */ }
catch (SerializationException ex) { /* Handle deserialization errors */ }
catch (SurrealDbException ex) { /* Handle DB errors */ }
```

**Migration Path**: Update exception handling to use specific exception types.

---

## Security Updates

### P0 Bugs (Critical - Upgrade Immediately)

All P0 bugs are security-related or cause data loss:

1. **DisposeAsync Deadlock** - Can cause application hangs on shutdown
2. **GetStatistics Race Condition** - Can cause monitoring system crashes
3. **WebSocket Truncation** - Can cause silent data loss

**When to Upgrade**: Before deploying to production.

### Future Security Advisories

Check releases page for security advisories:
https://github.com/your-org/SurrealDB.Client/releases

---

## Upgrade Guide

### From 0.9.0-beta to 1.0.0

**Step 1: Update Package**
```bash
dotnet add package SurrealDB.Client --version "1.0.0"
```

**Step 2: Update Connection Options**
```csharp
// Add Namespace and Database
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://localhost:8000",
    Namespace = "my_namespace",
    Database = "my_database"
};

var client = new SurrealDbClient(options);
```

**Step 3: Update Error Handling**
```csharp
try
{
    await client.QueryAsync<User>("SELECT * FROM users");
}
catch (QueryException ex)
{
    Console.WriteLine($"Query failed: {ex.Message}");
}
catch (SurrealDbException ex)
{
    Console.WriteLine($"Database error: {ex.Message}");
}
```

**Step 4: Test**
```bash
dotnet test
```

**Step 5: Deploy**
```bash
dotnet publish -c Release
```

---

## Detailed Changes by Category

### Connection & Authentication

- ConnectionPool.DisposeAsync deadlock fixed
- ConnectionPool.GetStatistics race condition fixed
- Namespace and Database validation added
- USE NS / USE DB initialization in ConnectAsync
- Token-based authentication improved
- Connection health checking enhanced

### Protocol Support

- WebSocketProtocolAdapter truncation bug fixed
- HTTP protocol adapter refined
- Automatic protocol selection optimized
- Protocol fallback improved

### Query Execution

- QueryAsync parameter validation
- ExecuteAsync for non-query statements
- Parameterized query support
- SurrealDbResponse<T> type improved

### Exception Handling

- More specific exception types
- Better error messages with context
- Exception serialization improved

### Testing & Diagnostics

- Mock adapter for unit testing
- Connection statistics improved
- Logging enhancements
- Diagnostic output improved

---

## Release Notes by Version

### 1.0.0 (2026-03-15)

**What's New**
- First production release
- All P0 bugs fixed
- Full CRUD API
- Complete protocol support

**What's Fixed**
- DisposeAsync deadlock
- GetStatistics race condition
- WebSocket truncation

**Known Limitations**
- Real-time subscriptions in development
- Query composition (IQueryable) in development
- Transactions in development

**Requirements**
- .NET 8.0 or 9.0
- SurrealDB 3.0+

### 0.9.0-beta (2026-02-26)

**What's Ready**
- Infrastructure complete
- Protocol adapters ready
- Connection pooling ready

**What's Missing**
- CRUD operations (stubs)
- Bug fixes (see P0.1-P0.3)
- Session management
- Query composition

**Known Issues**
- Critical bugs (see P0.1-P0.3)
- Incomplete CRUD API
- No real-time features

---

## When to Upgrade

### 1.0.0 (Immediate)

Upgrade immediately if you:
- Are on any beta version
- Need CRUD operations
- Are deploying to production
- Want bug fixes for critical issues

### Future Releases

Monitor for:
- Security advisories
- Performance improvements
- New features (real-time, IQueryable, transactions)
- Bug fixes

---

## Support

For issues or questions about upgrading:

1. **Check Documentation**: [GETTING_STARTED.md](GETTING_STARTED.md), [API_REFERENCE.md](API_REFERENCE.md)
2. **Review Examples**: [EXAMPLES.md](EXAMPLES.md)
3. **Report Issues**: GitHub Issues
4. **Contact Maintainers**: GitHub Discussions

---

## Related Documentation

- [Getting Started](GETTING_STARTED.md) - Installation and first steps
- [API Reference](API_REFERENCE.md) - Complete API documentation
- [Examples](EXAMPLES.md) - Code samples
- [Security](SECURITY.md) - Security best practices
