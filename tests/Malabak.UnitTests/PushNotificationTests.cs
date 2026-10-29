using System.Net;
using FluentAssertions;
using Malabak.Modules.Notifications.Application;
using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PushNotificationTests
{
    private readonly MutableClock _clock = new();
    private readonly FakeSender _sender = new();
    private readonly FakeSessions _sessions = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();

    [Fact]
    public async Task NewNotification_IsPushedToEachActiveDevice_InItsLanguage_WithoutSensitiveBodyAsync()
    {
        await using var db = NewDb();
        var devices = new DeviceTokenService(db, _clock);
        await devices.RegisterAsync(_userId, _sessionId, "token-ar", DevicePlatform.Android, "ar");
        await devices.RegisterAsync(_userId, Guid.NewGuid(), "token-en", DevicePlatform.Ios, "en");

        await PublishAndDispatchAsync(db, NotificationTypes.VenueRejected, new Dictionary<string, string>
        {
            ["venueId"] = "v1",
            ["venueName"] = "Club",
            ["reason"] = "Owner ID looks forged"
        });

        _sender.Sent.Should().HaveCount(2);
        var arabic = _sender.Sent.Single(p => p.Token == "token-ar");
        arabic.Title.Should().Be("لم تتم الموافقة على ملعبك");
        _sender.Sent.Single(p => p.Token == "token-en").Title.Should().Be("Venue not approved");
        _sender.Sent.Should().OnlyContain(p => !p.Body.Contains("forged") && !p.Data.Values.Any(v => v.Contains("forged")));
        arabic.Data["deepLink"].Should().Be("malabak://venue-manager/venue/v1");
    }

    [Fact]
    public async Task PushOptOut_StillCreatesInAppNotification_ButNoPushAsync()
    {
        await using var db = NewDb();
        await new DeviceTokenService(db, _clock).RegisterAsync(_userId, _sessionId, "t1", DevicePlatform.Android, "ar");
        await new UserNotificationService(db, _clock).UpdatePreferenceAsync(_userId, NotificationCategory.Venues, inApp: true, push: false);

        await PublishAndDispatchAsync(db, NotificationTypes.VenueApproved, new Dictionary<string, string> { ["venueName"] = "Club" });

        (await db.Notifications.CountAsync()).Should().Be(1);
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidToken_IsDeactivated_AndNotRetriedAsync()
    {
        await using var db = NewDb();
        await new DeviceTokenService(db, _clock).RegisterAsync(_userId, _sessionId, "dead", DevicePlatform.Android, "ar");
        _sender.Next = new PushSendResult(PushFailureKind.InvalidToken, "unregistered");

        await PublishAndDispatchAsync(db, NotificationTypes.VenueApproved, new Dictionary<string, string> { ["venueName"] = "Club" });

        (await db.DeviceTokens.SingleAsync()).IsActive.Should().BeFalse();
        (await db.PushDeliveries.SingleAsync()).Status.Should().Be(PushStatus.Failed);
    }

    [Fact]
    public async Task TransientFailure_RetriesWithExponentialBackoff_ThenGivesUpAsync()
    {
        await using var db = NewDb();
        await new DeviceTokenService(db, _clock).RegisterAsync(_userId, _sessionId, "t1", DevicePlatform.Android, "ar");
        _sender.Next = new PushSendResult(PushFailureKind.Transient, "http_503");
        await PublishAndDispatchAsync(db, NotificationTypes.VenueApproved, new Dictionary<string, string> { ["venueName"] = "Club" });
        var push = Push(db);

        var delivery = await db.PushDeliveries.SingleAsync();
        delivery.Attempts.Should().Be(1);
        delivery.NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddSeconds(30));

        (await push.ProcessDueAsync()).Should().Be(0, "not due yet");

        _clock.UtcNow = _clock.UtcNow.AddSeconds(30);
        await push.ProcessDueAsync();
        (await db.PushDeliveries.SingleAsync()).NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddSeconds(60));

        for (var i = 0; i < 5; i++)
        {
            _clock.UtcNow = _clock.UtcNow.AddHours(1);
            await push.ProcessDueAsync();
        }

        var final = await db.PushDeliveries.SingleAsync();
        final.Status.Should().Be(PushStatus.Failed);
        final.Attempts.Should().Be(PushDelivery.MaxAttempts);
    }

    [Fact]
    public async Task SignedOutSession_GetsNoPush_AndTokenIsDeactivatedAsync()
    {
        await using var db = NewDb();
        await new DeviceTokenService(db, _clock).RegisterAsync(_userId, _sessionId, "t1", DevicePlatform.Android, "ar");
        _sessions.Active = false;

        await PublishAndDispatchAsync(db, NotificationTypes.VenueApproved, new Dictionary<string, string> { ["venueName"] = "Club" });

        _sender.Sent.Should().BeEmpty();
        (await db.DeviceTokens.SingleAsync()).DeactivatedReason.Should().Be("session_ended");
    }

    [Fact]
    public async Task ReRegisteringToken_MovesItToTheNewUser_AndUnregisterIsOwnerOnlyAsync()
    {
        await using var db = NewDb();
        var devices = new DeviceTokenService(db, _clock);
        var newUser = Guid.NewGuid();
        await devices.RegisterAsync(_userId, _sessionId, "shared-phone", DevicePlatform.Android, "ar");
        await devices.RegisterAsync(newUser, Guid.NewGuid(), "shared-phone", DevicePlatform.Android, "en");

        var token = await db.DeviceTokens.SingleAsync();
        token.UserId.Should().Be(newUser);
        (await devices.UnregisterAsync(_userId, "shared-phone")).Should().BeFalse();
        (await devices.UnregisterAsync(newUser, "shared-phone")).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"status\":\"NOT_FOUND\"}}", PushFailureKind.InvalidToken)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}", PushFailureKind.InvalidToken)]
    [InlineData(HttpStatusCode.TooManyRequests, "", PushFailureKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", PushFailureKind.Transient)]
    [InlineData(HttpStatusCode.Forbidden, "", PushFailureKind.Permanent)]
    public void FcmErrors_AreClassified(HttpStatusCode status, string body, PushFailureKind expected)
    {
        FcmPushSender.Classify(status, body).Failure.Should().Be(expected);
    }

    private async Task PublishAndDispatchAsync(NotificationsDbContext db, string type, Dictionary<string, string> data)
    {
        await new OutboxNotificationPublisher(db, _clock).PublishAsync(new NotificationRequest(_userId, type, data, $"{type}:{Guid.NewGuid():N}"));
        await new NotificationDispatcher(db, _clock, NullLogger<NotificationDispatcher>.Instance, new DeviceTokenService(db, _clock)).ProcessPendingAsync();
        await Push(db).ProcessDueAsync();
    }

    private PushDispatcher Push(NotificationsDbContext db) => new(db, _sender, _sessions, _clock, NullLogger<PushDispatcher>.Instance);

    private static NotificationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeSender : IPushSender
    {
        public List<PushPayload> Sent { get; } = [];
        public PushSendResult Next { get; set; } = PushSendResult.Ok;

        public Task<PushSendResult> SendAsync(PushPayload payload, CancellationToken cancellationToken = default)
        {
            if (Next.Success)
            {
                Sent.Add(payload);
            }

            return Task.FromResult(Next);
        }
    }

    private sealed class FakeSessions : ISessionActivityChecker
    {
        public bool Active { get; set; } = true;

        public Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult(Active);
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
