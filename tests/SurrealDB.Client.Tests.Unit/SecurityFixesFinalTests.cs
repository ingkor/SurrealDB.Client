using System.Net;
using System.Reflection;
using SurrealDB.Client.Exceptions;
using Xunit;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for the final two security vulnerability fixes (P0-1 and P0-2).
///
/// P0-1: Error Message Exposure - Server error responses expose sensitive information
/// P0-2: Connection String Credentials - Connection strings may contain embedded credentials
/// </summary>
public class SecurityFixesFinalTests
{
    #region P0-1: Error Message Sanitization Tests

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Invalid request format")]
    [InlineData(HttpStatusCode.Unauthorized, "Authentication failed")]
    [InlineData(HttpStatusCode.Forbidden, "Access denied")]
    [InlineData(HttpStatusCode.NotFound, "Resource not found")]
    [InlineData(HttpStatusCode.InternalServerError, "Server error occurred")]
    [InlineData(HttpStatusCode.BadGateway, "Server error occurred")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Server error occurred")]
    [InlineData(HttpStatusCode.GatewayTimeout, "Server error occurred")]
    [InlineData(HttpStatusCode.Conflict, "Operation failed")]
    [InlineData(HttpStatusCode.RequestTimeout, "Operation failed")]
    public void P01_SanitizeErrorMessage_ReturnsGenericMessageForStatusCode(HttpStatusCode statusCode, string expectedMessage)
    {
        // Arrange - Simulate a detailed server error with sensitive information
        var sensitiveError = @"{
            ""error"": {
                ""code"": -32000,
                ""message"": ""Query failed at line 5: table 'users' does not exist"",
                ""stack"": ""at SurrealDB.Query.Execute() in /app/query.rs:line 145\nat SurrealDB.Connection.Process() in /app/conn.rs:line 89"",
                ""query"": ""SELECT * FROM users WHERE password = 'admin123'"",
                ""schema"": {
                    ""tables"": [""internal_users"", ""system_config"", ""auth_tokens""]
                }
            }
        }";

        // Act - Use reflection to call private SanitizeErrorMessage method
        var method = GetSanitizeErrorMessageMethod();
        var result = (string)method.Invoke(null, new object[] { sensitiveError, statusCode })!;

        // Assert - Verify generic message is returned, no sensitive info exposed
        Assert.Equal(expectedMessage, result);
        Assert.DoesNotContain("users", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin123", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("query.rs", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("schema", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal_users", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P01_SanitizeErrorMessage_DoesNotExposeDatabaseSchema()
    {
        // Arrange - Error containing database schema information
        var schemaError = @"{
            ""error"": {
                ""message"": ""Access denied to table 'admin_credentials'"",
                ""available_tables"": [""users"", ""sessions"", ""api_keys"", ""admin_credentials""]
            }
        }";

        // Act
        var method = GetSanitizeErrorMessageMethod();
        var result = (string)method.Invoke(null, new object[] { schemaError, HttpStatusCode.Forbidden })!;

        // Assert
        Assert.Equal("Access denied", result);
        Assert.DoesNotContain("admin_credentials", result);
        Assert.DoesNotContain("users", result);
        Assert.DoesNotContain("sessions", result);
        Assert.DoesNotContain("api_keys", result);
        Assert.DoesNotContain("available_tables", result);
    }

    [Fact]
    public void P01_SanitizeErrorMessage_DoesNotExposeStackTraces()
    {
        // Arrange - Error with detailed stack trace
        var stackTraceError = @"{
            ""error"": {
                ""message"": ""Internal server error"",
                ""stack"": ""at QueryEngine.execute(query.rs:234)\nat Connection.process(conn.rs:89)\nat Server.handle(server.rs:456)"",
                ""file"": ""/app/src/query.rs"",
                ""line"": 234
            }
        }";

        // Act
        var method = GetSanitizeErrorMessageMethod();
        var result = (string)method.Invoke(null, new object[] { stackTraceError, HttpStatusCode.InternalServerError })!;

        // Assert
        Assert.Equal("Server error occurred", result);
        Assert.DoesNotContain("query.rs", result);
        Assert.DoesNotContain("conn.rs", result);
        Assert.DoesNotContain("server.rs", result);
        Assert.DoesNotContain("234", result);
        Assert.DoesNotContain("/app/src", result);
        Assert.DoesNotContain("QueryEngine", result);
    }

