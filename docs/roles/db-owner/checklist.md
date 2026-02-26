# DB Owner Checklist - SurrealDB.Client

## Before Implementing a CRUD Operation

- [ ] Is the SurrealQL for this operation correct? Test it in SurrealDB CLI or Surrealist first
- [ ] Does the operation use `CONTENT` (full replace) or `MERGE` (partial update)?
- [ ] Does the operation use parameterization for any user-supplied values?
- [ ] Is the correct HTTP endpoint being used (`/sql` vs `/key/<table>`)?
- [ ] Has the namespace/database context been set before this query runs (`USE NS x DB y`)?
- [ ] Does the operation handle empty result arrays correctly (record not found)?

---

## Query Safety Checklist

For every new query in the codebase:

- [ ] No user-supplied string is directly interpolated into a SurrealQL string
- [ ] Parameterized queries use `$paramName` syntax in SurrealQL and pass values in the parameters dictionary
- [ ] Table names come from validated input (no spaces, no special chars)
- [ ] Record IDs contain the `table:id` colon separator
- [ ] The query is bounded (has `LIMIT` if selecting from potentially large tables)

---

## Namespace/Database Context Checklist

- [ ] `SurrealDbClientOptions.Namespace` and `SurrealDbClientOptions.Database` are documented
- [ ] `ConnectAsync` sends `USE NS <ns> DB <db>` after establishing the connection (if both are configured)
- [ ] If namespace/database are not configured, appropriate warning or error is surfaced
- [ ] Integration tests set namespace and database in test setup
- [ ] `SurrealDbClientOptions.Validate()` warns if Namespace/Database are null

---

## Type Mapping Checklist

When adding new type support or testing existing type mapping:

- [ ] `DateTime` values are serialized as ISO 8601 with UTC timezone
- [ ] `Guid` values are serialized as strings (no native UUID in SurrealDB)
- [ ] `null` vs `NONE` distinction is handled correctly (empty optional vs. absent field)
- [ ] Nested objects serialize as SurrealDB `object` type
- [ ] Arrays/collections serialize as SurrealDB `array` type
- [ ] Deserialization handles SurrealDB's `id` field (record ID format `table:id`) → C# `string Id`

---

## SurrealQL Correctness Review

When reviewing a PR that adds or modifies SurrealQL queries:

- [ ] Run the exact query manually against a SurrealDB instance before approving
- [ ] Check that `SELECT` queries return the expected structure (SurrealDB wraps results in an array)
- [ ] Verify `CREATE CONTENT` vs `INSERT INTO` semantics are correct for the use case
- [ ] Verify `UPDATE CONTENT` (full replace) vs `UPDATE MERGE` (partial) is intentional
- [ ] For `DELETE` — confirm whether deleting a non-existent record is silent or an error
- [ ] For `UPSERT` — confirm the behavior matches the method name and documentation

---

## Transaction Boundary Review

When reviewing code that performs multiple operations:

- [ ] Are sequential operations (create + update + relate) wrapped in `BEGIN TRANSACTION; ... COMMIT TRANSACTION;`?
- [ ] Is there rollback handling on failure?
- [ ] Does the current `SurrealDbTransaction` stub need to be fully implemented first?
- [ ] Are there integration tests that verify atomicity (partial failure → full rollback)?

---

## Performance Checklist for Database Queries

- [ ] Does the query use `LIMIT` when fetching from large tables?
- [ ] Does `SelectAsync` have an optional filter/where clause to avoid full table scans?
- [ ] Are connection pool statistics being monitored during load tests?
- [ ] Are queries using SurrealDB indexes where applicable? (Document the expected index)
- [ ] Does `ChangeTracker` send `UPDATE MERGE` (partial) instead of `UPDATE CONTENT` (full)?
- [ ] Are N+1 query patterns documented and warnings added to the docs?

---

## Release Data Integrity Checklist

- [ ] All CRUD operations produce correct SurrealQL verified against real SurrealDB
- [ ] No query uses direct string interpolation for user input
- [ ] `USE NS / USE DB` is sent before any query when namespace/database are configured
- [ ] Type mapping tests cover all supported C# types
- [ ] Integration tests cover create → get → update → delete lifecycle
- [ ] Behavior for non-existent records is documented and consistent
- [ ] `SurrealDbClientOptions.Validate()` enforces required configuration before connection
