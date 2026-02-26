# Aspire Integration Testing Skill Guide

**Status:** ✅ Production-Ready
**Version:** 1.0
**Framework:** .NET Aspire 8.0+
**Use Case:** Automated container orchestration for integration tests

---

## Overview

This skill provides a complete pattern for implementing production-grade integration tests using .NET Aspire. It handles:

- ✅ Automatic container startup/shutdown
- ✅ Dynamic endpoint discovery
- ✅ Resource lifecycle management
- ✅ CI/CD integration
- ✅ Test isolation and cleanup

---

## Quick Reference

### Step 1: Create AppHost Project

**File:** `tests/MyProject.AppHost/Program.cs`

```csharp
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Define any container resource
var surrealdb = builder
    .AddContainer("surrealdb", "surrealdb/surrealdb")
    .WithCommand("start", "--auth", "--user", "admin", "--pass", "password")
    .WithEnvironment("SURREAL_AUTH", "true")
    .WithHttpEndpoint(targetPort: 8000, name: "http")
    .WithHealthCheck();

builder.Build().Run();
```

**File:** `tests/MyProject.AppHost/MyProject.AppHost.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting" Version="8.0.0" />
  </ItemGroup>
</Project>
```

### Step 2: Create Integration Test Project

**File:** `tests/MyProject.Tests.Integration/MyProjectIntegrationTests.cs`

```csharp
using Aspire.Hosting.Testing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

[Trait("Category", "Integration")]
public class MyProjectIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private MyClient? _client;

    public async Task InitializeAsync()
    {
        // Start Aspire application
        _app = await DistributedApplicationTestingExtensions
            .BuildAndStartAsync(typeof(global::MyProject.AppHost.Program).Assembly);

        // Get resource from Aspire
        var appModel = _app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = appModel.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == "surrealdb");

        if (resource == null)
            throw new InvalidOperationException("Resource not found");

        // Get endpoint dynamically
        var endpoint = resource.Annotations
            .OfType<EndpointAnnotation>()
            .FirstOrDefault(e => e.Scheme == "http");

        // Initialize client with discovered endpoint
        var connectionString = $"protocol://localhost:{endpoint.Port}";
        _client = new MyClient(connectionString);

        // Setup (connect, authenticate, etc.)
        await _client.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
            await _client.DisposeAsync();
        if (_app != null)
            await _app.StopAsync();
    }

    [Fact]
    public async Task MyFeature_Scenario_ExpectedResult()
    {
        Assert.NotNull(_client);

        // Arrange
        var data = new TestModel { /* ... */ };

        // Act
        var result = await _client!.MethodAsync(data);

        // Assert
        Assert.NotNull(result);
    }
}
```

**File:** `tests/MyProject.Tests.Integration/MyProject.Tests.Integration.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.2" />
    <PackageReference Include="Aspire.Hosting.Testing" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/MyProject/MyProject.csproj" />
    <ProjectReference Include="../MyProject.AppHost/MyProject.AppHost.csproj" />
  </ItemGroup>
</Project>
```

### Step 3: Run Tests

```bash
# Run all integration tests
dotnet test --filter "Category=Integration"

# Skip integration tests (unit tests only)
dotnet test --filter "Category!=Integration"

# CI/CD friendly
dotnet test  # Automatically handles Aspire orchestration
```

---

## Core Patterns

### Pattern 1: Resource Discovery

```csharp
// Get resource by name
var resource = appModel.Resources
    .OfType<ContainerResource>()
    .FirstOrDefault(r => r.Name == "database");

// Get specific endpoint
var endpoint = resource.Annotations
    .OfType<EndpointAnnotation>()
    .FirstOrDefault(e => e.Scheme == "http");

// Use discovered endpoint
var url = $"http://localhost:{endpoint.Port}";
```

### Pattern 2: Multiple Resources

```csharp
var db = builder.AddContainer("db", "postgres:latest")
    .WithEnvironment("POSTGRES_PASSWORD", "password")
    .WithHttpEndpoint(5432, name: "postgres");

var cache = builder.AddContainer("redis", "redis:latest")
    .WithHttpEndpoint(6379, name: "redis");

var app = builder.AddProject<Projects.MyApp>("app")
    .WithReference(db)
    .WithReference(cache);
```

### Pattern 3: Credentials & Secrets

```csharp
var dbPassword = builder.AddParameter("db-password", secret: true);

var db = builder.AddContainer("db", "postgres:latest")
    .WithEnvironment("POSTGRES_PASSWORD", dbPassword);

// In tests, parameter values come from builder defaults
```

### Pattern 4: Health Checks

```csharp
var resource = builder.AddContainer("service", "myimage:latest")
    .WithHttpEndpoint(targetPort: 3000, name: "http")
    .WithHealthCheck();  // Aspire waits for health before proceeding

[Fact]
public async Task WaitsForHealthyService()
{
    // Service is guaranteed to be healthy at this point
    Assert.True(await _client!.IsHealthyAsync());
}
```

---

## Advanced Scenarios

### Scenario 1: Waiting for Conditions

```csharp
public async Task InitializeAsync()
{
    _app = await DistributedApplicationTestingExtensions
        .BuildAndStartAsync(typeof(Program).Assembly);

    // Aspire waits for all health checks automatically
    // No additional waiting needed

    _client = new MyClient(connectionString);
}
```

