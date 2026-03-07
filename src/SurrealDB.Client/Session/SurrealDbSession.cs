namespace SurrealDB.Client.Session;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Concurrency;
using Query;

/// <summary>
/// Implementation of ISurrealDbSession providing Unit of Work pattern.
/// </summary>
internal class SurrealDbSession : ISurrealDbSession
{
    private readonly SurrealDbClient _client;
    private readonly ChangeTracker _changeTracker;
    private bool _disposed;
    private SurrealDbSessionTransaction? _transaction;

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
        return new SurrealDbQuery<T>(provider);
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
        foreach (var entity in addedEntities)
        {
            var tableName = GetTableName(entity.GetType());
            await _client.CreateAsync(tableName, entity, cancellationToken).ConfigureAwait(false);

            var entry = _changeTracker.Entry(entity);
            entry.State = EntityState.Unchanged;
            affectedCount++;
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

                        await _client.UpdateAsync(recordId, entity, cancellationToken).ConfigureAwait(false);
                        entry.State = EntityState.Unchanged;
                        affectedCount++;
                    }
                }
            }
        }

        // Execute DELETES for Deleted entities
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SurrealDbSession));
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

        // All SaveChangesAsync calls within the transaction will be committed
        _isActive = false;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("Transaction is not active");

        _session.Discard();
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
