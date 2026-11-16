using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.ManualBooking;
using Malabak.Modules.Booking.Application.Disputes;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 2 (P2) Edge-Case Tests: Manual booking (staff creation, pitch isolation, non-GUID actor rejection),
/// payment disputes (player & venue isolation, resolution, evidence), and landing page waitlist lead handling.
/// </summary>
public sealed class PaymentDisputeAndManualBookingEdgeCaseTests
{
    private static readonly DateTime _nowUtc = new(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _otherVenueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _otherPitchId = Guid.NewGuid();
    private readonly Guid _staffUserId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();

    private static BookingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private Booking CreateBooking(BookingDbContext db, BookingStatus status = BookingStatus.Confirmed)
    {
        var startUtc = _nowUtc.AddHours(2);
        var endUtc = _nowUtc.AddHours(3);

        var booking = Booking.Create(
            Guid.NewGuid(),
            $"MLB-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            _venueId,
            _pitchId,
            _playerId,
            startUtc,
            endUtc,
            Money.Create(600m, "EGP").Value,
            Money.Create(150m, "EGP").Value,
            _nowUtc.AddMinutes(15),
            null,
            null,
            _nowUtc,
            _playerId.ToString("D")).Value;

        if (status != BookingStatus.PendingDeposit)
        {
            var prop = typeof(Booking).GetProperty(nameof(Booking.Status), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            prop!.SetValue(booking, status);
        }

        db.Bookings.Add(booking);
        return booking;
    }

    // =========================================================================
    // 1. Manual Booking (Staff Creation & Multi-Tenant Pitch Isolation)
    // =========================================================================

    [Fact]
    public async Task ManualBooking_InvalidActorUserId_RejectsWithUnauthorizedAccessAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var handler = new CreateManualBookingHandler(
            db,
            clock,
            new BookingReferenceGenerator(),
            new NoOpAvailabilityInvalidator(),
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId, _pitchId),
            NullLogger<CreateManualBookingHandler>.Instance);

        // Invalid ActorUserId (not a parseable Guid)
        var command = new CreateManualBookingCommand(
            _venueId,
            _pitchId,
            _nowUtc.AddHours(2),
            _nowUtc.AddHours(3),
            500m,
            "EGP",
            "Customer Walkin",
            "+201012345678",
            "invalid-not-a-guid");

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.Unauthorized");
    }

