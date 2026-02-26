# Event Sourcing: Event Store & Event Replay

> Event sourcing patterns for immutable event history, temporal queries, and event replay.

## Event Sourcing Concepts

Event sourcing stores state changes as immutable events rather than current state.

```csharp
// Event definition
public abstract class Event
{
    public string AggregateId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string UserId { get; set; }
    public int Version { get; set; }
}

// Domain events
public class AccountCreated : Event
{
    public string Owner { get; set; }
    public decimal InitialBalance { get; set; }
}

public class MoneyDeposited : Event
{
    public decimal Amount { get; set; }
}

public class MoneyWithdrawn : Event
{
    public decimal Amount { get; set; }
}
```

## Event Store

```csharp
// Save events
var @event = new MoneyDeposited
{
    AggregateId = "account:1",
    Amount = 100,
    OccurredAt = DateTime.UtcNow,
    UserId = "user:1"
};

await eventStore.AppendAsync(@event);

// Replay events to reconstruct state
var account = new Account { Id = "account:1" };
var events = await eventStore.GetEventsAsync("account:1");

foreach (var evt in events)
{
    account.Apply(evt);  // Update state based on event
}

// account.Balance is now correct
```

## Event Handlers

```csharp
public interface IEventHandler<in T> where T : Event
{
    Task HandleAsync(T @event);
}

// Implementation
public class AccountEventHandler : 
    IEventHandler<MoneyDeposited>,
    IEventHandler<MoneyWithdrawn>
{
    public async Task HandleAsync(MoneyDeposited @event)
    {
        // Update read model, send notifications, etc.
        await _notificationService.SendDepositNotificationAsync(@event);
    }

    public async Task HandleAsync(MoneyWithdrawn @event)
    {
        await _notificationService.SendWithdrawalNotificationAsync(@event);
    }
}
```

## Snapshots for Performance

```csharp
// Create snapshot every 100 events
var snapshot = new AccountSnapshot
{
    AggregateId = "account:1",
    Version = 100,
    Balance = 5000,
    Status = "active"
};

await eventStore.SaveSnapshotAsync(snapshot);

// Replay: load snapshot, then events after it
var snapshot = await eventStore.GetSnapshotAsync("account:1");
var account = snapshot.ToAccount();

var eventsAfterSnapshot = await eventStore.GetEventsAsync(
    "account:1",
    fromVersion: snapshot.Version + 1);

foreach (var evt in eventsAfterSnapshot)
    account.Apply(evt);
```

## Temporal Queries

```csharp
// Query state at specific point in time
var accountAt = await eventStore.GetStateAtAsync<Account>(
    aggregateId: "account:1",
    asOf: DateTime.Parse("2024-01-15T10:00:00Z"));

// accountAt shows state as it was on that date/time
```

## See full EVENT_SOURCING.md in repository
