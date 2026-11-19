using System.Net;
using System.Text;
using FluentAssertions;
using Hangfire;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// TASK-719: the authorization matrix of every admin endpoint, read from the real endpoint graph (no database needed):
/// deny-by-default, staff-only permissions, least privilege per role, and step-up on every destructive action.
/// </summary>
public sealed class AdminAuthorizationMatrixTests : IClassFixture<AdminAuthorizationMatrixTests.ApiFactory>
{
    private static readonly string[] _staffRoles = [UserRole.Admin, UserRole.SuperAdmin, UserRole.SupportAgent, UserRole.Finance];

    /// <summary>The only admin-area endpoints reachable without a token: the staff sign-in steps.</summary>
    private static readonly string[] _anonymousAdminEndpoints =
    [
        "POST /api/v{version:apiVersion}/admin/auth/login",
        "POST /api/v{version:apiVersion}/admin/auth/totp/enroll",
        "POST /api/v{version:apiVersion}/admin/auth/totp/confirm",
        "POST /api/v{version:apiVersion}/admin/auth/totp/reset",
        "POST /api/v{version:apiVersion}/admin/auth/refresh",
    ];

    /// <summary>Destructive actions that need a fresh authenticator code (step-up) on top of the permission.</summary>
    private static readonly string[] _stepUpEndpoints =
    [
        "POST /api/v{version:apiVersion}/admin/users/{userId:guid}/suspend",
        "POST /api/v{version:apiVersion}/admin/accounts/",
        "PUT /api/v{version:apiVersion}/admin/accounts/{accountId:guid}/roles",
        "POST /api/v{version:apiVersion}/admin/accounts/{accountId:guid}/disable",
        "POST /api/v{version:apiVersion}/admin/venues/{venueId:guid}/suspend",
        "POST /api/v{version:apiVersion}/admin/venues/{venueId:guid}/ownership",
        "POST /api/v{version:apiVersion}/admin/bookings/{bookingId:guid}/force-cancel",
        "POST /api/v{version:apiVersion}/admin/disputes/{disputeId:guid}/resolve",
        "POST /api/v{version:apiVersion}/admin/moderation/matches/{matchId:guid}/correct-result",
        "POST /api/v{version:apiVersion}/admin/impersonate",
    ];

    private readonly IReadOnlyList<AdminEndpoint> _endpoints;
    private readonly ApiFactory _factory;

