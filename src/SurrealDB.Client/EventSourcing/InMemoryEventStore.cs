namespace SurrealDB.Client.EventSourcing;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// In-memory event store implementation.
/// Suitable for testing and small-scale scenarios.
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, List<IDomainEvent>> _events = new();
    private readonly object _lock = new();
    private int _eventCounter;

    /// <summary>
    /// Appends an event to the store.
    /// </summary>
    public Task AppendEventAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        lock (_lock)
        {
            @event.Version = ++_eventCounter;

            _events.AddOrUpdate(
                @event.AggregateId,
                new List<IDomainEvent> { @event },
                (_, list) =>
                {
                    list.Add(@event);
                    return list;
                });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Appends multiple events atomically.
    /// </summary>
    public Task AppendEventsAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        var eventList = events.ToList();

        lock (_lock)
        {
            foreach (var @event in eventList)
            {
                @event.Version = ++_eventCounter;

                _events.AddOrUpdate(
                    @event.AggregateId,
                    new List<IDomainEvent> { @event },
                    (_, list) =>
                    {
                        list.Add(@event);
                        return list;
                    });
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets all events for an aggregate.
    /// </summary>
    public Task<IEnumerable<IDomainEvent>> GetEventsAsync(
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId);

        if (_events.TryGetValue(aggregateId, out var events))
        {
            return Task.FromResult<IEnumerable<IDomainEvent>>(events.ToList());
        }

        return Task.FromResult<IEnumerable<IDomainEvent>>(new List<IDomainEvent>());
    }

    /// <summary>
    /// Gets events since a version.
    /// </summary>
    public Task<IEnumerable<IDomainEvent>> GetEventsSinceAsync(
        string aggregateId,
        int sinceVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId);

        if (_events.TryGetValue(aggregateId, out var events))
        {
            var filtered = events.Where(e => e.Version > sinceVersion).ToList();
            return Task.FromResult<IEnumerable<IDomainEvent>>(filtered);
        }

        return Task.FromResult<IEnumerable<IDomainEvent>>(new List<IDomainEvent>());
    }

    /// <summary>
    /// Gets event count for an aggregate.
    /// </summary>
    public Task<int> GetEventCountAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId);

        if (_events.TryGetValue(aggregateId, out var events))
        {
            return Task.FromResult(events.Count);
        }

        return Task.FromResult(0);
    }

    /// <summary>
    /// Gets all events (for projections).
    /// </summary>
    public Task<IEnumerable<IDomainEvent>> GetAllEventsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var allEvents = _events.Values
                .SelectMany(list => list)
                .OrderBy(e => e.Version)
                .ToList();

            return Task.FromResult<IEnumerable<IDomainEvent>>(allEvents);
        }
    }

    /// <summary>
    /// Clears all events (for testing).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
            _eventCounter = 0;
        }
    }

    /// <summary>
    /// Gets statistics.
    /// </summary>
    public (int AggregateCount, int EventCount) GetStatistics()
    {
        lock (_lock)
        {
            var eventCount = _events.Values.Sum(list => list.Count);
            return (_events.Count, eventCount);
        }
    }
}

/// <summary>
/// In-memory event publisher implementation.
/// For testing and local scenarios.
/// </summary>
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly List<Func<IDomainEvent, Task>> _subscribers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Subscribes to events.
    /// </summary>
    public void Subscribe(Func<IDomainEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            _subscribers.Add(handler);
        }
    }

    /// <summary>
    /// Unsubscribes from events.
    /// </summary>
    public void Unsubscribe(Func<IDomainEvent, Task> handler)
    {
        lock (_lock)
        {
            _subscribers.Remove(handler);
        }
    }

    /// <summary>
    /// Publishes an event to all subscribers.
    /// </summary>
    public async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        List<Func<IDomainEvent, Task>> handlers;

        lock (_lock)
        {
            handlers = _subscribers.ToList();
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(@event).ConfigureAwait(false);
            }
            catch
            {
                // Ignore subscriber errors
            }
        }
    }

    /// <summary>
    /// Publishes multiple events.
    /// </summary>
    public async Task PublishManyAsync(
        IEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        foreach (var @event in events)
        {
            await PublishAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets subscriber count.
    /// </summary>
    public int GetSubscriberCount()
    {
        lock (_lock)
        {
            return _subscribers.Count;
        }
    }
}
