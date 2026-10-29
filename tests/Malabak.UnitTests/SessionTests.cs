using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SessionTests
{
    private readonly FixedClock _clock = new();
    private readonly IOptions<JwtOptions> _jwt = Options.Create(new JwtOptions { CurrentKeyId = "k" });

    [Fact]
    public async Task Start_CreatesSessionWithDeviceInfo_AndStoreSeesItActiveAsync()
    {
        await using var db = NewDb();
        var user = await SeedUserAsync(db);

        var sessionId = await TestSessions.Create(db, _clock).StartAsync(user, isAdminSession: false);

        var session = await db.UserSessions.SingleAsync();
        session.Id.Should().Be(sessionId);
        session.DeviceName.Should().Be("Pixel 8");
        session.IpAddress.Should().Be("10.0.0.1");
        (await TestSessions.Store(db).IsActiveAsync(sessionId)).Should().BeTrue();
    }

    [Fact]
    public void Token_CarriesSidClaim()
    {
        var user = User.CreatePlayer(Guid.NewGuid(), "+201001234567", "P", _clock.UtcNow);
        var sessionId = Guid.NewGuid();

        var token = new TokenService(new JwtKeyProvider(_jwt), _clock, _jwt).GenerateTokens(user, sessionId);

        new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken).Claims.Single(c => c.Type == "sid").Value
            .Should().Be(sessionId.ToString("D"));
    }

    [Fact]
    public async Task NewDeviceAlert_OnlyForUnknownDevice_NotFirstSignInAsync()
    {
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var alerts = new RecordingAlerts();
        var device = new TestDevice();
        var sessions = TestSessions.Create(db, _clock, device, alerts);

        await sessions.StartAsync(user, false);
        alerts.Messages.Should().BeEmpty("the very first sign-in isn't suspicious");

        await sessions.StartAsync(user, false);
        alerts.Messages.Should().BeEmpty("same device again");

        device.DeviceId = "device-2";
        device.DeviceName = "iPhone 15";
        await sessions.StartAsync(user, false);
        alerts.Messages.Should().ContainSingle().Which.Should().Contain("iPhone 15");
    }

    [Fact]
    public async Task Revoke_KillsSessionAndItsRefreshTokens_OnlyForOwnerAsync()
    {
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sessions = TestSessions.Create(db, _clock);
        var sessionId = await sessions.StartAsync(user, false);
        db.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), user.Id, "hash", sessionId, _clock.UtcNow.AddDays(7), _clock.UtcNow));
        await db.SaveChangesAsync();

        (await sessions.RevokeAsync(Guid.NewGuid(), sessionId, "attacker")).Should().BeFalse("another user can't revoke it");
        (await sessions.RevokeAsync(user.Id, sessionId, "user_revoked")).Should().BeTrue();

        (await db.RefreshTokens.SingleAsync()).IsRevoked.Should().BeTrue();
        (await TestSessions.Store(db).IsActiveAsync(sessionId)).Should().BeFalse();
        (await sessions.ListAsync(user.Id, null)).Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeOthers_KeepsCurrentSessionAsync()
    {
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sessions = TestSessions.Create(db, _clock);
        var current = await sessions.StartAsync(user, false);
        await sessions.StartAsync(user, false);
        await sessions.StartAsync(user, false);

        (await sessions.RevokeAllAsync(user.Id, current, "user_revoked_others")).Should().Be(2);

        var remaining = await sessions.ListAsync(user.Id, current);
        remaining.Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_OfRevokedSession_FailsEvenWithUnrevokedTokenAsync()
    {
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sessions = TestSessions.Create(db, _clock);
        var tokens = new TokenService(new JwtKeyProvider(_jwt), _clock, _jwt);
        var sessionId = await sessions.StartAsync(user, false);
        var raw = tokens.GenerateRawRefreshToken();
        db.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), user.Id, tokens.HashRefreshToken(raw), sessionId, _clock.UtcNow.AddDays(7), _clock.UtcNow));
        await db.SaveChangesAsync();
        var refresh = new RefreshTokenHandler(db, tokens, _clock, NullLogger<RefreshTokenHandler>.Instance, sessions);

        // Revoke the session only (simulating a revocation that raced the refresh-token update).
        (await db.UserSessions.SingleAsync()).Revoke(_clock.UtcNow, "test");
        await db.SaveChangesAsync();

        (await refresh.HandleAsync(new RefreshTokenCommand(raw))).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_KeepsSameSid_AndTouchesSessionAsync()
    {
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sessions = TestSessions.Create(db, _clock);
        var tokens = new TokenService(new JwtKeyProvider(_jwt), _clock, _jwt);
        var sessionId = await sessions.StartAsync(user, false);
        var raw = tokens.GenerateRawRefreshToken();
        db.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), user.Id, tokens.HashRefreshToken(raw), sessionId, _clock.UtcNow.AddDays(7), _clock.UtcNow));
        await db.SaveChangesAsync();

        var result = await new RefreshTokenHandler(db, tokens, _clock, NullLogger<RefreshTokenHandler>.Instance, sessions)
            .HandleAsync(new RefreshTokenCommand(raw));

        new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken).Claims.Single(c => c.Type == "sid").Value
            .Should().Be(sessionId.ToString("D"));
    }

    private async Task<User> SeedUserAsync(IdentityDbContext db)
    {
        var user = User.CreatePlayer(Guid.NewGuid(), "+201001234567", "Player", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
