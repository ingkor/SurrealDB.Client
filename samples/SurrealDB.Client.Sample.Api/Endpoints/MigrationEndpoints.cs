namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using SurrealDB.Client.Migrations;

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/migrations")
            .WithTags("Migrations — Schema Management");

        group.MapPost("/apply", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            var assembly = typeof(Sample.Api.Migrations.AddProductCategoryIndex).Assembly;

            try
            {
                await client.MigrateAsync(assembly, ct);
                return Results.Ok(new
                {
                    success = true,
                    message = "All pending migrations applied successfully.",
                    description = "MigrateAsync discovers all Migration subclasses in the assembly, " +
                                  "checks the _migrations history table, and applies only pending ones in name order."
                });
            }
            catch (MigrationException ex)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    migrationName = ex.MigrationName,
                    message = ex.Message
                });
            }
        })
        .WithName("ApplyMigrations")
        .WithSummary("MigrateAsync — apply pending migrations")
        .WithDescription(
            "Discovers all `Migration` subclasses in the Sample.Api assembly and applies " +
            "pending ones via `client.MigrateAsync(assembly)`. " +
            "Idempotent — running twice applies nothing the second time. " +
            "Applied migrations are tracked in the `_migrations` SurrealDB table.");

        group.MapPost("/rollback", async ([FromBody] RollbackRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var assembly = typeof(Sample.Api.Migrations.AddProductCategoryIndex).Assembly;

            try
            {
                await client.RollbackAsync(req.MigrationName, assembly, ct);
                return Results.Ok(new
                {
                    success = true,
                    rolledBack = req.MigrationName,
                    message = "Migration rolled back and removed from history."
                });
            }
            catch (MigrationException ex)
            {
                return Results.NotFound(new
                {
                    success = false,
                    migrationName = req.MigrationName,
                    message = ex.Message
                });
            }
        })
        .WithName("RollbackMigration")
        .WithSummary("RollbackAsync — revert a named migration")
        .WithDescription(
            "Rolls back a specific migration by name via `client.RollbackAsync(name, assembly)`. " +
            "Calls the migration's `Down()` method and removes it from the `_migrations` history table. " +
            "Returns 404 if the migration name is not found in the assembly.");

        group.MapGet("/sample", () =>
        {
            return Results.Ok(new
            {
                migrationName = "20260101_add_product_category_index",
                migrationClass = "AddProductCategoryIndex",
                up = "CREATE INDEX idx_product_category ON products FIELDS category",
                down = "DROP INDEX idx_product_category ON products",
                baseClass = "SurrealDB.Client.Migrations.Migration",
                executor = "IMigrationExecutor provides: ExecuteAsync, CreateTableAsync, DropTableAsync, " +
                           "AddColumnAsync, DropColumnAsync, RenameColumnAsync, CreateIndexAsync, DropIndexAsync"
            });
        })
        .WithName("SampleMigration")
        .WithSummary("Inspect sample migration Up/Down SurrealQL")
        .WithDescription(
            "Shows the sample `AddProductCategoryIndex` migration's Up and Down SurrealQL. " +
            "Migrations extend the abstract `Migration` class and use `IMigrationExecutor` " +
            "for type-safe schema operations.");
    }
}

public class RollbackRequest
{
    public string MigrationName { get; set; } = "";
}
