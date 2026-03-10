namespace SurrealDB.Client.Migrations;

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Discovers, orders, and applies <see cref="Migration"/> subclasses from a given assembly.
/// <para>
/// MigrateAsync is idempotent: running it twice applies nothing the second time.
/// </para>
/// <para>
/// WARNING: MigrateAsync is not safe for concurrent execution across multiple application
/// instances. Use application-level locking for multi-instance deployments.
/// </para>
/// </summary>
internal sealed class SurrealMigrationRunner
{
    /// <summary>The SurrealDB table used to track applied migrations.</summary>
    public const string HistoryTable = "_migrations";

    private readonly ISurrealDbClient _client;

    /// <summary>
    /// Initializes a new instance of <see cref="SurrealMigrationRunner"/>.
    /// </summary>
    public SurrealMigrationRunner(ISurrealDbClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Applies all pending migrations from <paramref name="assembly"/> in ascending name order.
    /// Already-applied migrations are skipped.
    /// </summary>
    public async Task MigrateAsync(Assembly assembly, CancellationToken cancellationToken = default)
    {
        await EnsureHistoryTableAsync(cancellationToken).ConfigureAwait(false);

        var migrations = DiscoverMigrations(assembly);
        var applied = await GetAppliedMigrationNamesAsync(cancellationToken).ConfigureAwait(false);

        var pending = migrations
            .Where(m => !applied.Contains(m.Name))
            .OrderBy(m => m.Name);

        foreach (var migration in pending)
        {
            var executor = new SurrealMigrationExecutor(_client);
            try
            {
                await migration.Up(executor, cancellationToken).ConfigureAwait(false);
                await RecordMigrationAsync(migration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw MigrationException.Create(migration.Name, "Migration failed during Up()", ex);
            }
        }
    }

    /// <summary>
    /// Rolls back the migration identified by <paramref name="migrationName"/>.
    /// </summary>
    public async Task RollbackAsync(string migrationName, Assembly assembly, CancellationToken cancellationToken = default)
    {
        var migrations = DiscoverMigrations(assembly);
        var target = migrations.FirstOrDefault(m => m.Name == migrationName);

        if (target == null)
            throw MigrationException.Create(migrationName, "Migration not found in assembly");

        var executor = new SurrealMigrationExecutor(_client);
        await target.Down(executor, cancellationToken).ConfigureAwait(false);
        await RemoveMigrationRecordAsync(migrationName, cancellationToken).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    internal List<Migration> DiscoverMigrations(Assembly assembly)
        => assembly.GetTypes()
                   .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Migration)))
                   .Select(t => (Migration)Activator.CreateInstance(t)!)
                   .ToList();

    private async Task EnsureHistoryTableAsync(CancellationToken cancellationToken)
        => await _client.QueryAsync($"DEFINE TABLE {HistoryTable} SCHEMALESS;", null, cancellationToken).ConfigureAwait(false);

    private async Task<List<string>> GetAppliedMigrationNamesAsync(CancellationToken cancellationToken)
    {
        var results = await _client
            .QueryAsync<MigrationInfo>($"SELECT name, checksum FROM {HistoryTable} ORDER BY name ASC;", null, cancellationToken)
            .ConfigureAwait(false);

        return results.Select(m => m.Name).ToList();
    }

    private async Task RecordMigrationAsync(Migration migration, CancellationToken cancellationToken)
    {
        var info = new MigrationInfo
        {
            Name = migration.Name,
            Description = migration.Description,
            AppliedAt = DateTime.UtcNow,
            Checksum = ComputeChecksum(migration)
        };

        var json = JsonSerializer.Serialize(info, MigrationInfoContext.Default.MigrationInfo);
        await _client.QueryAsync($"CREATE {HistoryTable} CONTENT {json};", null, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveMigrationRecordAsync(string migrationName, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object> { ["name"] = migrationName };
        await _client.QueryAsync($"DELETE {HistoryTable} WHERE name = $name;", parameters, cancellationToken).ConfigureAwait(false);
    }

    internal static string ComputeChecksum(Migration migration)
    {
        var input = Encoding.UTF8.GetBytes(migration.Name + migration.Description);
        var hash = SHA256.HashData(input);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

/// <summary>Source-generated JSON serializer context for <see cref="MigrationInfo"/>.</summary>
[JsonSerializable(typeof(MigrationInfo))]
internal partial class MigrationInfoContext : JsonSerializerContext { }
