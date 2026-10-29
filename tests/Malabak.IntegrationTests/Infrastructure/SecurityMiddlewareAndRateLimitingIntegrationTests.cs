using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class SecurityMiddlewareAndRateLimitingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityMiddlewareAndRateLimitingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SecurityHeaders_AreAppliedToAllResponsesAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Strict-Transport-Security")
            .WhoseValue.First().Should().Contain("max-age=31536000");

        response.Headers.Should().ContainKey("Content-Security-Policy")
            .WhoseValue.First().Should().Contain("default-src 'self'");

        response.Headers.Should().ContainKey("X-Content-Type-Options")
            .WhoseValue.First().Should().Be("nosniff");

        response.Headers.Should().ContainKey("X-Frame-Options")
            .WhoseValue.First().Should().Be("DENY");

        response.Headers.Should().ContainKey("Referrer-Policy")
            .WhoseValue.First().Should().Be("strict-origin-when-cross-origin");

        response.Headers.Should().ContainKey("Permissions-Policy")
            .WhoseValue.First().Should().Contain("geolocation=(self)");

        response.Headers.Should().ContainKey("Cross-Origin-Opener-Policy")
            .WhoseValue.First().Should().Be("same-origin");

        // API endpoints must have no-store cache control
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Cors_AllowedOrigin_ReturnsAccessControlHeadersAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ping");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin")
            .WhoseValue.First().Should().Be("http://localhost:3000");
        response.Headers.Should().ContainKey("Access-Control-Allow-Credentials")
            .WhoseValue.First().Should().Be("true");
    }

    [Fact]
    public async Task Cors_UntrustedOrigin_DoesNotReturnAccessControlAllowOriginAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ping");
        request.Headers.Add("Origin", "http://evil-attacker.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().NotContainKey("Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task CloudflareRealIp_FromTrustedLoopbackInDevelopment_ExtractsRealIpAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/client-ip");
        request.Headers.Add("CF-Connecting-IP", "197.35.120.45");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("clientIp").GetString().Should().Be("197.35.120.45");
    }

    [Fact]
    public async Task DistributedRateLimiter_ExceedingLimit_Returns429TooManyRequestsWithHeadersAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", $"197.100.50.{Random.Shared.Next(1, 250)}");

        // Act - limit is 3 requests per 10 seconds
        HttpResponseMessage? fourthResponse = null;
        for (var i = 1; i <= 4; i++)
        {
            var response = await client.GetAsync("/api/v1/test/rate-limited");
            if (i < 4)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                response.Headers.Should().ContainKey("X-RateLimit-Limit");
                response.Headers.Should().ContainKey("X-RateLimit-Remaining");
            }
            else
            {
                fourthResponse = response;
            }
        }

        // Assert - 4th request must be rejected with 429
        fourthResponse.Should().NotBeNull();
        fourthResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        fourthResponse.Headers.Should().ContainKey("Retry-After");
        fourthResponse.Headers.Should().ContainKey("X-RateLimit-Limit");
        fourthResponse.Headers.Should().ContainKey("X-RateLimit-Remaining")
            .WhoseValue.First().Should().Be("0");

        var json = await fourthResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetInt32().Should().Be(429);
        json.GetProperty("code").GetString().Should().Be("rate_limited");
    }
}
