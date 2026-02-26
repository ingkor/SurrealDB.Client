# Tester Tools Reference - SurrealDB.Client

## Running Tests

```bash
# Run all tests in the solution
dotnet test /home/user/SurrealDB.Client/SurrealDB.Client.sln

# Run only unit tests
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/

# Run only integration tests
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Integration/

# Run with verbose output (shows test names and timings)
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ -v normal

# Run a single test by full name
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --filter "FullyQualifiedName=SurrealDB.Client.Tests.Unit.ConnectionPoolTests.ConnectionPool_Acquire_ReturnsConnection"

# Run all tests in a class
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --filter "ClassName=SurrealDB.Client.Tests.Unit.ConnectionPoolTests"

# Run tests matching a pattern
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --filter "ConnectionPool"

# List all available tests without running them
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ --list-tests
```

## Code Coverage

```bash
# Collect code coverage (requires coverlet — included via test project reference)
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --collect:"XPlat Code Coverage" \
  --results-directory /tmp/surrealdb-coverage

# View the coverage XML
cat /tmp/surrealdb-coverage/*/coverage.cobertura.xml | \
  grep -E 'line-rate|branch-rate|name="SurrealDB'

# Install reportgenerator to get HTML coverage report
dotnet tool install -g dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator \
  -reports:"/tmp/surrealdb-coverage/*/coverage.cobertura.xml" \
  -targetdir:"/tmp/surrealdb-coverage-report" \
  -reporttypes:Html

# Open the report (adjust for your environment)
xdg-open /tmp/surrealdb-coverage-report/index.html
```

## Identifying Coverage Gaps

```bash
# Find source files with no corresponding test file
for f in /home/user/SurrealDB.Client/src/SurrealDB.Client/**/*.cs; do
  name=$(basename "$f" .cs)
  if ! find /home/user/SurrealDB.Client/tests -name "${name}Tests.cs" 2>/dev/null | grep -q .; then
    echo "No test file for: $name"
  fi
done

# Count test methods per test class
grep -rn "\[Fact\]\|\[Theory\]" /home/user/SurrealDB.Client/tests/ --include="*.cs" | \
  sed 's|.*tests/||' | cut -d'/' -f2 | sort | uniq -c | sort -rn
```

## Checking for Known Bug Scenarios

```bash
# Check if WebSocket buffer is still fixed size (the truncation bug)
grep -n "byte\[" /home/user/SurrealDB.Client/src/SurrealDB.Client/Protocol/WebSocketProtocolAdapter.cs
# If output shows "1024 * 4" or "4096", the buffer truncation bug is not yet fixed

# Check if DisposeAsync calls ClearAsync (the deadlock bug)
grep -A5 "public async ValueTask DisposeAsync" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
# If output shows "await ClearAsync()" inside DisposeAsync, deadlock is present

# Check if _allConnections is accessed without lock in GetStatistics
grep -A10 "public PoolStatistics GetStatistics" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/Connection/ConnectionPool.cs
# If "_allConnections.Count(c =>" appears without "lock (_allConnections)", race is present
```

## Verifying TODO Stubs Are Still Present

```bash
# Count remaining TODO stubs in CRUD methods
grep -c "await Task.CompletedTask" \
  /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs
# Expected: 9 (when all stubs remain); should be 0 after Phase 1 implementation

# Show all TODO locations
grep -n "// TODO" /home/user/SurrealDB.Client/src/SurrealDB.Client/SurrealDbClient.cs
```

## Test Environment Setup

```bash
# Set environment variables for integration tests
export SURREALDB_URL="surreal://localhost:8000"
export SURREALDB_USER="root"
export SURREALDB_PASS="root"
export SURREALDB_NS="testns"
export SURREALDB_DB="testdb"

# Verify SurrealDB is accessible
curl -s http://localhost:8000/health
# Expected: HTTP 200

# Run integration tests with environment set
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Integration/ -v normal
```

## Running SurrealDB Locally (Docker)

```bash
# Start SurrealDB with Docker
docker run -d \
  --name surrealdb-test \
  -p 8000:8000 \
  surrealdb/surrealdb:latest \
  start --log trace --user root --pass root memory

# Check SurrealDB is running
docker logs surrealdb-test
curl -s http://localhost:8000/health

# Stop and remove when done
docker stop surrealdb-test && docker rm surrealdb-test
```

## Useful Test Debugging Commands

```bash
# Run tests in verbose mode showing all test names
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --logger "console;verbosity=detailed"

# Output test results to XML (for CI integration)
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --logger "junit;LogFilePath=/tmp/test-results.xml"

# Run tests in a specific order (xUnit runs in parallel by default)
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --settings /home/user/SurrealDB.Client/tests/xunit.runsettings

# Check how long each test takes
dotnet test /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/ \
  --logger "console;verbosity=normal" 2>&1 | grep -E "passed|failed|ms\]"
```

## Counting Existing Tests

```bash
# Total test count
grep -rn "\[Fact\]\|\[Theory\]" /home/user/SurrealDB.Client/tests/ --include="*.cs" | wc -l

# Test count per file
grep -rn "\[Fact\]\|\[Theory\]" /home/user/SurrealDB.Client/tests/ --include="*.cs" | \
  sed 's|:.*||' | sort | uniq -c | sort -rn

# Find tests that might be testing stub behavior (trivially passing)
grep -B2 "\[Fact\]" /home/user/SurrealDB.Client/tests/SurrealDB.Client.Tests.Unit/SurrealDbClientTests.cs | \
  grep "Fact\|Assert"
```
