namespace SurrealDB.Client.Sample.Api.Endpoints;

using System.Text;

public static class TestEndpoints
{
    public static void MapTestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/test/run-all", async (HttpContext httpContext, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            using var client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);

            var testCases = BuildTestCases();
            var results = new List<object>();
            var passed = 0;
            var failed = 0;

            foreach (var tc in testCases)
            {
                var result = await RunTestAsync(client, tc, ct);
                results.Add(result);
                if ((bool)result.GetType().GetProperty("passed")!.GetValue(result)!)
                    passed++;
                else
                    failed++;
            }

            return Results.Ok(new { total = testCases.Count, passed, failed, results });
        })
        .WithTags("Test — Run All Endpoints")
        .WithName("RunAllTests")
        .WithSummary("Calls every endpoint once and returns a pass/fail report")
        .ExcludeFromDescription();
    }

    private static async Task<object> RunTestAsync(HttpClient client, TestCase tc, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(tc.Method, tc.Path);
            if (tc.Body != null)
                request.Content = new StringContent(tc.Body, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var snippet = body.Length > 200 ? body[..200] + "..." : body;
            var statusCode = (int)response.StatusCode;
            var isPassed = statusCode == tc.ExpectedStatus;

            return new
            {
                method = tc.Method.Method,
                path = tc.Path,
                statusCode,
                expectedStatus = tc.ExpectedStatus,
                passed = isPassed,
                snippet
            };
        }
        catch (Exception ex)
        {
            return new
            {
                method = tc.Method.Method,
                path = tc.Path,
                statusCode = 0,
                expectedStatus = tc.ExpectedStatus,
                passed = false,
                snippet = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static List<TestCase> BuildTestCases() =>
    [
        // Health (safe, no side effects)
        new(HttpMethod.Get, "/api/health/connection", null, 200),
        new(HttpMethod.Get, "/api/health/config", null, 200),
        new(HttpMethod.Get, "/api/health/cache", null, 200),

        // Observability (read-only)
        new(HttpMethod.Get, "/api/observability/config", null, 200),
        new(HttpMethod.Get, "/api/observability/metrics", null, 200),

        // Resilience
        new(HttpMethod.Get, "/api/resilience/config", null, 200),
        new(HttpMethod.Post, "/api/resilience/test", null, 200),

        // Products CRUD
        new(HttpMethod.Get, "/api/products/", null, 200),
        new(HttpMethod.Post, "/api/products/",
            """{"name":"TestProd","description":"d","price":9.99,"stock":5,"category":"Test"}""", 200),
        new(HttpMethod.Get, "/api/products/products:nonexistent", null, 404),
        new(HttpMethod.Put, "/api/products/products:test_put",
            """{"name":"Updated","description":"d","price":19.99,"stock":10,"category":"Test"}""", 200),
        new(HttpMethod.Put, "/api/products/products:test_upsert/upsert",
            """{"name":"Upserted","description":"d","price":29.99,"stock":15,"category":"Test"}""", 200),
        new(HttpMethod.Delete, "/api/products/products:test_upsert", null, 200),

        // Batch
        new(HttpMethod.Post, "/api/batch/products",
            """[{"name":"Batch1","description":"d","price":1,"stock":1,"category":"BatchTest"}]""", 200),
        new(HttpMethod.Put, "/api/batch/products",
            """[{"id":"products:batchupd","name":"BatchUpd","description":"d","price":2,"stock":2,"category":"BatchTest"}]""", 200),
        new(HttpMethod.Delete, "/api/batch/products",
            """["products:batchupd"]""", 200),

        // Users
        new(HttpMethod.Get, "/api/users/", null, 200),
        new(HttpMethod.Post, "/api/users/",
            """{"username":"testrunner","email":"t@t.com","password":"p"}""", 200),
        new(HttpMethod.Post, "/api/users/authenticate",
            """{"username":"admin","password":"password"}""", 200),
        new(HttpMethod.Post, "/api/users/logout", null, 200),
        // Re-authenticate after logout
        new(HttpMethod.Post, "/api/users/authenticate",
            """{"username":"admin","password":"password"}""", 200),

        // Query
        new(HttpMethod.Post, "/api/query/raw",
            """{"query":"RETURN 1;"}""", 200),
        new(HttpMethod.Post, "/api/query/typed",
            """{"query":"SELECT * FROM products LIMIT 1;"}""", 200),
        new(HttpMethod.Get, "/api/query/linq-preview", null, 200),
        new(HttpMethod.Post, "/api/query/transaction",
            """{"statements":["RETURN 1;"]}""", 200),

        // Orders
        new(HttpMethod.Get, "/api/orders/", null, 200),
        new(HttpMethod.Post, "/api/orders/",
            """{"customerId":"user:1","items":[{"productId":"products:test","quantity":1,"unitPrice":9.99}],"status":"pending"}""", 200),

        // Demo
        new(HttpMethod.Get, "/api/demo/interceptors", null, 200),
        new(HttpMethod.Get, "/api/demo/plugins", null, 200),
        new(HttpMethod.Get, "/api/demo/cache", null, 200),
        new(HttpMethod.Get, "/api/demo/concurrency", null, 200),
        new(HttpMethod.Get, "/api/demo/event-sourcing", null, 200),

        // Events (skip SSE stream)
        new(HttpMethod.Post, "/api/events/publish",
            """{"aggregateId":"test-agg","eventType":"TestEvent","data":{"k":"v"}}""", 200),
        new(HttpMethod.Get, "/api/events/test-agg", null, 200),

        // Audit
        new(HttpMethod.Post, "/api/audit/entities",
            """{"title":"AuditTest","content":"c","userId":"testrunner"}""", 200),

        // Migrations
        new(HttpMethod.Get, "/api/migrations/sample", null, 200),
        new(HttpMethod.Post, "/api/migrations/apply", null, 200),
        new(HttpMethod.Post, "/api/migrations/rollback",
            """{"migrationName":"20260101_add_product_category_index"}""", 200),
    ];

    private sealed record TestCase(HttpMethod Method, string Path, string? Body, int ExpectedStatus);
}
