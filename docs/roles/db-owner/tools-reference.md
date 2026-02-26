# DB Owner Tools Reference - SurrealDB.Client

## SurrealDB CLI Commands

```bash
# Start an interactive SurrealQL shell
surreal sql \
  --conn http://localhost:8000 \
  --user root \
  --pass root \
  --ns testns \
  --db testdb

# One-shot query
surreal sql \
  --conn http://localhost:8000 \
  --user root \
  --pass root \
  --ns testns \
  --db testdb \
  --query "SELECT * FROM users"
```

## SurrealDB HTTP API (curl)

```bash
# Health check
curl -s http://localhost:8000/health

# Authenticate and get a token
curl -s http://localhost:8000/signin \
  -H "Content-Type: application/json" \
  -d '{"user":"root","pass":"root"}'

# Run a query with credentials in header
curl -s http://localhost:8000/sql \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -u root:root \
  -H "NS: testns" \
  -H "DB: testdb" \
  -d 'SELECT * FROM users'

# Run a query with parameters
curl -s http://localhost:8000/sql \
  -H "Content-Type: application/json" \
  -u root:root \
  -H "NS: testns" \
  -H "DB: testdb" \
  -d 'SELECT * FROM users WHERE email = $email' \
  --data-urlencode 'email=alice@example.com'

# Create a record
curl -s http://localhost:8000/sql \
  -H "Content-Type: application/json" \
  -u root:root \
  -H "NS: testns" \
  -H "DB: testdb" \
  -d 'CREATE users CONTENT {"name": "Alice", "email": "alice@example.com"}'

# Delete all test data
curl -s http://localhost:8000/sql \
  -H "Content-Type: application/json" \
  -u root:root \
  -H "NS: testns" \
  -H "DB: testdb" \
  -d 'DELETE users'
```

## Verifying Query Implementation in Source

```bash
# Check if USE NS/DB is implemented in ConnectAsync
grep -A20 "public async Task ConnectAsync" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs | \
  grep -i "USE\|Namespace\|Database"

# Check all hardcoded SurrealQL strings in the client
grep -rn '"SELECT\|"CREATE\|"UPDATE\|"DELETE\|"INSERT\|"UPSERT\|"BEGIN\|"COMMIT' \
  /home/user/SurrealDB.Client/src/ --include="*.cs"

# Check if any query uses string interpolation (injection risk)
grep -rn '\$".*SELECT\|\$".*CREATE\|\$".*UPDATE\|\$".*DELETE' \
  /home/user/SurrealDB.Client/src/ --include="*.cs"
# Expected: 0 results in production code
```

## Checking Record ID Validation

```bash
# Check current ValidateRecordId implementation
grep -A10 "private static void ValidateRecordId" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs
# Should enforce "table:id" format with colon separator

# Check current ValidateTable implementation
grep -A10 "private static void ValidateTable" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs
```

## Checking Namespace/Database Configuration

```bash
# Check SurrealDbClientOptions for Namespace/Database properties
grep -A5 "Namespace\|Database" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClientOptions.cs

# Check if Namespace and Database are used anywhere in the client
grep -rn "\.Namespace\|\.Database\|options\.Namespace\|_options\.Namespace" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/ --include="*.cs"
```

## Type Mapping Verification

```bash
# Check serializer configuration
cat /home/user/SurrealDB.Client/src/SurrealDB.Client/Serialization/SystemTextJsonSerializer.cs

# Check if DateTime serialization is configured
grep -rn "DateTime\|JsonSerializerOptions\|RoundtripKind\|DateTimeStyles" \
  /home/user/SurrealDB.Client/src/ --include="*.cs"

# Check if JsonPropertyName attributes are used in test models
grep -rn "JsonPropertyName\|JsonIgnore" \
  /home/user/SurrealDB.Client/tests/ --include="*.cs"
```

## Useful SurrealQL Quick Reference

```sql
-- Show all tables in current database
INFO FOR DB;

-- Show schema for a table
INFO FOR TABLE users;

-- Create a unique index
DEFINE INDEX users_email ON users FIELDS email UNIQUE;

-- Show all indexes
INFO FOR TABLE users;

-- Count records
SELECT count() FROM users GROUP ALL;

-- Select with filter and limit
SELECT * FROM users WHERE status = 'active' LIMIT 10 START 0;

-- Partial update (only changes specified fields)
UPDATE users:alice MERGE { "email": "new@example.com" };

-- Full replace
UPDATE users:alice CONTENT { "name": "Alice", "email": "new@example.com" };

-- Delete all records from table
DELETE users;

-- Transaction
BEGIN TRANSACTION;
CREATE orders:1 CONTENT { "userId": "users:alice", "amount": 100 };
UPDATE users:alice SET orderCount += 1;
COMMIT TRANSACTION;

-- LIVE SELECT (real-time subscription)
LIVE SELECT * FROM users;

-- Graph traversal
SELECT ->purchased->product.* FROM users:alice;
```

## Checking HTTP Endpoint Usage

```bash
# Check which HTTP paths are used in the protocol adapters
grep -rn '"health"\|"sql"\|"signin"\|"signup"\|"key"' \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/Protocol/ --include="*.cs"

# Verify the HTTP method for each endpoint
grep -B2 -A10 "SendAsync\|HttpMethod" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/Protocol/HttpProtocolAdapter.cs | head -60
```

## SurrealDB Version Compatibility Check

SurrealDB 1.x and 2.x have different query features:

| Feature | SurrealDB 1.x | SurrealDB 2.x |
|---------|--------------|--------------|
| `INSERT INTO ... ON DUPLICATE KEY UPDATE` | No | Yes |
| `UPSERT` statement | No | Yes |
| `LIVE SELECT` | Yes | Yes (improved) |
| `RELATE` graph edges | Yes | Yes |
| `DEFINE ANALYZER` full-text | Partial | Full |

Check which version of SurrealDB is being targeted:

```bash
# Check SurrealDB version (if running locally)
curl -s http://localhost:8000/version
# or
surreal version
```

The client should document which minimum SurrealDB version is required in `README.md`.
