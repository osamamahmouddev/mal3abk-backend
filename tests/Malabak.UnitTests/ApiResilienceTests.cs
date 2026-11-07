using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Malabak.Api.Infrastructure.Platform;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// The API must keep answering correctly when its database is unreachable or not migrated yet: the phase 6 maintenance check
/// runs on every request and once turned every anonymous call into a 500 (seen in CI).
/// </summary>
public sealed class ApiResilienceTests : IClassFixture<AdminAuthorizationMatrixTests.ApiFactory>
{
    private readonly AdminAuthorizationMatrixTests.ApiFactory _factory;

    public ApiResilienceTests(AdminAuthorizationMatrixTests.ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/me/permissions")]
    [InlineData("/api/v1/venue-manager/venues")]
    [InlineData("/api/v1/venue-manager/venues/0192f3a1-0000-7000-8000-000000000001/blocked-slots")]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/support/tickets")]
    [InlineData("/api/v1/bookings/0192f3a1-0000-7000-8000-000000000001/dispute")]
    public async Task ProtectedEndpoints_AnswerAnonymousCallersWith401_EvenWithoutADatabaseAsync(string url)
    {
        var response = await _factory.CreateClient().GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnAdminToken_IsRefusedOnCustomerEndpointsAsync()
    {
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        var admin = User.CreateStaff(Guid.NewGuid(), "a@x.com", "+201001234568", "hash", "Admin", UserRole.Admin, DateTime.UtcNow);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateAdminTokens(admin, DateTime.UtcNow.AddHours(1)).AccessToken);

        (await client.GetAsync(new Uri("/api/v1/me/permissions", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACustomerToken_IsRefusedOnAdminEndpointsAsync()
    {
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201001234567", "Player", DateTime.UtcNow);
        player.AddRole(UserRole.Admin); // a forged role claim does not help: the audience is wrong
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateTokens(player).AccessToken);

        foreach (var url in new[] { "/api/v1/admin/users", "/api/v1/admin/dashboard", "/api/v1/admin/moderation/reports", "/api/v1/admin/audit-logs" })
        {
            (await client.GetAsync(new Uri(url, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        }
    }

    [Fact]
    public async Task PlatformStatus_StaysAvailable_WithoutADatabaseAsync()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/api/v1/platform/status", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("false", "maintenance falls back to its default (off)");
    }

    [Fact]
    public async Task SettingsAndFlags_FallBackToDefaults_WhenTheStoreFailsAsync()
    {
        await using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options);
        var service = new PlatformService(db, new NoAudit(), new Clock());

        (await service.GetBoolAsync(PlatformSettingKeys.MaintenanceEnabled)).Should().BeFalse();
        (await service.GetIntAsync(PlatformSettingKeys.HoldMinutes)).Should().Be(30);
        (await service.IsEnabledAsync(FeatureFlagKeys.FeaturedVenues)).Should().BeFalse();
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
