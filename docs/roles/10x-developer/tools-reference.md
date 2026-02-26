# 10x Developer Tools Reference - SurrealDB.Client

## Profiling & Benchmarking Tools

### BenchmarkDotNet

**Purpose:** Micro-benchmark hot paths with allocation tracking

**Install:**
```bash
dotnet add package BenchmarkDotNet --project tests/SurrealDB.Client.Benchmarks
```

**Usage:**
```csharp
[MemoryDiagnoser]
public class PoolBenchmarks
{
    [Benchmark]
    public async Task Acquire() { ... }
}
```

**Run:**
```bash
dotnet run -c Release --project tests/SurrealDB.Client.Benchmarks -- \
  --filter "*Pool*" --memory
```

**Output:** CSV with columns: Job, Mean, Median, StdDev, Allocated

### dotnet-trace

**Purpose:** CPU profiling, GC events, allocation tracking

**Record trace (10 seconds):**
```bash
dotnet trace collect --duration 00:00:10 \
  --output trace.nettrace \
  --providers GCCollectionOnly,GCHeapSurvivalAndMovement
```

**Analyze in PerfView (Windows):**
```bash
PerfView.exe trace.nettrace
# View: GC Heap Snapshot, CPU Stack
```

**On Linux, view with speedscope:**
```bash
dotnet-trace convert trace.nettrace --format speedscope
# Open trace.speedscope.json in https://speedscope.app
```

### PerfView (Windows Only)

**Purpose:** ETW tracing, detailed lock contention analysis

**Download:** https://github.com/microsoft/perfview/releases

**Capture application profile:**
```bash
PerfView.exe run -Merge -ThreadTime -Zip -NoGui myapp.exe
# Opens PerfView with results
```

**Analyze:**
- View "CPU Stacks" to find hot functions
- View "GC Heap Alloc Stacks" to find allocators
- Check "Thread Time Stacks" for lock contention

### dotnet-counters

**Purpose:** Real-time metrics monitoring

**Install:**
```bash
dotnet tool install -g dotnet-counters
```

**Monitor running process:**
```bash
dotnet-counters monitor -p [PID]
# Shows: CPU %, Memory, GC collections, lock contention
```

**Example output:**
```
% Time in GC: 0.1%
Allocation Rate: 50 MB/sec
Gen 2 Collections: 3
```

---

## Micro-benchmark Template

Create `/tests/SurrealDB.Client.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SurrealDB.Client;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 10)]
public class ConnectionPoolBenchmarks
{
    private ConnectionPool _pool;
    private IProtocolAdapter _adapter;

    [GlobalSetup]
    public async Task Setup()
    {
        var options = new SurrealDbClientOptions { PoolSize = 10 };
        _pool = new ConnectionPool(options, ...);
        _adapter = await _pool.AcquireAsync(default);
    }

    [Benchmark]
    public async Task Acquire()
    {
        var conn = await _pool.AcquireAsync(default);
        await _pool.ReleaseAsync(conn);
    }

    [Benchmark]
    public async Task HealthCheck()
    {
        await _adapter.HealthCheckAsync(default);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pool.DisposeAsync();
    }
}

BenchmarkRunner.Run<ConnectionPoolBenchmarks>();
```

**Run with memory tracking:**
```bash
dotnet run -c Release --filter "*ConnectionPool*" -- --memory --exportjson results.json
```

**Acceptable baselines for SurrealDB.Client:**

| Operation | Target | Max Allocs |
|-----------|--------|-----------|
| Pool Acquire | < 1 ms | 2 |
| Health Check | < 50 ms | 1 |
| Serialize (1KB) | < 100 µs | 2 |
| Deserialize (1KB) | < 100 µs | 1 |

---

## Memory Profiling

### Heap Dump Analysis

**Capture heap dump:**
```bash
dotnet-dump collect -p [PID] -output dump.dmp
```

**Analyze with dotnet-gcdump:**
```bash
dotnet-gcdump collect -p [PID]
# Opens GC heap snapshot in visual analyzer
```

**Check for:**
- Large retained objects (should be few)
- Circular references (indicates leaks)
- Gen 2 survival rate (should be low)
- Pinned objects (limit networking/GC)

### GC Pause Time

**Monitor during load test:**
```bash
dotnet-counters monitor -p [PID] --counters \
  System.Runtime[gc-pause-duration-percent], \
  System.Runtime[gen-2-gc-count]
```

**Target:** GC pause < 50 ms in 99th percentile

---

## Load Testing

### Simple Load Test with Apache Bench

```bash
# 1000 requests, 10 concurrent
ab -n 1000 -c 10 http://localhost:8000/health

# View: Requests/sec, response times
```

### Custom Load Test Script

