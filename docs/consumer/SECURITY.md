# Security Guide

Security best practices for using SurrealDB.Client in production applications.

## Table of Contents

1. [Connection Security](#connection-security)
2. [Credential Handling](#credential-handling)
3. [Parameterized Queries](#parameterized-queries)
4. [Authentication Methods](#authentication-methods)
5. [Network Security](#network-security)
6. [Data Handling](#data-handling)
7. [Known Limitations](#known-limitations)
8. [Security Updates](#security-updates)

## Connection Security

### HTTPS/TLS Connections

Always use HTTPS for connections over the network:

```csharp
// Good: HTTPS connection
var client = new SurrealDbClient("surreal://user:pass@db.example.com:443");
await client.ConnectAsync();

// Local development only: HTTP without credentials
var client = new SurrealDbClient("surreal://localhost:8000");
```

### Connection Strings

Never hardcode connection strings or credentials in source code:

```csharp
// Bad: Hardcoded credentials
var client = new SurrealDbClient("surreal://admin:secretpassword@localhost:8000");

// Good: From configuration
var connectionString = configuration["Database:ConnectionString"];
var client = new SurrealDbClient(connectionString);
```

### appsettings.json

Never commit sensitive data to version control:

```json
{
  "Database": {
    "ConnectionString": "surreal://localhost:8000"
  }
}
```

Use environment variables or secrets management:

```csharp
// Read from environment variable
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
var client = new SurrealDbClient(connectionString);

// ASP.NET Core secrets
services.Configure<DatabaseOptions>(options =>
{
    options.ConnectionString = configuration["Database:ConnectionString"];
});
```

### User Secrets (ASP.NET Core)

For local development:

```bash
dotnet user-secrets set "Database:ConnectionString" "surreal://user:pass@localhost:8000"
```

## Credential Handling

### Username and Password Authentication

Use strong passwords with complexity requirements:

```csharp
await client.AuthenticateAsync(
    new UsernamePasswordAuth
    {
        Username = "appuser",
        Password = "SecurePassword123!@#"
    }
);
```

### Token Authentication

Tokens are more secure for API-to-API communication:

```csharp
var token = Environment.GetEnvironmentVariable("SURREALDB_TOKEN");
await client.AuthenticateAsync(new TokenAuth { Token = token });
```

### Session Tokens

Keep tokens in memory, not on disk:

```csharp
// Token obtained from SurrealDB during auth
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync();
await client.AuthenticateAsync(auth);

// Use client in session
var users = await client.QueryAsync<User>("SELECT * FROM users");

// Clean up token when done
await client.LogoutAsync();
```

### Credential Rotation

Implement regular credential rotation:

```csharp
public class CredentialRotationService
{
    private readonly IConfiguration _config;

    public async Task<string> GetCurrentConnectionStringAsync()
    {
        // Fetch from secure store (e.g., Azure Key Vault, AWS Secrets Manager)
        var secret = await GetSecretAsync("Database:ConnectionString");
        return secret.Value;
    }

    private async Task<Secret> GetSecretAsync(string secretName)
    {
        // Implementation: fetch from your secret management system
        throw new NotImplementedException();
    }
}
```

## Parameterized Queries

Always use parameterized queries to prevent SQL injection:

```csharp
// Good: Parameterized query
var users = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE email = $email AND status = $status",
    new { email = userEmail, status = "active" }
);

// Bad: String concatenation (vulnerable to injection)
var query = $"SELECT * FROM users WHERE email = '{userEmail}' AND status = 'active'";
var users = await client.QueryAsync<User>(query);
```

### Injection Examples to Avoid

```csharp
// Vulnerable: User input directly in query
var userId = userInput; // Could be "'; DELETE FROM users; --"
var query = $"SELECT * FROM users WHERE id = '{userId}'";

// Safe: Parameterized
var users = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE id = $id",
    new { id = userId }
);
```

## Authentication Methods

### Type 1: Username/Password

Use for:
- Server-to-server communication
- Service accounts
- Internal tooling

```csharp
var auth = new UsernamePasswordAuth
{
    Username = Environment.GetEnvironmentVariable("DB_USER"),
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD")
};

await client.AuthenticateAsync(auth);
```

### Type 2: Token-Based

Use for:
- API authentication
- Service-to-service communication
- Temporary access

```csharp
var token = _tokenService.GenerateToken();
var auth = new TokenAuth { Token = token };

await client.AuthenticateAsync(auth);
```

### Type 3: OAuth2 / JWT (Future)

When supporting external authentication:

```csharp
// Validate JWT token
var handler = new JwtSecurityTokenHandler();
var token = handler.ReadToken(jwtToken) as JwtSecurityToken;

if (token != null && token.ValidTo > DateTime.UtcNow)
{
    await client.AuthenticateAsync(new TokenAuth { Token = jwtToken });
}
```

## Network Security

### Firewall Configuration

Restrict SurrealDB access by IP address:

```
Allow only:
- Application server: 192.168.1.100
- Admin workstations: 192.168.1.0/24
Deny: All other sources
```

### VPN/Private Network

For production, use VPN or private network:

```csharp
// Production: Private network connection
var client = new SurrealDbClient("surreal://internal-db.company.local:8000");

// Development: Local instance
var client = new SurrealDbClient("surreal://localhost:8000");
```

### Rate Limiting

Implement rate limiting at application level:

```csharp
public class RateLimitingService
{
    private readonly Dictionary<string, (int count, DateTime resetTime)> _userLimits = new();
    private readonly int _maxRequestsPerMinute = 100;

    public bool AllowRequest(string userId)
    {
        lock (_userLimits)
        {
            if (!_userLimits.TryGetValue(userId, out var limit))
            {
                _userLimits[userId] = (1, DateTime.UtcNow.AddMinutes(1));
                return true;
            }

            if (DateTime.UtcNow > limit.resetTime)
            {
                _userLimits[userId] = (1, DateTime.UtcNow.AddMinutes(1));
                return true;
            }

            if (limit.count < _maxRequestsPerMinute)
            {
                _userLimits[userId] = (limit.count + 1, limit.resetTime);
                return true;
            }

            return false;
        }
    }
}
```

## Data Handling

### Encryption in Transit

Use HTTPS for all connections:

```csharp
// Always use secure connection
var client = new SurrealDbClient("surreal://user:pass@db.example.com:443");
```

### Sensitive Data Logging

Never log sensitive data:

```csharp
// Bad: Logs password
_logger.LogInformation($"Authenticating with {username}:{password}");

// Good: Only log non-sensitive info
_logger.LogInformation($"Authenticating user: {username}");
```

### Data Sanitization

Sanitize input before querying:

```csharp
public string SanitizeInput(string input)
{
    if (string.IsNullOrWhiteSpace(input))
        return string.Empty;

    // Remove special characters, limit length
    return System.Text.RegularExpressions.Regex.Replace(
        input,
        @"[^\w\s-]",
        "",
        System.Globalization.RegexOptions.None
    ).Substring(0, Math.Min(input.Length, 255));
}

// Usage
var sanitized = SanitizeInput(userInput);
var users = await client.QueryAsync<User>(
    "SELECT * FROM users WHERE name = $name",
    new { name = sanitized }
);
```

### Memory Security

Clear sensitive data from memory:

```csharp
public class SecureCredentials : IDisposable
{
    private byte[] _password;

    public SecureCredentials(string password)
    {
        _password = System.Text.Encoding.UTF8.GetBytes(password);
    }

    public void Dispose()
    {
        // Clear from memory
        Array.Clear(_password, 0, _password.Length);
    }
}

// Usage
await using var credentials = new SecureCredentials(password);
// Password is cleared when disposed
```

## Known Limitations

### Connection Pool Size

The connection pool has a maximum size. Adjust based on load:

```csharp
var options = new SurrealDbClientOptions
{
    ConnectionString = "surreal://localhost:8000",
    PoolSize = 20  // Default is 10, adjust for your workload
};
```

### Timeout Configuration

Set appropriate timeouts:

```csharp
var options = new SurrealDbClientOptions
{
    Timeout = TimeSpan.FromSeconds(30)  // Adjust based on query complexity
};
```

### SurrealDB Version Support

Only SurrealDB 3.0+ is supported:

```csharp
// This will fail with SurrealDB < 3.0
var client = new SurrealDbClient("surreal://localhost:8000");
await client.ConnectAsync(); // Throws ConnectionException if version < 3.0
```

## Security Updates

### Monitoring

Stay informed of security updates:

1. **NuGet Notifications**: Enable package update notifications
2. **GitHub Watch**: Watch the repository for security notices
3. **Security Advisories**: Review published advisories

### Updating

Keep the client library updated:

```bash
# Check for updates
dotnet package search SurrealDB.Client

# Update to latest version
dotnet add package SurrealDB.Client --version "*"
```

### Vulnerability Reporting

Report security vulnerabilities responsibly:

1. Do not open public GitHub issues for security vulnerabilities
2. Contact the maintainers directly
3. Allow time for a fix before public disclosure

## Checklist for Production

Before deploying to production:

- [ ] HTTPS/TLS enabled for all connections
- [ ] Connection string from secure configuration (not hardcoded)
- [ ] Strong credentials with rotation policy
- [ ] Parameterized queries used throughout
- [ ] Firewall rules restrict database access
- [ ] Sensitive data not logged
- [ ] Rate limiting implemented
- [ ] Error handling doesn't expose sensitive info
- [ ] Connection pool size tuned for load
- [ ] Timeout values appropriate for workload
- [ ] Regular security updates applied
- [ ] Audit logging enabled for sensitive operations

## Example: Secure Application Setup

```csharp
public class SecureStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Load connection from secure source
        var connectionString = config["Database:ConnectionString"];

        // Register client with secure options
        services.AddScoped<ISurrealDbClient>(sp =>
        {
            var options = new SurrealDbClientOptions
            {
                ConnectionString = connectionString,
                PoolSize = 20,
                Timeout = TimeSpan.FromSeconds(30)
            };
            return new SurrealDbClient(options);
        });

        // Add logging with security filters
        services.AddLogging(options =>
        {
            options.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(
                level => level >= LogLevel.Information
            );
        });
    }

    public void Configure(IApplicationBuilder app)
    {
        // Enforce HTTPS
        app.UseHttpsRedirection();

        // Add security headers
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Add("X-Frame-Options", "DENY");
            context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
            await next();
        });
    }
}
```

## Related Documentation

- [Getting Started](GETTING_STARTED.md) - Installation and first steps
- [API Reference](API_REFERENCE.md) - Complete API documentation
- [Examples](EXAMPLES.md) - Code samples and patterns
- [SurrealDB Security](https://surrealdb.com/docs/security) - Server-side security
