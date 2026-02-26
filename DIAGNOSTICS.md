# Diagnostics, Monitoring & Profiling

> Comprehensive observability infrastructure including logging, metrics, performance profiling, and health checks - enterprise-grade monitoring.

## Overview

Multi-layered diagnostics:
1. **Logging** - Structured logs (ILogger)
2. **Metrics** - Performance counters
3. **Tracing** - Distributed tracing (OpenTelemetry)
4. **Diagnostics** - Runtime diagnostics
5. **Health Checks** - Connection health

---

## 1. Structured Logging

### Configuration

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddDebug();
    builder.AddApplicationInsights();
});

services.AddSurrealDbClient(options =>
{
    options.LogLevel = LogLevel.Information;
    options.LogQueryText = true;
    options.LogParameterValues = false;  // Security: don't log sensitive data
    options.LogQueryDuration = true;
});
```

### Log Categories

```
SurrealDB.Client.Connection
SurrealDB.Client.Protocol
SurrealDB.Client.Query
SurrealDB.Client.Auth
SurrealDB.Client.Error
SurrealDB.Client.Performance
SurrealDB.Client.StateManagement
```

### Example Logs

```
[INF] Connection established to ws://localhost:8000 in 125ms
[INF] Query executed: SELECT * FROM users WHERE age >= 18 in 45ms
[WRN] Slow query (250ms): SELECT * FROM orders WITH items, payments
[ERR] Authentication failed: Invalid credentials for user 'admin'
[DBG] Entity state changed: user:1 → Modified (2 properties changed)
[DBG] Change tracking detected: Email, UpdatedAt modified
```

### Structured Properties

```csharp
logger.LogInformation(
    "Query executed",
    new {
        QueryId = Guid.NewGuid(),
        Query = "SELECT * FROM users",
        Duration = 45,
        RowsAffected = 100,
        CacheHit = false
    }
);
```

---

## 2. Performance Metrics

Real-time performance counters:

```csharp
public class ClientMetrics
{
    public long TotalQueries { get; }
    public long TotalQueryErrors { get; }
    public TimeSpan AverageQueryTime { get; }
    public TimeSpan MaxQueryTime { get; }

    public int ActiveConnections { get; }
    public int PoolSize { get; }
    public double PoolUtilization { get; }

    public long BytesSent { get; }
    public long BytesReceived { get; }

    public double QueryPlanCacheHitRate { get; }
    public double ResultCacheHitRate { get; }

    public long TotalChangeTracked { get; }
    public long ChangesDetected { get; }

    public long AuthenticationAttempts { get; }
    public long AuthenticationFailures { get; }
}
```

### Usage

```csharp
var metrics = client.GetMetrics();

Console.WriteLine($"Total queries: {metrics.TotalQueries}");
Console.WriteLine($"Avg query time: {metrics.AverageQueryTime.TotalMilliseconds}ms");
Console.WriteLine($"Pool utilization: {metrics.PoolUtilization:P1}");
Console.WriteLine($"Cache hit rate: {metrics.QueryPlanCacheHitRate:P1}");
Console.WriteLine($"Data transferred: {metrics.BytesReceived / 1024 / 1024}MB");
```

### Reset Metrics

```csharp
// Reset for testing period
client.ResetMetrics();

// ... run operations ...

var metrics = client.GetMetrics();
// Metrics show only operations since reset
```

---

## 3. OpenTelemetry Integration

Distributed tracing across services:

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder =>
    {
        builder
            .AddSurrealDbClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:4317");
            });
    });
```

### Trace Export

```
Trace: Query.SelectAsync
├─ Span: Query.Building
│  └─ Duration: 5ms
├─ Span: Protocol.Executing
│  ├─ Span: Connection.Acquire
│  │  └─ Duration: 2ms
│  ├─ Span: Network.Transmission
│  │  └─ Duration: 25ms
│  └─ Duration: 30ms
├─ Span: Serialization.Deserializing
│  └─ Duration: 8ms
└─ Total Duration: 43ms
```

