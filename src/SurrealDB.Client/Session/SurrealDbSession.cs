namespace SurrealDB.Client.Session;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Concurrency;
using Query;
using SurrealDB.Client.Security;

/// <summary>
/// Implementation of ISurrealDbSession providing Unit of Work pattern.
/// </summary>
internal class SurrealDbSession : ISurrealDbSession
{
    private readonly SurrealDbClient _client;
    private readonly ChangeTracker _changeTracker;
    private bool _disposed;
    private SurrealDbSessionTransaction? _transaction;
    private string? _currentUserId;

    // Reflection cache — populated once per type, then lock-free reads
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _createdAtProps = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _updatedAtProps = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _createdByProps = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _updatedByProps = new();

    public SurrealDbSession(SurrealDbClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _changeTracker = new ChangeTracker();
    }

    public ChangeTracker ChangeTracker => _changeTracker;

    public bool HasChanges => _changeTracker.GetChangedEntities().Any();

    public bool IsDisposed => _disposed;

    public IQueryable<T> Set<T>(string table) where T : class
    {
        ThrowIfDisposed();
        var compiler = new SurrealQueryCompiler();
        var interceptors = _client.GetInterceptors();
        var provider = new SurrealDbQueryProvider(
            this,
            _client,
            compiler,
            table,
            _client.QueryCache,
            interceptors);
        return new SurrealDbQuery<T>(provider, tableName: table);
    }

