namespace SurrealDB.Client.Session;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Provides access to state and change tracking information for a tracked entity.
/// </summary>
public class EntityEntry<T> where T : class
{
    private readonly T _entity;
    private readonly Dictionary<string, object?> _snapshot;
    private EntityState _state;
    private readonly Dictionary<string, object?> _originalValues;

    /// <summary>
    /// Creates a new entity entry for tracking.
    /// </summary>
    public EntityEntry(T entity, EntityState state, Dictionary<string, object?>? snapshot = null)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _state = state;
        _snapshot = snapshot ?? new Dictionary<string, object?>();
        _originalValues = new Dictionary<string, object?>(_snapshot);
    }

    /// <summary>
    /// Gets the tracked entity.
    /// </summary>
    public T Entity => _entity;

    /// <summary>
    /// Gets or sets the current entity state.
    /// </summary>
    public EntityState State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// Gets the original value of a property (from snapshot).
    /// </summary>
    public object? GetOriginalValue(string propertyName)
    {
        if (!_originalValues.TryGetValue(propertyName, out var value))
            throw new InvalidOperationException($"Property '{propertyName}' not found in snapshot");
        return value;
    }

    /// <summary>
    /// Gets the current value of a property from the entity.
    /// </summary>
    public object? GetCurrentValue(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"Property '{propertyName}' not found on type {typeof(T).Name}");
        return property.GetValue(_entity);
    }

    /// <summary>
    /// Gets list of property names that have been modified.
    /// </summary>
    public IEnumerable<string> GetModifiedProperties()
    {
        var properties = typeof(T).GetProperties();
        foreach (var prop in properties)
        {
            if (!_originalValues.TryGetValue(prop.Name, out var original))
                continue;

            var current = prop.GetValue(_entity);

            // Check if values differ
            if (!Equals(original, current))
                yield return prop.Name;
        }
    }

    /// <summary>
    /// Reloads entity from snapshot, discarding modifications.
    /// </summary>
    public void Reload()
    {
        var properties = typeof(T).GetProperties();
        foreach (var prop in properties)
        {
            if (_snapshot.TryGetValue(prop.Name, out var value))
                prop.SetValue(_entity, value);
        }

        _state = EntityState.Unchanged;
    }

    /// <summary>
    /// Creates a snapshot of the current entity state.
    /// Used when entity is loaded from database.
    /// </summary>
    public void CreateSnapshot()
    {
        _snapshot.Clear();
        var properties = typeof(T).GetProperties();
        foreach (var prop in properties)
        {
            _snapshot[prop.Name] = prop.GetValue(_entity);
        }

        _originalValues.Clear();
        foreach (var kv in _snapshot) _originalValues[kv.Key] = kv.Value;
        _state = EntityState.Unchanged;
    }

    /// <summary>
    /// Detects if entity has been modified since snapshot.
    /// </summary>
    public bool IsModified()
    {
        if (_state == EntityState.Added || _state == EntityState.Deleted)
            return true;

        return GetModifiedProperties().Any();
    }

    /// <summary>
    /// Returns a dictionary of all modified properties with old and new values.
    /// </summary>
    public Dictionary<string, (object? Old, object? New)> GetChanges()
    {
        var changes = new Dictionary<string, (object?, object?)>();
        var properties = typeof(T).GetProperties();

        foreach (var prop in properties)
        {
            if (!_originalValues.TryGetValue(prop.Name, out var original))
                continue;

            var current = prop.GetValue(_entity);

            if (!Equals(original, current))
                changes[prop.Name] = (original, current);
        }

        return changes;
    }
}

/// <summary>
/// Non-generic version of EntityEntry.
/// </summary>
public class EntityEntry
{
    private readonly object _entity;
    private readonly Dictionary<string, object?> _snapshot;
    private EntityState _state;

    public EntityEntry(object entity, EntityState state, Dictionary<string, object?>? snapshot = null)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _state = state;
        _snapshot = snapshot ?? new Dictionary<string, object?>();
    }

    public object Entity => _entity;

    public EntityState State
    {
        get => _state;
        set => _state = value;
    }

    public Type EntityType => _entity.GetType();

    public object? GetOriginalValue(string propertyName)
    {
        if (!_snapshot.TryGetValue(propertyName, out var value))
            throw new InvalidOperationException($"Property '{propertyName}' not found in snapshot");
        return value;
    }

    public object? GetCurrentValue(string propertyName)
    {
        var property = EntityType.GetProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"Property '{propertyName}' not found");
        return property.GetValue(_entity);
    }

    public IEnumerable<string> GetModifiedProperties()
    {
        var properties = EntityType.GetProperties();
        foreach (var prop in properties)
        {
            if (!_snapshot.TryGetValue(prop.Name, out var original))
                continue;

            var current = prop.GetValue(_entity);

            if (!Equals(original, current))
                yield return prop.Name;
        }
    }
}
