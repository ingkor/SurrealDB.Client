namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using Models;

/// <summary>
/// Demonstrates: CreateAsync, GetAsync, SelectAsync, UpdateAsync, DeleteAsync, UpsertAsync
/// </summary>
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products — Basic CRUD");

        // Feature: SelectAsync<T> — fetch all records from a table
        group.MapGet("/", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            var products = await client.SelectAsync<Product>("products", cancellationToken: ct);
            return Results.Ok(products);
        })
        .WithName("GetAllProducts")
        .WithSummary("SelectAsync<T>")
        .WithDescription("Fetches all products using `client.SelectAsync<Product>(\"products\")`. Demonstrates table-level SELECT.");

        // Feature: GetAsync<T> — fetch single record by ID
        group.MapGet("/{id}", async (string id, ISurrealDbClient client, CancellationToken ct) =>
        {
            var product = await client.GetAsync<Product>($"products:{id}", ct);
            return product is null ? Results.NotFound() : Results.Ok(product);
        })
        .WithName("GetProduct")
        .WithSummary("GetAsync<T>")
        .WithDescription("Fetches a single product by record ID using `client.GetAsync<Product>(\"products:{id}\")`.");

        // Feature: CreateAsync<T> — insert a new record
        group.MapPost("/", async ([FromBody] CreateProductRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var product = new Product
            {
                Name = req.Name,
                Description = req.Description,
                Price = req.Price,
                Stock = req.Stock,
                Category = req.Category,
                CreatedAt = DateTime.UtcNow
            };

            var created = await client.CreateAsync("products", product, ct);
            return Results.Created($"/api/products/{created?.Id}", created);
        })
        .WithName("CreateProduct")
        .WithSummary("CreateAsync<T>")
        .WithDescription("Inserts a new product record using `client.CreateAsync(\"products\", product)`. Returns the created record including server-assigned ID.");

        // Feature: UpdateAsync<T> — replace a record's content
        group.MapPut("/{id}", async (string id, [FromBody] CreateProductRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var product = new Product
            {
                Name = req.Name,
                Description = req.Description,
                Price = req.Price,
                Stock = req.Stock,
                Category = req.Category
            };

            var updated = await client.UpdateAsync($"products:{id}", product, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateProduct")
        .WithSummary("UpdateAsync<T>")
        .WithDescription("Replaces a product record using `client.UpdateAsync(\"products:{id}\", product)`. Uses SurrealDB MERGE semantics.");

        // Feature: UpsertAsync<T> — create-or-replace
        group.MapPut("/{id}/upsert", async (string id, [FromBody] CreateProductRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var product = new Product
            {
                Name = req.Name,
                Description = req.Description,
                Price = req.Price,
                Stock = req.Stock,
                Category = req.Category
            };

            var upserted = await client.UpsertAsync($"products:{id}", product, ct);
            return Results.Ok(upserted);
        })
        .WithName("UpsertProduct")
        .WithSummary("UpsertAsync<T>")
        .WithDescription("Creates or replaces a product using `client.UpsertAsync(\"products:{id}\", product)`. Idempotent — safe to call multiple times.");

        // Feature: DeleteAsync — remove a record
        group.MapDelete("/{id}", async (string id, ISurrealDbClient client, CancellationToken ct) =>
        {
            await client.DeleteAsync($"products:{id}", ct);
            return Results.NoContent();
        })
        .WithName("DeleteProduct")
        .WithSummary("DeleteAsync")
        .WithDescription("Deletes a product record using `client.DeleteAsync(\"products:{id}\")`.");
    }
}
