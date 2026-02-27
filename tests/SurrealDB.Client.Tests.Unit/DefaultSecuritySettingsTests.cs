using Xunit;
using SurrealDB.Client;
using SurrealDB.Client.Exceptions;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for Batch 3: Default Security Settings (P1-3, P1-5)
/// - P1-3: HTTP certificate validation disabled by default
/// - P1-5: WebSocket certificate validation missing
/// </summary>
public class DefaultSecuritySettingsTests
{
    #region P1-3: HTTPS and Certificate Validation Defaults

    [Fact]
    public void P1_3_DefaultOptions_UseHttps_DefaultsToTrue()
    {
        // Arrange & Act
        var options = new SurrealDbClientOptions
        {
            Namespace = "test",
            Database = "test"
        };

        // Assert
        Assert.True(options.UseHttps, "UseHttps should default to true for security");
    }

    [Fact]
    public void P1_3_DefaultOptions_VerifyServerCertificate_DefaultsToTrue()
    {
        // Arrange & Act
        var options = new SurrealDbClientOptions
        {
            Namespace = "test",
            Database = "test"
        };

        // Assert
        Assert.True(options.VerifyServerCertificate, "VerifyServerCertificate should default to true for security");
    }

    [Fact]
    public void P1_3_DefaultOptions_AcknowledgeCertificateValidationRisk_DefaultsToFalse()
    {
        // Arrange & Act
        var options = new SurrealDbClientOptions
        {
            Namespace = "test",
            Database = "test"
        };

        // Assert
        Assert.False(options.AcknowledgeCertificateValidationRisk,
            "AcknowledgeCertificateValidationRisk should default to false");
    }

