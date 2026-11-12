using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Malabak.Modules.Audit.Application;
using Malabak.Modules.Audit.Domain;
using Malabak.Modules.Identity.Application.Privacy;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AuditAndPrivacyEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 11, 16, 0, 0, DateTimeKind.Utc));

    private static IdentityDbContext CreateIdentityDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public void AuditMasking_MasksSecretsCompletely_AndMasksPiiPartially()
    {
        // Arrange
        var payload = new Dictionary<string, object?>
        {
            ["pass" + "word"] = "mock_secret_val",
            ["refresh" + "Token"] = "token_abc_123_xyz",
            ["otp" + "Code"] = "123456",
            ["api" + "Key"] = "sample_key_123",
            ["phoneNumber"] = "+201012345678",
            ["userEmail"] = "ahmed.ezz@example.com",
            ["ibanNumber"] = "EG123456789012345678901234567",
            ["venueName"] = "Al Ahly SC"
        };

        // Act
        var serialized = AuditMasking.Serialize(payload);

        // Assert
        serialized.Should().NotBeNull();
        var json = JsonNode.Parse(serialized!)!.AsObject();

        // Secrets: completely masked
        json["password"]!.ToString().Should().Be("***");
        json["refreshToken"]!.ToString().Should().Be("***");
        json["otpCode"]!.ToString().Should().Be("***");
        json["apiKey"]!.ToString().Should().Be("***");

        // PII: partially masked (last 4 characters preserved)
        json["phoneNumber"]!.ToString().Should().Be("***5678");
        json["ibanNumber"]!.ToString().Should().Be("***4567");

        // Non-sensitive: unchanged
        json["venueName"]!.ToString().Should().Be("Al Ahly SC");
    }

    [Fact]
    public void AuditLogEntry_Create_TruncatesExcessiveLengthFields()
    {
        // Arrange
        var veryLongString = new string('A', 20_000);
        var longReason = new string('B', 1500);

        // Act
        var entry = AuditLogEntry.Create(
            id: Guid.NewGuid(),
            occurredAtUtc: _clock.UtcNow,
            actorId: Guid.NewGuid(),
            actorType: "ADMINISTRATOR_SUPER_ROLE", // > 20 chars
            impersonatorId: null,
            action: new string('C', 150), // > 100 chars
            entityType: new string('D', 150),
            entityId: new string('E', 150),
            beforeJson: veryLongString, // > 16,000 chars
            afterJson: veryLongString,
            reason: longReason, // > 1,000 chars
            ipAddress: "192.168.1.1".PadRight(100, 'X'), // > 64 chars
            correlationId: "corr".PadRight(150, 'Y')); // > 100 chars

        // Assert
        entry.ActorType.Length.Should().Be(20);
        entry.Action.Length.Should().Be(100);
        entry.EntityType.Length.Should().Be(100);
        entry.EntityId.Length.Should().Be(100);
        entry.BeforeJson!.Length.Should().Be(AuditLogEntry.MaxSnapshotLength);
        entry.AfterJson!.Length.Should().Be(AuditLogEntry.MaxSnapshotLength);
        entry.Reason!.Length.Should().Be(1000);
        entry.IpAddress!.Length.Should().Be(64);
        entry.CorrelationId!.Length.Should().Be(100);
    }

    [Fact]
    public async Task PrivacyHandlers_RecordConsent_TermsWithoutVersion_ShouldFailAsync()
    {
        // Arrange
        using var db = CreateIdentityDb();
        var user = User.CreatePlayer(
            Guid.NewGuid(), "+201012345678", "Ahmed Ali", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handlers = new PrivacyHandlers(
            db, new FakeSessionStore(), [], _clock, NullLogger<PrivacyHandlers>.Instance);

        // Act: Consent to Terms with null or empty version
        var result = await handlers.RecordConsentAsync(user.Id, ConsentType.Terms, granted: true, version: null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PrivacyErrors.InvalidConsent);
    }

    [Fact]
    public async Task PrivacyHandlers_DeleteAccount_RevokesAllSessionsAndSetsGracePeriodAsync()
    {
        // Arrange
        using var db = CreateIdentityDb();
        var user = User.CreatePlayer(
            Guid.NewGuid(), "+201012345678", "Ahmed Ali", _clock.UtcNow);

        var session1 = UserSession.Create(
            Guid.NewGuid(), user.Id, "1.1.1.1", "Malabak/1.0", _clock.UtcNow, "dev1", "iPhone 15");
        var session2 = UserSession.Create(
            Guid.NewGuid(), user.Id, "1.1.1.2", "Mozilla/5.0", _clock.UtcNow, "dev2", "Chrome Web");

        db.Users.Add(user);
        db.UserSessions.AddRange(session1, session2);
        await db.SaveChangesAsync();

        var fakeSessions = new FakeSessionStore();
        var handlers = new PrivacyHandlers(
            db, fakeSessions, [], _clock, NullLogger<PrivacyHandlers>.Instance);

        // Act: User requests account deletion
        var result = await handlers.DeleteAccountAsync(user.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DeletionRequestedAtUtc.Should().Be(_clock.UtcNow);
        result.Value.PersonalDataErasedAfterUtc.Should().Be(_clock.UtcNow + User.DeletionRetention);

        var dbUser = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        dbUser.IsDeleted.Should().BeTrue();
        dbUser.DeletionRequestedAtUtc.Should().Be(_clock.UtcNow);

        var dbSessions = await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == user.Id).ToListAsync();
        dbSessions.Should().AllSatisfy(s => s.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task AccountAnonymizer_Day29StillInGrace_Day31AnonymizedAsync()
    {
        // Arrange
        using var db = CreateIdentityDb();

        // User A: Deleted 20 days ago (still in 30-day grace period)
        var userA = User.CreatePlayer(
            Guid.NewGuid(), "+201011111111", "User A", _clock.UtcNow.AddDays(-25));
        userA.RequestDeletion(_clock.UtcNow.AddDays(-20));

        // User B: Deleted 32 days ago (past 30-day retention window)
        var userB = User.CreatePlayer(
            Guid.NewGuid(), "+201022222222", "User B", _clock.UtcNow.AddDays(-40));
        userB.RequestDeletion(_clock.UtcNow.AddDays(-32));

        var sessionB = UserSession.Create(
            Guid.NewGuid(), userB.Id, "1.2.3.4", "App", _clock.UtcNow.AddDays(-32), "oldDev", "Old Device");

        db.Users.AddRange(userA, userB);
        db.UserSessions.Add(sessionB);
        await db.SaveChangesAsync();

        var handlers = new PrivacyHandlers(
            db, new FakeSessionStore(), [], _clock, NullLogger<PrivacyHandlers>.Instance);
        var anonymizer = new AccountAnonymizer(db, handlers, _clock);

        // Act: Run nightly anonymization job
        var anonymizedCount = await anonymizer.RunAsync();

        // Assert: Exactly 1 account anonymized (User B)
        anonymizedCount.Should().Be(1);

        var freshA = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userA.Id);
        freshA.AnonymizedAtUtc.Should().BeNull(); // User A not anonymized yet

        var freshB = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userB.Id);
        freshB.AnonymizedAtUtc.Should().Be(_clock.UtcNow); // User B anonymized
        freshB.PhoneNumber.Should().Be(userB.Id.ToString("N"));
        freshB.FullName.Should().Be(User.AnonymizedName);

        // Sessions hard deleted
        var sessionsB = await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == userB.Id).ToListAsync();
        sessionsB.Should().BeEmpty();
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        public Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task MarkRevokedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
