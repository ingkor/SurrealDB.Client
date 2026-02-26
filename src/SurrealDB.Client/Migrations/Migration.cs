namespace SurrealDB.Client.Migrations;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Base class for defining database migrations.
/// Migrations are versioned schema changes applied to the database.
/// </summary>
public abstract class Migration
{
    /// <summary>
    /// Gets the migration name (usually timestamp-based).
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the migration description.
    /// </summary>
    public virtual string Description => string.Empty;

    /// <summary>
    /// Executes schema changes to apply the migration.
    /// </summary>
    /// <param name="executor">The migration executor</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public abstract Task Up(IMigrationExecutor executor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses the migration (optional).
    /// </summary>
    /// <param name="executor">The migration executor</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task Down(IMigrationExecutor executor, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// Interface for executing migration operations.
/// </summary>
public interface IMigrationExecutor
{
    /// <summary>
    /// Executes a raw SurrealQL statement.
    /// </summary>
    Task ExecuteAsync(string surrealQL, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a table.
    /// </summary>
    Task CreateTableAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a table.
    /// </summary>
    Task DropTableAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a column to a table.
    /// </summary>
    Task AddColumnAsync(
        string tableName,
        string columnName,
        string type,
        Dictionary<string, object>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a column from a table.
    /// </summary>
    Task DropColumnAsync(string tableName, string columnName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a column.
    /// </summary>
    Task RenameColumnAsync(
        string tableName,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an index on a table.
    /// </summary>
    Task CreateIndexAsync(
        string tableName,
        string indexName,
        IEnumerable<string> columns,
        bool unique = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops an index.
    /// </summary>
    Task DropIndexAsync(string tableName, string indexName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Migration metadata for tracking applied migrations.
/// </summary>
public class MigrationInfo
{
    /// <summary>
    /// The migration name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// When the migration was applied.
    /// </summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The migration description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Checksum for integrity verification.
    /// </summary>
    public string? Checksum { get; set; }
}
