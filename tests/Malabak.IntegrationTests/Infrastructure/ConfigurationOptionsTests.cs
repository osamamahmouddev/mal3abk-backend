using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Modules.Identity.Application.Common;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class ConfigurationOptionsTests
{
    [Fact]
    public void DatabaseOptions_WithValidValues_ShouldPassValidation()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            ConnectionString = "Host=localhost;Database=malabak_test",
            MaxPoolSize = 50,
            CommandTimeoutSeconds = 15,
            EnableSensitiveDataLogging = false
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DatabaseOptions_WithMissingConnectionString_ShouldFailValidation(string invalidConnection)
    {
        // Arrange
        var options = new DatabaseOptions
        {
            ConnectionString = invalidConnection
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("Database connection string is required"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(600)]
    public void DatabaseOptions_WithInvalidMaxPoolSize_ShouldFailValidation(int invalidPoolSize)
    {
        // Arrange
        var options = new DatabaseOptions
        {
            ConnectionString = "ValidConnectionString",
            MaxPoolSize = invalidPoolSize
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("MaxPoolSize must be between 5 and 500"));
    }

    [Fact]
    public void RedisOptions_WithValidValues_ShouldPassValidation()
    {
        // Arrange
        var options = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            ConnectTimeoutMs = 3000,
            SyncTimeoutMs = 2000
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void RedisOptions_WithMissingConnectionString_ShouldFailValidation()
    {
        // Arrange
        var options = new RedisOptions
        {
            ConnectionString = string.Empty
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("Redis connection string is required"));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(40000)]
    public void RedisOptions_WithInvalidTimeout_ShouldFailValidation(int invalidTimeout)
    {
        // Arrange
        var options = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            ConnectTimeoutMs = invalidTimeout
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("ConnectTimeoutMs must be between 100 and 30000 ms"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void RedisOptions_WithInvalidDefaultDatabase_ShouldFailValidation(int invalidDatabase)
    {
        // Arrange
        var options = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultDatabase = invalidDatabase
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("DefaultDatabase must be between 0 and 15"));
    }

    [Fact]
    public void TurnstileOptions_WithValidValues_ShouldPassValidation()
    {
        // Arrange
        var options = new TurnstileOptions
        {
            SecretKey = "dummy-secret-key",
            SiteKey = "dummy-site-key",
            VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify"
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void TurnstileOptions_WithInvalidUrl_ShouldFailValidation()
    {
        // Arrange
        var options = new TurnstileOptions
        {
            VerifyUrl = "invalid-url-not-http"
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("VerifyUrl must be a valid URL"));
    }

    [Fact]
    public void RateLimiterOptions_WithValidValues_ShouldPassValidation()
    {
        // Arrange
        var options = new RateLimiterOptions
        {
            PermitLimit = 150,
            WindowSeconds = 60,
            QueueLimit = 15
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(20000)]
    public void RateLimiterOptions_WithInvalidPermitLimit_ShouldFailValidation(int invalidLimit)
    {
        // Arrange
        var options = new RateLimiterOptions
        {
            PermitLimit = invalidLimit
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("PermitLimit must be between 1 and 10000"));
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, context, validationResults, validateAllProperties: true);
        return validationResults;
    }
}
