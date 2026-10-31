using System.Collections.Concurrent;
using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.ReviewProof;
using Malabak.Modules.Booking.Application.Bookings.SubmitProof;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Venue staff reviewing a payment proof (TASK-035) against a real PostgreSQL: who decided and why is recorded correctly,
/// nobody is told about a decision that did not save, only the booking's own venue can decide, and a second reviewer or a
/// slot taken in the meantime is handled as a conflict rather than a crash.
/// </summary>
public sealed class ReviewProofTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 5, 8, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public ReviewProofTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Approving_ConfirmsTheBookingAndRecordsWhoApprovedAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();
        var staffId = Guid.NewGuid();

        var result = await ReviewAsync(new ReviewProofCommand(bookingId, venueId, staffId, Approved: true));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.Include(b => b.Proofs).Include(b => b.History).SingleAsync(b => b.Id == bookingId);
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.Proofs.Single().IsApproved.Should().BeTrue();
        booking.Proofs.Single().ReviewedByUserId.Should().Be(staffId.ToString("D"));
        booking.History.Single(h => h.ToStatus == BookingStatus.Confirmed).ChangedByUserId.Should().Be(staffId.ToString("D"));
    }

    [Fact]
    public async Task Rejecting_StoresTheReasonAsTheReasonAndTheStaffMemberAsTheActorAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();
        var staffId = Guid.NewGuid();
        const string reason = "The receipt is for a different amount";

        var result = await ReviewAsync(new ReviewProofCommand(bookingId, venueId, staffId, Approved: false, reason));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.Include(b => b.Proofs).Include(b => b.History).SingleAsync(b => b.Id == bookingId);
        booking.Status.Should().Be(BookingStatus.Rejected);
        booking.RejectionReason.Should().Be(reason, "the player is shown this text");
        booking.Proofs.Single().RejectionReason.Should().Be(reason);
        var entry = booking.History.Single(h => h.ToStatus == BookingStatus.Rejected);
        entry.ChangedByUserId.Should().Be(staffId.ToString("D"), "the audit trail names the person, not the reason text");
        entry.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task ALongRejectionReason_IsAcceptedUpToTheColumnLengthAndRefusedBeyondItAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();
        var (otherId, otherVenue) = await SaveBookingWithProofAsync();

        var atLimit = await ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), false, new string('x', ReviewProofHandler.MaxRejectionReasonLength)));
        var tooLong = await ReviewAsync(new ReviewProofCommand(otherId, otherVenue, Guid.NewGuid(), false, new string('x', ReviewProofHandler.MaxRejectionReasonLength + 1)));

        atLimit.IsSuccess.Should().BeTrue();
        tooLong.Error.Code.Should().Be("Booking.RejectionReasonTooLong");
    }

    [Fact]
    public async Task ARejectionWithoutAReason_IsRefusedAndNothingChangesAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();

        var result = await ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), false, "   "));

        result.IsFailure.Should().BeTrue();
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.ProofSubmitted);
    }

    [Fact]
    public async Task AnotherVenuesBooking_LooksLikeItDoesNotExistAsync()
    {
        var (bookingId, _) = await SaveBookingWithProofAsync();

        var result = await ReviewAsync(new ReviewProofCommand(bookingId, Guid.NewGuid(), Guid.NewGuid(), Approved: true));

        result.Error.Should().Be(BookingErrors.NotFound);
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.ProofSubmitted);
    }

    [Fact]
    public async Task ThePlayerIsNotified_OnlyAfterTheDecisionIsSavedAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();
        var notifications = new RecordingPublisher(_database, bookingId);

        await ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), Approved: true), notifications);

        notifications.StatusWhenPublished.Should().ContainSingle().Which.Should().Be(BookingStatus.Confirmed, "the notification must not go out before the decision is stored");
    }

    [Fact]
    public async Task TheDecisionsEvent_IsStoredInTheOutboxWithTheDecisionAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();

        var result = await ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), Approved: true), withOutbox: true);

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var messages = await verify.OutboxMessages.Where(m => m.Content.Contains(bookingId.ToString())).ToListAsync();
        messages.Should().Contain(m => m.Type.Contains("BookingConfirmedDomainEvent"), "an event that is only dispatched in memory is lost if the process stops after the save");
    }

    [Fact]
    public async Task ABrokenSideEffect_DoesNotUndoOrFailTheDecisionAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();

        var result = await ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), Approved: true), new ThrowingPublisher());

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task TwoReviewersDecidingAtOnce_OneWinsAndTheOtherGetsAConflictAsync()
    {
        var (bookingId, venueId) = await SaveBookingWithProofAsync();

        var results = await Task.WhenAll(
            Task.Run(() => ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), Approved: true))),
            Task.Run(() => ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), Approved: false, "Not readable"))));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task AProofResubmittedAfterRejection_ConflictsIfSomeoneTookTheSlotMeanwhileAsync()
    {
        var pitchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var (bookingId, venueId) = await SaveBookingWithProofAsync(pitchId, playerId);
        (await ReviewAsync(new ReviewProofCommand(bookingId, venueId, Guid.NewGuid(), false, "Wrong amount"))).IsSuccess.Should().BeTrue();

        // A rejected booking no longer holds the slot in the database, so another player can take it.
        await using (var taken = _database.CreateContext())
        {
            taken.Bookings.Add(new BookingBuilder().ForPitch(pitchId).WithTimeSlot(_start, _start.AddHours(1)).Build());
            await taken.SaveChangesAsync();
        }

        var resubmit = await SubmitAsync(bookingId, playerId);

        resubmit.IsFailure.Should().BeTrue();
        resubmit.Error.Should().Be(BookingErrors.SlotOverlap);
    }

    private async Task<(Guid BookingId, Guid VenueId)> SaveBookingWithProofAsync(Guid? pitchId = null, Guid? playerId = null)
    {
        var venueId = Guid.NewGuid();
        var player = playerId ?? Guid.NewGuid();
        var booking = new BookingBuilder().ForVenue(venueId).ForPlayer(player).ForPitch(pitchId ?? Guid.NewGuid()).WithTimeSlot(_start, _start.AddHours(1)).Build();
        var proofId = Guid.NewGuid();
        booking.SubmitProof(proofId, player.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        booking.AddProof(PaymentProof.Create(proofId, booking.Id, Guid.NewGuid(), "media/key", "proof.webp", "image/webp", 1234, Convert.ToHexString(Guid.NewGuid().ToByteArray()), player.ToString("D"), DateTime.UtcNow));

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return (booking.Id, venueId);
    }

    private async Task<Result> ReviewAsync(ReviewProofCommand command, INotificationPublisher? publisher = null, bool withOutbox = false)
    {
        await using var context = _database.CreateContext(withOutbox ? new SystemClock() : null);
        var handler = new ReviewProofHandler(
            context, new FakeClock(DateTime.UtcNow), new QuietBroadcaster(), new QuietInvalidator(),
            NullLogger<ReviewProofHandler>.Instance, publisher);
        return await handler.HandleAsync(command);
    }

    private async Task<Result> SubmitAsync(Guid bookingId, Guid playerId)
    {
        await using var context = _database.CreateContext();
        var handler = new SubmitProofHandler(
            context, new FakeClock(DateTime.UtcNow), new QuietBroadcaster(), NullLogger<SubmitProofHandler>.Instance);
        return await handler.HandleAsync(new SubmitProofCommand(
            bookingId, playerId, Guid.NewGuid(), "media/key", "proof.webp", "image/webp", 1234, Convert.ToHexString(Guid.NewGuid().ToByteArray())));
    }

    private sealed class QuietInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }


    private sealed class QuietBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingPublisher : INotificationPublisher
    {
        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The notification service is down");
    }

    /// <summary>On publish, reads the booking from the database to see what had been saved by then.</summary>
    private sealed class RecordingPublisher(BookingDatabaseFixture database, Guid bookingId) : INotificationPublisher
    {
        private readonly ConcurrentQueue<BookingStatus> _seen = new();

        public IReadOnlyCollection<BookingStatus> StatusWhenPublished => _seen;

        public async Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            await using var context = database.CreateContext();
            _seen.Enqueue((await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId, cancellationToken)).Status);
        }
    }
}