### Custom Spans

```csharp
using var activity = new Activity("CustomOperation").Start();

try
{
    // Your code
}
finally
{
    activity.Dispose();
}

// Traces custom operation
```

---

## 4. Runtime Diagnostics

### Connection Diagnostics

```csharp
var diagnostics = await client.GetConnectionDiagnosticsAsync();

Console.WriteLine($"Server version: {diagnostics.ServerVersion}");
Console.WriteLine($"Connection latency: {diagnostics.Latency}ms");
Console.WriteLine($"Protocol: {diagnostics.Protocol}");
Console.WriteLine($"Is connected: {diagnostics.IsConnected}");
Console.WriteLine($"Last error: {diagnostics.LastError}");
Console.WriteLine($"Connection uptime: {diagnostics.Uptime}");
```

### Session Diagnostics

```csharp
using var session = client.CreateSession();

var diag = session.GetDiagnostics();

Console.WriteLine($"Tracked entities: {diag.TrackedEntityCount}");
Console.WriteLine($"Modified entities: {diag.ModifiedEntityCount}");
Console.WriteLine($"Memory usage: {diag.MemoryUsageMB}MB");
Console.WriteLine($"Change detection time: {diag.ChangeDetectionMs}ms");
```

### Performance Profiler

```csharp
using var profiler = client.StartProfilingSession();

// ... execute operations ...

var results = profiler.GetResults();

foreach (var operation in results.Operations)
{
    Console.WriteLine($"{operation.Name}: {operation.Duration}ms");
}

Console.WriteLine($"Total duration: {results.TotalDuration}ms");
Console.WriteLine($"Memory allocated: {results.MemoryAllocatedMB}MB");
```

---

## 5. Health Checks

### Built-in Health Check

```csharp
services.AddHealthChecks()
    .AddSurrealDbHealth("surrealdb", timeout: TimeSpan.FromSeconds(5));

// Endpoint
app.MapHealthChecks("/health");

// Response:
// {
//   "status": "Healthy",
//   "checks": {
//     "surrealdb": {
//       "status": "Healthy",
//       "latency": "45ms",
//       "connected": true
//     }
//   }
// }
```

### Custom Health Check

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ISurrealDbClient _client;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct)
    {
        try
        {
            var diagnostics = await _client.GetConnectionDiagnosticsAsync(ct);

            if (!diagnostics.IsConnected)
                return HealthCheckResult.Unhealthy("Not connected");

            if (diagnostics.Latency > 1000)
                return HealthCheckResult.Degraded($"High latency: {diagnostics.Latency}ms");

            return HealthCheckResult.Healthy(
                $"Connected with latency: {diagnostics.Latency}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Error: {ex.Message}");
        }
    }
}
```

---

## 6. Event Diagnostics

Listen to diagnostic events:

```csharp
client.DiagnosticListener += (source, eventName, args) =>
{
    if (eventName == "Query.Executing")
    {
        var query = args["Query"] as string;
        Console.WriteLine($"Executing: {query}");
    }
    else if (eventName == "Query.Executed")
    {
        var duration = (TimeSpan)args["Duration"];
        Console.WriteLine($"Completed in {duration.TotalMilliseconds}ms");
    }
};
```

### Diagnostic Event Types

- `Connection.Opening`
- `Connection.Opened`
- `Connection.Closing`
- `Query.Executing`
- `Query.Executed`
- `Query.Failed`
- `SaveChanges.Starting`
- `SaveChanges.Completed`
- `SaveChanges.Failed`
- `Authentication.Attempting`
- `Authentication.Succeeded`
- `Authentication.Failed`

---

## 7. Debugging Tools

### Query Inspector

```csharp
// See generated SurrealQL
var query = session.Set<User>()
    .Where(u => u.Age >= 18)
    .OrderBy(u => u.Name);