    public AdminAuthorizationMatrixTests(ApiFactory factory)
    {
        _factory = factory;
        _endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => IsAdminArea(e.RoutePattern.RawText ?? string.Empty))
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"]).Select(m => new AdminEndpoint(e, m)))
            .ToList();
    }

    [Fact]
    public void TheAdminAreaIsFullyMapped()
    {
        _endpoints.Should().HaveCountGreaterThan(80, "every admin feature of phase 6 should be in the matrix");
    }

    [Fact]
    public void EveryAdminEndpoint_RequiresAuthentication_ExceptTheSignInSteps()
    {
        var anonymous = _endpoints.Where(e => e.IsAnonymous).Select(e => e.Key).Order(StringComparer.Ordinal).ToList();

        anonymous.Should().BeEquivalentTo(_anonymousAdminEndpoints);
        _endpoints.Where(e => !e.IsAnonymous).Should().OnlyContain(e => e.Policies.Count > 0, "deny by default: every other admin endpoint names a policy");
    }

    [Fact]
    public void AdminEndpoints_OnlyUseStaffPermissions()
    {
        var allowed = Permissions.Platform.Where(MalabakAuthorization.IsPlatformStaffPermission)
            .Append(MalabakAuthorization.AdminSessionPolicy)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var endpoint in _endpoints.Where(e => !e.IsAnonymous))
        {
            endpoint.Policies.Should().Contain(p => allowed.Contains(p), $"{endpoint.Key} must require a platform-staff permission");
        }
    }

    [Fact]
    public void DestructiveActions_RequireStepUp()
    {
        var withStepUp = _endpoints.Where(e => e.StepUpScope is not null).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        withStepUp.Should().Contain(_stepUpEndpoints);
        _endpoints.Where(e => e.StepUpScope is not null).Should().OnlyContain(e => e.StepUpScope == "admin_destructive");
    }

    [Fact]
    public void Customers_CanReachNoAdminEndpoint_AndAdminsCanReachAll()
    {
        foreach (var endpoint in _endpoints.Where(e => !e.IsAnonymous))
        {
            var roles = AllowedRoles(endpoint);
            roles.Should().Contain(UserRole.Admin, endpoint.Key);
            roles.Should().NotContain([UserRole.Player, UserRole.VenueOwner, UserRole.VenueStaff], endpoint.Key);
        }
    }

    [Theory]
    [InlineData("GET /api/v{version:apiVersion}/admin/users/", "Admin,SuperAdmin,SupportAgent")]
    [InlineData("POST /api/v{version:apiVersion}/admin/users/{userId:guid}/suspend", "Admin,SuperAdmin")]
    [InlineData("GET /api/v{version:apiVersion}/admin/accounts/", "Admin,SuperAdmin")]
    [InlineData("GET /api/v{version:apiVersion}/admin/settings", "Admin,SuperAdmin")]
    [InlineData("GET /api/v{version:apiVersion}/admin/support/tickets/", "Admin,SuperAdmin,SupportAgent")]
    [InlineData("GET /api/v{version:apiVersion}/admin/bookings/", "Admin,SuperAdmin,SupportAgent")]
    [InlineData("POST /api/v{version:apiVersion}/admin/bookings/{bookingId:guid}/force-cancel", "Admin,SuperAdmin")]
    [InlineData("POST /api/v{version:apiVersion}/admin/disputes/{disputeId:guid}/resolve", "Admin,SuperAdmin")]
    [InlineData("GET /api/v{version:apiVersion}/admin/plans/", "Admin,SuperAdmin,Finance")]
    [InlineData("POST /api/v{version:apiVersion}/admin/subscriptions/owners/{ownerId:guid}/payments", "Admin,SuperAdmin,Finance")]
    [InlineData("GET /api/v{version:apiVersion}/admin/invoices/", "Admin,SuperAdmin,Finance")]
    [InlineData("GET /api/v{version:apiVersion}/admin/audit-logs", "Admin,SuperAdmin")]
    [InlineData("POST /api/v{version:apiVersion}/admin/impersonate", "Admin,SuperAdmin,SupportAgent")]
    [InlineData("GET /api/v{version:apiVersion}/admin/navigation", "Admin,SuperAdmin,SupportAgent,Finance")]
    public void EachStaffRole_GetsOnlyWhatItsJobNeeds(string key, string expected)
    {
        var endpoint = _endpoints.SingleOrDefault(e => e.Key == key);
        endpoint.Should().NotBeNull($"{key} should exist; mapped: {string.Join(", ", _endpoints.Select(e => e.Key).Where(k => k.Contains(key.Split('/')[4], StringComparison.Ordinal)).Take(8))}");

        AllowedRoles(endpoint!).Order(StringComparer.Ordinal).Should().Equal(expected.Split(',').Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EveryAdminRead_RefusesAnonymousCallersAsync()
    {
        var client = _factory.CreateClient();
        foreach (var endpoint in _endpoints.Where(e => !e.IsAnonymous && e.Method == "GET"))
        {
            var url = "/" + endpoint.Endpoint.RoutePattern.RawText!.TrimStart('/')
                .Replace("v{version:apiVersion}", "v1", StringComparison.Ordinal);
            url = System.Text.RegularExpressions.Regex.Replace(url, "{[^}]+}", "0192f3a1-0000-7000-8000-000000000001");

            var response = await client.GetAsync(new Uri(url, UriKind.Relative));

            response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], url);
        }
    }

    /// <summary>Writes the full matrix as Markdown when ADMIN_MATRIX_OUT is set (used to refresh docs/security).</summary>
    [Fact]
    public void MatrixCanBeExported()
    {
        var path = Environment.GetEnvironmentVariable("ADMIN_MATRIX_OUT");
        var markdown = new StringBuilder("| Endpoint | Admin | SupportAgent | Finance | Step-up |\n|---|---|---|---|---|\n");
        foreach (var endpoint in _endpoints.OrderBy(e => e.Endpoint.RoutePattern.RawText, StringComparer.Ordinal).ThenBy(e => e.Method, StringComparer.Ordinal))
        {
            var roles = endpoint.IsAnonymous ? _staffRoles.ToHashSet() : AllowedRoles(endpoint);
            var cells = _staffRoles.Select(r => roles.Contains(r) ? "✓" : "—");
            markdown.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{endpoint.Method} {endpoint.Endpoint.RoutePattern.RawText!.Replace("/api/v{version:apiVersion}", "/api/v1", StringComparison.Ordinal)}`{(endpoint.IsAnonymous ? " (sign-in)" : string.Empty)} | {string.Join(" | ", cells)} | {(endpoint.StepUpScope is null ? "" : "✓")} |\n");
        }

        markdown.Length.Should().BeGreaterThan(100);
        if (!string.IsNullOrWhiteSpace(path))
        {
            File.WriteAllText(path, markdown.ToString());
        }
    }

    private static bool IsAdminArea(string pattern) => pattern.Contains("/admin/", StringComparison.Ordinal);

    /// <summary>Roles that pass every policy on the endpoint (permissions come from <see cref="RolePermissions"/>).</summary>
    private static HashSet<string> AllowedRoles(AdminEndpoint endpoint) =>
        UserRole.All.Where(role =>
        {
            var granted = RolePermissions.For([role]);
            return endpoint.Policies.All(policy =>
                policy == MalabakAuthorization.AdminSessionPolicy ? _staffRoles.Contains(role) : granted.Contains(policy));
        }).ToHashSet(StringComparer.Ordinal);

    private sealed record AdminEndpoint(RouteEndpoint Endpoint, string Method)
    {
        public string Key => $"{Method} {Endpoint.RoutePattern.RawText}";

        public bool IsAnonymous => Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        public IReadOnlyList<string> Policies =>
            Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).Distinct().ToList();

        public string? StepUpScope => Endpoint.Metadata.GetMetadata<StepUpRequiredMetadata>()?.Scope;
    }

    /// <summary>Boots the real API without its database: Hangfire's job registration is removed, everything else is real.</summary>
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            const string postgres = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";
            const string redis = "127.0.0.1:1,abortConnect=false,connectTimeout=200";
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", postgres);
            builder.UseSetting("Database:ConnectionString", postgres);
            builder.UseSetting("ConnectionStrings:Hangfire", postgres);
            builder.UseSetting("ConnectionStrings:Redis", redis);
            builder.UseSetting("Redis:ConnectionString", redis);
            builder.UseSetting("Hangfire:EnableDashboard", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRecurringJobManager>();
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            });
        }
    }
}