### Scenario 2: Environment-Specific Configuration

```csharp
// AppHost.cs
var environment = Environment.GetEnvironmentVariable("ASPIRE_ENV") ?? "test";

if (environment == "test")
{
    resource.WithEnvironment("LOG_LEVEL", "debug");
}
else
{
    resource.WithEnvironment("LOG_LEVEL", "info");
}
```

### Scenario 3: Resource Dependencies

```csharp
// AppHost.cs
var db = builder.AddPostgres("postgres")
    .WithHealthCheck();

var api = builder.AddProject<Projects.MyApi>("api")
    .WithReference(db)  // API depends on DB
    .DependsOn(db);     // API won't start until DB is healthy
```

### Scenario 4: Test Fixtures with Shared Resources

```csharp
[CollectionDefinition("Aspire Tests")]
public class AspireCollection : ICollectionFixture<AspireFixture>
{
    // Tests in this collection share resources
}

public class AspireFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public async Task InitializeAsync()
    {
        _app = await DistributedApplicationTestingExtensions
            .BuildAndStartAsync(typeof(Program).Assembly);
    }

    public async Task DisposeAsync()
    {
        await _app?.StopAsync();
    }

    public DistributedApplication App => _app!;
}

[Collection("Aspire Tests")]
public class SharedResourceTests
{
    private readonly AspireFixture _fixture;

    public SharedResourceTests(AspireFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Test1() { /* ... */ }

    [Fact]
    public async Task Test2() { /* ... */ }  // Reuses resources from Test1
}
```

---

## Benefits vs Alternatives

| Feature | Aspire | Docker Compose | Manual Docker | Testcontainers |
|---------|--------|---|---|---|
| **Automatic Startup** | ✅ | ✅ | ❌ | ✅ |
| **.NET Integration** | ✅✅ | ⚠️ | ⚠️ | ✅ |
| **Dynamic Discovery** | ✅ | ❌ | ❌ | ✅ |
| **Health Checks** | ✅ | ✅ | ❌ | ✅ |
| **Minimal Config** | ✅ | ⚠️ | ❌ | ✅ |
| **CI/CD Native** | ✅ | ⚠️ | ❌ | ✅ |

---

## Troubleshooting

### Issue: Container Won't Start

```bash
# Check Docker availability
docker ps

# Check Aspire logs
dotnet test --verbosity detailed
```

### Issue: Endpoint Not Discovered

```csharp
// Verify endpoint name matches AppHost definition
var endpoint = resource.Annotations
    .OfType<EndpointAnnotation>()
    .FirstOrDefault(e => e.Name == "http");  // Must match .WithHttpEndpoint(name: "http")

Assert.NotNull(endpoint, "Endpoint 'http' not found");
```

### Issue: Connection Timeout

```csharp
// Increase timeout
var options = new ClientOptions
{
    ConnectionTimeout = TimeSpan.FromSeconds(30)
};
```

---

## Best Practices

1. **Use IAsyncLifetime** - Proper async resource management
2. **Mark Tests** - Use `[Trait("Category", "Integration")]` for filtering
3. **Skip in CI** - Use `--filter "Category!=Integration"` in standard pipeline
4. **Health Checks** - Always include `.WithHealthCheck()`
5. **Cleanup** - Let Aspire handle resource cleanup in DisposeAsync
6. **Isolation** - Each test gets fresh resource instances (unless using shared fixtures)
7. **Timeouts** - Set reasonable connection timeouts
8. **Logging** - Enable debug logging for troubleshooting

---

## Checklist for Implementation

- [ ] Create AppHost project with resource definitions
- [ ] Create test project with Aspire.Hosting.Testing reference
- [ ] Implement IAsyncLifetime in test class
- [ ] Add endpoint discovery in InitializeAsync
- [ ] Create client instance with discovered endpoints
- [ ] Mark tests with [Trait("Category", "Integration")]
- [ ] Test locally: `dotnet test --filter "Category=Integration"`
- [ ] Test CI filter: `dotnet test --filter "Category!=Integration"`
- [ ] Document resource configuration
- [ ] Add troubleshooting guide

---

## Real-World Example

See `SurrealDB.Client` implementation:
- **AppHost:** `tests/SurrealDB.Client.AppHost/Program.cs`
- **Tests:** `tests/SurrealDB.Client.Tests.Integration/SurrealDbClientIntegrationTests.cs`
- **Documentation:** `tests/SurrealDB.Client.Tests.Integration/README.md`

---

## References

- [.NET Aspire Docs](https://learn.microsoft.com/en-us/dotnet/aspire)
- [Aspire Testing](https://learn.microsoft.com/en-us/dotnet/aspire/testing)
- [Aspire Container Resources](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/components-overview)

---

## Feedback Loop Integration

This skill is designed to:

1. **Accept input:** Container requirements (image, ports, env vars)
2. **Generate output:** Complete AppHost + test boilerplate
3. **Provide feedback:** Resource status, connection strings, errors
4. **Iterate:** Adjust configuration based on test results

Use this pattern as a starting point and customize based on your specific needs.
