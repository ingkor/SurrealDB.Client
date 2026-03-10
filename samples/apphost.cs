#:sdk Aspire.AppHost.Sdk@13.1.2

var builder = DistributedApplication.CreateBuilder(args);

var surrealdb = builder.AddContainer("surrealdb", "surrealdb/surrealdb", "latest")
    .WithArgs("start", "--auth", "--user", "admin", "--pass", "password", "memory")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http");

builder.AddProject("sample-api", "SurrealDB.Client.Sample.Api/SurrealDB.Client.Sample.Api.csproj")
    .WaitFor(surrealdb)
    .WithReference(surrealdb.GetEndpoint("http"));

builder.Build().Run();
