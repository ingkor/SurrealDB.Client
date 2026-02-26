namespace SurrealDB.Client.Session;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents a session (Unit of Work) with SurrealDB.
/// Manages entity tracking, change detection, and atomic SaveChangesAsync.
/// </summary>
public interface ISurrealDbSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the change tracker for this session.
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>
    /// Creates an IQueryable<T> for querying a table.
    /// Supports deferred execution and composition.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="table">The table name</param>
    /// <returns>IQueryable for composition</returns>
    IQueryable<T> Set<T>(string table) where T : class;

    /// <summary>
    /// Adds a new entity to the session for insertion.
    /// Sets entity state to Added.
    /// </summary>
    void Add<T>(T entity) where T : class;

    /// <summary>
    /// Adds multiple entities for insertion.
    /// </summary>
    void AddRange<T>(IEnumerable<T> entities) where T : class;

    /// <summary>
    /// Updates an existing entity.
    /// Sets entity state to Modified for differential updates.
    /// </summary>
    void Update<T>(T entity) where T : class;

    /// <summary>
    /// Updates multiple entities.
    /// </summary>
    void UpdateRange<T>(IEnumerable<T> entities) where T : class;

    /// <summary>
    /// Marks an entity for deletion.
    /// Sets entity state to Deleted.
    /// </summary>
    void Remove<T>(T entity) where T : class;

    /// <summary>
    /// Marks multiple entities for deletion.
    /// </summary>
    void RemoveRange<T>(IEnumerable<T> entities) where T : class;

    /// <summary>
    /// Retrieves an entity by its primary key.
    /// Returns null if not found.
    /// Tracks entity in session.
    /// </summary>
    Task<T?> FindAsync<T>(string recordId, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Saves all tracked changes atomically.
    /// Executes INSERT for Added, UPDATE for Modified, DELETE for Deleted.
    /// </summary>
    /// <returns>Number of entities affected</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads an entity from the database, discarding modifications.
    /// </summary>
    Task<T> ReloadAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Detaches an entity from change tracking.
    /// </summary>
    void Detach<T>(T entity) where T : class;

    /// <summary>
    /// Detaches all entities from change tracking.
    /// </summary>
    void DetachAll();

    /// <summary>
    /// Clears all pending changes without saving.
    /// Reverts all entities to their last known state.
    /// </summary>
    void Discard();

    /// <summary>
    /// Begins a transaction for this session.
    /// All changes are committed atomically in SaveChangesAsync.
    /// </summary>
    ISurrealDbSessionTransaction BeginTransaction();

    /// <summary>
    /// Gets whether the session has pending changes.
    /// </summary>
    bool HasChanges { get; }

    /// <summary>
    /// Gets whether the session is disposed.
    /// </summary>
    bool IsDisposed { get; }
}

/// <summary>
/// Represents a transaction within a session.
/// </summary>
public interface ISurrealDbSessionTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction.
    /// All changes are persisted atomically.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction.
    /// All changes are discarded.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether this transaction is active.
    /// </summary>
    bool IsActive { get; }
}
