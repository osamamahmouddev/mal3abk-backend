using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Privacy;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PrivacyTests
{
    private readonly MutableClock _clock = new();
    private readonly FakeSessionStore _store = new();
    private readonly FakeContributor _contributor = new();

    [Fact]
    public async Task DeleteAccount_SoftDeletes_RevokesSessions_AndErasesNonEssentialDataAsync()
    {
        await using var db = NewDb();
        var user = await SeedAsync(db);
        var session = UserSession.Create(Guid.NewGuid(), user.Id, "1.2.3.4", "ua", _clock.UtcNow);
        db.UserSessions.Add(session);
        db.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), user.Id, "hash", session.Id, _clock.UtcNow.AddDays(30), _clock.UtcNow));
        await db.SaveChangesAsync();
        var stampBefore = user.SecurityStamp;

        var result = await Privacy(db).DeleteAccountAsync(user.Id);

        result.Value.PersonalDataErasedAfterUtc.Should().Be(_clock.UtcNow + User.DeletionRetention);
        user.IsDeleted.Should().BeTrue();
        user.SecurityStamp.Should().NotBe(stampBefore, "outstanding step-up tokens must stop working");
        user.FullName.Should().Be("Omar Khaled", "personal data stays until the retention window ends");
        (await db.UserSessions.SingleAsync()).IsRevoked.Should().BeTrue();
        (await db.RefreshTokens.SingleAsync()).IsRevoked.Should().BeTrue();
        _store.Revoked.Should().Contain(session.Id);
        _contributor.Erased.Should().Contain(user.Id);
        (await Privacy(db).DeleteAccountAsync(user.Id)).Error.Should().Be(PrivacyErrors.NotFound);
    }

    [Fact]
    public async Task Anonymizer_ErasesOnlyAfterRetention_AndFreesThePhoneNumberAsync()
    {
        // With a clock the context runs the soft-delete interceptor and query filters, as it does in the API.
        await using var db = NewDb(_clock);
        var user = await SeedAsync(db);
        db.UserSessions.Add(UserSession.Create(Guid.NewGuid(), user.Id, "1.2.3.4", "ua", _clock.UtcNow));
        await db.SaveChangesAsync();
        var privacy = Privacy(db);
        await privacy.DeleteAccountAsync(user.Id);
        var anonymizer = new AccountAnonymizer(db, privacy, _clock);

        _clock.UtcNow += User.DeletionRetention - TimeSpan.FromHours(1);
        (await anonymizer.RunAsync()).Should().Be(0);

        _clock.UtcNow += TimeSpan.FromHours(2);
        (await anonymizer.RunAsync()).Should().Be(1);
        (await anonymizer.RunAsync()).Should().Be(0, "idempotent");

        user.FullName.Should().Be(User.AnonymizedName);
        user.PhoneNumber.Should().NotContain("+20");
        user.Email.Should().BeNull();
        user.City.Should().BeNull();
        user.AvatarUrl.Should().BeNull();
        user.Positions.Should().BeEmpty();
        (await db.UserSessions.IgnoreQueryFilters().CountAsync()).Should().Be(0, "sessions are physically erased, not soft-deleted");
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.PhoneNumber == "+201001234567")).Should().BeFalse();
    }

    [Fact]
    public async Task Consents_AreAppendOnly_WithCurrentStatePerTypeAsync()
    {
        await using var db = NewDb();
        var user = await SeedAsync(db);
        var privacy = Privacy(db);

        await privacy.RecordConsentAsync(user.Id, ConsentType.Terms, true, "2026-10");
        await privacy.RecordConsentAsync(user.Id, ConsentType.Location, true, null);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var result = await privacy.RecordConsentAsync(user.Id, ConsentType.Location, false, null);

        result.Value.History.Should().HaveCount(3);
        result.Value.Current.Should().HaveCount(2);
        result.Value.Current.Single(c => c.Type == "Location").Granted.Should().BeFalse();
        result.Value.Current.Single(c => c.Type == "Terms").Version.Should().Be("2026-10");
    }

    [Theory]
    [InlineData(ConsentType.Terms, true, null)]
    [InlineData(ConsentType.Privacy, true, " ")]
    [InlineData((ConsentType)42, true, "1")]
    [InlineData(ConsentType.Terms, true, "this-version-is-far-too-long")]
    public async Task Consents_RejectInvalidInputAsync(ConsentType type, bool granted, string? version)
    {
        await using var db = NewDb();
        var user = await SeedAsync(db);

        (await Privacy(db).RecordConsentAsync(user.Id, type, granted, version)).Error.Should().Be(PrivacyErrors.InvalidConsent);
    }

    [Fact]
    public async Task Export_ContainsAccountConsentsSessionsAndModuleSections_ButNoSecretsAsync()
    {
        await using var db = NewDb();
        var user = await SeedAsync(db);
        user.SetPassword("argon2-hash-value", _clock.UtcNow);
        user.SetPendingTotpSecret([1, 2, 3], _clock.UtcNow);
        db.UserSessions.Add(UserSession.Create(Guid.NewGuid(), user.Id, "1.2.3.4", "ua", _clock.UtcNow, deviceName: "Pixel"));
        db.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), user.Id, "refresh-hash-value", Guid.NewGuid(), _clock.UtcNow.AddDays(1), _clock.UtcNow));
        await db.SaveChangesAsync();
        var privacy = Privacy(db);
        await privacy.RecordConsentAsync(user.Id, ConsentType.Notifications, true, null);

        var export = (await privacy.ExportAsync(user.Id)).Value;
        var json = JsonSerializer.Serialize(export);

        export.Keys.Should().Contain(["account", "consents", "sessions", "fake"]);
        json.Should().Contain("201001234567").And.Contain("Pixel").And.Contain("Notifications");
        json.Should().NotContain("argon2-hash-value").And.NotContain("refresh-hash-value").And.NotContain("SecurityStamp").And.NotContain("Totp");
    }

    private PrivacyHandlers Privacy(IdentityDbContext db) =>
        new(db, _store, [_contributor], _clock, NullLogger<PrivacyHandlers>.Instance);

    private async Task<User> SeedAsync(IdentityDbContext db)
    {
        var user = User.CreatePlayer(Guid.NewGuid(), "+201001234567", "Player", _clock.UtcNow);
        user.UpdateProfile("Omar Khaled", "cairo", "maadi", [PlayerPosition.Forward], SkillLevel.Advanced, _clock.UtcNow);
        user.SetAvatar("/api/v1/media/x/medium", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IdentityDbContext NewDb(IClock? clock = null) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, clock);

    private sealed class FakeSessionStore : ISessionStore
    {
        public List<Guid> Revoked { get; } = [];

        public Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult(!Revoked.Contains(sessionId));

        public Task MarkRevokedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default)
        {
            Revoked.AddRange(sessionIds);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeContributor : IPersonalDataContributor
    {
        public List<Guid> Erased { get; } = [];

        public string Section => "fake";

        public Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<object?>(new { Items = 1 });

        public Task EraseAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Erased.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(2);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