    [Fact]
    public void P01_SanitizeErrorMessage_DoesNotExposeQueryDetails()
    {
        // Arrange - Error containing the actual query that failed
        var queryError = @"{
            ""error"": {
                ""message"": ""Syntax error in query"",
                ""query"": ""SELECT * FROM users WHERE username='admin' AND password='secretpassword123'"",
                ""position"": 42
            }
        }";

        // Act
        var method = GetSanitizeErrorMessageMethod();
        var result = (string)method.Invoke(null, new object[] { queryError, HttpStatusCode.BadRequest })!;

        // Assert
        Assert.Equal("Invalid request format", result);
        Assert.DoesNotContain("SELECT", result);
        Assert.DoesNotContain("users", result);
        Assert.DoesNotContain("admin", result);
        Assert.DoesNotContain("secretpassword123", result);
        Assert.DoesNotContain("query", result);
    }

    [Fact]
    public void P01_SanitizeErrorMessage_DoesNotExposeAuthenticationInfo()
    {
        // Arrange - Error with authentication details
        var authError = @"{
            ""error"": {
                ""message"": ""Invalid credentials for user 'dbadmin'"",
                ""username"": ""dbadmin"",
                ""attempted_method"": ""basic_auth"",
                ""valid_users"": [""admin"", ""root"", ""system""]
            }
        }";

        // Act
        var method = GetSanitizeErrorMessageMethod();
        var result = (string)method.Invoke(null, new object[] { authError, HttpStatusCode.Unauthorized })!;

        // Assert
        Assert.Equal("Authentication failed", result);
        Assert.DoesNotContain("dbadmin", result);
        Assert.DoesNotContain("admin", result);
        Assert.DoesNotContain("root", result);
        Assert.DoesNotContain("system", result);
        Assert.DoesNotContain("basic_auth", result);
        Assert.DoesNotContain("valid_users", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not JSON at all")]
    [InlineData("{malformed json}")]
    public void P01_SanitizeErrorMessage_HandlesInvalidErrorContent(string errorContent)
    {
        // Arrange - Various invalid/malformed error content

        // Act - Should not throw, should return generic message
        var method = GetSanitizeErrorMessageMethod();
        var result = (string)method.Invoke(null, new object[] { errorContent, HttpStatusCode.InternalServerError })!;

        // Assert
        Assert.Equal("Server error occurred", result);
    }

    [Fact]
    public void P01_SanitizeErrorMessage_HandlesNullErrorContent()
    {
        // Act - Null error content should be handled gracefully
        var method = GetSanitizeErrorMessageMethod();

        // In C#, passing null for string parameter
        var result = (string)method.Invoke(null, new object?[] { null, HttpStatusCode.BadRequest })!;

        // Assert
        Assert.Equal("Invalid request format", result);
    }

    /// <summary>
    /// Helper method to get the private SanitizeErrorMessage method via reflection.
    /// </summary>
    private static MethodInfo GetSanitizeErrorMessageMethod()
    {
        var adapterType = Type.GetType("SurrealDB.Client.Protocol.HttpProtocolAdapter, SurrealDB.Client");
        Assert.NotNull(adapterType);

        var method = adapterType.GetMethod(
            "SanitizeErrorMessage",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(string), typeof(HttpStatusCode) },
            null);

        Assert.NotNull(method);
        return method;
    }

    #endregion

    #region P0-2: Connection String Validation Tests

    [Theory]
    [InlineData("surreal://localhost:8000")]
    [InlineData("http://localhost:8000")]
    [InlineData("https://localhost:8000")]
    [InlineData("ws://localhost:8000")]
    [InlineData("wss://localhost:8000")]
    [InlineData("surreal://db.example.com:8000")]
    [InlineData("https://192.168.1.100:8000")]
    [InlineData("wss://db.example.com:8000/rpc")]
    public void P02_ValidateConnectionString_AllowsValidConnectionStrings(string connectionString)
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = connectionString,
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Theory]
    [InlineData("surreal://admin:password@localhost:8000")]
    [InlineData("http://user:pass@localhost:8000")]
    [InlineData("https://dbadmin:secretpassword123@db.example.com:8000")]
    [InlineData("ws://root:toor@localhost:8000")]
    [InlineData("wss://api_user:api_key_12345@db.example.com:8000")]
    [InlineData("surreal://admin:p@ssw0rd!@192.168.1.100:8000")]
    public void P02_ValidateConnectionString_RejectsEmbeddedCredentials(string connectionString)
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = connectionString,
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("cannot contain embedded credentials", exception.Message);
        Assert.Contains("user:password@", exception.Message);
        Assert.Contains("AuthenticateAsync()", exception.Message);
    }

    [Fact]
    public void P02_ValidateConnectionString_RejectsUsernameWithoutPassword()
    {
        // Arrange - Even username without password should be rejected (@ in authority)
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://admin@localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("cannot contain embedded credentials", exception.Message);
    }

    [Fact]
    public void P02_ValidateConnectionString_RejectsPasswordWithColonInIt()
    {
        // Arrange - Password containing special characters
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://user:p@ss:word@localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("cannot contain embedded credentials", exception.Message);
    }

    [Theory]
    [InlineData("surreal://localhost:8000/path/with/@/symbol")]
    [InlineData("http://localhost:8000/api/@username/profile")]
    public void P02_ValidateConnectionString_AllowsAtSymbolInPath(string connectionString)
    {
        // Arrange - @ in the path (after authority) should be allowed
        var options = new SurrealDbClientOptions
        {
            ConnectionString = connectionString,
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Theory]
    [InlineData("file:///@/path/to/database")]
    [InlineData("file:///c:/path/with/@/database")]
    public void P02_ValidateConnectionString_AllowsFileUrlsWithAtSymbol(string connectionString)
    {
        // Arrange - file:// URLs are allowed to have @ anywhere (local file system)
        var options = new SurrealDbClientOptions
        {
            ConnectionString = connectionString,
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Fact]
    public void P02_ValidateConnectionString_RejectsComplexEmbeddedCredentials()
    {
        // Arrange - Complex realistic credentials with special chars
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "https://api_user:Xk9$mP2#nQ7@db.production.example.com:8000/rpc",
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("cannot contain embedded credentials", exception.Message);

        // Verify error message doesn't expose the actual credentials
        Assert.DoesNotContain("api_user", exception.Message);
        Assert.DoesNotContain("Xk9$mP2#nQ7", exception.Message);
    }

    [Fact]
    public void P02_ValidateConnectionString_HandlesEmptyConnectionString()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "",
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert - Should throw for empty connection string
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("ConnectionString cannot be empty", exception.Message);
    }

    [Fact]
    public void P02_ValidateConnectionString_HandlesWhitespaceConnectionString()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "   ",
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("ConnectionString cannot be empty", exception.Message);
    }

    [Theory]
    [InlineData("localhost:8000")]  // No scheme
    [InlineData("192.168.1.100:8000")]  // No scheme
    public void P02_ValidateConnectionString_AllowsConnectionStringWithoutScheme(string connectionString)
    {
        // Arrange - Connection strings without scheme can't have embedded credentials in our format
        var options = new SurrealDbClientOptions
        {
            ConnectionString = connectionString,
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert - Should not throw (can't have credentials without scheme://)
        options.Validate();
    }

    [Fact]
    public void P02_ValidateConnectionString_ProvidesHelpfulErrorMessage()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://admin:secret@localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());

        // Verify error message is helpful
        Assert.Contains("Connection string cannot contain embedded credentials", exception.Message);
        Assert.Contains("user:password@", exception.Message);
        Assert.Contains("AuthenticateAsync()", exception.Message);
        Assert.Contains("security", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_ConnectionStringWithCredentials_RejectedAtOptionsCreation()
    {
        // Arrange - Simulate real-world scenario where developer accidentally includes credentials

        // Act & Assert - Should fail during options validation
        var exception = Assert.Throws<ValidationException>(() =>
        {
            var options = new SurrealDbClientOptions
            {
                ConnectionString = "surreal://admin:password@localhost:8000",
                Namespace = "production",
                Database = "maindb"
            };
            options.Validate();
        });

        Assert.Contains("cannot contain embedded credentials", exception.Message);
    }

    [Fact]
    public void Integration_SecureConnectionStringPattern_AcceptedWithAuthenticateAsync()
    {
        // Arrange - Demonstrate the correct, secure pattern

        // Act - Connection string without credentials
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };

        // Assert - Validation succeeds
        options.Validate();

        // Demonstrate that authentication should be done via AuthenticateAsync
        // (In real usage: await client.AuthenticateAsync("username", "password"))
    }

    #endregion
}
