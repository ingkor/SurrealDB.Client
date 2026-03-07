using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Add SurrealDB container with health checks
var surrealdb = builder
    .AddContainer("surrealdb", "surrealdb/surrealdb")
    .WithCommand("start", "--auth", "--user", "admin", "--pass", "password")
    .WithEnvironment("SURREAL_AUTH", "true")
    .WithEnvironment("SURREAL_USER", "admin")
    .WithEnvironment("SURREAL_PASS", "password")
    .WithHttpEndpoint(targetPort: 8000, name: "http")
    .WithHealthCheck();

builder.Build().Run();
