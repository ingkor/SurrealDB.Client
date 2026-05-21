using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// SurrealDB container
// Podman or Docker: surrealdb/surrealdb:latest
// Starts in-memory mode with root credentials on port 8000
var surrealdb = builder
    .AddContainer("surrealdb", "surrealdb/surrealdb", "latest")
    .WithArgs("start", "--user", "admin", "--pass", "password", "memory")
    .WithEnvironment("SURREAL_USER", "admin")
    .WithEnvironment("SURREAL_PASS", "password")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http");

// Sample API — receives SurrealDB endpoint via Aspire service discovery
// Aspire injects: services__surrealdb__http__0=http://host:port
builder
    .AddProject<Projects.SurrealDB_Client_Sample_Api>("sample-api")
    .WithReference(surrealdb.GetEndpoint("http"))
    .WaitFor(surrealdb);

builder.Build().Run();
