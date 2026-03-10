namespace SurrealDB.Client.Migrations;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Concrete <see cref="IMigrationExecutor"/> backed by an <see cref="ISurrealDbClient"/>.
/// Translates schema operations into SurrealQL and executes them via QueryAsync.
/// </summary>
internal sealed class SurrealMigrationExecutor : IMigrationExecutor
{
    private readonly ISurrealDbClient _client;

    /// <summary>
    /// Initializes a new instance of <see cref="SurrealMigrationExecutor"/>.
    /// </summary>
    public SurrealMigrationExecutor(ISurrealDbClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(string surrealQL, CancellationToken cancellationToken = default)
        => await _client.QueryAsync(surrealQL, null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task CreateTableAsync(string tableName, CancellationToken cancellationToken = default)
        => await ExecuteAsync($"DEFINE TABLE {Escape(tableName)} SCHEMALESS;", cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task DropTableAsync(string tableName, CancellationToken cancellationToken = default)
        => await ExecuteAsync($"REMOVE TABLE {Escape(tableName)};", cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task AddColumnAsync(
        string tableName,
        string columnName,
        string type,
        Dictionary<string, object>? options = null,
        CancellationToken cancellationToken = default)
    {
        var sql = $"DEFINE FIELD {Escape(columnName)} ON TABLE {Escape(tableName)} TYPE {type}";

        if (options != null)
        {
            if (options.TryGetValue("default", out var defaultVal))
                sql += $" DEFAULT {defaultVal}";
            if (options.TryGetValue("assert", out var assert))
                sql += $" ASSERT {assert}";
        }

        sql += ";";
        await ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DropColumnAsync(string tableName, string columnName, CancellationToken cancellationToken = default)
        => await ExecuteAsync($"REMOVE FIELD {Escape(columnName)} ON TABLE {Escape(tableName)};", cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Renames a column by copying data then removing the old field.
    /// <para>
    /// WARNING: This operation is not atomic. If the UPDATE succeeds but REMOVE FIELD fails,
    /// data will exist in both columns. Wrap in a migration you can manually roll back.
    /// </para>
    /// </summary>
    public async Task RenameColumnAsync(
        string tableName,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync($"UPDATE {Escape(tableName)} SET {Escape(newName)} = {Escape(oldName)};", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync($"REMOVE FIELD {Escape(oldName)} ON TABLE {Escape(tableName)};", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CreateIndexAsync(
        string tableName,
        string indexName,
        IEnumerable<string> columns,
        bool unique = false,
        CancellationToken cancellationToken = default)
    {
        var cols = string.Join(", ", columns.Select(Escape));
        var uniqueClause = unique ? " UNIQUE" : string.Empty;
        var sql = $"DEFINE INDEX {Escape(indexName)} ON TABLE {Escape(tableName)} FIELDS {cols}{uniqueClause};";
        await ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DropIndexAsync(string tableName, string indexName, CancellationToken cancellationToken = default)
        => await ExecuteAsync($"REMOVE INDEX {Escape(indexName)} ON TABLE {Escape(tableName)};", cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Escapes an identifier by wrapping it in backticks.
    /// Any embedded backtick characters are escaped as \`.
    /// </summary>
    private static string Escape(string identifier)
        => $"`{identifier.Replace("`", "\\`")}`";
}