    public void Add<T>(T entity) where T : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);

        _changeTracker.TrackEntity(entity);
    }

    public void AddRange<T>(IEnumerable<T> entities) where T : class
    {
        foreach (var entity in entities)
            Add(entity);
    }

    public void Update<T>(T entity) where T : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);

        var entry = _changeTracker.Entry(entity);
        entry.State = EntityState.Modified;
    }

    public void UpdateRange<T>(IEnumerable<T> entities) where T : class
    {
        foreach (var entity in entities)
            Update(entity);
    }

    public void Remove<T>(T entity) where T : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);

        _changeTracker.MarkDeleted(entity);
    }

    public void RemoveRange<T>(IEnumerable<T> entities) where T : class
    {
        foreach (var entity in entities)
            Remove(entity);
    }

    public async Task<T?> FindAsync<T>(string recordId, CancellationToken cancellationToken = default) where T : class
    {
        ThrowIfDisposed();

        var entity = await _client.GetAsync<T>(recordId, cancellationToken).ConfigureAwait(false);

        if (entity != null)
        {
            // Create snapshot for change detection
            var snapshot = ExtractSnapshot(entity);
            _changeTracker.TrackLoadedEntity(entity, snapshot);
        }

        return entity;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Detect changes in tracked entities
        _changeTracker.DetectChanges();

        var affectedCount = 0;

        // Get entities by state
        var addedEntities = _changeTracker.GetAddedEntities().ToList();
        var modifiedEntities = _changeTracker.GetModifiedEntities().ToList();
        var deletedEntities = _changeTracker.GetDeletedEntities().ToList();

        // Execute within transaction if one exists, otherwise use implicit transaction
        if (_transaction?.IsActive ?? false)
        {
            affectedCount = await ExecuteChangesInTransaction(
                addedEntities, modifiedEntities, deletedEntities, cancellationToken);
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Use temporary transaction
            await using var txn = BeginTransaction();
            affectedCount = await ExecuteChangesInTransaction(
                addedEntities, modifiedEntities, deletedEntities, cancellationToken);
            await txn.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return affectedCount;
    }

    private async Task<int> ExecuteChangesInTransaction(
        List<object> addedEntities,
        List<object> modifiedEntities,
        List<object> deletedEntities,
        CancellationToken cancellationToken)
    {
        var affectedCount = 0;

        // Execute INSERTS for Added entities
        if (addedEntities.Count >= _client.Options.BatchThreshold)
        {
            foreach (var group in addedEntities.GroupBy(e => GetTableName(e.GetType())))
            {
                foreach (var entity in group)
                    try { ApplyAuditAttributes(entity, isInsert: true); } catch { /* suppress */ }
                var typedData = group.Cast<object>().ToList();
                await _client.CreateManyAsync<object>(group.Key, typedData, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var entity in group)
                    _changeTracker.Entry(entity).State = EntityState.Unchanged;
                affectedCount += typedData.Count;
            }
        }
        else
        {
            foreach (var entity in addedEntities)
            {
                var tableName = GetTableName(entity.GetType());
                try { ApplyAuditAttributes(entity, isInsert: true); } catch { /* suppress */ }
                await _client.CreateAsync(tableName, entity, cancellationToken).ConfigureAwait(false);

                var entry = _changeTracker.Entry(entity);
                entry.State = EntityState.Unchanged;
                affectedCount++;
            }
        }

        // Execute UPDATES for Modified entities
        foreach (var entity in modifiedEntities)
        {
            var entry = _changeTracker.Entry(entity);
            var changes = entry.GetModifiedProperties();

            if (changes.Any())
            {
                // Get record ID from entity
                var idProperty = entity.GetType().GetProperty("Id");
                if (idProperty != null)
                {
                    var recordId = idProperty.GetValue(entity)?.ToString();
                    if (recordId != null)
                    {
                        // Check for concurrency token
                        var concurrencyProperty = ConcurrencyTokenManager.GetConcurrencyTokenProperty(entity.GetType());
                        if (concurrencyProperty != null)
                        {
                            // Get the expected (original) token value
                            var expectedToken = entry.GetOriginalValue(concurrencyProperty.Name);

                            // Load from database to check current token
                            // Use QueryAsync since GetAsync<T> requires a compile-time type
                            var dbResult = await _client.QueryAsync($"SELECT * FROM {recordId};", null, cancellationToken)
                                .ConfigureAwait(false);
                            var dbEntity = dbResult.Data;

                            if (dbEntity != null)
                            {
                                var actualToken = concurrencyProperty.GetValue(dbEntity);

                                // Check for conflict
                                if (!ConcurrencyTokenManager.HasNoConflict(expectedToken, actualToken))
                                {
                                    throw DbUpdateConcurrencyException.Create(
                                        recordId,
                                        expectedToken,
                                        actualToken);
                                }

                                // Update the token (increment or regenerate)
                                var newToken = ConcurrencyTokenManager.IncrementToken(actualToken);
                                concurrencyProperty.SetValue(entity, newToken);
                            }
                        }

                        try { ApplyAuditAttributes(entity, isInsert: false); } catch { /* suppress */ }
                        await _client.UpdateAsync(recordId, entity, cancellationToken).ConfigureAwait(false);
                        entry.State = EntityState.Unchanged;
                        affectedCount++;
                    }
                }
            }
        }

        // Execute DELETES for Deleted entities
        if (deletedEntities.Count >= _client.Options.BatchThreshold)
        {
            var idsToDelete = new List<string>();
            var entitiesToDetach = new List<object>();
            foreach (var entity in deletedEntities)
            {
                var idProperty = entity.GetType().GetProperty("Id");
                var recordId = idProperty?.GetValue(entity)?.ToString();
                if (recordId != null)
                {
                    idsToDelete.Add(recordId);
                    entitiesToDetach.Add(entity);
                }
            }
            if (idsToDelete.Count > 0)
            {
                await _client.DeleteManyAsync(idsToDelete, cancellationToken).ConfigureAwait(false);
                foreach (var entity in entitiesToDetach)
                    _changeTracker.Detach(entity);
                affectedCount += idsToDelete.Count;
            }
        }
        else
        {
            foreach (var entity in deletedEntities)
            {
                var idProperty = entity.GetType().GetProperty("Id");
                if (idProperty != null)
                {
                    var recordId = idProperty.GetValue(entity)?.ToString();
                    if (recordId != null)
                    {
                        await _client.DeleteAsync(recordId, cancellationToken).ConfigureAwait(false);
                        _changeTracker.Detach(entity);
                        affectedCount++;
                    }
                }
            }
        }

        return affectedCount;
    }

    public async Task<T> ReloadAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        ThrowIfDisposed();

        var idProperty = typeof(T).GetProperty("Id");
        if (idProperty == null)
            throw new InvalidOperationException("Entity must have an 'Id' property");

        var recordId = idProperty.GetValue(entity)?.ToString();
        if (recordId == null)
            throw new InvalidOperationException("Entity Id cannot be null");

        var reloaded = await _client.GetAsync<T>(recordId, cancellationToken).ConfigureAwait(false);
        if (reloaded != null)
        {
            var snapshot = ExtractSnapshot(reloaded);
            _changeTracker.TrackLoadedEntity(reloaded, snapshot);
        }

        return reloaded!;
    }

    public void Detach<T>(T entity) where T : class
    {
        ThrowIfDisposed();
        _changeTracker.Detach(entity);
    }

    public void DetachAll()
    {
        ThrowIfDisposed();
        _changeTracker.Clear();
    }

    public void Discard()
    {
        ThrowIfDisposed();
        foreach (var entity in _changeTracker.TrackedEntities.ToList())
        {
            var entry = _changeTracker.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Unchanged;
            }
        }
    }

    public ISurrealDbSessionTransaction BeginTransaction()
    {
        ThrowIfDisposed();
        _transaction = new SurrealDbSessionTransaction(this);
        return _transaction;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _changeTracker.Clear();

            if (_transaction != null)
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public void SetCurrentUser(string? userId) => _currentUserId = userId;

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SurrealDbSession));
    }

    private static PropertyInfo[] GetPropertiesWithAttribute<TAttr>(Type type) where TAttr : Attribute
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Where(p => p.GetCustomAttribute<TAttr>() != null && p.CanWrite)
               .ToArray();

    /// <summary>
    /// Applies audit attributes to <paramref name="entity"/> before persistence.
    /// All exceptions are suppressed — audit is best-effort.
    /// </summary>
    private void ApplyAuditAttributes(object entity, bool isInsert)
    {
        var type = entity.GetType();
        var now = DateTime.UtcNow;

        if (isInsert)
        {
            foreach (var prop in _createdAtProps.GetOrAdd(type, t => GetPropertiesWithAttribute<CreatedAtAttribute>(t)))
            {
                if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
                    prop.SetValue(entity, now);
                else if (prop.PropertyType == typeof(DateTimeOffset) || prop.PropertyType == typeof(DateTimeOffset?))
                    prop.SetValue(entity, new DateTimeOffset(now));
            }

            if (_currentUserId != null)
            {
                foreach (var prop in _createdByProps.GetOrAdd(type, t => GetPropertiesWithAttribute<CreatedByAttribute>(t)))
                {
                    if (prop.PropertyType == typeof(string))
                        prop.SetValue(entity, _currentUserId);
                }
            }
        }

        foreach (var prop in _updatedAtProps.GetOrAdd(type, t => GetPropertiesWithAttribute<UpdatedAtAttribute>(t)))
        {
            if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
                prop.SetValue(entity, now);
            else if (prop.PropertyType == typeof(DateTimeOffset) || prop.PropertyType == typeof(DateTimeOffset?))
                prop.SetValue(entity, new DateTimeOffset(now));
        }

        if (_currentUserId != null)
        {
            foreach (var prop in _updatedByProps.GetOrAdd(type, t => GetPropertiesWithAttribute<UpdatedByAttribute>(t)))
            {
                if (prop.PropertyType == typeof(string))
                    prop.SetValue(entity, _currentUserId);
            }
        }
    }

    private Dictionary<string, object?> ExtractSnapshot<T>(T entity) where T : class
    {
        var snapshot = new Dictionary<string, object?>();
        var properties = typeof(T).GetProperties();

        foreach (var prop in properties)
        {
            snapshot[prop.Name] = prop.GetValue(entity);
        }

        return snapshot;
    }

    internal async Task SendRollbackAsync(CancellationToken cancellationToken = default)
    {
        await _client.QueryAsync("ROLLBACK TRANSACTION;", null, cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetTableName(Type entityType)
    {
        // Get table name from entity type
        // For now, use lowercase type name
        return entityType.Name.ToLower();
    }
}

/// <summary>
/// Transaction implementation for sessions.
/// </summary>
internal class SurrealDbSessionTransaction : ISurrealDbSessionTransaction
{
    private readonly SurrealDbSession _session;
    private bool _isActive;
    private bool _disposed;

    public SurrealDbSessionTransaction(SurrealDbSession session)
    {
        _session = session;
        _isActive = true;
    }

    public bool IsActive => _isActive && !_disposed;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("Transaction is not active");

        if (_session.HasChanges)
            throw new InvalidOperationException(
                "Cannot commit: session has unsaved changes. Call SaveChangesAsync before CommitAsync.");

        _isActive = false;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("Transaction is not active");

        _session.Discard();

        try
        {
            await _session.SendRollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // suppress — if connection is gone we still want to clean up local state
        }

        _isActive = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_isActive)
            {
                await RollbackAsync().ConfigureAwait(false);
            }
        }
    }
}
