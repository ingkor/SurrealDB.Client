# Database Migrations: Schema Evolution & Versioning

> Comprehensive migration system for managing schema changes, data evolution, and rollback capabilities - foundation for S-Grade architecture.

## Overview

Migration system for:
- Schema versioning and history
- Up/Down migrations with rollback
- Seed data management
- Migration ordering and dependencies
- Atomic schema changes

---

## Core Concepts

### Migration Lifecycle

```
Pending → Applied → Success
           ↓
         Failed → Rollback → Pending
```

### Migration Structure

```csharp
public abstract class Migration
{
    public string Id { get; }  // Timestamp + name: "20240115_CreateUsersTable"
    public string Description { get; }
    public int Version { get; }  // Sequential version
    public DateTime CreatedAt { get; }

    public abstract void Up(MigrationBuilder migration);
    public abstract void Down(MigrationBuilder migration);
}
```

---

## Creating Migrations

### Automatic Generation (Recommended)

```csharp
// From code-first model
dotnet surrealdb-cli migrations add CreateUsersTable

// Generated migration:
// Migrations/20240115100000_CreateUsersTable.cs

public class CreateUsersTable : Migration
{
    public override void Up(MigrationBuilder migration)
    {
        migration.CreateTable<User>("users")
            .WithColumn("id", ColumnType.String)
                .PrimaryKey()
            .WithColumn("email", ColumnType.String)
                .Unique()
                .NotNull()
            .WithColumn("name", ColumnType.String)
            .WithColumn("age", ColumnType.Int)
            .WithColumn("status", ColumnType.String)
                .Default("'active'")
            .WithColumn("created_at", ColumnType.DateTime)
                .Default("now()")
            .WithColumn("updated_at", ColumnType.DateTime)
                .Default("now()");
    }

    public override void Down(MigrationBuilder migration)
    {
        migration.DropTable("users");
    }
}
```

### Manual Migration Creation

```csharp
public class AddPhoneToUsers : Migration
{
    public override void Up(MigrationBuilder migration)
    {
        migration.AddColumn<User>(
            tableName: "users",
            columnName: "phone",
            columnType: ColumnType.String,
            nullable: true);

        migration.CreateIndex("users", new[] { "phone" });
    }

    public override void Down(MigrationBuilder migration)
    {
        migration.DropIndex("users", "phone");
        migration.DropColumn("users", "phone");
    }
}

// Register
public class DbContextModelCreating : ModelCreatingContext
{
    protected override void OnMigrationsConfiguring(MigrationsBuilder builder)
    {
        builder.AddMigration<AddPhoneToUsers>();
    }
}
```

---

## MigrationBuilder API

### Table Operations

```csharp
public class MigrationBuilder
{
    // Create table
    public TableBuilder CreateTable<T>(string tableName) where T : class;

    // Rename table
    public void RenameTable(string oldName, string newName);

    // Drop table
    public void DropTable(string tableName);
    public void DropTable<T>() where T : class;

    // Truncate table (dangerous!)
    public void TruncateTable(string tableName);
}

public class TableBuilder
{
    // Add columns
    public ColumnBuilder WithColumn(string name, ColumnType type);

    // Set constraints
    public TableBuilder PrimaryKey(string columnName);
    public TableBuilder ForeignKey(string columnName, string referencedTable, string referencedColumn);
    public TableBuilder Unique(string columnName);
    public TableBuilder NotNull(string columnName);
}

public class ColumnBuilder
{
    public ColumnBuilder PrimaryKey();
    public ColumnBuilder ForeignKey(string referencedTable, string referencedColumn);
    public ColumnBuilder Unique();
    public ColumnBuilder NotNull();
    public ColumnBuilder Default(string defaultValue);
    public ColumnBuilder AutoIncrement();
    public ColumnBuilder Indexed();
}
```

### Column Operations

```csharp
// Add column
migration.AddColumn<User>("phone", ColumnType.String, nullable: true);

// Rename column
migration.RenameColumn("users", "old_name", "new_name");

// Drop column
migration.DropColumn("users", "phone");

// Modify column
migration.AlterColumn("users", "email", ColumnType.String, nullable: false);
```

### Index Operations

```csharp
// Create index
migration.CreateIndex("users", "email");
migration.CreateIndex("users", new[] { "status", "created_at" });

// Create unique index
migration.CreateUniqueIndex("users", "email");

// Drop index
migration.DropIndex("users", "email");
migration.DropIndex("users", "idx_status_created");
```

### Relationship Operations

```csharp
// Add foreign key
migration.AddForeignKey(
    tableName: "orders",
    columnName: "user_id",
    referencedTable: "users",
    referencedColumn: "id",
    onDelete: ReferentialAction.Cascade);

// Drop foreign key
migration.DropForeignKey("orders", "user_id");
```

---

## Executing Migrations

### Apply Latest

```csharp
// Apply all pending migrations
var migrator = new SurrealDbMigrator(client);
await migrator.MigrateAsync();

// Output:
// [20240115100000] CreateUsersTable
// [20240115100100] CreateOrdersTable
// [20240115100200] AddIndexesToOrders
// Applied 3 migrations
```

