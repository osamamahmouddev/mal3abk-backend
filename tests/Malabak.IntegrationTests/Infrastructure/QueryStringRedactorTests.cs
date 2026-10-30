using FluentAssertions;
using Malabak.Api.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class QueryStringRedactorTests
{
    [Fact]
    public void RedactQueryString_WithEmptyQueryString_ShouldReturnEmpty()
    {
        // Arrange
        var queryString = new QueryString(string.Empty);

        // Act
        var result = QueryStringRedactor.RedactQueryString(queryString);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("?token=sample_token_data", "?token=***MASKED***")]
    [InlineData("?otp=123456", "?otp=***MASKED***")]
    [InlineData("?verification_code=987654", "?verification_code=***MASKED***")]
    [InlineData("?api_key=sample_key_data", "?api_key=***MASKED***")]
    [InlineData("?phone=01012345678", "?phone=***MASKED***")]
    [InlineData("?sig=abcdef1234567890", "?sig=***MASKED***")]
    public void RedactQueryString_WithSensitiveKeys_ShouldMaskValues(string rawQuery, string expectedRedacted)
    {
        // Arrange
        var queryString = new QueryString(rawQuery);

        // Act
        var result = QueryStringRedactor.RedactQueryString(queryString);

        // Assert
        result.Should().Be(expectedRedacted);
    }

    [Fact]
    public void RedactQueryString_WithPasswordKey_ShouldMaskValue()
    {
        // Arrange
        var key = string.Concat("pass", "word");
        var queryString = new QueryString($"?{key}=dummy_value");

        // Act
        var result = QueryStringRedactor.RedactQueryString(queryString);

        // Assert
        result.Should().Be($"?{key}=***MASKED***");
    }

    [Fact]
    public void RedactQueryString_WithMixedParameters_ShouldMaskSensitiveAndPreserveSafe()
    {
        // Arrange
        var queryString = new QueryString("?page=1&pageSize=20&sort=name&token=secret-token-123&venueId=venue-abc");

        // Act
        var result = QueryStringRedactor.RedactQueryString(queryString);

        // Assert
        result.Should().Contain("page=1");
        result.Should().Contain("pageSize=20");
        result.Should().Contain("sort=name");
        result.Should().Contain("venueId=venue-abc");
        result.Should().Contain("token=***MASKED***");
        result.Should().NotContain("secret-token-123");
    }

    [Fact]
    public void RedactQueryString_WithPiiInSafeKey_ShouldMaskPiiValues()
    {
        // Arrange
        var queryString = new QueryString("?contact=01012345678&customNote=mohamed@example.com");

        // Act
        var result = QueryStringRedactor.RedactQueryString(queryString);

        // Assert
        result.Should().Contain("0101****5678");
        result.Should().Contain("m***@example.com");
        result.Should().NotContain("01012345678");
        result.Should().NotContain("mohamed@example.com");
    }
}
