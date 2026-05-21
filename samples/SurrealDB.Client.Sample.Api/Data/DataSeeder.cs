namespace SurrealDB.Client.Sample.Api.Data;

using Microsoft.Extensions.Logging;
using Models;

public static class DataSeeder
{
    private static readonly string[] Categories =
        ["Electronics", "Books", "Clothing", "Home", "Sports", "Toys", "Food", "Beauty", "Garden", "Automotive"];

    private static readonly string[] Roles = ["admin", "editor", "viewer", "editor", "viewer"];

    public static async Task SeedAsync(ISurrealDbClient client, ILogger logger, CancellationToken ct = default)
    {
        var existing = await client.SelectAsync<Product>("products", 1, ct);
        if (existing.Any())
        {
            logger.LogInformation("Already seeded — skipping");
            return;
        }

        // Seed 100 products (10 categories x 10 items) via batch
        var products = new List<Product>(100);
        for (var c = 0; c < Categories.Length; c++)
        {
            for (var i = 1; i <= 10; i++)
            {
                var idx = c * 10 + i;
                products.Add(new Product
                {
                    Name = $"{Categories[c]} Item {i}",
                    Description = $"Sample {Categories[c].ToLowerInvariant()} product #{i}",
                    Price = 10.99m + idx * 3.50m,
                    Stock = 10 + idx * 5,
                    Category = Categories[c],
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await client.CreateManyAsync("products", products, ct);
        logger.LogInformation("Seeded {Count} products", products.Count);

        // Seed 5 users
        for (var i = 0; i < 5; i++)
        {
            await client.CreateAsync("users", new
            {
                username = $"user{i + 1}",
                email = $"user{i + 1}@demo.com",
                role = Roles[i]
            }, ct);
        }

        logger.LogInformation("Seeded 5 users");

        // Seed 5 auditable entities
        for (var i = 1; i <= 5; i++)
        {
            await client.CreateAsync("auditableentity", new AuditableEntity
            {
                Title = $"Article {i}",
                Content = $"Sample content for article {i}"
            }, ct);
        }

        logger.LogInformation("Seeded 5 auditable entities — total: 110 records");
    }
}
