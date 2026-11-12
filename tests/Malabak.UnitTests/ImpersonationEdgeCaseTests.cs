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
using Malabak.SharedKernel.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 1 (P1) Edge-Case Tests: Admin Support Impersonation (SECURITY.md §10.4).
/// Validates single-use exchange codes, RFC 8693 act claim, device transparency, audit logging,
/// and restriction middleware blocking sensitive operations.
/// </summary>
public sealed class ImpersonationEdgeCaseTests
{
    private static readonly DateTime _initialTime = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _adminId = Guid.NewGuid();
    private readonly FakeClock _clock = new(_initialTime);
    private readonly MemoryImpersonationStore _codes;
    private readonly RecordingEventSink _recorder = new();

    public ImpersonationEdgeCaseTests()
    {
        _codes = new MemoryImpersonationStore(_clock);
    }

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private ImpersonationService CreateService(IdentityDbContext db)
    {
        var jwt = new JwtOptions
        {
            Issuer = "Malabak.Api.Test",
            Audience = "Malabak.Client.Test",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
            CurrentKeyId = "test-es256-key-v1"
        };
        var tokenService = new TokenService(new JwtKeyProvider(Options.Create(jwt)), _clock, Options.Create(jwt));
        return new ImpersonationService(db, _codes, tokenService, new TestDevice(), _recorder, _recorder, _clock);
    }

    private static User CreateSeedPlayer(IdentityDbContext db, string phone = "+201011223344")
    {
        var user = User.CreatePlayer(Guid.NewGuid(), phone, "Player Name", _initialTime);
        db.Users.Add(user);
        return user;
    }

    // =========================================================================
    // 1. Issuance & Restrictions on Target Accounts (SECURITY.md §10.4)
    // =========================================================================

