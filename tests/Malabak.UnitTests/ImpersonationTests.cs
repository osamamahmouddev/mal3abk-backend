using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.Admin;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class ImpersonationTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly MemoryCodes _codes = new();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task ACode_IsSingleUse_AndGivesAShortTokenWithTheActorClaimAsync()
    {
        await using var db = NewDb();
        var player = Player(db);
        await db.SaveChangesAsync();
        var service = Service(db);

        (await service.IssueCodeAsync(_admin, player.Id, " ")).Error.Should().Be(ImpersonationErrors.ReasonRequired);
        var code = (await service.IssueCodeAsync(_admin, player.Id, "Ticket SUP-7KQ2MX: cannot see booking")).Value;
        code.ExpiresInSeconds.Should().Be(60);
        _codes.Stored.Keys.Should().NotContain(code.Code, "only a hash of the code is stored");

        var token = (await service.ExchangeAsync(code.Code)).Value;
        (await service.ExchangeAsync(code.Code)).Error.Should().Be(ImpersonationErrors.InvalidCode);

        token.ExpiresIn.Should().Be(15 * 60);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        jwt.Subject.Should().Be(player.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "act" && c.Value.Contains(_admin.ToString("D")));
        jwt.Claims.Should().Contain(c => c.Type == "sid");
        (await db.UserSessions.SingleAsync()).DeviceName.Should().Be(ImpersonationService.SessionDeviceName, "the customer can see and revoke it");
        _recorder.Notifications.Should().ContainSingle(n => n.UserId == player.Id && n.Type == NotificationTypes.ImpersonationStarted);
        _recorder.Audits.Select(a => a.Action).Should().Equal("admin.impersonation.code_issued", "admin.impersonation.started");
    }

    [Fact]
    public async Task StaffSuspendedAndOwnAccounts_CannotBeImpersonatedAsync()
    {
        await using var db = NewDb();
        var staff = User.CreateStaff(Guid.NewGuid(), "help@example.com", Guid.NewGuid().ToString("N"), "hash", "Help", UserRole.SupportAgent, _now);
        var suspended = Player(db);
        suspended.Suspend("fraud", _now);
        db.Users.Add(staff);
        await db.SaveChangesAsync();
        var service = Service(db);

        (await service.IssueCodeAsync(_admin, staff.Id, "x")).Error.Should().Be(ImpersonationErrors.NotAllowed);
        (await service.IssueCodeAsync(_admin, suspended.Id, "x")).Error.Should().Be(ImpersonationErrors.NotAllowed);
        (await service.IssueCodeAsync(staff.Id, staff.Id, "x")).Error.Should().Be(ImpersonationErrors.NotAllowed);
        (await service.ExchangeAsync("made-up")).Error.Should().Be(ImpersonationErrors.InvalidCode);
    }

    [Theory]
    [InlineData("POST", "/api/v1/auth/password/change", true)]
    [InlineData("POST", "/api/v1/auth/2fa/totp/enroll", true)]
    [InlineData("POST", "/api/v1/me/phone-change/start", true)]
    [InlineData("GET", "/api/v1/me/data-export", true)]
    [InlineData("DELETE", "/api/v1/me", true)]
    [InlineData("DELETE", "/api/v1/me/sessions/0b3c", true)]
    [InlineData("POST", "/api/v1/me/consents", true)]
    [InlineData("GET", "/api/v1/venue-manager/venues/1/payment-methods", true)]
    [InlineData("GET", "/api/v1/admin/users", true)]
    [InlineData("GET", "/api/v1/me", false)]
    [InlineData("GET", "/api/v1/me/sessions", false)]
    [InlineData("GET", "/api/v1/me/bookings", false)]
    [InlineData("POST", "/api/v1/bookings", false)]
    [InlineData("GET", "/health", false)]
    public void ImpersonationTokens_AreRefusedForSensitiveActions(string method, string path, bool restricted)
    {
        ImpersonationRestrictionMiddleware.IsRestricted(method, new PathString(path)).Should().Be(restricted);
    }

    private ImpersonationService Service(IdentityDbContext db)
    {
        var jwt = new JwtOptions { Issuer = "test", Audience = "test-client", AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7, CurrentKeyId = "test-es256-key-v1" };
        var tokens = new TokenService(new JwtKeyProvider(Options.Create(jwt)), _clock, Options.Create(jwt));
        return new ImpersonationService(db, _codes, tokens, new TestDevice(), _recorder, _recorder, _clock);
    }

    private static User Player(IdentityDbContext db)
    {
        var user = User.CreatePlayer(Guid.NewGuid(), "+2010" + Random.Shared.Next(10000000, 99999999), "Ahmed", _now);
        db.Users.Add(user);
        return user;
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class MemoryCodes : IImpersonationCodeStore
    {
        public Dictionary<string, ImpersonationGrant> Stored { get; } = [];

        public Task SaveAsync(string codeHash, ImpersonationGrant grant, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            Stored[codeHash] = grant;
            return Task.CompletedTask;
        }

        public Task<ImpersonationGrant?> TakeAsync(string codeHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.Remove(codeHash, out var grant) ? grant : null);
    }

    private sealed class Recorder : IAuditLog, INotificationPublisher
    {
        public List<AuditEntry> Audits { get; } = [];
        public List<NotificationRequest> Notifications { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Audits.Add(entry);
            return Task.CompletedTask;
        }

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Notifications.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
