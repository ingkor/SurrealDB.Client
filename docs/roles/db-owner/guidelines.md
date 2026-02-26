# DB Owner Guidelines - SurrealDB.Client

## SurrealQL Query Design

### Use /sql for All Complex Operations

The SurrealDB HTTP API has a REST key endpoint (`/key/<table>`) and a query endpoint (`/sql`). Always use `/sql` unless there is a specific reason not to. The `/sql` endpoint:
- Supports all SurrealQL operations
- Supports transactions (`BEGIN TRANSACTION; ... COMMIT TRANSACTION;`)
- Supports parameterization
- Returns a consistent response structure

The REST `/key/` endpoints are simpler but do not support all SurrealQL features.

### Parameterization Is Mandatory

Any time a user-supplied value appears in a query, it MUST use the `$param` syntax:

```sql
-- WRONG: direct interpolation
SELECT * FROM users WHERE email = 'alice@example.com';

-- CORRECT: parameterized
SELECT * FROM users WHERE email = $email;
```

The HTTP body for a parameterized query is JSON:
```json
{
  "query": "SELECT * FROM users WHERE email = $email",
  "vars": { "email": "alice@example.com" }
}
```

If parameterization is not supported for a specific use case (e.g., dynamic table names), validate the input strictly against a safe pattern before use:

```csharp
private static readonly Regex SafeTableName = new(@"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$", RegexOptions.Compiled);

private static void ValidateTable(string table)
{
    if (string.IsNullOrWhiteSpace(table))
        throw new ValidationException("Table name cannot be empty.");
    if (!SafeTableName.IsMatch(table))
        throw new ValidationException($"Table name '{table}' contains invalid characters.");
}
```

---

## SurrealDB Response Structure

Every response from `/sql` is an array of statement results:

```json
[
  {
    "time": "52.1µs",
    "status": "OK",
    "result": [
      { "id": "users:alice", "name": "Alice", "email": "alice@example.com" }
    ]
  }
]
```

Key points:
- `status` is either `"OK"` or `"ERR"`
- `result` is always an array, even for single records
- For `SELECT * FROM users:alice`, `result` will be `[]` if the record does not exist
- For `CREATE`, `result` will contain the created record

The current `QueryResult` class in `ISurrealDbClient.cs` does not model this structure correctly. The `Data` field is `object?` and there is no typed deserialization from the SurrealDB response envelope. This must be fixed when implementing CRUD.

---

## Record ID Conventions

### Format
All record IDs passed to methods like `GetAsync("users:alice")` must be in the format `table:id`. The client must validate this format.

### Auto-Generated IDs
When calling `CreateAsync` without a specific ID, SurrealDB generates a ULID (Universally Unique Lexicographically Sortable Identifier). The generated ID is returned in the response and the client must surface it to the caller.

### ID Type Considerations

| Use Case | ID Format | Example |
|----------|-----------|---------|
| Human-readable entity | String slug | `users:alice` |
| Auto-generated, sortable | ULID | `users:01J5K3P2B...` |
| Numeric sequence | Integer | `orders:1234` |
| UUID | String (no native type) | `sessions:⟨550e8400-e29b-41d4-a716-446655440000⟩` |

Note: Integer IDs like `users:1` are different from string IDs like `users:⟨1⟩`. In SurrealDB, `users:1` is stored with the integer 1 as the ID.

---

## Namespace/Database Setup

Every SurrealDB connection must specify a namespace and database before running queries. The setup sequence is:

1. Connect (HTTP or WebSocket)
2. Authenticate (optional but typical)
3. Send `USE NS <namespace> DB <database>;`
4. Begin running queries

If `SurrealDbClientOptions.Namespace` and `SurrealDbClientOptions.Database` are both set, `ConnectAsync` must send the `USE` statement immediately after verifying the connection.

If they are not set, the client should either:
a) Throw a `ValidationException` during `Validate()` (strictest approach), or
b) Allow connection but throw `QueryException` with a helpful message on the first query attempt

Approach (a) is recommended for production use — configure these as required options.

---

## Handling SurrealDB's Type System

### datetime

SurrealDB stores datetimes in ISO 8601 format. When serializing:

```csharp
// C# DateTime to SurrealDB
DateTime.UtcNow.ToString("O"); // "2026-02-26T10:30:00.0000000Z"

// SurrealDB to C# DateTime
DateTime.Parse(surrealDateString, null, DateTimeStyles.RoundtripKind);
```

Configure `System.Text.Json` with `JsonSerializerOptions` that handle this:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};
// DateTime is handled correctly by System.Text.Json with ISO 8601 format by default
```

### Record IDs in Deserialized Objects

SurrealDB returns IDs in the format `"id": "users:alice"`. When deserializing into a C# class:

```csharp
public class User
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
```

The `Id` property will contain the full record ID including table prefix: `"users:alice"`. Be consistent about whether your domain model stores the full ID or just the ID part. Document this convention clearly.

---

## Index and Performance Guidance

SurrealDB indexes can be defined with SurrealQL. The client does not manage schema or indexes directly — that is a database administration concern. However, the DB Owner for the client should document which fields the client queries on, so the DBA can create appropriate indexes.

For example, if `SelectAsync` with a `WHERE email = $email` filter is added, document:

```
Query pattern: SELECT * FROM users WHERE email = $email
Recommended index: DEFINE INDEX users_email ON users FIELDS email UNIQUE;
Expected performance: O(log n) with index, O(n) without
```

---

## NONE vs null in SurrealDB

SurrealDB distinguishes between:
- `null` — the field exists with a null value
- `NONE` — the field does not exist at all

When deserializing, both map to C# `null`, but when serializing, the behavior differs:
- `System.Text.Json` serializes `null` as `null` (not `NONE`)
- Fields omitted from serialization become `NONE` in SurrealDB

For `UPDATE MERGE` operations (change tracking), fields should be omitted from the payload rather than set to `null` to avoid changing their SurrealDB representation.

---

## Testing Queries Against SurrealDB

Use the SurrealDB CLI or the Surrealist GUI to test queries before implementing them in C#:

```bash
# Using surreal CLI (if installed)
surreal sql --conn http://localhost:8000 --user root --pass root --ns testns --db testdb

# Then paste queries to test:
# CREATE users CONTENT { "name": "Alice", "email": "alice@test.com" };
# SELECT * FROM users;
# UPDATE users:alice MERGE { "email": "newalice@test.com" };
# DELETE users:alice;
```

This validation step must happen before any query is merged into the codebase.
