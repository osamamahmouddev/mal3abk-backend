using FluentAssertions;
using Malabak.Modules.Notifications.Application;
using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.Modules.Support.Domain;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class NotificationAndSupportEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc));

    private static NotificationsDbContext CreateNotificationsDb()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NotificationsDbContext(options);
    }

    [Fact]
    public void NotificationPreference_SecurityCategory_CannotBeDisabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var pref = NotificationPreference.Create(userId, NotificationCategory.Security, inApp: true, push: true, _clock.UtcNow);

        // Act: User attempts to disable security alerts
        pref.Update(inApp: false, push: false, _clock.UtcNow);

        // Assert: Security invariant - security alerts remain forced on
        pref.InAppEnabled.Should().BeTrue();
        pref.PushEnabled.Should().BeTrue();
    }

    [Fact]
    public void NotificationPreference_Defaults_MarketingIsOptIn_OthersAreOptOut()
    {
        // Assert
        NotificationPreference.Defaults(NotificationCategory.Marketing).Should().Be((false, false));
        NotificationPreference.Defaults(NotificationCategory.Bookings).Should().Be((true, true));
        NotificationPreference.Defaults(NotificationCategory.Payments).Should().Be((true, true));
        NotificationPreference.Defaults(NotificationCategory.Security).Should().Be((true, true));
        NotificationPreference.Defaults(NotificationCategory.Teams).Should().Be((true, true));
        NotificationPreference.Defaults(NotificationCategory.Venues).Should().Be((true, true));
    }

    [Fact]
    public async Task NotificationDispatcher_DeduplicatesIdenticalDedupeKeyAsync()
    {
        // Arrange
        using var db = CreateNotificationsDb();
        var userId = Guid.NewGuid();
        var dedupeKey = $"booking-confirmed-{Guid.NewGuid():N}";

        var msg1 = NotificationOutboxMessage.Create(
            userId,
            NotificationTypes.BookingConfirmed,
            new Dictionary<string, string> { ["venueName"] = "Al Ahly", ["bookingId"] = Guid.NewGuid().ToString() },
            dedupeKey,
            _clock.UtcNow);

        var msg2 = NotificationOutboxMessage.Create(
            userId,
            NotificationTypes.BookingConfirmed,
            new Dictionary<string, string> { ["venueName"] = "Al Ahly", ["bookingId"] = Guid.NewGuid().ToString() },
            dedupeKey,
            _clock.UtcNow);

        db.Outbox.AddRange(msg1, msg2);
        await db.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(db, _clock, NullLogger<NotificationDispatcher>.Instance);

        // Act
        var processedCount = await dispatcher.ProcessPendingAsync();

        // Assert: Both messages processed, but only 1 notification generated in notifications table
        processedCount.Should().Be(2);

        var notifications = await db.Notifications.Where(n => n.UserId == userId).ToListAsync();
        notifications.Should().ContainSingle();
        notifications[0].DedupeKey.Should().Be(dedupeKey);
    }

    [Fact]
    public async Task UserNotificationService_OtherUserCannotMarkNotificationAsReadAsync()
    {
        // Arrange
        using var db = CreateNotificationsDb();
        var legitimateUserId = Guid.NewGuid();
        var strangerUserId = Guid.NewGuid();

        var notification = Notification.Create(
            legitimateUserId,
            NotificationTypes.BookingConfirmed,
            NotificationCategory.Bookings,
            new Dictionary<string, string> { ["venueName"] = "Cairo Club" },
            "/bookings/1",
            "dedupe-1",
            _clock.UtcNow);

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var service = new UserNotificationService(db, _clock);

        // Act 1: Stranger tries to mark read
        var strangerResult = await service.MarkReadAsync(strangerUserId, notification.Id);
        // Act 2: Owner marks read
        var ownerResult = await service.MarkReadAsync(legitimateUserId, notification.Id);

        // Assert: Stranger denied, owner succeeds
        strangerResult.Should().BeFalse();
        ownerResult.Should().BeTrue();

        var updated = await db.Notifications.FirstAsync(n => n.Id == notification.Id);
        updated.ReadAtUtc.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void SupportTicket_Open_VenueComplaintWithoutVenueId_ShouldFail()
    {
        // Act: Open venue complaint ticket without specifying relatedVenueId
        var result = SupportTicket.Open(
            requesterId: Guid.NewGuid(),
            kind: TicketRequesterKind.Player,
            category: TicketCategory.VenueComplaint,
            subject: "Lighting issue on pitch",
            body: "The floodlights turned off 15 minutes before time",
            relatedBookingId: null,
            relatedVenueId: null, // Missing mandatory venue ID
            relatedUserId: null,
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SupportErrors.VenueRequired);
    }

    [Theory]
    [InlineData(TicketCategory.Deposit, TicketPriority.High)]
    [InlineData(TicketCategory.Refund, TicketPriority.High)]
    [InlineData(TicketCategory.Booking, TicketPriority.Normal)]
    [InlineData(TicketCategory.Account, TicketPriority.Normal)]
    [InlineData(TicketCategory.Other, TicketPriority.Normal)]
    public void SupportTicket_DefaultPriority_PrioritizesFinancialDisputes(TicketCategory category, TicketPriority expectedPriority)
    {
        var priority = SupportTicket.DefaultPriority(category);
        priority.Should().Be(expectedPriority);
    }

    [Fact]
    public void SupportTicket_ClosedTicket_RejectsNewReplies()
    {
        // Arrange
        var requesterId = Guid.NewGuid();
        var ticket = SupportTicket.Open(
            requesterId,
            TicketRequesterKind.Player,
            TicketCategory.Booking,
            "Slot question",
            "Can I change the pitch?",
            null, null, null, _clock.UtcNow).Value;

        ticket.Close("Issue fully resolved and agreed on phone", _clock.UtcNow);
        ticket.Status.Should().Be(TicketStatus.Closed);

        // Act: Requester attempts to reply to closed ticket
        var replyResult = ticket.RequesterReply(requesterId, "Another question", _clock.UtcNow);

        // Assert
        replyResult.IsFailure.Should().BeTrue();
        replyResult.Error.Should().Be(SupportErrors.Closed);
    }

    [Fact]
    public void SupportTicket_ResolvedTicket_ReopensWhenRequesterReplies()
    {
        // Arrange
        var requesterId = Guid.NewGuid();
        var ticket = SupportTicket.Open(
            requesterId,
            TicketRequesterKind.Player,
            TicketCategory.Booking,
            "Deposit question",
            "Did you receive my transfer?",
            null, null, null, _clock.UtcNow).Value;

        ticket.Resolve("Deposit has been verified by finance", _clock.UtcNow);
        ticket.Status.Should().Be(TicketStatus.Resolved);

        // Act: Requester replies after resolution
        var replyResult = ticket.RequesterReply(requesterId, "Actually I was charged twice", _clock.UtcNow);

        // Assert: Reopens to Open when unassigned
        replyResult.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void SupportTicket_Resolve_WithoutReason_ShouldFail()
    {
        // Arrange
        var ticket = SupportTicket.Open(
            Guid.NewGuid(),
            TicketRequesterKind.Player,
            TicketCategory.Account,
            "Name change",
            "Please fix typo in my name",
            null, null, null, _clock.UtcNow).Value;

        // Act: Resolve without reason
        var result = ticket.Resolve("", _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SupportErrors.ReasonRequired);
    }
}
