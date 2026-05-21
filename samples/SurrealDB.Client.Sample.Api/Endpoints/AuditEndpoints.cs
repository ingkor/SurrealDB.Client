namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using Models;
using SurrealDB.Client.Session;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit")
            .WithTags("Audit — Automatic Timestamps & User Tracking");

        group.MapPost("/entities", async ([FromBody] CreateAuditableEntityRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            await using var session = client.CreateSession();

            // SetCurrentUser tells the session who is making changes
            // The ChangeTracker uses this for [CreatedBy] and [UpdatedBy] attributes
            if (req.UserId != null)
                session.SetCurrentUser(req.UserId);

            var entity = new AuditableEntity
            {
                Title = req.Title,
                Content = req.Content
                // CreatedAt, UpdatedAt, CreatedBy, UpdatedBy are left null —
                // the session's ApplyAuditAttributes fills them automatically on save
            };

            session.Add(entity);
            var affected = await session.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                entity,
                affected,
                auditFields = new
                {
                    createdAtAutoSet = entity.CreatedAt != null,
                    updatedAtAutoSet = entity.UpdatedAt != null,
                    createdByAutoSet = entity.CreatedBy != null,
                    updatedByAutoSet = entity.UpdatedBy != null,
                    currentUser = req.UserId
                },
                description = "The [CreatedAt], [UpdatedAt], [CreatedBy], [UpdatedBy] attributes " +
                              "on the entity properties are detected by the session's ChangeTracker. " +
                              "On Add: sets CreatedAt + CreatedBy. On both Add and Modify: sets UpdatedAt + UpdatedBy."
            });
        })
        .WithName("CreateAuditableEntity")
        .WithSummary("[CreatedAt]/[CreatedBy] — auto-populated on insert")
        .WithDescription(
            "Creates an entity with audit attributes via session. " +
            "Call `session.SetCurrentUser(userId)` before saving. " +
            "The ChangeTracker's `ApplyAuditAttributes` method uses reflection " +
            "to fill `[CreatedAt]`, `[UpdatedAt]`, `[CreatedBy]`, `[UpdatedBy]` properties.");

        group.MapPut("/entities/{id}", async (string id, [FromBody] CreateAuditableEntityRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            await using var session = client.CreateSession();

            if (req.UserId != null)
                session.SetCurrentUser(req.UserId);

            var entity = await session.FindAsync<AuditableEntity>($"auditableentity:{id}", ct);
            if (entity is null)
                return Results.NotFound();

            var originalCreatedAt = entity.CreatedAt;
            var originalCreatedBy = entity.CreatedBy;

            entity.Title = req.Title;
            entity.Content = req.Content;
            session.Update(entity);
            await session.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                entity,
                auditFields = new
                {
                    createdAtPreserved = entity.CreatedAt == originalCreatedAt,
                    createdByPreserved = entity.CreatedBy == originalCreatedBy,
                    updatedAtAutoSet = entity.UpdatedAt != null,
                    updatedByAutoSet = entity.UpdatedBy != null
                },
                description = "On update, only [UpdatedAt] and [UpdatedBy] are refreshed. " +
                              "[CreatedAt] and [CreatedBy] remain unchanged from the original insert."
            });
        })
        .WithName("UpdateAuditableEntity")
        .WithSummary("[UpdatedAt]/[UpdatedBy] — auto-populated on update")
        .WithDescription(
            "Updates an auditable entity. Only `[UpdatedAt]` and `[UpdatedBy]` are refreshed — " +
            "`[CreatedAt]` and `[CreatedBy]` are preserved from the original insert.");
    }
}