```csharp
using var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();

var sw = Stopwatch.StartNew();
var tasks = Enumerable.Range(0, 1000)
    .Select(i => client.QueryAsync($"SELECT * FROM thing WHERE id = {i}"))
    .ToList();

await Task.WhenAll(tasks);
sw.Stop();

Console.WriteLine($"1000 queries in {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"Throughput: {1000 / (sw.ElapsedMilliseconds / 1000.0):F2} req/sec");
```

---

## Profiler Integration in Tests

### Add BenchmarkDotNet to Tests

```xml
<!-- tests/SurrealDB.Client.Benchmarks/SurrealDB.Client.Benchmarks.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/SurrealDB.Client/SurrealDB.Client.csproj" />
  </ItemGroup>
</Project>
```

### CI Integration

Add to `.github/workflows/benchmark.yml`:

```yaml
name: Benchmarks

on: [push, pull_request]

jobs:
  benchmark:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 9.0.x

      - run: dotnet run -c Release --project tests/SurrealDB.Client.Benchmarks

      - uses: benchmark-action/github-action@v1
        with:
          name: SurrealDB.Client Benchmarks
          tool: 'benchmarkdotnet'
          output-file-path: BenchmarkDotNet.Artifacts/results/results.json
```

---

## Code Inspection Tools

### Roslyn Analyzers

**Install code quality analyzers:**
```bash
dotnet add package Microsoft.CodeAnalysis.NetAnalyzers
```

**Enable strict checks in .csproj:**
```xml
<PropertyGroup>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <AnalysisLevel>latest</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

**Run analysis:**
```bash
dotnet build /p:TreatWarningsAsErrors=true
```

### Static Analysis with SonarQube (Optional)

```bash
dotnet sonarscanner begin /k:"SurrealDB.Client" /o:"your-org" /d:sonar.host.url=...
dotnet build
dotnet sonarscanner end
```

---

## Dependency Analysis

### Check for transitive dependency bloat

```bash
dotnet list package --include-transitive
```

**Target:** Minimal external dependencies. For B-Grade, only:
- `System.Text.Json` (built-in)
- `System.Net.Http` (built-in)
- Test frameworks (Xunit, Moq)

### Assembly size analysis

```bash
# List all assemblies and their size
ls -lh bin/Release/net9.0/*.dll

# Target: SurrealDB.Client < 500 KB
```

---

## Continuous Profiling

### Daily Performance Check

```bash
#!/bin/bash
# run-perf-check.sh

echo "=== Running Benchmarks ==="
dotnet run -c Release --project tests/SurrealDB.Client.Benchmarks \
  --exportjson results.json

echo "=== Memory Profiling ==="
dotnet-trace collect --duration 00:01:00 --output trace.nettrace

echo "=== Comparing to Baseline ==="
if [ -f docs/PERFORMANCE_BASELINE.md ]; then
  echo "Previous baseline:"
  cat docs/PERFORMANCE_BASELINE.md
fi

echo "Current results: results.json"
echo "Trace captured: trace.nettrace"
```

**Run daily in CI:**
```bash
chmod +x run-perf-check.sh
./run-perf-check.sh
```

---

## Performance Regression Detection

### GitHub Action to Catch Regressions

```yaml
name: Performance Check

on: [pull_request]

jobs:
  benchmark:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - run: dotnet run -c Release --project tests/SurrealDB.Client.Benchmarks \
              --exportjson /tmp/pr-results.json

      - uses: actions/checkout@v3
        with:
          ref: main

      - run: dotnet run -c Release --project tests/SurrealDB.Client.Benchmarks \
              --exportjson /tmp/main-results.json

      - name: Compare
        run: |
          # Compare /tmp/main-results.json vs /tmp/pr-results.json
          # Fail if latency regressed > 5%
          python3 .github/compare-benchmarks.py \
            /tmp/main-results.json /tmp/pr-results.json
```

---

## Key Commands Cheat Sheet

```bash
# Benchmark hot paths
dotnet run -c Release --project tests/SurrealDB.Client.Benchmarks --filter "*Acquire*" --memory

# Profile CPU
dotnet-trace collect --duration 00:00:10 --output trace.nettrace

# Monitor live
dotnet-counters monitor -p [PID]

# Memory leak detection
dotnet-gcdump collect -p [PID]

# Build with strict checks
dotnet build /p:TreatWarningsAsErrors=true

# Load test
ab -n 1000 -c 10 http://localhost:8000/health

# Check allocation
BenchmarkDotNet /MemoryDiagnoser /ExportJson=results.json
```

---

## Performance SLA Reminders

**Before every release:**
- [ ] Benchmarks run, no regressions
- [ ] Memory profile: no leaks, GC pause < 50 ms
- [ ] Load test: 1000 req/sec on 10-connection pool
- [ ] CPU profile: no obvious hotspots
- [ ] Lock contention: < 1% CPU in locks

**Commit performance baseline to git:**
```bash
cp BenchmarkDotNet.Artifacts/results/results.json docs/PERFORMANCE_BASELINE.md
git add docs/PERFORMANCE_BASELINE.md
git commit -m "Update performance baseline"
```
