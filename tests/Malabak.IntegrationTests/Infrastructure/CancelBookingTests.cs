using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// A player cancelling a booking (TASK-037) against a real PostgreSQL: only the owner can, a reason is required, the slot is
/// freed and only announced once the cancellation is saved, an expected refund is recorded when a deposit was involved, and
/// a simultaneous change is a conflict rather than a crash.
/// </summary>
public sealed class CancelBookingTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 6, 9, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public CancelBookingTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task APendingBooking_IsCancelledAndItsSlotCanBeBookedAgainAsync()
    {
        var pitchId = Guid.NewGuid();
        var (bookingId, playerId) = await SaveBookingAsync(pitchId);

        var result = await CancelAsync(new CancelBookingCommand(bookingId, playerId, "Plans changed"));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.Include(b => b.History).SingleAsync(b => b.Id == bookingId);
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancellationReason.Should().Be("Plans changed");
        booking.History.Single(h => h.ToStatus == BookingStatus.Cancelled).ChangedByUserId.Should().Be(playerId.ToString("D"));
        (await verify.BookingRefunds.CountAsync(r => r.BookingId == bookingId)).Should().Be(0, "no deposit was submitted, so nothing is owed back");

        var rebook = async () =>
        {
            await using var context = _database.CreateContext();
            context.Bookings.Add(new BookingBuilder().ForPitch(pitchId).WithTimeSlot(_start, _start.AddHours(1)).Build());
            await context.SaveChangesAsync();
        };
        await rebook.Should().NotThrowAsync("the cancelled booking no longer holds the slot");
    }

    [Fact]
    public async Task CancellingAfterTheDepositProofWasSent_RecordsAnExpectedRefundAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync(withProof: true);

        var result = await CancelAsync(new CancelBookingCommand(bookingId, playerId, "Cannot make it"));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var refund = await verify.BookingRefunds.SingleAsync(r => r.BookingId == bookingId);
        refund.ExpectedRefundAmount.Should().BeGreaterThan(0);
        refund.Reason.Should().Be("Cannot make it");
    }

    [Fact]
    public async Task CancellingWhileTheProofIsUnderReview_AlsoRecordsAnExpectedRefundAsync()
    {
        var playerId = Guid.NewGuid();
        var booking = new BookingBuilder().ForPlayer(playerId).WithTimeSlot(_start, _start.AddHours(1)).Build();
        var proofId = Guid.NewGuid();
        booking.SubmitProof(proofId, playerId.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        booking.AddProof(PaymentProof.Create(proofId, booking.Id, Guid.NewGuid(), "media/key", "proof.webp", "image/webp", 1234, Convert.ToHexString(Guid.NewGuid().ToByteArray()), playerId.ToString("D"), DateTime.UtcNow));
        booking.MarkUnderReview("staff", DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await using (var save = _database.CreateContext())
        {
            save.Bookings.Add(booking);
            await save.SaveChangesAsync();
        }

        var result = await CancelAsync(new CancelBookingCommand(booking.Id, playerId, "Cannot make it"));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        (await verify.BookingRefunds.CountAsync(r => r.BookingId == booking.Id)).Should().Be(1, "the deposit proof was already sent, so a refund may be owed");
    }

    [Fact]
    public async Task SomeoneElsesBooking_LooksLikeItDoesNotExistAndIsLeftAloneAsync()
    {
        var (bookingId, _) = await SaveBookingAsync();

        var result = await CancelAsync(new CancelBookingCommand(bookingId, Guid.NewGuid(), "Not mine"));

        result.Error.Should().Be(BookingErrors.NotFound);
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.PendingDeposit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ACancellationNeedsAReason_AndOneThatIsTooLongIsRefusedAsync(string? reason)
    {
        var (bookingId, playerId) = await SaveBookingAsync();

        var missing = await CancelAsync(new CancelBookingCommand(bookingId, playerId, reason));
        var tooLong = await CancelAsync(new CancelBookingCommand(bookingId, playerId, new string('x', CancelBookingHandler.MaxReasonLength + 1)));

        missing.Error.Should().Be(BookingErrors.ReasonRequired);
        tooLong.Error.Code.Should().Be("Booking.CancellationReasonTooLong");
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.PendingDeposit);
    }

    [Fact]
    public async Task TheSlotIsAnnouncedFree_OnlyAfterTheCancellationIsSavedAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();
        var broadcaster = new StatusAtBroadcastRecorder(_database, bookingId);

        await CancelAsync(new CancelBookingCommand(bookingId, playerId, "Plans changed"), broadcaster);

        broadcaster.StatusSeen.Should().NotBeEmpty();
        broadcaster.StatusSeen.Should().OnlyContain(s => s == BookingStatus.Cancelled, "nothing may be announced before the cancellation is stored");
    }

    [Fact]
    public async Task ABrokenSideEffect_DoesNotFailTheCancellationAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();

        var result = await CancelAsync(new CancelBookingCommand(bookingId, playerId, "Plans changed"), new StatusAtBroadcastRecorder(_database, bookingId, throwOnUpdate: true));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task TwoCancellationsAtOnce_NeverCrashAndLeaveOneRefundAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync(withProof: true);

        var results = await Task.WhenAll(
            Task.Run(() => CancelAsync(new CancelBookingCommand(bookingId, playerId, "First"))),
            Task.Run(() => CancelAsync(new CancelBookingCommand(bookingId, playerId, "Second"))));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Type.Should().Be(ErrorType.Conflict);
        await using var verify = _database.CreateContext();
        (await verify.BookingRefunds.CountAsync(r => r.BookingId == bookingId)).Should().Be(1, "a double cancel must not record two refunds");
    }

    private async Task<(Guid BookingId, Guid PlayerId)> SaveBookingAsync(Guid? pitchId = null, bool withProof = false)
    {
        var playerId = Guid.NewGuid();
        var booking = new BookingBuilder().ForPlayer(playerId).ForPitch(pitchId ?? Guid.NewGuid()).WithTimeSlot(_start, _start.AddHours(1)).Build();
        if (withProof)
        {
            var proofId = Guid.NewGuid();
            booking.SubmitProof(proofId, playerId.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
            booking.AddProof(PaymentProof.Create(proofId, booking.Id, Guid.NewGuid(), "media/key", "proof.webp", "image/webp", 1234, Convert.ToHexString(Guid.NewGuid().ToByteArray()), playerId.ToString("D"), DateTime.UtcNow));
        }

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return (booking.Id, playerId);
    }

    private async Task<Result> CancelAsync(CancelBookingCommand command, IBookingRealtimeBroadcaster? broadcaster = null)
    {
        await using var context = _database.CreateContext();
        var handler = new CancelBookingHandler(
            context, new FakeClock(DateTime.UtcNow), new QuietInvalidator(), broadcaster ?? new StatusAtBroadcastRecorder(_database, Guid.Empty), NullLogger<CancelBookingHandler>.Instance);
        return await handler.HandleAsync(command);
    }

    private sealed class QuietInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>On each broadcast, reads the booking from the database to see what had been saved by then.</summary>
    private sealed class StatusAtBroadcastRecorder(BookingDatabaseFixture database, Guid bookingId, bool throwOnUpdate = false) : IBookingRealtimeBroadcaster
    {
        private readonly List<BookingStatus> _seen = [];

        public IReadOnlyCollection<BookingStatus> StatusSeen => _seen;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) =>
            RecordAsync(cancellationToken);

        public async Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
        {
            await RecordAsync(cancellationToken);
            if (throwOnUpdate)
            {
                throw new InvalidOperationException("SignalR is down");
            }
        }

        private async Task RecordAsync(CancellationToken cancellationToken)
        {
            if (bookingId == Guid.Empty)
            {
                return;
            }

            await using var context = database.CreateContext();
            var status = (await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId, cancellationToken)).Status;
            lock (_seen)
            {
                _seen.Add(status);
            }
        }
    }
}
