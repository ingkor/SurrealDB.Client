# B-Grade Baseline: MVP Implementation Specification

> Detailed specification for minimum viable product (B-Grade) implementation - 4-6 week timeline.

## Phase 1: B-Grade Foundation (Weeks 1-6)

### Week 1: Project Structure & Build System

**Deliverables:**
- ✅ Project structure setup
- ✅ Build system configuration
- ✅ NuGet packaging
- ✅ CI/CD pipeline (GitHub Actions)

**Critical Files to Create:**
```
src/
├── SurrealDB.Client/
│   ├── SurrealDbClient.cs          (main entry point)
│   ├── SurrealDbClientOptions.cs   (configuration)
│   └── Connection/
│       ├── IConnectionPool.cs
│       └── ConnectionPool.cs
├── SurrealDB.Client.Protocol/
│   ├── IProtocolAdapter.cs
│   ├── HttpProtocolAdapter.cs
│   └── WebSocketProtocolAdapter.cs
└── SurrealDB.Client.Exceptions/
    └── SurrealDbException.cs

tests/
├── SurrealDB.Client.Tests/
│   └── ConnectionTests.cs
```

**Key Infrastructure:**
- Directory.Build.props (version, build config)
- .github/workflows/build.yml
- .github/workflows/test.yml

---

### Week 2: Connection & Authentication

**Deliverables:**
- ✅ Connection pooling
- ✅ Connection lifecycle management
- ✅ Basic authentication
- ✅ Connection tests

**API Surface:**
```csharp
public class SurrealDbClient
{
    public Task ConnectAsync(CancellationToken ct = default);
    public Task DisconnectAsync();
    public Task<bool> IsConnectedAsync();

    // Authentication
    public Task AuthenticateAsync(
        IAuthenticationProvider auth,
        CancellationToken ct = default);
    public Task LogoutAsync();
}

public interface IAuthenticationProvider
{
    Task AuthenticateAsync(ISurrealDbClient client, CancellationToken ct);
}

public class UsernamePasswordAuth : IAuthenticationProvider
{
    public string Username { get; set; }
    public string Password { get; set; }
    public Task AuthenticateAsync(ISurrealDbClient client, CancellationToken ct) { }
}

public class TokenAuth : IAuthenticationProvider
{
    public string Token { get; set; }
    public Task AuthenticateAsync(ISurrealDbClient client, CancellationToken ct) { }
}
```

**Connection Management:**
- Pool size configuration
- Connection timeouts
- Automatic reconnection (basic exponential backoff)
- Connection validation

**Tests to Write:**
- Connect/Disconnect lifecycle
- Authentication success/failure
- Pool management
- Timeout handling

---

### Week 3: CRUD Operations

**Deliverables:**
- ✅ Create (INSERT)
- ✅ Read (SELECT)
- ✅ Update (UPDATE)
- ✅ Delete (DELETE)
- ✅ Upsert (INSERT OR UPDATE)

**API Surface:**
```csharp
public class SurrealDbClient
{
    // Create
    public Task<T> CreateAsync<T>(
        string table,
        T data,
        CancellationToken ct = default);

    public Task<IEnumerable<T>> CreateAsync<T>(
        string table,
        IEnumerable<T> data,
        CancellationToken ct = default);

    // Read
    public Task<T> GetAsync<T>(
        string recordId,
        CancellationToken ct = default);

    public Task<IEnumerable<T>> SelectAsync<T>(
        string table,
        CancellationToken ct = default);

    // Update
    public Task<T> UpdateAsync<T>(
        string recordId,
        T data,
        CancellationToken ct = default);

    // Delete
    public Task DeleteAsync(
        string recordId,
        CancellationToken ct = default);

    // Upsert
    public Task<T> UpsertAsync<T>(
        string recordId,
        T data,
        CancellationToken ct = default);
}
```

**Implementation:**
- HTTP protocol implementation
- WebSocket protocol implementation
- Result mapping/deserialization
- Error handling

**Tests to Write:**
- Create single/multiple records
- Read by ID, from table
- Update existing records
- Delete records
- Upsert (create or update)
- Type serialization/deserialization

---

### Week 4: Query API & Transactions

**Deliverables:**
- ✅ QueryBuilder DSL
- ✅ Raw SurrealQL support
- ✅ Parameter binding
- ✅ Basic transactions

**API Surface:**
```csharp
public class SurrealDbClient
{
    // QueryBuilder
    public IQueryBuilder Query();

    // Raw queries
    public Task<QueryResult> QueryAsync(
        string surrealQL,
        Dictionary<string, object> parameters = null,
        CancellationToken ct = default);

    public Task<T> QueryAsync<T>(
        string surrealQL,
        Dictionary<string, object> parameters = null,
        CancellationToken ct = default);

    // Transactions
    public Task<ITransaction> BeginTransactionAsync(
        CancellationToken ct = default);
}

public interface IQueryBuilder
{
    IQueryBuilder Select<T>();
    IQueryBuilder From(string table);
    IQueryBuilder Where(string condition);
    IQueryBuilder OrderBy(string field);
    IQueryBuilder OrderByDescending(string field);
    IQueryBuilder Limit(int count);
    IQueryBuilder Offset(int count);
    IQueryBuilder Parameter(string name, object value);

    string Build();
    Task<QueryResult> ExecuteAsync(CancellationToken ct = default);
    Task<IEnumerable<T>> ExecuteAsync<T>(CancellationToken ct = default);
}

public interface ITransaction : IAsyncDisposable
{
    Task QueryAsync(string surrealQL, Dictionary<string, object> parameters, CancellationToken ct);
    Task<T> QueryAsync<T>(string surrealQL, Dictionary<string, object> parameters, CancellationToken ct);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
```

