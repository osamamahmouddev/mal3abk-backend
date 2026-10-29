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

public sealed class NotificationTests
{
    private readonly FixedClock _clock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private static NotificationRequest VenueApproved(Guid userId, string dedupe = "venue.approved:1") => new(
        userId, NotificationTypes.VenueApproved,
        new Dictionary<string, string> { ["venueId"] = "11111111-1111-1111-1111-111111111111", ["venueName"] = "ملعب النصر" },
        dedupe);

    [Fact]
    public void EveryPublicType_HasArabicAndEnglishTemplate()
    {
        var publicTypes = typeof(NotificationTypes).GetFields().Select(f => (string)f.GetValue(null)!);

        foreach (var type in publicTypes)
        {
            NotificationCatalog.TryGet(type, out var template).Should().BeTrue(type);
            template.TitleAr.Should().NotBeNullOrWhiteSpace();
            template.TitleEn.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Render_FillsPlaceholders_AndDropsMissingOnes()
    {
        NotificationCatalog.Render("{venueName} on {date}", new Dictionary<string, string> { ["venueName"] = "Club" })
            .Should().Be("Club on ");
    }

    [Fact]
    public void DeepLinks_AreBuiltFromData()
    {
        NotificationCatalog.TryGet(NotificationTypes.BookingConfirmed, out var template);

        template.DeepLink(new Dictionary<string, string> { ["bookingId"] = "abc" }).Should().Be("malabak://booking/abc");
        template.DeepLink(new Dictionary<string, string>()).Should().BeNull();
    }

    [Fact]
    public async Task Publish_ThenDispatch_CreatesNotification_WithDeepLink_AndRendersPerLanguageAsync()
    {
        await using var db = NewDb();
        await new OutboxNotificationPublisher(db, _clock).PublishAsync(VenueApproved(_userId));

        (await Dispatcher(db).ProcessPendingAsync()).Should().Be(1);

        var service = new UserNotificationService(db, _clock);
        var arabic = await service.ListAsync(_userId, 1, 20, arabic: true);
        var english = await service.ListAsync(_userId, 1, 20, arabic: false);

        arabic.Items.Should().ContainSingle().Which.Title.Should().Be("تمت الموافقة على ملعبك");
        arabic.Items[0].Body.Should().Contain("ملعب النصر");
        english.Items[0].Title.Should().Be("Venue approved");
        english.Items[0].DeepLink.Should().Be("malabak://venue-manager/venue/11111111-1111-1111-1111-111111111111");
        arabic.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispatch_IsIdempotent_ByDedupeKeyAsync()
    {
        await using var db = NewDb();
        var publisher = new OutboxNotificationPublisher(db, _clock);
        await publisher.PublishAsync(VenueApproved(_userId));
        await publisher.PublishAsync(VenueApproved(_userId));

        await Dispatcher(db).ProcessPendingAsync();

        (await db.Notifications.CountAsync()).Should().Be(1);
        (await db.Outbox.AllAsync(m => m.ProcessedAtUtc != null)).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_UnknownType_IsRejectedAsync()
    {
        await using var db = NewDb();

        var act = () => new OutboxNotificationPublisher(db, _clock)
            .PublishAsync(new NotificationRequest(_userId, "made.up", new Dictionary<string, string>(), "k"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Preferences_OptOut_SkipsCategory_ButSecurityCannotBeDisabledAsync()
    {
        await using var db = NewDb();
        var service = new UserNotificationService(db, _clock);
        await service.UpdatePreferenceAsync(_userId, NotificationCategory.Venues, inApp: false, push: false);
        var prefs = await service.UpdatePreferenceAsync(_userId, NotificationCategory.Security, inApp: false, push: false);

        var security = prefs.Single(p => p.Category == "Security");
        security.InAppEnabled.Should().BeTrue();
        security.CanDisable.Should().BeFalse();
        prefs.Single(p => p.Category == "Marketing").InAppEnabled.Should().BeFalse("marketing is opt-in by default");

        var publisher = new OutboxNotificationPublisher(db, _clock);
        await publisher.PublishAsync(VenueApproved(_userId));
        await publisher.PublishAsync(new NotificationRequest(_userId, NotificationTypes.NewDeviceSignIn,
            new Dictionary<string, string> { ["device"] = "iPhone" }, "security:1"));
        await Dispatcher(db).ProcessPendingAsync();

        (await db.Notifications.Select(n => n.Type).ToListAsync()).Should().Equal(NotificationTypes.NewDeviceSignIn);
    }

    [Fact]
    public async Task MarkRead_IsScopedToOwner_AndReadAllClearsUnreadAsync()
    {
        await using var db = NewDb();
        var publisher = new OutboxNotificationPublisher(db, _clock);
        await publisher.PublishAsync(VenueApproved(_userId, "a"));
        await publisher.PublishAsync(VenueApproved(_userId, "b"));
        await Dispatcher(db).ProcessPendingAsync();
        var service = new UserNotificationService(db, _clock);
        var id = (await db.Notifications.FirstAsync()).Id;

        (await service.MarkReadAsync(Guid.NewGuid(), id)).Should().BeFalse("another user can't touch it");
        (await service.MarkReadAsync(_userId, id)).Should().BeTrue();
        (await service.UnreadCountAsync(_userId)).Should().Be(1);
        (await service.MarkAllReadAsync(_userId)).Should().Be(1);
        (await service.UnreadCountAsync(_userId)).Should().Be(0);
    }

    [Fact]
    public async Task List_IsPaged_AndCapsPageSizeAsync()
    {
        await using var db = NewDb();
        var publisher = new OutboxNotificationPublisher(db, _clock);
        for (var i = 0; i < 60; i++)
        {
            await publisher.PublishAsync(VenueApproved(_userId, $"k{i}"));
        }

        await Dispatcher(db).ProcessPendingAsync();
        var page = await new UserNotificationService(db, _clock).ListAsync(_userId, 1, 500, arabic: false);

        page.PageSize.Should().Be(UserNotificationService.MaxPageSize);
        page.Items.Should().HaveCount(50);
        page.TotalCount.Should().Be(60);
    }

    private NotificationDispatcher Dispatcher(NotificationsDbContext db) => new(db, _clock, NullLogger<NotificationDispatcher>.Instance);

    private static NotificationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
