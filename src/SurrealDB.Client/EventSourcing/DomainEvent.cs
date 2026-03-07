namespace SurrealDB.Client.EventSourcing;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Base interface for domain events.
/// Events represent important domain occurrences that should be logged.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Gets the event ID.
    /// </summary>
    string EventId { get; }

    /// <summary>
    /// Gets the aggregate ID (entity being changed).
    /// </summary>
    string AggregateId { get; }

    /// <summary>
    /// Gets the event type name.
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Gets when the event occurred.
    /// </summary>
    DateTime OccurredAt { get; }

    /// <summary>
    /// Gets the event version/sequence.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// Gets user ID who caused the event.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the event metadata.
    /// </summary>
    Dictionary<string, object>? Metadata { get; }
}

/// <summary>
/// Abstract base class for domain events.
/// </summary>
public abstract class DomainEventBase : IDomainEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public virtual string AggregateId { get; set; } = string.Empty;
    public virtual string EventType => GetType().Name;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
    public string? UserId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Event store interface for persisting domain events.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends an event to the event log.
    /// </summary>
    Task AppendEventAsync(IDomainEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple events.
    /// </summary>
    Task AppendEventsAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events for an aggregate.
    /// </summary>
    Task<IEnumerable<IDomainEvent>> GetEventsAsync(string aggregateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events since a specific version.
    /// </summary>
    Task<IEnumerable<IDomainEvent>> GetEventsSinceAsync(
        string aggregateId,
        int sinceVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets event count for an aggregate.
    /// </summary>
    Task<int> GetEventCountAsync(string aggregateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events (for projections).
    /// </summary>
    Task<IEnumerable<IDomainEvent>> GetAllEventsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event publisher for distributing events.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event.
    /// </summary>
    Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes multiple events.
    /// </summary>
    Task PublishManyAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event sourcing manager for replay and projection.
/// </summary>
public class EventSourcingManager
{
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _eventPublisher;

    public EventSourcingManager(IEventStore eventStore, IEventPublisher eventPublisher)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// Records an event and publishes it.
    /// </summary>
    public async Task RecordEventAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Store the event
        await _eventStore.AppendEventAsync(@event, cancellationToken).ConfigureAwait(false);

        // Publish it
        await _eventPublisher.PublishAsync(@event, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records multiple events atomically.
    /// </summary>
    public async Task RecordEventsAsync(
        IEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        var eventList = events.ToList();

        // Store all events
        await _eventStore.AppendEventsAsync(eventList, cancellationToken).ConfigureAwait(false);

        // Publish them
        await _eventPublisher.PublishManyAsync(eventList, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds an aggregate from its event history.
    /// </summary>
    public async Task<IEnumerable<IDomainEvent>> ReplayEventsAsync(
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        return await _eventStore.GetEventsAsync(aggregateId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets events for projection.
    /// </summary>
    public async Task<IEnumerable<IDomainEvent>> GetProjectionEventsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _eventStore.GetAllEventsAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Common domain events.
/// </summary>
public class EntityCreatedEvent : DomainEventBase
{
    public string EntityType { get; set; } = string.Empty;
    public Dictionary<string, object>? InitialData { get; set; }
}

public class EntityUpdatedEvent : DomainEventBase
{
    public Dictionary<string, object>? Changes { get; set; }
}

public class EntityDeletedEvent : DomainEventBase
{
    public string Reason { get; set; } = string.Empty;
}
