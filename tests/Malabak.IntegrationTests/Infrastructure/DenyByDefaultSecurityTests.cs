using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class DenyByDefaultSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DenyByDefaultSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Explicitly reviewed list of routes permitted to allow anonymous access (SECURITY.md).
    /// Any route not on this list that allows anonymous access represents a security regression.
    /// </summary>
    private static readonly HashSet<string> _reviewedAnonymousAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Platform & Health
        "/",
        "/.well-known/security.txt",
        "/health/live",
        "/health/ready",
        "/api/v{version:apiVersion}/ping",
        "/api/v{version:apiVersion}/platform/status",

        // Authentication & Session Bootstrap (SECURITY.md §4)
        "/api/v{version:apiVersion}/auth/otp/send",
        "/api/v{version:apiVersion}/auth/otp/verify",
        "/api/v{version:apiVersion}/auth/firebase",
        "/api/v{version:apiVersion}/auth/refresh",
        "/api/v{version:apiVersion}/auth/login",
        "/api/v{version:apiVersion}/auth/logout",
        "/api/v{version:apiVersion}/auth/register/venue-owner",
        "/api/v{version:apiVersion}/auth/password/forgot",
        "/api/v{version:apiVersion}/auth/password/reset",
        "/api/v{version:apiVersion}/auth/email/resend",
        "/api/v{version:apiVersion}/auth/email/verify",
        "/api/v{version:apiVersion}/auth/impersonation/exchange",
        "/api/v{version:apiVersion}/me/phone-change/recover",
        "/api/v{version:apiVersion}/venue-invitations/preview",
        "/api/v{version:apiVersion}/venue-invitations/register",

        // Admin Auth Bootstrap
        "/api/v{version:apiVersion}/admin/auth/login",
        "/api/v{version:apiVersion}/admin/auth/refresh",
        "/api/v{version:apiVersion}/admin/auth/totp/enroll",
        "/api/v{version:apiVersion}/admin/auth/totp/confirm",

        // Public Discovery & Reference Data (Unauthenticated Browsing)
        "/api/v{version:apiVersion}/home",
        // Team invite landing page: static HTML for a validated code, no data, strict CSP (reviewed).
        "/join/{code}",
        "/api/v{version:apiVersion}/waitlist",
        "/api/v{version:apiVersion}/teams/leaderboard",
        "/api/v{version:apiVersion}/media/{id:guid}/{variant}",
        "/api/v{version:apiVersion}/reference/facilities",
        "/api/v{version:apiVersion}/reference/governorates",
        "/api/v{version:apiVersion}/reference/holidays",
        "/api/v{version:apiVersion}/reference/pitch-types",
        "/api/v{version:apiVersion}/reference/surfaces",
        "/api/v{version:apiVersion}/venues/{venueId:guid}",
        "/api/v{version:apiVersion}/venues/{venueId:guid}/availability",
        "/api/v{version:apiVersion}/venues/{venueId:guid}/pitches",
        "/api/v{version:apiVersion}/venues/areas",
        "/api/v{version:apiVersion}/venues/explore",
        "/api/v{version:apiVersion}/venues/featured",
        "/api/v{version:apiVersion}/venues/map",

        // Webhooks (Authenticated via signature verification)
        "/api/v{version:apiVersion}/webhooks/email/bounce",
        "/api/v{version:apiVersion}/webhooks/sms/twilio/{account}",
        "/api/v{version:apiVersion}/payments/paymob/webhook",

        // Test Endpoints (Development harness)
        "/api/v{version:apiVersion}/test/client-ip",
        "/api/v{version:apiVersion}/test/idempotent-action",
        "/api/v{version:apiVersion}/test/rate-limited",
        "/api/v{version:apiVersion}/test/result-error/{code}",
        "/api/v{version:apiVersion}/test/validation"
    };

    [Fact]
    public void EveryEndpoint_MustHaveExplicitAuthorizationOrBeOnReviewedAllowlist()
    {
        // Arrange
        _ = _factory.CreateClient(); // Ensure endpoints are initialized
        var endpointDataSources = _factory.Services.GetServices<EndpointDataSource>();
        var endpoints = endpointDataSources.SelectMany(ds => ds.Endpoints).OfType<RouteEndpoint>().ToList();

        var unauthorizedOrUnreviewed = new List<string>();

        // Act
        foreach (var endpoint in endpoints)
        {
            var route = "/" + (endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty);
            var isAllowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;
            var hasAuthorizeData = endpoint.Metadata.OfType<IAuthorizeData>().Any();

            if (isAllowAnonymous)
            {
                if (!_reviewedAnonymousAllowlist.Contains(route))
                {
                    unauthorizedOrUnreviewed.Add($"[Unreviewed AllowAnonymous] {route} ({endpoint.DisplayName})");
                }
            }
            else if (!hasAuthorizeData && route != "/api/v{version:apiVersion}/test/auth-fallback-protected")
            {
                unauthorizedOrUnreviewed.Add($"[Missing Authorization & AllowAnonymous] {route} ({endpoint.DisplayName})");
            }
        }

        // Assert
        unauthorizedOrUnreviewed.Should().BeEmpty(
            "every API endpoint must either enforce an explicit authorization policy or be on the reviewed anonymous allowlist (SECURITY.md §2)");
    }

    [Fact]
    public void EveryReviewedAllowlistRoute_MustExistInRegisteredEndpoints()
    {
        // Arrange
        _ = _factory.CreateClient();
        var endpointDataSources = _factory.Services.GetServices<EndpointDataSource>();
        var registeredRoutes = endpointDataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText?.TrimStart('/') ?? string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deadAllowlistEntries = _reviewedAnonymousAllowlist
            .Where(route => !registeredRoutes.Contains(route))
            .ToList();

        // Assert
        deadAllowlistEntries.Should().BeEmpty(
            "the reviewed anonymous allowlist must not contain obsolete routes that are no longer registered in the application");
    }

    [Fact]
    public async Task FallbackPolicy_RejectsAnonymousRequest_WhenEndpointLacksExplicitPolicyAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act: Calling the fallback-protected test endpoint without authentication
        var response = await client.GetAsync("/api/v1/test/auth-fallback-protected");

        // Assert: Fallback policy must deny-by-default with 401 Unauthorized
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
