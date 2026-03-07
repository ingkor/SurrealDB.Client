namespace SurrealDB.Client.Sample.Api.Endpoints;

using System.Text.Json;
using SurrealDB.Client.EventSourcing;

/// <summary>
/// Demonstrates: Server-Sent Events (SSE) using IEventStore for real-time streaming
/// </summary>
public static class EventStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapEventStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Events — Server-Sent Events (SSE)");

        // Feature: SSE — push live event store updates to connected clients
        // Scalar renders this as a streaming endpoint in its UI
        group.MapGet("/stream", async (HttpContext ctx, IEventStore eventStore, CancellationToken ct) =>
        {
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            ctx.Response.Headers.Append("Cache-Control", "no-cache");
            ctx.Response.Headers.Append("Connection", "keep-alive");
            ctx.Response.Headers.Append("X-Accel-Buffering", "no");

            await ctx.Response.WriteAsync("retry: 3000\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);

            var lastCount = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var allEvents = (await eventStore.GetAllEventsAsync(ct)).ToList();
                    var newEvents = allEvents.Skip(lastCount).ToList();

                    if (newEvents.Count > 0)
                    {
                        foreach (var evt in newEvents)
                        {
                            var data = JsonSerializer.Serialize(new
                            {
                                eventId = evt.EventId,
                                eventType = evt.EventType,
                                aggregateId = evt.AggregateId,
                                occurredAt = evt.OccurredAt,
                                version = evt.Version,
                                userId = evt.UserId
                            }, JsonOptions);

                            await ctx.Response.WriteAsync($"event: domain-event\n", ct);
                            await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
                        }

                        lastCount = allEvents.Count;
                    }
                    else
                    {
                        // Send heartbeat to keep the connection alive
                        var heartbeat = JsonSerializer.Serialize(new
                        {
                            type = "heartbeat",
                            timestamp = DateTime.UtcNow,
                            totalEvents = lastCount
                        }, JsonOptions);

                        await ctx.Response.WriteAsync($"event: heartbeat\n", ct);
                        await ctx.Response.WriteAsync($"data: {heartbeat}\n\n", ct);
                    }

                    await ctx.Response.Body.FlushAsync(ct);
                    await Task.Delay(2000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        })
        .WithName("EventStream")
        .WithSummary("SSE — live event store stream")
        .WithDescription(
            "Opens a Server-Sent Events stream over the `InMemoryEventStore`. " +
            "Pushes `domain-event` messages when new events are appended (trigger via POST /api/demo/event-sourcing). " +
            "Sends `heartbeat` events every 2 seconds to keep the connection alive. " +
            "Connect to this endpoint from the Scalar UI or via `EventSource` in a browser.");

        // Convenience: append a named event and see it appear in the stream
        group.MapPost("/publish", async (PublishEventRequest req, IEventStore eventStore, CancellationToken ct) =>
        {
            var evt = new EntityUpdatedEvent
            {
                AggregateId = req.AggregateId,
                UserId = req.UserId,
                Changes = req.Payload
            };

            await eventStore.AppendEventAsync(evt, ct);

            return Results.Accepted($"/api/events/{req.AggregateId}", new
            {
                message = "Event appended — subscribers on /api/events/stream will receive it within 2 s",
                eventId = evt.EventId,
                eventType = evt.EventType,
                version = evt.Version
            });
        })
        .WithName("PublishEvent")
        .WithSummary("Publish a domain event (triggers SSE stream)")
        .WithDescription(
            "Appends an `EntityUpdatedEvent` to the `InMemoryEventStore`. " +
            "Any client connected to GET /api/events/stream will receive the event within 2 seconds.");

        // Replay all events for an aggregate
        group.MapGet("/{aggregateId}", async (string aggregateId, IEventStore eventStore, CancellationToken ct) =>
        {
            var events = await eventStore.GetEventsAsync(aggregateId, ct);
            var count = await eventStore.GetEventCountAsync(aggregateId, ct);

            return Results.Ok(new
            {
                aggregateId,
                eventCount = count,
                events = events.Select(e => new
                {
                    e.EventId,
                    e.EventType,
                    e.OccurredAt,
                    e.Version,
                    e.UserId
                })
            });
        })
        .WithName("GetAggregateEvents")
        .WithSummary("Replay aggregate event history")
        .WithDescription("Returns the full event history for an aggregate ID via `eventStore.GetEventsAsync(aggregateId)`. Enables event replay and audit trails.");
    }
}

public class PublishEventRequest
{
    public string AggregateId { get; set; } = $"order:{Guid.NewGuid():N}";
    public string UserId { get; set; } = "users:demo";
    public Dictionary<string, object>? Payload { get; set; }
}
