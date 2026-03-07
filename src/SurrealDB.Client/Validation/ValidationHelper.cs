namespace SurrealDB.Client.Validation;

using System.Text.RegularExpressions;
using Exceptions;

/// <summary>
/// Provides validation helpers for SurrealDB table names and record IDs.
/// </summary>
internal static class ValidationHelper
{
    /// <summary>
    /// Valid table name pattern: starts with letter/underscore, contains only alphanumeric and underscores, max 64 chars.
    /// </summary>
    private static readonly Regex TableNamePattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$", RegexOptions.Compiled);

    /// <summary>
    /// Valid record ID pattern: alphanumeric, colon, dash, dot, max 255 chars.
    /// </summary>
    private static readonly Regex RecordIdPattern = new(@"^[a-zA-Z0-9_:.\-]{1,255}$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that a table name conforms to SurrealDB naming rules.
    /// </summary>
    /// <exception cref="ValidationException">Thrown if table name is invalid.</exception>
    public static void ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ValidationException("Table name cannot be empty.");

        if (!TableNamePattern.IsMatch(tableName))
            throw new ValidationException(
                $"Invalid table name '{tableName}'. " +
                "Table names must start with a letter or underscore, " +
                "contain only alphanumeric characters and underscores, " +
                "and be at most 64 characters.");
    }

    /// <summary>
    /// Validates that a record ID conforms to SurrealDB ID rules.
    /// </summary>
    /// <exception cref="ValidationException">Thrown if record ID is invalid.</exception>
    public static void ValidateRecordId(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            throw new ValidationException("Record ID cannot be empty.");

        if (!RecordIdPattern.IsMatch(recordId))
            throw new ValidationException(
                $"Invalid record ID '{recordId}'. " +
                "Record IDs must contain only alphanumeric characters, colons, dashes, and dots, " +
                "and be at most 255 characters.");
    }
}