**QueryBuilder Implementation:**
- Fluent method chaining
- Parameter validation
- WHERE clause parsing
- ORDER BY support
- LIMIT/OFFSET support

**Transaction Implementation:**
- BEGIN transaction
- Query execution within transaction
- COMMIT on success
- ROLLBACK on error

**Tests to Write:**
- QueryBuilder with various filters
- Parameter binding and escaping
- Transaction commit/rollback
- Raw SurrealQL execution

---

### Week 5: Error Handling & Serialization

**Deliverables:**
- ✅ Exception hierarchy
- ✅ Serialization/Deserialization
- ✅ Type mapping
- ✅ Error handling

**Exception Hierarchy:**
```csharp
public abstract class SurrealDbException : Exception
{
    public string ErrorCode { get; set; }
}

public class ConnectionException : SurrealDbException { }
public class AuthenticationException : SurrealDbException { }
public class QueryException : SurrealDbException { }
public class TimeoutException : SurrealDbException { }
public class SerializationException : SurrealDbException { }
public class ValidationException : SurrealDbException { }
```

**Serialization:**
```csharp
public interface ISerializer
{
    string Serialize(object obj);
    T Deserialize<T>(string json);
}

public class SystemTextJsonSerializer : ISerializer
{
    // Implementation using System.Text.Json
}
```

**Type Mapping:**
```csharp
public interface ITypeMapper
{
    object MapFromDatabase(string surrealType, object value);
    object MapToDatabase<T>(T value);
}
```

**Tests to Write:**
- Exception types and codes
- JSON serialization round-trip
- Type mapping (string, int, DateTime, etc.)
- Null value handling
- Error message formatting

---

### Week 6: Testing & Documentation

**Deliverables:**
- ✅ Unit tests (70%+ coverage)
- ✅ Integration tests
- ✅ README with quick start
- ✅ API documentation

**Test Structure:**
```
tests/
├── SurrealDB.Client.Tests.Unit/
│   ├── ConnectionTests.cs
│   ├── CrudTests.cs
│   ├── QueryBuilderTests.cs
│   ├── TransactionTests.cs
│   ├── SerializationTests.cs
│   └── ExceptionTests.cs
└── SurrealDB.Client.Tests.Integration/
    ├── ConnectionIntegrationTests.cs
    ├── CrudIntegrationTests.cs
    └── TransactionIntegrationTests.cs
```

**Documentation:**
```markdown
# README.md
- Features overview
- Quick start guide
- Installation instructions
- Basic examples

# QUICK_START.md
- 5-minute guide
- Complete example
- Troubleshooting

# API_REFERENCE.md
- Class/method documentation
- Type signatures
- Parameters and returns
```

**Documentation Files to Create:**
- README.md (quick start, features)
- QUICK_START.md (5-minute guide)
- API_REFERENCE.md (API docs)
- EXAMPLES.md (code examples)
- TROUBLESHOOTING.md (common issues)

**Key Examples to Document:**
```csharp
// Connection
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();
await client.AuthenticateAsync(new UsernamePasswordAuth("user", "pass"));

// CRUD
var user = new User { Name = "John", Email = "john@example.com" };
await client.CreateAsync("users", user);

// Query
var results = await client
    .Query()
    .Select("*")
    .From("users")
    .Where("age >= 18")
    .ExecuteAsync();

// Transaction
using var tx = await client.BeginTransactionAsync();
await tx.QueryAsync("INSERT INTO users VALUES ($data)", new { data = userData });
await tx.CommitAsync();

// Error handling
try
{
    await client.CreateAsync("users", user);
}
catch (ValidationException ex)
{
    Console.WriteLine($"Validation error: {ex.Message}");
}
```

---

## B-Grade Success Criteria

### Functionality
- ✅ All CRUD operations working
- ✅ Connections pooled and managed
- ✅ Authentication working
- ✅ Query builder functional
- ✅ Transactions working
- ✅ Errors handled correctly

### Quality
- ✅ 70%+ unit test coverage
- ✅ All integration tests passing
- ✅ No critical bugs
- ✅ Code reviewed

### Performance
- ✅ Connection setup < 1s
- ✅ Query execution < 100ms
- ✅ Memory usage acceptable
- ✅ Pool reuse working

### Documentation
- ✅ README with quick start
- ✅ API reference complete
- ✅ 10+ code examples
- ✅ Troubleshooting guide

---

## B-Grade Timeline

| Week | Focus | Hours |
|------|-------|-------|
| 1 | Structure & Build | 50 |
| 2 | Connection & Auth | 60 |
| 3 | CRUD Operations | 80 |
| 4 | Query & Transactions | 80 |
| 5 | Errors & Serialization | 70 |
| 6 | Testing & Docs | 60 |
| **Total** | **B-Grade MVP** | **~400 hours** |

---

## B-Grade Deliverables Checklist

- ✅ Source code (complete)
- ✅ Unit tests (70%+ coverage)
- ✅ Integration tests
- ✅ CI/CD pipeline
- ✅ README
- ✅ API documentation
- ✅ Quick start guide
- ✅ Examples
- ✅ NuGet package (beta)

---

## Next Steps: A Grade

Once B-Grade is complete:
1. Add ISurrealDbSession (Unit of Work)
2. Implement ChangeTracker
3. Add IQueryable composition
4. Include/Lazy loading patterns
5. Interceptors and caching
6. Real-time subscriptions

Estimated effort: **400-500 additional hours**

---

