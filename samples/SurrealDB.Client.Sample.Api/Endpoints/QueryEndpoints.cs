namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using Models;
using SurrealDB.Client.Query;

/// <summary>
/// Demonstrates: QueryAsync (raw SurrealQL), QueryAsync&lt;T&gt;, parameterized queries, SurrealQueryCompiler
/// </summary>
public static class QueryEndpoints
{
    public static void MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/query")
            .WithTags("Query — Raw SurrealQL & LINQ Compilation");

        // Feature: QueryAsync — execute raw SurrealQL, returns QueryResult
        group.MapPost("/raw", async ([FromBody] RawQueryRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var result = await client.QueryAsync(req.SurrealQL, req.Parameters, ct);
            return Results.Ok(result);
        })
        .WithName("RawQuery")
        .WithSummary("QueryAsync — raw SurrealQL")
        .WithDescription(
            "Executes any SurrealQL statement via `client.QueryAsync(surrealQL, parameters)`. " +
            "Returns `QueryResult` with `Data`, `Status`, `Time`, and `IsSuccess`.\n\n" +
            "Example: `SELECT * FROM products WHERE price > $minPrice`\n" +
            "Parameters: `{ \"minPrice\": 10 }`");

        // Feature: QueryAsync<T> — typed results
        group.MapPost("/typed", async ([FromBody] RawQueryRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            var products = await client.QueryAsync<Product>(req.SurrealQL, req.Parameters, ct);
            return Results.Ok(products);
        })
        .WithName("TypedQuery")
        .WithSummary("QueryAsync<T> — typed results")
        .WithDescription(
            "Executes SurrealQL and deserializes results to `IEnumerable<Product>` " +
            "via `client.QueryAsync<Product>(surrealQL, parameters)`.\n\n" +
            "Example: `SELECT * FROM products WHERE category = $cat`\n" +
            "Parameters: `{ \"cat\": \"Electronics\" }`");

        // Feature: SurrealQueryCompiler — inspect LINQ → SurrealQL translation
        group.MapGet("/linq-preview", ([FromQuery] string? category, [FromQuery] decimal? minPrice, [FromQuery] int? limit) =>
        {
            // Use the public SurrealQueryCompiler to show what SurrealQL LINQ produces
            // This is a dry-run — no DB call, just expression compilation
            var compiler = new SurrealQueryCompiler();

            // Build a mock queryable to capture the expression tree
            IQueryable<Product> query = new EnumerableQuery<Product>(Array.Empty<Product>());

            if (!string.IsNullOrEmpty(category))
                query = query.Where(p => p.Category == category);

            if (minPrice.HasValue)
            {
                var min = minPrice.Value;
                query = query.Where(p => p.Price >= min);
            }

            query = query.OrderByDescending(p => p.CreatedAt);

            if (limit.HasValue)
                query = query.Take(limit.Value);

            // Compile the LINQ expression to SurrealQL
            var compiled = compiler.CompileDetailed(query.Expression, "products");

            return Results.Ok(new
            {
                linq = new
                {
                    category,
                    minPrice,
                    limit,
                    orderBy = "CreatedAt DESC"
                },
                compiled = new
                {
                    surrealQL = compiled.SurrealQL,
                    tableName = compiled.TableName,
                    isScalar = compiled.IsScalar
                },
                note = "This is a dry-run. The SurrealQL above would be sent to SurrealDB when executed via session.Set<Product>()."
            });
        })
        .WithName("LinqPreview")
        .WithSummary("SurrealQueryCompiler — LINQ → SurrealQL")
        .WithDescription(
            "Shows what SurrealQL the `SurrealQueryCompiler` generates from a LINQ expression. " +
            "No database call is made — this is a pure compilation preview.\n\n" +
            "Try: `?category=Electronics&minPrice=50&limit=10`");

        // Feature: BeginTransactionAsync — wrap multiple queries in a transaction
        group.MapPost("/transaction", async ([FromBody] TransactionDemoRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            await using var tx = await client.BeginTransactionAsync(ct);
            try
            {
                var results = new List<QueryResult>();
                foreach (var sql in req.Statements)
                {
                    var result = await tx.QueryAsync(sql, null, ct);
                    results.Add(result);
                }

                await tx.CommitAsync(ct);
                return Results.Ok(new { committed = true, results });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return Results.BadRequest(new { rolledBack = true, reason = ex.Message });
            }
        })
        .WithName("Transaction")
        .WithSummary("BeginTransactionAsync — multi-statement transaction")
        .WithDescription(
            "Executes multiple SurrealQL statements inside a transaction via " +
            "`client.BeginTransactionAsync()`. On success calls `tx.CommitAsync()`. " +
            "On failure calls `tx.RollbackAsync()` and reports the error.");
    }
}

public class RawQueryRequest
{
    public string SurrealQL { get; set; } = "SELECT * FROM products LIMIT 10;";
    public Dictionary<string, object>? Parameters { get; set; }
}

public class TransactionDemoRequest
{
    public List<string> Statements { get; set; } = new();
}
