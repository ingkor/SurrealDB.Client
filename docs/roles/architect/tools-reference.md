# Architect Tools Reference - SurrealDB.Client

## Analyzing Code Structure

### List all public types in the library
```bash
grep -rn "^public " /home/user/SurrealDB.Client/src/SurrealDB.Client/ --include="*.cs" | \
  grep -E "class|interface|enum|struct" | sort
```

### Find all places that bypass the IProtocolAdapter abstraction
```bash
grep -rn "HttpClient\|ClientWebSocket" /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs
```
Expected: 0 results. Any result means a layer violation.

### Find all async methods missing CancellationToken
```bash
grep -rn "async Task" /home/user/SurrealDB.Client/src/SurrealDB.Client/ --include="*.cs" | \
  grep -v "CancellationToken\|void\|ValueTask"
```

### Find all .Result and .Wait() calls (deadlock risk)
```bash
grep -rn "\.Result\b\|\.Wait()" /home/user/SurrealDB.Client/src/ --include="*.cs"
```
Expected: 0 results in library code.

### Find all direct semaphore operations to audit for balance
```bash
grep -n "WaitAsync\|\.Release()" /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
```

### Find all _allConnections accesses without lock
```bash
grep -n "_allConnections" /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
```
Every access should be either inside `lock (_allConnections) { }` or in a single-threaded startup/disposal context.

---

## Checking Multi-Target Compatibility

### Build and check for target-specific warnings
```bash
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -f net8.0 2>&1 | grep -i "warning\|error"
dotnet build /home/user/SurrealDB.Client/src/SurrealDB.Client/ -f net9.0 2>&1 | grep -i "warning\|error"
```

### Check for #if preprocessor directives (target-specific code paths)
```bash
grep -rn "#if\|#elif" /home/user/SurrealDB.Client/src/ --include="*.cs"
```

---

## Architecture Smell Detection

### Find any async void (should be zero in library code)
```bash
grep -rn "async void" /home/user/SurrealDB.Client/src/ --include="*.cs"
```

### Find exception swallowing outside of disposal
```bash
grep -n "catch\s*{" /home/user/SurrealDB.Client/src/SurrealDB.Client/ -r --include="*.cs"
```
Review each result — bare `catch {}` is only acceptable in disposal paths, and must have a comment.

### Find types that own resources but don't implement IAsyncDisposable
```bash
grep -rn "SemaphoreSlim\|ClientWebSocket\|HttpClient\|CancellationTokenSource" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/ --include="*.cs" -l
```
Then verify each file also implements `IAsyncDisposable` or `IDisposable`.

### Find all TODO comments (should be zero before Phase 1 release)
```bash
grep -rn "// TODO" /home/user/SurrealDB.Client/src/ --include="*.cs"
```

---

## Dependency Analysis

### View all project dependencies
```bash
dotnet list /home/user/SurrealDB.Client/src/SurrealDB.Client/ package
dotnet list /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ package
dotnet list /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Integration/ package
```

### Check if library has any external NuGet dependencies (should be minimal)
```bash
cat /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDB.Client.csproj
```

### View Directory.Build.props (shared build config)
```bash
cat /home/user/SurrealDB.Client/Directory.Build.props
```

---

## Performance Analysis Commands

### Check WebSocket buffer sizes
```bash
grep -n "byte\[" /home/user/SurrealDB.Client/src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs
```
The 4 KB buffer (`new byte[1024 * 4]`) must be replaced with a loop that reads until `EndOfMessage`.

### Find all object allocations in hot paths (connection acquire/release)
```bash
grep -n "new " /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
```
Allocations inside `AcquireAsync` and `ReleaseAsync` should be minimized.

### Find all locks in the codebase
```bash
grep -rn "\block\b" /home/user/SurrealDB.Client/src/ --include="*.cs"
```

---

## Architecture Document Locations

| Document | Path |
|----------|------|
| Architecture overview | `/home/user/SurrealDB.Client/ARCHITECTURE.md` |
| Risk assessment | `/home/user/SurrealDB.Client/RISK_ASSESSMENT.md` |
| Design decisions | `/home/user/SurrealDB.Client/DESIGN_DECISIONS.md` |
| Security model | `/home/user/SurrealDB.Client/SECURITY.md` |
| Grade assessment | `/home/user/SurrealDB.Client/B_GRADE_BASELINE.md` |
| Grade levels | `/home/user/SurrealDB.Client/GRADE_LEVELS.md` |

---

## Visualizing the Codebase Layout

```bash
find /home/user/SurrealDB.Client/src -name "*.cs" | sort | \
  sed 's|/home/user/SurrealDB.Client/src/SurrealDB.Client/||'
```

Expected output at Phase 1:
```
Authentication/IAuthenticationProvider.cs
Connection/ConnectionPool.cs
Connection/IConnectionPool.cs
Exceptions/AuthenticationException.cs
Exceptions/ConnectionException.cs
Exceptions/QueryException.cs
Exceptions/SerializationException.cs
Exceptions/SurrealDbException.cs
Exceptions/TimeoutException.cs
Exceptions/ValidationException.cs
ISurrealDbClient.cs
Protocol/HttpProtocolAdapter.cs
Protocol/IProtocolAdapter.cs
Protocol/ProtocolAdapterFactory.cs
Protocol/WebSocketProtocolAdapter.cs
Serialization/ISerializer.cs
Serialization/SystemTextJsonSerializer.cs
SurrealDB.Client.csproj
SurrealDbClient.cs
SurrealDbClientOptions.cs
```
