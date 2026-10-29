using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class ValidationAndResultMappingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ValidationAndResultMappingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FallbackPolicy_WhenUnauthenticated_DeniesByDefaultWith401UnauthorizedAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - hit endpoint without [AllowAnonymous]
        var response = await client.GetAsync("/api/v1/test/auth-fallback-protected");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllowAnonymous_ExplicitlyPermitsPublicEndpointsAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - hit endpoint with [AllowAnonymous]
        var response = await client.GetAsync("/api/v1/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidationFilter_WhenModelInvalid_Returns400ProblemDetailsWithErrorsAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var invalidPayload = new
        {
            Name = "",                          // Invalid (empty)
            PhoneNumber = "0101234",            // Invalid length
            Amount = -50.0m                     // Invalid (negative)
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/test/validation", invalidPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("status").GetInt32().Should().Be(400);
        json.GetProperty("code").GetString().Should().Be("validation_failed");
        json.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("Name", out _).Should().BeTrue();
        errors.TryGetProperty("PhoneNumber", out _).Should().BeTrue();
        errors.TryGetProperty("Amount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ValidationFilter_WhenModelValid_PassesAndReturns200OkAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var validPayload = new
        {
            Name = "Ahmed Hassan",
            PhoneNumber = "01012345678",
            Amount = 150.0m
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/test/validation", validPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "not_found")]
    [InlineData("conflict", HttpStatusCode.Conflict, "conflict")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "forbidden")]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData("validation", HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData("business_rule_failed", HttpStatusCode.UnprocessableEntity, "business_rule_failed")]
    public async Task ResultToHttpResult_MapsErrorTypesToCorrectHttpStatusCodesAsync(
        string code,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/v1/test/result-error/{code}");

        // Assert
        response.StatusCode.Should().Be(expectedStatus);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be(expectedErrorCode);
    }

    [Fact]
    public async Task Localization_WithArabicHeader_ReturnsArabicErrorMessageAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/result-error/not-found");
        request.Headers.Add("Accept-Language", "ar-EG");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("title").GetString().Should().Be("غير موجود");
        json.GetProperty("detail").GetString().Should().Be("المورد المطلوب غير موجود.");
    }

    [Fact]
    public async Task Localization_WithXLocaleHeader_OverridesToEnglishAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/result-error/not-found");
        request.Headers.Add("X-Locale", "en");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("title").GetString().Should().Be("Not Found");
        json.GetProperty("detail").GetString().Should().Be("The requested resource was not found.");
    }

    [Fact]
    public async Task Localization_DefaultCulture_IsArabicEgyptianAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/result-error/conflict");
        // No Accept-Language or X-Locale header provided

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("title").GetString().Should().Be("تعارض في البيانات");
        json.GetProperty("detail").GetString().Should().Be("تعارض في حالة المورد أو العملية المطلوبة.");
    }
}