### Apply To Specific Version

```csharp
// Apply to specific migration
await migrator.MigrateAsync("20240115100100");
// Applies: CreateUsersTable, CreateOrdersTable

// Rollback to specific version
await migrator.RollbackAsync("20240115100000");
// Reverts: CreateOrdersTable, AddIndexesToOrders
```

### Migration History

```csharp
var history = await migrator.GetMigrationHistoryAsync();

foreach (var applied in history)
{
    Console.WriteLine($"[{applied.Id}] {applied.Name}");
    Console.WriteLine($"  Applied: {applied.AppliedAt}");
    Console.WriteLine($"  Duration: {applied.Duration}ms");
    Console.WriteLine($"  Status: {applied.Status}");
}

// Output:
// [20240115100000] CreateUsersTable
//   Applied: 2024-01-15T10:00:00Z
//   Duration: 125ms
//   Status: Success
```

---

## Rollback Strategies

### Rollback Latest

```csharp
// Rollback last migration
await migrator.RollbackAsync();

// Rollback last 3 migrations
await migrator.RollbackAsync(steps: 3);

// Rollback to specific version (runs Down() methods)
await migrator.RollbackAsync("20240115100000");
```

### Rollback with Confirmation

```csharp
var pending = await migrator.GetPendingMigrationsAsync();
if (pending.Any())
{
    Console.WriteLine("Warning: This will rollback the following migrations:");
    foreach (var m in pending)
        Console.WriteLine($"  - {m.Id}: {m.Description}");

    if (Console.ReadLine() == "yes")
        await migrator.RollbackAsync(steps: pending.Count);
}
```

### Dry Run

```csharp
// See what would be applied without executing
var dryRunResult = await migrator.DryRunAsync();

Console.WriteLine($"Would apply {dryRunResult.MigrationsToApply.Count} migrations");
Console.WriteLine($"Database operations:");

foreach (var op in dryRunResult.DatabaseOperations)
{
    Console.WriteLine($"  {op.Type}: {op.Description}");
    Console.WriteLine($"  SQL: {op.GeneratedSql}");
}
```

---

## Seeding Data

### Seed Migration

```csharp
public class SeedInitialUsers : Migration
{
    public override void Up(MigrationBuilder migration)
    {
        migration.InsertData(
            table: "users",
            columns: new[] { "id", "email", "name", "status" },
            values: new object[,]
            {
                { "user:admin", "admin@example.com", "Administrator", "active" },
                { "user:demo", "demo@example.com", "Demo User", "active" }
            });
    }

    public override void Down(MigrationBuilder migration)
    {
        migration.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValues: new object[] { "user:admin", "user:demo" });
    }
}
```

### Seeder Class

```csharp
public class DatabaseSeeder
{
    private readonly ISurrealDbSession _session;

    public async Task SeedAsync()
    {
        await SeedUsersAsync();
        await SeedOrdersAsync();
        await SeedPaymentsAsync();
    }

    private async Task SeedUsersAsync()
    {
        var users = new[]
        {
            new User { Id = "user:1", Email = "john@example.com", Name = "John" },
            new User { Id = "user:2", Email = "jane@example.com", Name = "Jane" }
        };

        foreach (var user in users)
            _session.Add(user);

        await _session.SaveChangesAsync();
    }

    private async Task SeedOrdersAsync()
    {
        var orders = new[]
        {
            new Order { Id = "order:1", UserId = "user:1", Amount = 100 },
            new Order { Id = "order:2", UserId = "user:1", Amount = 200 }
        };

        foreach (var order in orders)
            _session.Add(order);

        await _session.SaveChangesAsync();
    }
}

// Usage
var seeder = new DatabaseSeeder(session);
await seeder.SeedAsync();
```

---

## Migration Conflicts & Resolution

### Conflict Detection

```csharp
var status = await migrator.GetMigrationStatusAsync();

if (status.HasConflicts)
{
    Console.WriteLine("Migration conflicts detected:");
    foreach (var conflict in status.Conflicts)
    {
        Console.WriteLine($"  {conflict.Migration1.Id} vs {conflict.Migration2.Id}");
        Console.WriteLine($"  Conflict: {conflict.Description}");
    }
}
```

### Resolving Conflicts

```csharp
// Reorder migrations
public class MigrationOrdering : IMigrationOrdering
{
    public IEnumerable<Migration> Order(IEnumerable<Migration> migrations)
    {
        return migrations
            .OrderBy(m => m.Timestamp)  // Apply by timestamp
            .ThenBy(m => m.Priority);   // Then by priority
    }
}

// Squash migrations
await migrator.SquashAsync(
    from: "20240115100000",
    to: "20240115100300",
    newMigrationName: "InitialSchema");
// Combines multiple migrations into one
```

---

## Advanced Patterns

### Conditional Migrations