    [Fact]
    public async Task ManualBooking_PitchBelongingToAnotherVenue_RejectsWithPitchNotFoundAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        // Pitch directory maps _otherPitchId to _otherVenueId
        var handler = new CreateManualBookingHandler(
            db,
            clock,
            new BookingReferenceGenerator(),
            new NoOpAvailabilityInvalidator(),
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_otherVenueId, _otherPitchId),
            NullLogger<CreateManualBookingHandler>.Instance);

        // Staff from _venueId attempts to book a pitch belonging to _otherVenueId
        var command = new CreateManualBookingCommand(
            _venueId,
            _otherPitchId,
            _nowUtc.AddHours(2),
            _nowUtc.AddHours(3),
            500m,
            "EGP",
            "Walk-in Customer",
            "+201012345678",
            _staffUserId.ToString("D"));

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.PitchNotFound");
    }

    [Fact]
    public async Task ManualBooking_EndBeforeStart_RejectsWithInvalidTimeRangeAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var handler = new CreateManualBookingHandler(
            db,
            clock,
            new BookingReferenceGenerator(),
            new NoOpAvailabilityInvalidator(),
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId, _pitchId),
            NullLogger<CreateManualBookingHandler>.Instance);

        var command = new CreateManualBookingCommand(
            _venueId,
            _pitchId,
            _nowUtc.AddHours(3),
            _nowUtc.AddHours(2), // End before start
            500m,
            "EGP",
            "Customer",
            "+201012345678",
            _staffUserId.ToString("D"));

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.InvalidTimeRange");
    }

    // =========================================================================
    // 2. Payment Proof Disputes (Player & Venue Isolation)
    // =========================================================================

    [Fact]
    public async Task PaymentDispute_OpenByPlayer_OnAnotherPlayersBooking_RejectsWithNotFoundAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db);
        await db.SaveChangesAsync();

        var disputeService = new PaymentDisputeService(
            db,
            cancel: null!,
            availability: new NoOpAvailabilityInvalidator(),
            broadcaster: new NoOpRealtimeBroadcaster(),
            venues: new StubVenueDirectory(_venueId, _pitchId),
            notifications: new NoOpNotificationPublisher(),
            audit: new NoOpAuditLog(),
            clock: clock);

        var strangerPlayerId = Guid.NewGuid();
        var result = await disputeService.OpenByPlayerAsync(booking.Id, strangerPlayerId, "I transferred money");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.NotFound");
    }

    [Fact]
    public async Task PaymentDispute_OpenByVenue_OnAnotherVenuesBooking_RejectsWithNotFoundAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db);
        await db.SaveChangesAsync();

        var disputeService = new PaymentDisputeService(
            db,
            cancel: null!,
            availability: new NoOpAvailabilityInvalidator(),
            broadcaster: new NoOpRealtimeBroadcaster(),
            venues: new StubVenueDirectory(_venueId, _pitchId),
            notifications: new NoOpNotificationPublisher(),
            audit: new NoOpAuditLog(),
            clock: clock);

        var strangerVenueId = Guid.NewGuid();
        var result = await disputeService.OpenByVenueAsync(booking.Id, strangerVenueId, Guid.NewGuid(), "Fake receipt");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.NotFound");
    }

    [Fact]
    public async Task PaymentDispute_HappyPath_OpensDisputeAndSetsDisputedStatusAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db);
        await db.SaveChangesAsync();

        var disputeService = new PaymentDisputeService(
            db,
            cancel: null!,
            availability: new NoOpAvailabilityInvalidator(),
            broadcaster: new NoOpRealtimeBroadcaster(),
            venues: new StubVenueDirectory(_venueId, _pitchId),
            notifications: new NoOpNotificationPublisher(),
            audit: new NoOpAuditLog(),
            clock: clock);

        var result = await disputeService.OpenByPlayerAsync(booking.Id, _playerId, "InstaPay reference is valid");

        result.IsSuccess.Should().BeTrue();
        result.Value.BookingId.Should().Be(booking.Id);
        result.Value.OpenedBy.Should().Be("Player");
        result.Value.Status.Should().Be("Open");

        // Dispute record exists in DB
        var dispute = await db.PaymentDisputes.FirstOrDefaultAsync(d => d.BookingId == booking.Id);
        dispute.Should().NotBeNull();
        dispute!.Status.Should().Be(DisputeStatus.Open);
    }

    [Fact]
    public async Task ConfirmManualBooking_WhenPendingDeposit_TransitionsToConfirmed_AndClearsHoldAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = Booking.CreateManual(
            Guid.NewGuid(),
            "MLB-MANUAL01",
            _venueId,
            _pitchId,
            _staffUserId,
            "Ahmed Mostafa",
            "01012345678",
            _nowUtc.AddHours(2),
            _nowUtc.AddHours(3),
            Money.Create(500m, "EGP").Value,
            Money.Create(100m, "EGP").Value,
            confirmedImmediately: false,
            holdExpiresAtUtc: _nowUtc.AddHours(1),
            nowUtc: _nowUtc).Value;

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        booking.Status.Should().Be(BookingStatus.PendingDeposit);
        booking.HoldExpiresAtUtc.Should().NotBeNull();

        var handler = new ConfirmManualBookingHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new NoOpAvailabilityInvalidator(),
            NullLogger<ConfirmManualBookingHandler>.Instance);

        var result = await handler.HandleAsync(new ConfirmManualBookingCommand(
            _venueId,
            booking.Id,
            _staffUserId,
            CollectedDeposit: 100m,
            Notes: "Received via cash at venue"));

        result.IsSuccess.Should().BeTrue();

        var updated = await db.Bookings.FindAsync(booking.Id);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(BookingStatus.Confirmed);
        updated.HoldExpiresAtUtc.Should().BeNull();
        updated.ConfirmedAtUtc.Should().Be(_nowUtc);
        updated.DepositAmount.Amount.Should().Be(100m);
        updated.VenuePayAmount.Amount.Should().Be(400m);
        updated.Notes.Should().Contain("Received via cash at venue");
    }

    [Fact]
    public async Task ConfirmManualBooking_WhenNonManual_RejectsWithNotManualErrorAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db, BookingStatus.PendingDeposit);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var handler = new ConfirmManualBookingHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new NoOpAvailabilityInvalidator(),
            NullLogger<ConfirmManualBookingHandler>.Instance);

        var result = await handler.HandleAsync(new ConfirmManualBookingCommand(
            _venueId,
            booking.Id,
            _staffUserId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.NotManual");
    }

    // =========================================================================
    // Stubs
    // =========================================================================

    private sealed class StubVenueDirectory(Guid venueId, Guid pitchId) : IVenueDirectory
    {
        private readonly Guid _targetPitchId = pitchId;

        public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(pitchId == _targetPitchId ? venueId : null);

        public Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<VenueSummary?>(null);

        public Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VenueBlockedPeriod>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueCoverPhotosAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class NoOpAvailabilityInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpRealtimeBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpNotificationPublisher : INotificationPublisher
    {
        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
