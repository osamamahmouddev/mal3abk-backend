using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class HangfireOptionsTests
{
    private static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, validationContext, validationResults, true);
        return validationResults;
    }

    [Fact]
    public void HangfireOptions_WithValidValues_ShouldPassValidation()
    {
        // Arrange
        var options = new HangfireOptions
        {
            SchemaName = "hangfire",
            WorkerCount = 15,
            DashboardPath = "/admin/hangfire",
            DefaultRetryAttempts = 3,
            PollIntervalSeconds = 5
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void HangfireOptions_WithMissingSchema_ShouldFailValidation(string invalidSchema)
    {
        // Arrange
        var options = new HangfireOptions
        {
            SchemaName = invalidSchema
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("Hangfire schema name is required"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void HangfireOptions_WithInvalidWorkerCount_ShouldFailValidation(int invalidWorkers)
    {
        // Arrange
        var options = new HangfireOptions
        {
            WorkerCount = invalidWorkers
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("WorkerCount must be between 1 and 100"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void HangfireOptions_WithInvalidRetryAttempts_ShouldFailValidation(int invalidRetries)
    {
        // Arrange
        var options = new HangfireOptions
        {
            DefaultRetryAttempts = invalidRetries
        };

        // Act
        var results = ValidateModel(options);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("DefaultRetryAttempts must be between 0 and 20"));
    }
}