    [Fact]
    public async Task IssueCode_ReasonRequired_RejectsEmptyOrWhitespaceAsync()
    {
        await using var db = CreateDbContext();
        var player = CreateSeedPlayer(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var emptyResult = await service.IssueCodeAsync(_adminId, player.Id, "");
        var whitespaceResult = await service.IssueCodeAsync(_adminId, player.Id, "   ");

        emptyResult.IsFailure.Should().BeTrue();
        whitespaceResult.IsFailure.Should().BeTrue();
        emptyResult.Error.Should().Be(ImpersonationErrors.ReasonRequired);
        whitespaceResult.Error.Should().Be(ImpersonationErrors.ReasonRequired);
    }

    [Fact]
    public async Task IssueCode_DisallowedTargets_StaffSuspendedOrSelf_RejectedAsync()
    {
        await using var db = CreateDbContext();
        var staff = User.CreateStaff(Guid.NewGuid(), "agent@malabak.test", "+201055556666", "pwd", "Agent", UserRole.SupportAgent, _initialTime);
        var suspendedPlayer = CreateSeedPlayer(db, "+201077778888");
        suspendedPlayer.Suspend("Suspicious activity", _initialTime);
        db.Users.Add(staff);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Staff account cannot be impersonated
        (await service.IssueCodeAsync(_adminId, staff.Id, "Valid ticket SUP-100")).Error.Should().Be(ImpersonationErrors.NotAllowed);

        // Suspended account cannot be impersonated
        (await service.IssueCodeAsync(_adminId, suspendedPlayer.Id, "Valid ticket SUP-100")).Error.Should().Be(ImpersonationErrors.NotAllowed);

        // Admin cannot impersonate themselves
        (await service.IssueCodeAsync(_adminId, _adminId, "Valid ticket SUP-100")).Error.Should().Be(ImpersonationErrors.NotAllowed);
    }

    // =========================================================================
    // 2. Single-Use Code, Expiration, and Secure Storage
    // =========================================================================

    [Fact]
    public async Task ExchangeCode_IsSingleUse_AndReplayIsRejectedAsync()
    {
        await using var db = CreateDbContext();
        var player = CreateSeedPlayer(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var codeResult = await service.IssueCodeAsync(_adminId, player.Id, "Ticket SUP-123: Customer inquiry");
        codeResult.IsSuccess.Should().BeTrue();
        codeResult.Value.ExpiresInSeconds.Should().Be(60);

        // The plaintext code must never be stored in the cache (hash only)
        _codes.Stored.Keys.Should().NotContain(codeResult.Value.Code);

        // First exchange: succeeds
        var firstExchange = await service.ExchangeAsync(codeResult.Value.Code);
        firstExchange.IsSuccess.Should().BeTrue();

        // Second exchange with same code: rejected (single-use)
        var secondExchange = await service.ExchangeAsync(codeResult.Value.Code);
        secondExchange.IsFailure.Should().BeTrue();
        secondExchange.Error.Should().Be(ImpersonationErrors.InvalidCode);
    }

    [Fact]
    public async Task ExchangeCode_After60Seconds_ExpiresAsync()
    {
        await using var db = CreateDbContext();
        var player = CreateSeedPlayer(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var codeResult = await service.IssueCodeAsync(_adminId, player.Id, "Ticket SUP-123");
        codeResult.IsSuccess.Should().BeTrue();

        // Advance 61 seconds (past 60s expiration)
        _clock.Advance(TimeSpan.FromSeconds(61));

        var exchangeResult = await service.ExchangeAsync(codeResult.Value.Code);
        exchangeResult.IsFailure.Should().BeTrue();
        exchangeResult.Error.Should().Be(ImpersonationErrors.InvalidCode);
    }

    // =========================================================================
    // 3. Token Claims, Actor Identification, and Auditing (RFC 8693)
    // =========================================================================

    [Fact]
    public async Task ImpersonationToken_CarriesActorClaimAndVisibleSessionAsync()
    {
        await using var db = CreateDbContext();
        var player = CreateSeedPlayer(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var code = (await service.IssueCodeAsync(_adminId, player.Id, "Ticket SUP-456: investigation")).Value;
        var tokenResult = (await service.ExchangeAsync(code.Code)).Value;

        // Lifetime is 15 minutes
        tokenResult.ExpiresIn.Should().Be(15 * 60);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenResult.AccessToken);
        jwt.Subject.Should().Be(player.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == TokenService.SessionClaim);

        // RFC 8693: act claim contains actor (the admin)
        var actClaim = jwt.Claims.Single(c => c.Type == TokenService.ImpersonationActorClaim);
        actClaim.Value.Should().Contain(_adminId.ToString("D"));

        // User session created with explicit support device name for player visibility
        var session = await db.UserSessions.SingleAsync(s => s.UserId == player.Id);
        session.DeviceName.Should().Be(ImpersonationService.SessionDeviceName);

        // Audit events & notification dispatched
        _recorder.Audits.Select(a => a.Action).Should().Equal("admin.impersonation.code_issued", "admin.impersonation.started");
        _recorder.Notifications.Should().ContainSingle(n => n.UserId == player.Id && n.Type == NotificationTypes.ImpersonationStarted);
    }

    // =========================================================================
    // 4. Endpoint Restrictions: Sensitive Actions Blocked for Impersonated Tokens
    // =========================================================================

    [Theory]
    [InlineData("POST", "/api/v1/auth/password/change", true)]
    [InlineData("POST", "/api/v1/auth/2fa/totp/enroll", true)]
    [InlineData("POST", "/api/v1/me/phone-change/start", true)]
    [InlineData("GET", "/api/v1/me/data-export", true)]
    [InlineData("DELETE", "/api/v1/me", true)]
    [InlineData("DELETE", "/api/v1/me/sessions/123", true)]
    [InlineData("GET", "/api/v1/admin/users", true)]
    [InlineData("GET", "/api/v1/venue-manager/venues/1/payment-methods", true)]
    [InlineData("GET", "/api/v1/me", false)]
    [InlineData("GET", "/api/v1/me/bookings", false)]
    [InlineData("POST", "/api/v1/bookings", false)]
    public void ImpersonationRestrictionMiddleware_BlocksSensitiveActions(string method, string path, bool shouldBeBlocked)
    {
        var isBlocked = ImpersonationRestrictionMiddleware.IsRestricted(method, new PathString(path));
        isBlocked.Should().Be(shouldBeBlocked);
    }

    // =========================================================================
    // Test Doubles
    // =========================================================================

    private sealed class MemoryImpersonationStore(IClock clock) : IImpersonationCodeStore
    {
        public Dictionary<string, (ImpersonationGrant Grant, DateTime ExpiresAtUtc)> Stored { get; } = [];

        public Task SaveAsync(string codeHash, ImpersonationGrant grant, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            Stored[codeHash] = (grant, clock.UtcNow + lifetime);
            return Task.CompletedTask;
        }

        public Task<ImpersonationGrant?> TakeAsync(string codeHash, CancellationToken cancellationToken = default)
        {
            if (Stored.Remove(codeHash, out var entry) && clock.UtcNow <= entry.ExpiresAtUtc)
            {
                return Task.FromResult<ImpersonationGrant?>(entry.Grant);
            }
            return Task.FromResult<ImpersonationGrant?>(null);
        }
    }

    private sealed class RecordingEventSink : IAuditLog, INotificationPublisher
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
}