```csharp
public class AddOptionalColumn : Migration
{
    public override void Up(MigrationBuilder migration)
    {
        if (Environment.GetEnvironmentVariable("ENABLE_FEATURE_X") == "true")
        {
            migration.AddColumn<User>(
                tableName: "users",
                columnName: "feature_x_data",
                columnType: ColumnType.String);
        }
    }

    public override void Down(MigrationBuilder migration)
    {
        if (Environment.GetEnvironmentVariable("ENABLE_FEATURE_X") == "true")
        {
            migration.DropColumn("users", "feature_x_data");
        }
    }
}
```

### Batch Migrations

```csharp
public class BatchInsertUsers : Migration
{
    public override async Task UpAsync(MigrationBuilder migration, CancellationToken ct)
    {
        var users = Enumerable.Range(1, 1000)
            .Select(i => new { id = $"user:{i}", email = $"user{i}@example.com", name = $"User {i}" })
            .ToList();

        // Insert in batches
        await migration.InsertDataInBatchesAsync(
            table: "users",
            data: users,
            batchSize: 100);
    }

    public override void Down(MigrationBuilder migration)
    {
        migration.DeleteData("users", "id", "user:1", "user:1000");
    }
}
```

### Data Transformation

```csharp
public class NormalizeEmailFormat : Migration
{
    public override async Task UpAsync(MigrationBuilder migration, CancellationToken ct)
    {
        // Read all data
        var users = await migration.QueryAsync(
            "SELECT id, email FROM users",
            typeof(User));

        // Transform
        foreach (var user in users)
        {
            user.Email = user.Email.ToLowerInvariant().Trim();
        }

        // Write back
        await migration.UpdateDataAsync("users", users);
    }

    public override void Down(MigrationBuilder migration)
    {
        // Cannot reliably reverse email normalization
        throw new NotSupportedException("Cannot rollback email normalization");
    }
}
```

---

## CI/CD Integration

### Automated Migration Checks

```csharp
// In CI pipeline
var validator = new MigrationValidator();

// Check for migration issues
var issues = await validator.ValidateAsync();

if (issues.HasErrors)
{
    foreach (var error in issues.Errors)
        Console.WriteLine($"ERROR: {error.Message}");
    Environment.Exit(1);
}

if (issues.HasWarnings)
{
    foreach (var warning in issues.Warnings)
        Console.WriteLine($"WARNING: {warning.Message}");
}
```

### Pre-Deployment Checks

```csharp
public class PreDeploymentCheck
{
    public async Task ValidateAsync(SurrealDbClient client)
    {
        var migrator = new SurrealDbMigrator(client);

        // Check for pending migrations
        var pending = await migrator.GetPendingMigrationsAsync();
        if (pending.Any())
            throw new InvalidOperationException($"{pending.Count} migrations pending");

        // Check for rollback viability
        var history = await migrator.GetMigrationHistoryAsync();
        var lastFailed = history.LastOrDefault(m => m.Status == MigrationStatus.Failed);
        if (lastFailed != null)
            throw new InvalidOperationException($"Failed migration: {lastFailed.Id}");

        // Validate schema consistency
        var validation = await migrator.ValidateSchemaAsync();
        if (!validation.IsValid)
            throw new InvalidOperationException($"Schema validation failed: {validation.Error}");
    }
}
```

### Rollback Plan

```csharp
public class RollbackPlan
{
    public async Task ExecuteAsync(SurrealDbClient client, int stepsBack)
    {
        var migrator = new SurrealDbMigrator(client);

        // Create backup before rollback
        var backup = await migrator.CreateBackupAsync();
        Console.WriteLine($"Backup created: {backup.Path}");

        try
        {
            // Rollback
            await migrator.RollbackAsync(steps: stepsBack);
            Console.WriteLine($"Rolled back {stepsBack} migrations");
        }
        catch (Exception ex)
        {
            // Restore from backup
            await migrator.RestoreAsync(backup);
            throw;
        }
    }
}
```

---

## Best Practices

1. **Small, focused migrations** - One change per migration
2. **Test rollbacks** - Ensure Down() works
3. **Version control migrations** - Track in git
4. **Backup before production** - Create backup before applying
5. **Review migrations** - Code review before deployment
6. **Document complex changes** - Comment non-obvious logic
7. **Avoid data loss** - Test data transformations
8. **Test in staging** - Apply to staging environment first
9. **Plan rollback** - Have rollback strategy
10. **Monitor application** - Watch for issues after migration

---

## Troubleshooting

### Migration Stuck

```csharp
// Get migration status
var status = await migrator.GetMigrationStatusAsync();
if (status.CurrentMigration.IsStuck)
{
    // Force rollback
    await migrator.ForceRollbackAsync(status.CurrentMigration.Id);
}
```

### Schema Mismatch

```csharp
// Validate schema matches migrations
var validation = await migrator.ValidateSchemaAsync();
if (!validation.IsValid)
{
    Console.WriteLine($"Schema mismatch: {validation.Error}");
    // Regenerate from migrations
    await migrator.RegenerateSchemaAsync();
}
```

### Lock Timeout

```csharp
// Increase migration timeout
var options = new MigrationOptions
{
    LockTimeout = TimeSpan.FromMinutes(5),
    CommandTimeout = TimeSpan.FromMinutes(10)
};

await migrator.MigrateAsync(options);
```

