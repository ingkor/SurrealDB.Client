# DB Owner Role - SurrealDB.Client

## Overview

The DB Owner is responsible for the correctness, performance, and integrity of how SurrealDB.Client interacts with the SurrealDB database. This role does not own the SurrealDB server itself (that is an infrastructure concern), but owns how the client library queries, shapes, and manages data through the SurrealDB API.

SurrealDB is a multi-model database that supports document, relational, and graph data with a SQL-like query language called SurrealQL. Understanding SurrealDB's data model and query semantics is essential for this role.

---

## Primary Responsibilities

- Define and document the SurrealQL query patterns used by each client operation
- Ensure queries are correct, efficient, and use parameterization to prevent injection
- Define table naming conventions and record ID format requirements
- Document the expected SurrealDB schema assumptions the client makes
- Review all query-related code for correctness and injection safety
- Validate that the client's serialization correctly maps C# types to SurrealDB types
- Define and document behavior when records do not exist, tables are empty, or IDs are invalid
- Evaluate the use of SurrealDB-specific features (LIVE SELECT, RELATE, graph traversal)

---

## SurrealDB Data Model Overview

### Record IDs

SurrealDB record IDs use the format `table:id`. Examples:
- `users:1` — table "users", integer ID 1
- `users:alice` — table "users", string ID "alice"
- `users:01J5K...` — table "users", ULID auto-generated ID
- `users:⟨john@example.com⟩` — table "users", complex string ID

The current `ISurrealDbClient` methods use `string recordId` for `GetAsync`, `UpdateAsync`, `DeleteAsync`, and `UpsertAsync`. These must always be the full `table:id` format, not just the ID portion.

### Namespaces and Databases

SurrealDB organizes data in a hierarchy: **namespace** > **database** > **table**. The `SurrealDbClientOptions` exposes `Namespace` and `Database` properties that must be set correctly before any query runs.

The current client does NOT set namespace/database during `ConnectAsync`. This means all queries run without a namespace/database context, which will cause SurrealDB to reject queries. This is a critical missing feature.

SurrealQL to set context (must be sent after connect):
```sql
USE NS myns DB mydb;
```

### Data Types

| C# Type | SurrealDB Type | Notes |
|---------|---------------|-------|
| `string` | string | UTF-8 |
| `int`, `long` | int | Arbitrary precision |
| `float`, `double` | float | 64-bit |
| `bool` | bool | |
| `DateTime` | datetime | ISO 8601 with timezone |
| `Guid` | string (recommend) | SurrealDB has no native UUID type — store as string |
| `IEnumerable<T>` | array | |
| `Dictionary<string, T>` | object | |
| `null` | NONE / null | NONE and null are distinct in SurrealDB |

---

## Query Patterns for Each CRUD Operation

### CREATE
```sql
-- Auto-generated ID
CREATE users CONTENT { "name": "Alice", "email": "alice@example.com" };

-- Specific ID
CREATE users:alice CONTENT { "name": "Alice" };
```

The client should use `CONTENT` (not `SET`) to set all fields atomically.

### GET
```sql
SELECT * FROM users:alice;
-- Returns an array with 0 or 1 records
```

The response will be an array — the client must handle empty array (record not found) by returning `null`.

### SELECT (all records)
```sql
SELECT * FROM users;
```

### UPDATE (full replace)
```sql
UPDATE users:alice CONTENT { "name": "Alice", "email": "new@example.com" };
```

### UPDATE (partial — for ChangeTracker)
```sql
UPDATE users:alice MERGE { "email": "new@example.com" };
```

`MERGE` only updates the specified fields. This is the correct operation for differential updates.

### DELETE
```sql
DELETE users:alice;
-- Succeeds even if record does not exist
```

### UPSERT
```sql
-- SurrealDB 2.x supports INSERT ... ON DUPLICATE KEY UPDATE
INSERT INTO users { "id": "users:alice", "name": "Alice" }
  ON DUPLICATE KEY UPDATE name = $input.name;
```

