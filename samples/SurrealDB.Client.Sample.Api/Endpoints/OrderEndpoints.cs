namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using Models;
using SurrealDB.Client.Session;

/// <summary>
/// Demonstrates: ISurrealDbSession, ChangeTracker, SaveChangesAsync (Unit of Work pattern)
/// </summary>
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders — Session & Unit of Work");

        // Feature: ISurrealDbSession — EF Core-style Unit of Work
        // Add entities, track changes, persist atomically with SaveChangesAsync
        group.MapPost("/", async ([FromBody] CreateOrderRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var total = req.Items.Sum(i => i.Quantity * i.UnitPrice);
            var order = new Order
            {
                UserId = req.UserId,
                Items = req.Items,
                Total = total,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Open a session (Unit of Work scope)
            await using var session = client.CreateSession();

            // Stage the new entity — sets EntityState.Added in ChangeTracker
            session.Add(order);

            // Verify ChangeTracker sees the pending change before saving
            var hasPending = session.HasChanges;

            // Persist all staged changes atomically
            var affected = await session.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                order,
                session = new
                {
                    hadPendingChanges = hasPending,
                    entitiesAffected = affected
                }
            });
        })
        .WithName("CreateOrder")
        .WithSummary("ISurrealDbSession + SaveChangesAsync")
        .WithDescription(
            "Creates an order via the Unit of Work pattern. " +
            "Calls `session.Add(order)` to stage the entity (EntityState.Added), " +
            "then `session.SaveChangesAsync()` to persist atomically. " +
            "The response includes ChangeTracker metadata.");

        // Feature: ChangeTracker — track modifications and save diff
        group.MapPatch("/{id}/status", async (string id, [FromBody] UpdateOrderStatusRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            await using var session = client.CreateSession();

            // Load entity into session — begins tracking (EntityState.Unchanged)
            var order = await session.FindAsync<Order>($"orders:{id}", ct);
            if (order is null)
                return Results.NotFound();

            // Mutate — ChangeTracker detects this as EntityState.Modified
            order.Status = req.Status;
            order.UpdatedAt = DateTime.UtcNow;
            session.Update(order);

            var tracker = session.ChangeTracker;
            var changedEntities = tracker.GetChangedEntities();

            // Only the changed fields are sent to SurrealDB
            var affected = await session.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                order,
                changeTracker = new
                {
                    changedEntitiesCount = changedEntities.Count(),
                    entitiesAffected = affected
                }
            });
        })
        .WithName("UpdateOrderStatus")
        .WithSummary("ChangeTracker — detect & persist diffs")
        .WithDescription(
            "Updates order status via ChangeTracker. " +
            "`session.FindAsync()` loads and tracks the entity. " +
            "After mutation, `session.Update(order)` marks it Modified. " +
            "`SaveChangesAsync()` generates and runs UPDATE for only changed entities.");

        // Feature: Session.Set<T> — IQueryable over a table
        group.MapGet("/", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            await using var session = client.CreateSession();

            // IQueryable<Order> — LINQ expression is compiled to SurrealQL on execution
            var orders = session.Set<Order>("orders")
                .Where(o => o.Status != OrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .ToList();

            return Results.Ok(orders);
        })
        .WithName("GetOrders")
        .WithSummary("Session.Set<T>() — IQueryable<T>")
        .WithDescription(
            "Queries orders using `session.Set<Order>(\"orders\")` which returns `IQueryable<Order>`. " +
            "The LINQ expression (Where, OrderByDescending, Take) is compiled to SurrealQL " +
            "by `SurrealQueryCompiler` at execution time.");

        // Feature: Session.Discard — rollback pending changes
        group.MapPost("/{id}/cancel", async (string id, ISurrealDbClient client, CancellationToken ct) =>
        {
            await using var session = client.CreateSession();

            var order = await session.FindAsync<Order>($"orders:{id}", ct);
            if (order is null)
                return Results.NotFound();

            if (order.Status == OrderStatus.Delivered)
            {
                // Demonstrate: stage change then discard without saving
                order.Status = OrderStatus.Cancelled;
                session.Update(order);

                var pendingBefore = session.HasChanges;
                session.Discard(); // Roll back — nothing is sent to DB
                var pendingAfter = session.HasChanges;

                return Results.Conflict(new
                {
                    message = "Cannot cancel a delivered order. Staged change was discarded.",
                    changeTracker = new { pendingBefore, pendingAfter }
                });
            }

            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;
            session.Update(order);
            await session.SaveChangesAsync(ct);

            return Results.Ok(order);
        })
        .WithName("CancelOrder")
        .WithSummary("Session.Discard() — rollback staged changes")
        .WithDescription(
            "Shows conditional discard: if the order is already delivered, " +
            "the status change is staged then rolled back via `session.Discard()` " +
            "without touching the database.");
    }
}
