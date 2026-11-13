using FluentAssertions;
using Malabak.Modules.Notifications.Application;
using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>One account can be a player and work at a venue: each app must show only its own notifications.</summary>
public sealed class NotificationSurfaceTests
{
    private static readonly DateTime _now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _userId = Guid.NewGuid();

    [Theory]
    [InlineData(NotificationTypes.BookingReminder2h)]
    [InlineData(NotificationTypes.BookingConfirmed)]
    [InlineData(NotificationTypes.TeamChallengeReceived)]
    [InlineData(NotificationTypes.MatchScheduled)]
    public void PlayerTypes_BelongOnlyToThePlayerApp(string type)
    {
        NotificationCatalog.SurfacesOf(type).Should().Equal(NotificationSurface.Player);
    }

    [Theory]
    [InlineData(NotificationTypes.PaymentProofSubmitted)]
    [InlineData(NotificationTypes.VenueInfoRequested)]
    [InlineData(NotificationTypes.VenueApproved)]
    [InlineData(NotificationTypes.StaffInvitationAccepted)]
    [InlineData(NotificationTypes.SubscriptionExpiring)]
    [InlineData(NotificationTypes.PaymentDisputeOpened)]
    public void VenueTypes_BelongOnlyToTheVenueDashboard(string type)
    {
        NotificationCatalog.SurfacesOf(type).Should().Equal(NotificationSurface.Venue);
    }

    [Theory]
    [InlineData(NotificationTypes.NewDeviceSignIn)]
    [InlineData(NotificationTypes.ImpersonationStarted)]
    [InlineData(NotificationTypes.SupportTicketReplied)]
    public void AccountWideTypes_ShowInEveryApp(string type)
    {
        NotificationCatalog.SurfacesOf(type).Should().BeEquivalentTo(
            new List<NotificationSurface> { NotificationSurface.Player, NotificationSurface.Venue, NotificationSurface.Admin });
    }

    [Fact]
    public void EveryKnownType_BelongsToAtLeastOneApp()
    {
        NotificationCatalog.Types.Should().OnlyContain(type => NotificationCatalog.SurfacesOf(type).Count > 0);
    }

    [Fact]
    public async Task List_ForEachApp_ReturnsOnlyItsOwnNotificationsAsync()
    {
        await using var db = await SeedAsync();
        var service = new UserNotificationService(db, new TestClock());

        var player = await service.ListAsync(_userId, 1, 50, arabic: false, NotificationSurface.Player);
        var venue = await service.ListAsync(_userId, 1, 50, arabic: false, NotificationSurface.Venue);
        var admin = await service.ListAsync(_userId, 1, 50, arabic: false, NotificationSurface.Admin);
        var all = await service.ListAsync(_userId, 1, 50, arabic: false);

        player.Items.Select(n => n.Type).Should().BeEquivalentTo(
            NotificationTypes.BookingReminder2h, NotificationTypes.NewDeviceSignIn, NotificationTypes.SupportTicketReplied);
        venue.Items.Select(n => n.Type).Should().BeEquivalentTo(
            NotificationTypes.PaymentProofSubmitted, NotificationTypes.VenueInfoRequested, NotificationTypes.NewDeviceSignIn, NotificationTypes.SupportTicketReplied);
        admin.Items.Select(n => n.Type).Should().BeEquivalentTo(NotificationTypes.NewDeviceSignIn, NotificationTypes.SupportTicketReplied);
        all.Items.Should().HaveCount(5, "older clients that send no surface keep getting everything");
        venue.TotalCount.Should().Be(4);
        venue.UnreadCount.Should().Be(4);
    }

    [Fact]
    public async Task UnreadCount_AndReadAll_AreScopedToTheAppAsync()
    {
        await using var db = await SeedAsync();
        var service = new UserNotificationService(db, new TestClock());

        (await service.UnreadCountAsync(_userId, NotificationSurface.Player)).Should().Be(3);
        (await service.UnreadCountAsync(_userId, NotificationSurface.Venue)).Should().Be(4);

        (await service.MarkAllReadAsync(_userId, NotificationSurface.Venue)).Should().Be(4);

        (await service.UnreadCountAsync(_userId, NotificationSurface.Venue)).Should().Be(0);
        (await service.UnreadCountAsync(_userId, NotificationSurface.Player)).Should().Be(1, "only the player-only booking reminder is left; the shared ones were read in the venue app");
    }

    [Fact]
    public async Task AnotherUsersNotifications_NeverLeakIntoAnAppAsync()
    {
        await using var db = await SeedAsync();
        db.Notifications.Add(Notification.Create(Guid.NewGuid(), NotificationTypes.PaymentProofSubmitted, NotificationCategory.Payments, new Dictionary<string, string>(), null, "other", _now));
        await db.SaveChangesAsync();

        var venue = await new UserNotificationService(db, new TestClock()).ListAsync(_userId, 1, 50, arabic: false, NotificationSurface.Venue);

        venue.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task UnknownTypes_StayInThePlayerApp_WhereTheyAlwaysAppearedAsync()
    {
        await using var db = NewDb();
        db.Notifications.Add(Notification.Create(_userId, "retired.type", NotificationCategory.Marketing, new Dictionary<string, string>(), null, "old", _now));
        await db.SaveChangesAsync();
        var service = new UserNotificationService(db, new TestClock());

        (await service.ListAsync(_userId, 1, 50, arabic: false, NotificationSurface.Player)).TotalCount.Should().Be(1);
        (await service.ListAsync(_userId, 1, 50, arabic: false, NotificationSurface.Venue)).TotalCount.Should().Be(0);
    }

    private async Task<NotificationsDbContext> SeedAsync()
    {
        var db = NewDb();
        (string Type, NotificationCategory Category)[] rows =
        [
            (NotificationTypes.BookingReminder2h, NotificationCategory.Bookings),
            (NotificationTypes.PaymentProofSubmitted, NotificationCategory.Payments),
            (NotificationTypes.VenueInfoRequested, NotificationCategory.Venues),
            (NotificationTypes.NewDeviceSignIn, NotificationCategory.Security),
            (NotificationTypes.SupportTicketReplied, NotificationCategory.Security)
        ];
        foreach (var (type, category) in rows)
        {
            db.Notifications.Add(Notification.Create(_userId, type, category, new Dictionary<string, string>(), null, $"{type}:1", _now));
        }

        await db.SaveChangesAsync();
        return db;
    }

    private static NotificationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => UtcNow.AddHours(2);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