Or using `UPDATE ... CONTENT` which is an implicit upsert in SurrealDB 1.x.

---

## Query Parameterization (Injection Prevention)

Never interpolate user-supplied values directly into SurrealQL strings. Use SurrealDB query parameters:

```csharp
// UNSAFE — SQL injection risk
var query = $"SELECT * FROM users WHERE email = '{userEmail}'";

// SAFE — parameterized
var query = "SELECT * FROM users WHERE email = $email";
var parameters = new Dictionary<string, object> { ["email"] = userEmail };
```

The SurrealDB HTTP API supports parameters by sending them in the request body alongside the query. The `QueryAsync` method signature already accepts `Dictionary<string, object>? parameters` — this must be used for all user-supplied values.

---

## Critical Missing Feature: USE NS / USE DB

After connecting, the client must send a `USE NS <ns> DB <db>` statement before running any queries. Without this, SurrealDB rejects all queries with an error like:

```
"Specify a namespace to use"
```

The `SurrealDbClientOptions` already has `Namespace` and `Database` properties. The implementation must use them in `ConnectAsync`:

```csharp
// After establishing the connection:
if (_options.Namespace != null && _options.Database != null)
{
    await connection.SendAsync("POST", "sql",
        $"USE NS {_options.Namespace} DB {_options.Database};",
        cancellationToken);
}
```

This affects every query-executing operation. It is the DB Owner's responsibility to ensure this is implemented and documented.

---

## Data Integrity Responsibilities

### Atomicity
Single CRUD operations in SurrealDB are atomic. However, sequences of operations (create + relate + update) are not atomic unless wrapped in `BEGIN TRANSACTION; ... COMMIT;`. The `ISurrealDbTransaction` interface exists for this but is not yet implemented.

### Concurrency
SurrealDB has no built-in row-level locking for optimistic concurrency (see `RISK_ASSESSMENT.md` Risk #4). Until `[ConcurrencyToken]` is implemented, concurrent updates to the same record will silently overwrite each other.

### Validation
Input validation (`ValidateTable`, `ValidateRecordId`) currently only checks for empty strings. The DB Owner should define additional validation rules:
- Table names: must match `[a-zA-Z_][a-zA-Z0-9_]*` — no spaces, special characters, or SQL keywords
- Record IDs: must contain `:` separator; before the colon must be a valid table name
- Query strings: must not be empty; consider max length

---

## Key Files for DB Owner Work

| File | DB Relevance |
|------|-------------|
| `src/SurrealDB.Client/ISurrealDbClient.cs` | Method signatures for all DB operations |
| `src/SurrealDB.Client/SurrealDbClient.cs` | All CRUD TODO stubs to implement |
| `src/SurrealDB.Client/SurrealDbClientOptions.cs` | `Namespace`, `Database`, `ConnectionString` |
| `src/SurrealDB.Client/Serialization/SystemTextJsonSerializer.cs` | C# ↔ JSON ↔ SurrealDB type mapping |
| `src/SurrealDB.Client/Protocol/HttpProtocolAdapter.cs` | HTTP endpoint paths (`/sql`, `/signin`, `/health`) |
| `src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs` | JSON-RPC message format for WebSocket |

---

## SurrealDB HTTP API Reference

| Endpoint | Method | Purpose |
|---------|--------|---------|
| `/sql` | POST | Execute a SurrealQL query |
| `/health` | GET | Health check |
| `/signin` | POST | Authenticate with credentials |
| `/signup` | POST | Create a new user |
| `/key/<table>` | GET | Select all from table |
| `/key/<table>` | POST | Create a record |
| `/key/<table>/<id>` | GET | Select one record |
| `/key/<table>/<id>` | PUT | Replace a record |
| `/key/<table>/<id>` | PATCH | Merge/update a record |
| `/key/<table>/<id>` | DELETE | Delete a record |

Note: The `/sql` endpoint is the most flexible and should be the primary path for complex queries. The `/key/` REST endpoints are simpler but less flexible.