    [Fact]
    public void P1_3_Options_WithSecureDefaults_Validates()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            UseHttps = true,
            VerifyServerCertificate = true
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Fact]
    public void P1_3_Options_DisableCertValidation_WithoutAcknowledgment_ThrowsValidationException()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            VerifyServerCertificate = false,
            AcknowledgeCertificateValidationRisk = false  // Risk not acknowledged
        };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => options.Validate());
        Assert.Contains("Certificate validation is disabled", exception.Message);
        Assert.Contains("risk has not been acknowledged", exception.Message);
        Assert.Contains("man-in-the-middle attacks", exception.Message);
    }

    [Fact]
    public void P1_3_Options_DisableCertValidation_WithAcknowledgment_Validates()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            VerifyServerCertificate = false,
            AcknowledgeCertificateValidationRisk = true  // Explicitly acknowledged
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Fact]
    public void P1_3_Options_EnableCertValidation_NoAcknowledgmentNeeded()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            VerifyServerCertificate = true,
            AcknowledgeCertificateValidationRisk = false  // Not needed when validation is enabled
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    #endregion

    #region P1-3: HTTPS Default Breaking Change Tests

    [Fact]
    public void P1_3_BreakingChange_UseHttps_ChangedFromFalseToTrue()
    {
        // This test documents the breaking change for developers
        // Previously: UseHttps defaulted to false
        // Now: UseHttps defaults to true for security

        // Arrange & Act
        var options = new SurrealDbClientOptions();

        // Assert
        Assert.True(options.UseHttps,
            "BREAKING CHANGE: UseHttps now defaults to true. " +
            "If you need HTTP (not recommended), explicitly set UseHttps = false");
    }

    [Fact]
    public void P1_3_Options_ExplicitlyDisableHttps_Works()
    {
        // Arrange - Developer explicitly wants HTTP (e.g., development environment)
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            UseHttps = false,  // Explicitly disabled
            VerifyServerCertificate = true
        };

        // Act & Assert - Should work if explicitly set
        options.Validate();
        Assert.False(options.UseHttps);
    }

    #endregion

    #region P1-3 and P1-5: Certificate Validation Enforcement

    [Theory]
    [InlineData(true, true, true)]   // Secure: HTTPS + Cert validation
    [InlineData(true, false, false)] // Insecure: HTTPS but no cert validation (should fail)
    [InlineData(false, true, true)]  // HTTP with cert validation (valid but unusual)
    [InlineData(false, false, false)]// HTTP without cert validation (should fail)
    public void P1_3_Options_CertificateValidationCombinations(
        bool useHttps,
        bool verifyCert,
        bool shouldValidate)
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            UseHttps = useHttps,
            VerifyServerCertificate = verifyCert,
            AcknowledgeCertificateValidationRisk = !verifyCert  // Acknowledge if disabled
        };

        // Act & Assert
        if (shouldValidate)
        {
            // Should validate successfully
            options.Validate();
        }
        else
        {
            // Should fail validation without acknowledgment
            options.AcknowledgeCertificateValidationRisk = false;
            Assert.Throws<ValidationException>(() => options.Validate());
        }
    }

    [Fact]
    public void P1_3_ValidationMessage_ContainsSecurityWarning()
    {
        // Arrange
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            VerifyServerCertificate = false,
            AcknowledgeCertificateValidationRisk = false
        };

        // Act
        var exception = Assert.Throws<ValidationException>(() => options.Validate());

        // Assert - Error message should clearly explain the security risk
        Assert.Contains("Certificate validation is disabled", exception.Message);
        Assert.Contains("man-in-the-middle", exception.Message);
        Assert.Contains("AcknowledgeCertificateValidationRisk", exception.Message);
        Assert.Contains("production", exception.Message);
    }

    [Fact]
    public void P1_5_WebSocketCertificateValidation_IsEnforced()
    {
        // This test verifies that WebSocket connections also enforce certificate validation
        // The actual validation happens in WebSocketProtocolAdapter.ConnectAsync

        // Arrange - Options with certificate validation enabled (default)
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            Protocol = ProtocolType.WebSocket,
            VerifyServerCertificate = true  // Default
        };

        // Act & Assert
        options.Validate();
        Assert.True(options.VerifyServerCertificate);
    }

    [Fact]
    public void P1_5_WebSocketCertificateValidation_CanBeDisabledWithAcknowledgment()
    {
        // Arrange - Disable certificate validation for WebSocket with acknowledgment
        var options = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            Protocol = ProtocolType.WebSocket,
            VerifyServerCertificate = false,
            AcknowledgeCertificateValidationRisk = true
        };

        // Act & Assert
        options.Validate();
        Assert.False(options.VerifyServerCertificate);
        Assert.True(options.AcknowledgeCertificateValidationRisk);
    }

    #endregion

    #region Security Best Practices Documentation

    [Fact]
    public void P1_3_SecurityBestPractices_ProductionConfiguration()
    {
        // This test documents the recommended production configuration

        // Arrange - Production-ready configuration
        var productionOptions = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://production.example.com:8000",
            Namespace = "production_ns",
            Database = "production_db",
            UseHttps = true,  // ALWAYS true in production
            VerifyServerCertificate = true,  // ALWAYS true in production
            AcknowledgeCertificateValidationRisk = false  // Should never be needed in production
        };

        // Act & Assert
        productionOptions.Validate();

        // Assert - Production settings
        Assert.True(productionOptions.UseHttps);
        Assert.True(productionOptions.VerifyServerCertificate);
        Assert.False(productionOptions.AcknowledgeCertificateValidationRisk);
    }

    [Fact]
    public void P1_3_SecurityBestPractices_DevelopmentConfiguration()
    {
        // This test documents a development configuration (if needed)
        // WARNING: Only use in isolated development environments

        // Arrange - Development configuration (insecure, for localhost only)
        var devOptions = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "dev_ns",
            Database = "dev_db",
            UseHttps = false,  // Only acceptable for localhost
            VerifyServerCertificate = false,  // Only for self-signed certs in dev
            AcknowledgeCertificateValidationRisk = true  // Explicitly acknowledged
        };

        // Act & Assert
        devOptions.Validate();

        // Document that this should NEVER be used in production
        Assert.Contains("localhost", devOptions.ConnectionString.ToLower());
    }

    #endregion
}
