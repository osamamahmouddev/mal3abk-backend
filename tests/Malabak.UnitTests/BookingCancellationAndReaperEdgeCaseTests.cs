using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Bookings.ReviewProof;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Booking.Infrastructure.Jobs;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 2 (P2) Edge-Case Tests: Review proof, hold reaper job boundaries (<= nowUtc),
/// player & venue cancellation, cancel preview equivalence, and double action guards.
/// </summary>
public sealed class BookingCancellationAndReaperEdgeCaseTests
{
    private static readonly DateTime _nowUtc = new(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _otherVenueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();

    private static BookingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private Booking CreateBooking(
        BookingDbContext db,
        BookingStatus status = BookingStatus.PendingDeposit,
        DateTime? holdExpiresAtUtc = null,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        int cancellationLeadHours = 24,
        decimal total = 600m,
        decimal deposit = 200m)
    {
        var start = startUtc ?? _nowUtc.AddHours(48);
        var end = endUtc ?? _nowUtc.AddHours(49);
        var hold = holdExpiresAtUtc ?? _nowUtc.AddMinutes(15);

        var policySnapshot = new BookingPolicySnapshot(
            Guid.NewGuid(),
            _venueId,
            1,
            "Fixed",
            deposit,
            15,
            120,
            cancellationLeadHours,
            "FullRefund",
            100m,
            "ForfeitDeposit",
            15,
            true,
            24,
            true,
            "Cancellation policy",
            _nowUtc);

        var booking = Booking.Create(
            Guid.NewGuid(),
            $"MLB-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            _venueId,
            _pitchId,
            _playerId,
            start,
            end,
            Money.Create(total, "EGP").Value,
            Money.Create(deposit, "EGP").Value,
            hold,
            null,
            policySnapshot,
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
    // 1. ExpireBookingHoldsJob Exact Boundary Testing
    // =========================================================================

    [Fact]
    public async Task ExpireBookingHoldsJob_ChecksExactExpirationBoundaryAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        // Booking A: Expired 1 second ago (nowUtc - 1s) -> MUST EXPIRE
        var bookingA = CreateBooking(db, BookingStatus.PendingDeposit, holdExpiresAtUtc: _nowUtc.AddSeconds(-1));

        // Booking B: Expires exactly now (nowUtc) -> MUST EXPIRE (code uses b.HoldExpiresAtUtc <= nowUtc)
        var bookingB = CreateBooking(db, BookingStatus.PendingDeposit, holdExpiresAtUtc: _nowUtc);

        // Booking C: Expires 1 second in future (nowUtc + 1s) -> MUST NOT EXPIRE
        var bookingC = CreateBooking(db, BookingStatus.PendingDeposit, holdExpiresAtUtc: _nowUtc.AddSeconds(1));

        await db.SaveChangesAsync();

        var reaper = new ExpireBookingHoldsJob(
            db,
            clock,
            new NoOpAvailabilityInvalidator(),
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId),
            NullLogger<ExpireBookingHoldsJob>.Instance);

        await reaper.ExecuteRecurringSweepAsync();

        // Reload bookings
        var reloadedA = await db.Bookings.FindAsync(bookingA.Id);
        var reloadedB = await db.Bookings.FindAsync(bookingB.Id);
        var reloadedC = await db.Bookings.FindAsync(bookingC.Id);

        reloadedA!.Status.Should().Be(BookingStatus.Expired, "holds past now must expire");
        reloadedB!.Status.Should().Be(BookingStatus.Expired, "holds at exactly nowUtc expire under <= operator");
        reloadedC!.Status.Should().Be(BookingStatus.PendingDeposit, "holds 1s in future must NOT expire");
    }

    [Fact]
    public async Task ExpireBookingHoldsJob_RunningTwiceInARow_IsIdempotentAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db, BookingStatus.PendingDeposit, holdExpiresAtUtc: _nowUtc.AddMinutes(-5));
        await db.SaveChangesAsync();