var sql = query.ToSurrealQL();
Console.WriteLine(sql);
// Output: SELECT * FROM users WHERE age >= 18 ORDER BY name

// See query parameters
var parameters = query.GetParameters();
// Output: { }

// See execution plan
var plan = query.GetExecutionPlan();
// Output: QueryPlan { ... }
```

### Entity State Inspector

```csharp
var user = await session.FindAsync<User>("user:1");
user.Email = "new@test.com";

var entry = session.ChangeTracker.Entry(user);

Console.WriteLine($"State: {entry.State}");
// Output: Modified

Console.WriteLine($"Modified properties: {string.Join(", ", entry.GetModifiedProperties())}");
// Output: Email

var changes = entry.GetPropertyChanges();
// Output: { Email: "new@test.com" }

var original = entry.GetOriginalValues();
// Output: { Email: "old@test.com" }
```

---

## 8. Performance Monitoring Dashboard

Suggested dashboard metrics:

```
┌─────────────────────────────────────────────────┐
│ SurrealDB.Client Performance Dashboard          │
├─────────────────────────────────────────────────┤
│ Queries                                         │
│  Total: 15,234  Errors: 12  Avg: 45ms Max: 2s │
│                                                 │
│ Connections                                    │
│  Active: 8/10 (80%)  Pool utilization: 80%    │
│  Avg latency: 25ms                             │
│                                                 │
│ Cache Performance                              │
│  Query plan hit rate: 98%                      │
│  Result cache hit rate: 75%                    │
│  Bytes transferred: 523MB (↓ 88% with cache)   │
│                                                 │
│ Entity Tracking                                │
│  Tracked: 1,234  Modified: 89  Bandwidth: 12MB│
│  Avg session memory: 2.3MB                     │
│                                                 │
│ Authentication                                 │
│  Attempts: 234  Failures: 3  Success rate: 99%│
│                                                 │
│ Errors Last Hour                              │
│  Connection: 1  Auth: 0  Query: 2  State: 0   │
└─────────────────────────────────────────────────┘
```

---

## 9. Alerting

```csharp
// Alert on high query latency
client.OnMetricsUpdated += (metrics) =>
{
    if (metrics.AverageQueryTime > TimeSpan.FromSeconds(1))
    {
        alertService.SendAlert(
            "High query latency",
            $"Average: {metrics.AverageQueryTime.TotalSeconds}s");
    }

    if (metrics.PoolUtilization > 0.9)
    {
        alertService.SendAlert(
            "Pool exhaustion risk",
            $"Utilization: {metrics.PoolUtilization:P}");
    }

    if (metrics.QueryPlanCacheHitRate < 0.7)
    {
        alertService.SendAlert(
            "Cache efficiency degraded",
            $"Hit rate: {metrics.QueryPlanCacheHitRate:P}");
    }
};
```

---

## 10. Best Practices

1. **Log selectively** - Not every query
   ```csharp
   LogLevel = LogLevel.Warning;  // Only warnings+
   ```

2. **Use structured logging** - For aggregation
   ```csharp
   logger.LogInformation("Query executed", new { Duration = 45, Rows = 100 });
   ```

3. **Monitor metrics** - Track trends
   ```csharp
   var metrics = client.GetMetrics();
   // Log to monitoring system
   ```

4. **Profile in production** - But carefully
   ```csharp
   // Sample 1% of requests
   if (Random.Shared.Next(100) == 0) profiler.Start();
   ```

5. **Alert on thresholds** - Not anomalies
   ```csharp
   if (latency > 1000ms) Alert();  // Clear threshold
   ```

6. **Maintain dashboards** - Real-time visibility
   ```
   Live metrics endpoint for Grafana/Datadog
   ```

7. **Clean up logs** - Prevent disk bloat
   ```csharp
   // Rotate logs daily
   // Archive old logs
   ```

