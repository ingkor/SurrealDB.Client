namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using Models;

public static class BatchEndpoints
{
    public static void MapBatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/batch")
            .WithTags("Batch — Bulk Operations");

        group.MapPost("/products", async ([FromBody] CreateProductRequest[] requests, ISurrealDbClient client, CancellationToken ct) =>
        {
            if (requests.Length == 0)
                return Results.Ok(new { created = Array.Empty<Product>(), count = 0 });

            var products = requests.Select(r => new Product
            {
                Name = r.Name,
                Description = r.Description,
                Price = r.Price,
                Stock = r.Stock,
                Category = r.Category,
                CreatedAt = DateTime.UtcNow
            });

            var created = await client.CreateManyAsync("products", products, ct);
            var list = created.ToList();

            return Results.Ok(new { created = list, count = list.Count });
        })
        .WithName("BatchCreateProducts")
        .WithSummary("CreateManyAsync<T> — bulk insert")
        .WithDescription(
            "Inserts multiple products in a single round trip using `client.CreateManyAsync(\"products\", items)`. " +
            "Internally chunks items into batches of 1000 and builds SurrealQL INSERT statements.");

        group.MapPut("/products", async ([FromBody] BatchUpdateItem[] updates, ISurrealDbClient client, CancellationToken ct) =>
        {
            if (updates.Length == 0)
                return Results.Ok(new { updated = Array.Empty<Product>(), count = 0 });

            var pairs = updates.Select(u => (u.Id, (Product)new Product
            {
                Name = u.Name,
                Description = u.Description,
                Price = u.Price,
                Stock = u.Stock,
                Category = u.Category
            }));

            var results = await client.UpdateManyAsync(pairs, ct);
            var list = results.ToList();

            return Results.Ok(new { updated = list, count = list.Count });
        })
        .WithName("BatchUpdateProducts")
        .WithSummary("UpdateManyAsync<T> — bulk update")
        .WithDescription(
            "Updates multiple products by ID in a single round trip using " +
            "`client.UpdateManyAsync([(id, data), ...])`. Each item generates an UPDATE statement.");

        group.MapDelete("/products", async ([FromBody] string[] ids, ISurrealDbClient client, CancellationToken ct) =>
        {
            if (ids.Length == 0)
                return Results.Ok(new { deleted = 0 });

            var deleted = await client.DeleteManyAsync(ids, ct);
            return Results.Ok(new { deleted });
        })
        .WithName("BatchDeleteProducts")
        .WithSummary("DeleteManyAsync — bulk delete")
        .WithDescription(
            "Deletes multiple records by ID in a single round trip using " +
            "`client.DeleteManyAsync([\"products:id1\", \"products:id2\", ...])`. " +
            "Returns the count of records requested for deletion.");
    }
}

public class BatchUpdateItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Category { get; set; } = "";
}
