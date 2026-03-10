# Task 3 — Migration Runner: Implement `IMigrationExecutor` + `SurrealMigrationRunner`

The `Migration` base class and `IMigrationExecutor` interface already exist in
`src/SurrealDB.Client/Migrations/Migration.cs`. There is no concrete implementation.
This prompt builds the full migration system: executor, runner, history tracking,
and a clean entry point on `ISurrealDbClient`.

```xml
<project>
  <name>SurrealDB.Client — Migration Runner</name>
  <description>
    Implement the missing migration execution layer. The interface and abstract base
    are already defined. Add: SurrealMigrationExecutor (implements IMigrationExecutor),
    SurrealMigrationRunner (discovers, orders, and applies migrations), migration history
    tracking in a SurrealDB table, and MigrateAsync / RollbackAsync entry points on
    ISurrealDbClient / SurrealDbClient.
  </description>
  <language>C# 13 / .NET 9</language>
  <repo_root>C:\Projects\SurrealDB.Client</repo_root>
</project>

<scope>
  WHAT TO BUILD
  1. SurrealMigrationExecutor — concrete IMigrationExecutor backed by SurrealDbClient.
     Translates each schema operation into SurrealQL and calls QueryAsync.
  2. SurrealMigrationRunner — discovers Migration subclasses in a given assembly,
     orders them by Name (lexicographic / timestamp prefix), loads applied migrations
     from the _migrations history table, and applies pending ones in order.
  3. Migration history table — tracked in a SurrealDB table named `_migrations` with
     MigrationInfo shape (name, applied_at, description, checksum).
  4. ISurrealDbClient additions — two new methods:
       Task MigrateAsync(Assembly migrationsAssembly, CancellationToken ct = default)
       Task RollbackAsync(string migrationName, Assembly migrationsAssembly, CancellationToken ct = default)
  5. Unit tests — SurrealMigrationExecutorTests and SurrealMigrationRunnerTests.
     Use MockProtocolAdapter (already in tests/Mocks/) for the client.

  WHAT NOT TO DO
  - Do not change Migration.cs, IMigrationExecutor, or MigrationInfo — they are correct as-is
  - Do not add NuGet packages
  - Do not create a CLI tool or dotnet-ef style command runner
  - Do not implement a file-based migration scaffold generator
</scope>

<constraints>
  - Target framework: net9.0
  - No new NuGet packages — use only existing references
  - Must follow existing patterns: IAsyncDisposable, ThrowIfDisposed(), exception wrapping
  - All exceptions must derive from SurrealDbException
  - The _migrations table name is a constant: MigrationRunner.HistoryTable = "_migrations"
  - Migrations are ordered by Name ascending (ISO-8601 timestamp prefix recommended)
  - Checksum = SHA256 of migration Name+Description (hex string, lowercase)
  - MigrateAsync is idempotent: running twice applies nothing the second time
</constraints>

<architecture>
  New files to create:

  src/SurrealDB.Client/Migrations/
    SurrealMigrationExecutor.cs     ← IMigrationExecutor implementation
    SurrealMigrationRunner.cs       ← discovery, ordering, apply/rollback
    MigrationException.cs           ← new exception type (derives SurrealDbException)

  Modifications:
    src/SurrealDB.Client/ISurrealDbClient.cs   ← add MigrateAsync, RollbackAsync signatures
    src/SurrealDB.Client/SurrealDbClient.cs    ← implement MigrateAsync, RollbackAsync

  Tests:
    tests/SurrealDB.Client.Tests.Unit/
      MigrationExecutorTests.cs
      MigrationRunnerTests.cs
</architecture>

<data_sources>
  No external API. All data goes through the existing SurrealDbClient.QueryAsync.

  _migrations table shape in SurrealDB (SurrealQL):
    {
      "id":          "...",          // auto-assigned SurrealDB record ID
      "name":        "20240101_CreateUsers",
      "applied_at":  "2024-01-01T00:00:00Z",
      "description": "Creates the users table",
      "checksum":    "a3f8b2..."
    }

  QueryAsync response shape (existing SurrealDbResponse<T>):
    { "result": [ { ...record... } ], "error": null }
    EnsureSuccess() throws QueryException if error != null.
</data_sources>

<models>
  // Already defined in Migration.cs — do not change:
  public abstract class Migration
  public interface IMigrationExecutor
  public class MigrationInfo { Name, AppliedAt, Description, Checksum }

  // New:
  public class MigrationException : SurrealDbException
  {
      public string MigrationName { get; }
      public static MigrationException Create(string migrationName, string message, Exception? inner = null)
  }
</models>

<algorithm>
  SurrealMigrationExecutor
  -------------------------
  Constructor: SurrealMigrationExecutor(SurrealDbClient client)

  ExecuteAsync(string surrealQL, CancellationToken ct)
    → await _client.QueryAsync(surrealQL, null, ct)

  CreateTableAsync(string tableName, CancellationToken ct)
    → ExecuteAsync($"DEFINE TABLE {Escape(tableName)} SCHEMALESS;", ct)

  DropTableAsync(string tableName, CancellationToken ct)
    → ExecuteAsync($"REMOVE TABLE {Escape(tableName)};", ct)

  AddColumnAsync(string table, string col, string type, Dictionary? opts, CancellationToken ct)
    → Build: "DEFINE FIELD {col} ON TABLE {table} TYPE {type};"
    → If opts contains "default": append "DEFAULT {opts["default"]}"
    → If opts contains "assert": append "ASSERT {opts["assert"]}"
    → ExecuteAsync(...)

  DropColumnAsync(string table, string col, CancellationToken ct)
    → ExecuteAsync($"REMOVE FIELD {Escape(col)} ON TABLE {Escape(table)};", ct)

  RenameColumnAsync(string table, string oldName, string newName, CancellationToken ct)
    → SurrealDB has no RENAME FIELD. Implement as two steps:
      1. ExecuteAsync($"UPDATE {Escape(table)} SET {Escape(newName)} = {Escape(oldName)};", ct)
      2. ExecuteAsync($"REMOVE FIELD {Escape(oldName)} ON TABLE {Escape(table)};", ct)

  CreateIndexAsync(string table, string indexName, IEnumerable<string> cols, bool unique, CancellationToken ct)
    → columns = string.Join(", ", cols.Select(Escape))
    → unique ? "DEFINE INDEX {indexName} ON TABLE {table} FIELDS {columns} UNIQUE;"
             : "DEFINE INDEX {indexName} ON TABLE {table} FIELDS {columns};"
    → ExecuteAsync(...)

  DropIndexAsync(string table, string indexName, CancellationToken ct)
    → ExecuteAsync($"REMOVE INDEX {Escape(indexName)} ON TABLE {Escape(table)};", ct)

  private static string Escape(string identifier)
    → return $"`{identifier.Replace("`", "\\`")}`";

  SurrealMigrationRunner
  ----------------------
  Constructor: SurrealMigrationRunner(SurrealDbClient client)

  private static string ComputeChecksum(Migration m)
    → SHA256(Encoding.UTF8.GetBytes(m.Name + m.Description))
    → return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

  private async Task EnsureHistoryTableAsync(CancellationToken ct)
    → ExecuteAsync("DEFINE TABLE _migrations SCHEMALESS;", ct)  [idempotent in SurrealDB]

  private async Task<List<string>> GetAppliedMigrationNamesAsync(CancellationToken ct)
    → result = await _client.QueryAsync<MigrationInfo>("SELECT name FROM _migrations ORDER BY name ASC;", null, ct)
    → return result.Select(m => m.Name).ToList()

  public async Task MigrateAsync(Assembly assembly, CancellationToken ct = default)
    → await EnsureHistoryTableAsync(ct)
    → migrations = DiscoverMigrations(assembly)  // find all non-abstract Migration subclasses
    → applied = await GetAppliedMigrationNamesAsync(ct)
    → pending = migrations.Where(m => !applied.Contains(m.Name)).OrderBy(m => m.Name)
    → foreach pending:
        executor = new SurrealMigrationExecutor(_client)
        try:
          await migration.Up(executor, ct)
          await RecordMigrationAsync(migration, ct)
        catch Exception ex:
          throw MigrationException.Create(migration.Name, "Migration failed during Up()", ex)

  public async Task RollbackAsync(string migrationName, Assembly assembly, CancellationToken ct = default)
    → migrations = DiscoverMigrations(assembly)
    → target = migrations.FirstOrDefault(m => m.Name == migrationName)
    → if target == null: throw MigrationException.Create(migrationName, "Migration not found")
    → executor = new SurrealMigrationExecutor(_client)
    → await target.Down(executor, ct)
    → await RemoveMigrationRecordAsync(migrationName, ct)

  private List<Migration> DiscoverMigrations(Assembly assembly)
    → return assembly.GetTypes()
                     .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Migration)))
                     .Select(t => (Migration)Activator.CreateInstance(t)!)
                     .ToList()

  private async Task RecordMigrationAsync(Migration m, CancellationToken ct)
    → info = new MigrationInfo { Name = m.Name, Description = m.Description,
                                  AppliedAt = DateTime.UtcNow, Checksum = ComputeChecksum(m) }
    → json = JsonSerializer.Serialize(info)
    → await _client.QueryAsync($"CREATE _migrations CONTENT {json};", null, ct)

  private async Task RemoveMigrationRecordAsync(string name, CancellationToken ct)
    → await _client.QueryAsync($"DELETE _migrations WHERE name = $name;",
                                new {{ name }}, ct)

  ISurrealDbClient / SurrealDbClient additions
  --------------------------------------------
  In ISurrealDbClient.cs, add:
    Task MigrateAsync(Assembly migrationsAssembly, CancellationToken cancellationToken = default);
    Task RollbackAsync(string migrationName, Assembly migrationsAssembly, CancellationToken cancellationToken = default);

  In SurrealDbClient.cs, implement:
    public async Task MigrateAsync(Assembly migrationsAssembly, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_isConnected) throw new ConnectionException("Not connected.");
        var runner = new SurrealMigrationRunner(this);
        await runner.MigrateAsync(migrationsAssembly, cancellationToken);
    }

    public async Task RollbackAsync(string migrationName, Assembly migrationsAssembly, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_isConnected) throw new ConnectionException("Not connected.");
        var runner = new SurrealMigrationRunner(this);
        await runner.RollbackAsync(migrationName, migrationsAssembly, cancellationToken);
    }
</algorithm>

<store>
  SurrealDB table: _migrations
  CREATE _migrations CONTENT { name: string, applied_at: datetime, description: string, checksum: string }
  SELECT name FROM _migrations ORDER BY name ASC;
  DELETE _migrations WHERE name = $name;
</store>

<edge_cases>
  1. MIGRATION NOT FOUND during rollback — throw MigrationException.Create(name, "Not found in assembly").
     Do not throw KeyNotFoundException or ArgumentException.

  2. ALREADY APPLIED migration in MigrateAsync — skip silently. The applied-names check makes
     MigrateAsync fully idempotent. Never apply the same migration twice.

  3. CHECKSUM MISMATCH — When loading history in GetAppliedMigrationNamesAsync, if a MigrationInfo
     has a non-null Checksum that doesn't match the current migration's computed checksum,
     log a warning but do NOT block the migration. (The migration may have been legitimately
     updated before production run.) Emit to Debug output via Debug.WriteLine.

  4. EMPTY ASSEMBLY — DiscoverMigrations returns empty list. MigrateAsync does nothing, no error.

  5. MIGRATION Up() THROWS — catch the exception, throw MigrationException.Create(..., ex).
     Do NOT attempt to call Down() automatically (no auto-rollback on failure).

  6. CONCURRENT MIGRATE — If two processes call MigrateAsync simultaneously, both will try to
     insert the same _migrations records. SurrealDB will create two records. Document this
     limitation in XML doc: "MigrateAsync is not safe for concurrent execution. Use
     application-level locking for multi-instance deployments."

  7. IDENTIFIER WITH SPACES — Escape() wraps in backticks. Test with table name "my table"
     → "`my table`".

  8. RenameColumnAsync DATA LOSS — The copy+remove approach loses data if the UPDATE fails
     partway. Document in XML doc that RenameColumnAsync is not atomic and should be wrapped
     in a migration that the developer can roll back manually.
</edge_cases>

<testing>
  File: tests/SurrealDB.Client.Tests.Unit/MigrationExecutorTests.cs

  test_CreateTable_GeneratesCorrectSurrealQL
    → Capture the SurrealQL sent to MockProtocolAdapter
    → CreateTableAsync("users") → assert contains "DEFINE TABLE `users` SCHEMALESS"

  test_DropTable_GeneratesCorrectSurrealQL
    → DropTableAsync("orders") → assert contains "REMOVE TABLE `orders`"

  test_AddColumn_BasicType_GeneratesCorrectSurrealQL
    → AddColumnAsync("users", "email", "string") → assert "DEFINE FIELD `email` ON TABLE `users` TYPE string"

  test_AddColumn_WithDefault_IncludesDefaultClause
    → AddColumnAsync("users", "active", "bool", new {{ "default", true }})
    → assert contains "DEFAULT true"

  test_CreateIndex_Unique_GeneratesUniqueKeyword
    → CreateIndexAsync("users", "idx_email", ["email"], unique: true)
    → assert contains "UNIQUE"

  test_CreateIndex_NonUnique_NoUniqueKeyword
    → CreateIndexAsync("users", "idx_name", ["name"], unique: false)
    → assert does NOT contain "UNIQUE"

  test_Escape_IdentifierWithBacktick_EscapesCorrectly
    → Use reflection to call private Escape("`bad`") → "`\\`bad\\``"

  File: tests/SurrealDB.Client.Tests.Unit/MigrationRunnerTests.cs
  (Use a fake in-memory Assembly with test migration classes)

  test_DiscoverMigrations_FindsConcreteSubclasses
    → Register two Migration subclasses in test assembly
    → Assert DiscoverMigrations returns exactly 2

  test_DiscoverMigrations_IgnoresAbstractBase
    → Assert Migration itself is not returned

  test_MigrateAsync_AppliesPendingInOrder
    → Two migrations: "20240101_First", "20240102_Second"
    → Applied: ["20240101_First"]
    → Assert only "20240102_Second".Up() is called

  test_MigrateAsync_Idempotent_NoPendingAppliesNothing
    → All migrations already applied
    → Assert no CreateAsync / QueryAsync called for migration Up()

  test_RollbackAsync_CallsDownOnTarget
    → Migration "20240101_First" exists in assembly and history
    → Call RollbackAsync("20240101_First", assembly)
    → Assert Down() was called and DELETE from _migrations was sent

  test_RollbackAsync_MigrationNotFound_ThrowsMigrationException
    → RollbackAsync("does_not_exist", assembly)
    → Assert throws MigrationException

  test_ComputeChecksum_Deterministic
    → Same migration name+description → same checksum
    → Different name → different checksum
</testing>

<implementation_order>
  Step 1 — Create MigrationException.cs
    Verify: dotnet build src/SurrealDB.Client/ → 0 errors

  Step 2 — Create SurrealMigrationExecutor.cs (implement all 7 IMigrationExecutor methods)
    Verify: dotnet build → 0 errors

  Step 3 — Create SurrealMigrationRunner.cs (Discover, MigrateAsync, RollbackAsync)
    Verify: dotnet build → 0 errors

  Step 4 — Add MigrateAsync + RollbackAsync to ISurrealDbClient.cs
    Verify: dotnet build → 0 errors (SurrealDbClient will have errors — that's expected)

  Step 5 — Implement MigrateAsync + RollbackAsync in SurrealDbClient.cs
    Verify: dotnet build → 0 errors

  Step 6 — Write MigrationExecutorTests.cs and MigrationRunnerTests.cs
    Verify: dotnet test tests/SurrealDB.Client.Tests.Unit/ → all pass
</implementation_order>

<quality>
  - All public types have XML doc comments
  - SurrealMigrationExecutor and SurrealMigrationRunner are internal (not public)
    unless consumed from outside the assembly — only Migration, MigrationInfo,
    MigrationException, and the ISurrealDbClient.MigrateAsync entry point are public
  - No reflection beyond DiscoverMigrations (which is unavoidable for assembly scanning)
  - Escape() must be called on ALL user-supplied identifiers (table names, column names,
    index names) to prevent SurrealQL injection
  - No static state — runner holds a reference to the client, not a global
</quality>

<bootstrap>
  No extra setup needed. Confirm build state:
    dotnet build src/SurrealDB.Client/ → should show 0 errors currently
    dotnet test tests/SurrealDB.Client.Tests.Unit/ → fix QueryCompilerTests first if still failing
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code. All interfaces and base classes
> are already in place — this prompt builds the missing concrete implementation,
> runner, and tests. No external dependencies required.
