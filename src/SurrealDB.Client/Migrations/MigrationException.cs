namespace SurrealDB.Client.Migrations;

using SurrealDB.Client.Exceptions;

/// <summary>
/// Thrown when a database migration fails to apply or roll back.
/// </summary>
public class MigrationException : SurrealDbException
{
    /// <summary>
    /// Gets the name of the migration that caused the failure.
    /// </summary>
    public string MigrationName { get; }

    private MigrationException(string migrationName, string message, Exception? inner = null)
        : base(message, inner)
    {
        MigrationName = migrationName;
    }

    /// <summary>
    /// Creates a <see cref="MigrationException"/> for the specified migration.
    /// </summary>
    public static MigrationException Create(string migrationName, string message, Exception? inner = null)
        => new(migrationName, $"[{migrationName}] {message}", inner);
}
