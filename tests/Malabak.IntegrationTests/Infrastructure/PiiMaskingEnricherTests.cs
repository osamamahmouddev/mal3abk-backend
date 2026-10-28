using FluentAssertions;
using Malabak.Api.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class PiiMaskingEnricherTests
{
    private readonly PiiMaskingEnricher _enricher = new();

    [Theory]
    [InlineData("Password", "SuperSecret123!", "***MASKED***")]
    [InlineData("Token", "eyJhbGciOi...", "***MASKED***")]
    [InlineData("Otp", "123456", "***MASKED***")]
    [InlineData("ApiKey", "api_key_abc123", "***MASKED***")]
    [InlineData("Phone", "01012345678", "0101****5678")]
    [InlineData("Email", "ahmed@example.com", "a***@example.com")]
    public void Enrich_ShouldMaskSensitiveScalarProperties(string key, string originalValue, string expectedMasked)
    {
        // Arrange
        var messageTemplate = new MessageTemplateParser().Parse("Test message");
        var property = new LogEventProperty(key, new ScalarValue(originalValue));
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            messageTemplate,
            new[] { property });

        // Act
        _enricher.Enrich(logEvent, new TestPropertyFactory());

        // Assert
        var enrichedProperty = logEvent.Properties[key];
        enrichedProperty.Should().NotBeNull();
        var scalarValue = enrichedProperty as ScalarValue;
        scalarValue.Should().NotBeNull();
        scalarValue!.Value.Should().Be(expectedMasked);
    }

    [Fact]
    public void Enrich_ShouldNotMaskNonSensitiveProperties()
    {
        // Arrange
        var messageTemplate = new MessageTemplateParser().Parse("Test message");
        var property = new LogEventProperty("VenueName", new ScalarValue("Al-Ahly Stadium"));
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            messageTemplate,
            new[] { property });

        // Act
        _enricher.Enrich(logEvent, new TestPropertyFactory());

        // Assert
        var enrichedProperty = logEvent.Properties["VenueName"] as ScalarValue;
        enrichedProperty!.Value.Should().Be("Al-Ahly Stadium");
    }

    private sealed class TestPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }
}
