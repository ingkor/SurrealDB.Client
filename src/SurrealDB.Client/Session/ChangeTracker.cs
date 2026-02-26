namespace SurrealDB.Client.Session;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Tracks changes to entities within a session.
/// Maintains entity state and provides change detection.
/// </summary>
public class ChangeTracker
{
    private readonly Dictionary<object, EntityEntry> _trackedEntities = new();
    private readonly object _lock = new object();

    /// <summary>
    /// Gets the entry for a tracked entity.
    /// </summary>
    public EntityEntry Entry(object entity)
    {
        lock (_lock)
        {
            if (!_trackedEntities.TryGetValue(entity, out var entry))
                throw new InvalidOperationException("Entity is not tracked by this session");
            return entry;
        }
    }

    /// <summary>
    /// Gets the entry for a tracked entity (generic).
    /// </summary>
    public EntityEntry<T> Entry<T>(T entity) where T : class
    {
        var entry = Entry((object)entity);
        return new EntityEntry<T>(entity, entry.State);
    }

    /// <summary>
    /// Tracks a new entity in Added state.
    /// </summary>
    public void TrackEntity<T>(T entity) where T : class
    {
        lock (_lock)
        {
            if (_trackedEntities.ContainsKey(entity))
                return; // Already tracked

            var entry = new EntityEntry(entity, EntityState.Added);
            _trackedEntities[entity] = entry;
        }
    }

    /// <summary>
    /// Tracks an entity retrieved from database in Unchanged state.
    /// Creates a snapshot for change detection.
    /// </summary>
    public void TrackLoadedEntity<T>(T entity, Dictionary<string, object?>? snapshot = null) where T : class
    {
        lock (_lock)
        {
            if (_trackedEntities.ContainsKey(entity))
                return;

            var entry = new EntityEntry(entity, EntityState.Unchanged, snapshot);
            _trackedEntities[entity] = entry;
        }
    }

    /// <summary>
    /// Marks an entity for deletion.
    /// </summary>
    public void MarkDeleted<T>(T entity) where T : class
    {
        lock (_lock)
        {
            if (_trackedEntities.TryGetValue(entity, out var entry))
            {
                entry.State = EntityState.Deleted;
            }
        }
    }

    /// <summary>
    /// Detaches an entity from tracking.
    /// </summary>
    public void Detach<T>(T entity) where T : class
    {
        lock (_lock)
        {
            _trackedEntities.Remove(entity);
        }
    }

    /// <summary>
    /// Gets all tracked entities in Added state.
    /// </summary>
    public IEnumerable<object> GetAddedEntities()
    {
        lock (_lock)
        {
            return _trackedEntities
                .Where(x => x.Value.State == EntityState.Added)
                .Select(x => x.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Gets all tracked entities in Modified state.
    /// </summary>
    public IEnumerable<object> GetModifiedEntities()
    {
        lock (_lock)
        {
            return _trackedEntities
                .Where(x => x.Value.State == EntityState.Modified)
                .Select(x => x.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Gets all tracked entities in Deleted state.
    /// </summary>
    public IEnumerable<object> GetDeletedEntities()
    {
        lock (_lock)
        {
            return _trackedEntities
                .Where(x => x.Value.State == EntityState.Deleted)
                .Select(x => x.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Gets all entities that have changes (Added, Modified, or Deleted).
    /// </summary>
    public IEnumerable<object> GetChangedEntities()
    {
        lock (_lock)
        {
            return _trackedEntities
                .Where(x => x.Value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(x => x.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Detects modifications by comparing current values to snapshot.
    /// Marks Modified entities appropriately.
    /// </summary>
    public void DetectChanges()
    {
        lock (_lock)
        {
            foreach (var entry in _trackedEntities.Values.ToList())
            {
                if (entry.State == EntityState.Unchanged)
                {
                    // Check if entity was modified
                    var isModified = entry.GetModifiedProperties().Any();
                    if (isModified)
                    {
                        entry.State = EntityState.Modified;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clears all tracked entities.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _trackedEntities.Clear();
        }
    }

    /// <summary>
    /// Gets the total count of tracked entities.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _trackedEntities.Count;
            }
        }
    }

    /// <summary>
    /// Gets all tracked entities.
    /// </summary>
    public IEnumerable<object> TrackedEntities
    {
        get
        {
            lock (_lock)
            {
                return _trackedEntities.Keys.ToList();
            }
        }
    }
}
