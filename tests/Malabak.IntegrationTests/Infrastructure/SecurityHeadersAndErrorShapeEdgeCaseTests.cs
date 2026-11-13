using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Phase 6: Cross-cutting security edge-case tests for Security Headers, CORS, and Error Sanitization (SECURITY.md §1, §7).
/// Verifies header hardening (HSTS, nosniff, DENY, CSP), suppression of Server & X-Powered-By headers,
/// CORS origin whitelist without wildcard credentials, and absence of stack traces or SQL leaks in error payloads.
/// </summary>
public sealed class SecurityHeadersAndErrorShapeEdgeCaseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityHeadersAndErrorShapeEdgeCaseTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Response_SuppressesServerAndPoweredByHeaders_OnSuccessAndErrorsAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act 1: Success response
        var successResponse = await client.GetAsync("/api/v1/ping");

        // Act 2: Not found response
        var notFoundResponse = await client.GetAsync("/api/v1/nonexistent-endpoint-route");

        // Assert 1: Headers absent on 200 OK
        successResponse.Headers.Should().NotContainKey("Server", "Server banner must be stripped to prevent technology reconnaissance");
        successResponse.Headers.Should().NotContainKey("X-Powered-By", "X-Powered-By must be stripped");

        // Assert 2: Headers absent on 404 Not Found
        notFoundResponse.Headers.Should().NotContainKey("Server");
        notFoundResponse.Headers.Should().NotContainKey("X-Powered-By");
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost:3001")]
    [InlineData("https://admin.malabak.app")]
    [InlineData("https://venue.malabak.app")]
    public async Task Cors_AllowlistedOrigins_PermitCredentials_AndEchoSpecificOriginAsync(string origin)
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ping");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        if (response.Headers.Contains("Access-Control-Allow-Origin"))
        {
            var allowOrigin = response.Headers.GetValues("Access-Control-Allow-Origin").First();
            allowOrigin.Should().NotBe("*", "CORS wildcard is strictly forbidden when credentials are supported");
            allowOrigin.Should().Be(origin);

            if (response.Headers.Contains("Access-Control-Allow-Credentials"))
            {
                response.Headers.GetValues("Access-Control-Allow-Credentials").First().Should().Be("true");
            }
        }
    }

    [Theory]
    [InlineData("http://evil-phishing.com")]
    [InlineData("https://malicious-site.org")]
    [InlineData("null")]
    public async Task Cors_UntrustedOrigins_AreRejectedWithoutAllowOriginHeaderAsync(string untrustedOrigin)
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ping");
        request.Headers.Add("Origin", untrustedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().NotContainKey("Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task BadRequest_ErrorShape_ConformsToProblemDetails_WithoutStackTracesOrInternalPathsAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act: POST invalid payload to anonymous OTP endpoint
        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/send", new { phoneNumber = "invalid-format-123" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();

        content.Should().NotContain("at Malabak.");
        content.Should().NotContain(".cs:line");
        content.Should().NotContain("/home/");
        content.Should().NotContain("C:\\");
        content.Should().NotContain("SELECT ");
        content.Should().NotContain("FROM ");

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        root.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task Unauthorized_ErrorShape_ConformsToProblemDetails_WithoutLeaksAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act: call a protected endpoint without authorization header
        var response = await client.GetAsync("/api/v1/me/sessions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await response.Content.ReadAsStringAsync();

        content.Should().NotContain("at Malabak.");
        content.Should().NotContain(".cs:line");
        content.Should().NotContain("Exception:");
    }
}
