namespace SurrealDB.Client.Session;

/// <summary>
/// Represents the state of an entity tracked by ISurrealDbSession.
/// </summary>
public enum EntityState
{
    /// <summary>
    /// Entity is not tracked by any session.
    /// </summary>
    Detached = 0,

    /// <summary>
    /// Entity exists in database with no modifications.
    /// A snapshot has been created for change detection.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// Entity marked for deletion.
    /// Will be deleted when SaveChangesAsync() is called.
    /// </summary>
    Deleted = 2,

    /// <summary>
    /// Entity loaded from database and has been modified.
    /// A snapshot exists for comparison.
    /// </summary>
    Modified = 3,

    /// <summary>
    /// New entity added to session for insertion.
    /// No snapshot exists (new entity).
    /// Will be inserted when SaveChangesAsync() is called.
    /// </summary>
    Added = 4
}