        var reaper = new ExpireBookingHoldsJob(
            db,
            clock,
            new NoOpAvailabilityInvalidator(),
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId),
            NullLogger<ExpireBookingHoldsJob>.Instance);

        // 1st run
        await reaper.ExecuteRecurringSweepAsync();
        (await db.Bookings.FindAsync(booking.Id))!.Status.Should().Be(BookingStatus.Expired);

        // 2nd run immediately
        await reaper.ExecuteRecurringSweepAsync();
        (await db.Bookings.FindAsync(booking.Id))!.Status.Should().Be(BookingStatus.Expired);
    }

    // =========================================================================
    // 2. Review Proof (Approve / Reject) & Multi-Tenant Isolation
    // =========================================================================

    [Fact]
    public async Task ReviewProof_ApproveAndReject_HandlesDecisionsAndRejectsDoubleReviewAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);
        var staffId = Guid.NewGuid();

        var booking = CreateBooking(db, BookingStatus.PendingDeposit);
        booking.SubmitProof(Guid.NewGuid(), _playerId.ToString("D"), _nowUtc);
        await db.SaveChangesAsync();

        var handler = new ReviewProofHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new NoOpAvailabilityInvalidator(),
            new StubVenueDirectory(_venueId),
            NullLogger<ReviewProofHandler>.Instance);

        // Approve proof
        var approveResult = await handler.HandleAsync(new ReviewProofCommand(booking.Id, _venueId, staffId, Approved: true));
        approveResult.IsSuccess.Should().BeTrue();

        var approvedBooking = await db.Bookings.FindAsync(booking.Id);
        approvedBooking!.Status.Should().Be(BookingStatus.Confirmed);
        approvedBooking.HoldExpiresAtUtc.Should().BeNull();

        // Second review attempt on already confirmed booking must fail
        var secondReview = await handler.HandleAsync(new ReviewProofCommand(booking.Id, _venueId, staffId, Approved: false, "Too late"));
        secondReview.IsFailure.Should().BeTrue();
        secondReview.Error.Code.Should().Be("Booking.InvalidTransition");
    }

    [Fact]
    public async Task ReviewProof_StaffFromAnotherVenue_RejectsWithNotFoundAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);
        var booking = CreateBooking(db, BookingStatus.PendingDeposit);
        booking.SubmitProof(Guid.NewGuid(), _playerId.ToString("D"), _nowUtc);
        await db.SaveChangesAsync();

        var handler = new ReviewProofHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new NoOpAvailabilityInvalidator(),
            new StubVenueDirectory(_otherVenueId),
            NullLogger<ReviewProofHandler>.Instance);

        // Staff from _otherVenueId attempts to review booking belonging to _venueId
        var result = await handler.HandleAsync(new ReviewProofCommand(booking.Id, _otherVenueId, Guid.NewGuid(), Approved: true));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.NotFound");
    }

    // =========================================================================
    // 3. Cancellation Equivalence (Preview vs Actual) & Policy Enforcement
    // =========================================================================

    [Fact]
    public async Task CancelPreview_ReturnsIdenticalValuesAsActualCancelAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db, BookingStatus.Confirmed, startUtc: _nowUtc.AddHours(48), cancellationLeadHours: 24, deposit: 200m);
        await db.SaveChangesAsync();

        var previewHandler = new GetCancelPreviewHandler(db, clock);
        var previewResult = await previewHandler.HandleAsync(new GetCancelPreviewQuery(booking.Id, _playerId));

        previewResult.IsSuccess.Should().BeTrue();
        previewResult.Value.CanCancel.Should().BeTrue();
        previewResult.Value.RefundPercentage.Should().Be(100);
        previewResult.Value.EstimatedRefundAmount.Should().Be(200m);

        // Execute actual cancel
        var cancelHandler = new CancelBookingHandler(
            db, clock, new NoOpAvailabilityInvalidator(), new NoOpRealtimeBroadcaster(), NullLogger<CancelBookingHandler>.Instance);

        var cancelResult = await cancelHandler.HandleAsync(new CancelBookingCommand(booking.Id, _playerId, "Can no longer make it"));
        cancelResult.IsSuccess.Should().BeTrue();

        // Verify created refund matches preview
        var refund = await db.BookingRefunds.SingleOrDefaultAsync(r => r.BookingId == booking.Id);
        refund.Should().NotBeNull();
        refund!.ExpectedRefundAmount.Should().Be(previewResult.Value.EstimatedRefundAmount);
    }

    [Fact]
    public async Task Cancel_ByPlayer_AfterKickoff_RejectsCancellationAsync()
    {
        await using var db = CreateDbContext();
        var kickoff = _nowUtc.AddHours(1);
        var clock = new FakeClock(kickoff.AddMinutes(5)); // 5 minutes past kickoff

        var booking = CreateBooking(db, BookingStatus.Confirmed, startUtc: kickoff, endUtc: kickoff.AddHours(1));
        await db.SaveChangesAsync();

        var cancelHandler = new CancelBookingHandler(
            db, clock, new NoOpAvailabilityInvalidator(), new NoOpRealtimeBroadcaster(), NullLogger<CancelBookingHandler>.Instance);

        var result = await cancelHandler.HandleAsync(new CancelBookingCommand(booking.Id, _playerId, "Match started already"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.CancellationDeadlinePassed");
    }

    [Fact]
    public async Task Cancel_VenueCancelBooking_GrantsFull100PercentRefundAtAnyTimeAsync()
    {
        await using var db = CreateDbContext();
        var kickoff = _nowUtc.AddHours(2);
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db, BookingStatus.Confirmed, startUtc: kickoff, endUtc: kickoff.AddHours(1), deposit: 250m);
        await db.SaveChangesAsync();

        var venueCancelHandler = new VenueCancelBookingHandler(
            db, clock, new NoOpAvailabilityInvalidator(), new NoOpRealtimeBroadcaster(), NullLogger<VenueCancelBookingHandler>.Instance);

        var staffUserId = Guid.NewGuid();
        var result = await venueCancelHandler.HandleAsync(new VenueCancelBookingCommand(booking.Id, _venueId, staffUserId, "Floodlight power failure"));

        result.IsSuccess.Should().BeTrue();

        var cancelledBooking = await db.Bookings.FindAsync(booking.Id);
        cancelledBooking!.Status.Should().Be(BookingStatus.Cancelled);

        var refund = await db.BookingRefunds.SingleOrDefaultAsync(r => r.BookingId == booking.Id);
        refund.Should().NotBeNull();
        refund!.ExpectedRefundAmount.Should().Be(250m, "venue cancellation must guarantee 100% refund of deposit");
        refund.Status.Should().Be(RefundStatus.Pending);
    }

    [Fact]
    public async Task Cancel_DoubleCancellation_RejectsWithInvalidTransitionAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db, BookingStatus.Confirmed, startUtc: _nowUtc.AddHours(48));
        await db.SaveChangesAsync();

        var cancelHandler = new CancelBookingHandler(
            db, clock, new NoOpAvailabilityInvalidator(), new NoOpRealtimeBroadcaster(), NullLogger<CancelBookingHandler>.Instance);

        var firstCancel = await cancelHandler.HandleAsync(new CancelBookingCommand(booking.Id, _playerId, "First cancel"));
        firstCancel.IsSuccess.Should().BeTrue();

        var secondCancel = await cancelHandler.HandleAsync(new CancelBookingCommand(booking.Id, _playerId, "Second cancel"));
        secondCancel.IsFailure.Should().BeTrue();
        secondCancel.Error.Code.Should().Be("Booking.InvalidTransition");
    }

    // =========================================================================
    // Stubs
    // =========================================================================

    private sealed class StubVenueDirectory(Guid venueId) : IVenueDirectory
    {
        public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(venueId);

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
}
