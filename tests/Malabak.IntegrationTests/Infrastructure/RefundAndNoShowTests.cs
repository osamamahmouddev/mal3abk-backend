using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.NoShow;
using Malabak.Modules.Booking.Application.Bookings.Refunds;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Recording a manual refund (TASK-406) and marking a no-show (TASK-405) against a real PostgreSQL. A refund is a financial
/// record: it exists only for a cancelled booking that had a deposit, is recorded once for a sensible amount, and is never
/// overwritten.
/// </summary>
public sealed class RefundAndNoShowTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _slot = new(DateTime.UtcNow.Year + 9, 12, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public RefundAndNoShowTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    // ----- refunds -----

    [Fact]
    public async Task APendingRefund_IsRecordedWithTheAmountAndWhoPaidItAsync()
    {
        var (bookingId, venueId) = await SaveCancelledWithRefundAsync();
        var staffId = Guid.NewGuid();

        var result = await RecordAsync(new RecordManualRefundCommand(venueId, bookingId, 100m, "InstaPay ref 8841", staffId));

        result.IsSuccess.Should().BeTrue();
        var refund = await RefundAsync(bookingId);
        refund.Status.Should().Be(RefundStatus.Refunded);
        refund.ActualRefundAmount.Should().Be(100m);
        refund.Notes.Should().Be("InstaPay ref 8841");
        refund.RecordedByUserId.Should().Be(staffId.ToString("D"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(150.01)]
    public async Task ARefundMustBePositiveAndNoMoreThanTheDepositAsync(double amount)
    {
        var (bookingId, venueId) = await SaveCancelledWithRefundAsync(deposit: 150m);

        var result = await RecordAsync(new RecordManualRefundCommand(venueId, bookingId, (decimal)amount, "ref", Guid.NewGuid()));

        result.Error.Should().Be(BookingErrors.RefundAmountInvalid);
        (await RefundAsync(bookingId)).Status.Should().Be(RefundStatus.Pending);
    }

    [Fact]
    public async Task ARefundNeedsNotes_AndOnlyOnceNeverOverwrittenAsync()
    {
        var (bookingId, venueId) = await SaveCancelledWithRefundAsync();

        (await RecordAsync(new RecordManualRefundCommand(venueId, bookingId, 50m, "  ", Guid.NewGuid()))).Error.Should().Be(BookingErrors.RefundNotesRequired);
        (await RecordAsync(new RecordManualRefundCommand(venueId, bookingId, 50m, "first transfer", Guid.NewGuid()))).IsSuccess.Should().BeTrue();

        var again = await RecordAsync(new RecordManualRefundCommand(venueId, bookingId, 10m, "second attempt", Guid.NewGuid()));

        again.Error.Should().Be(BookingErrors.RefundAlreadyDecided);
        var refund = await RefundAsync(bookingId);
        refund.ActualRefundAmount.Should().Be(50m, "a recorded refund is a financial record and is never overwritten");
        refund.Notes.Should().Be("first transfer");
    }

    [Fact]
    public async Task ABookingThatWasNotCancelled_OrHasNoRefundDue_CannotBeRefundedAsync()
    {
        var (confirmedId, confirmedVenue) = await SaveBookingAsync(BookingStatus.Confirmed);
        var (cancelledNoDepositId, cancelledVenue) = await SaveBookingAsync(BookingStatus.Cancelled);

        (await RecordAsync(new RecordManualRefundCommand(confirmedVenue, confirmedId, 50m, "ref", Guid.NewGuid()))).Error.Should().Be(BookingErrors.NoRefundDue);
        (await RecordAsync(new RecordManualRefundCommand(cancelledVenue, cancelledNoDepositId, 50m, "ref", Guid.NewGuid()))).Error.Should().Be(BookingErrors.NoRefundDue);
    }

    [Fact]
    public async Task AnotherVenuesBooking_LooksLikeItDoesNotExistAsync()
    {
        var (bookingId, _) = await SaveCancelledWithRefundAsync();

        var result = await RecordAsync(new RecordManualRefundCommand(Guid.NewGuid(), bookingId, 50m, "ref", Guid.NewGuid()));

        result.Error.Should().Be(BookingErrors.NotFound);
        (await RefundAsync(bookingId)).Status.Should().Be(RefundStatus.Pending);
    }

    [Fact]
    public async Task TwoRecordingsAtOnce_OnlyOneIsKeptAsync()
    {
        var (bookingId, venueId) = await SaveCancelledWithRefundAsync();

        var results = await Task.WhenAll(
            Task.Run(() => RecordAsync(new RecordManualRefundCommand(venueId, bookingId, 40m, "a", Guid.NewGuid()))),
            Task.Run(() => RecordAsync(new RecordManualRefundCommand(venueId, bookingId, 60m, "b", Guid.NewGuid()))));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Should().Be(BookingErrors.RefundAlreadyDecided);
    }

    [Fact]
    public async Task TheDatabase_AllowsOneRefundRecordPerBookingAsync()
    {
        var (bookingId, _) = await SaveCancelledWithRefundAsync();

        var second = async () =>
        {
            await using var context = _database.CreateContext();
            context.BookingRefunds.Add(BookingRefund.CreatePending(Guid.NewGuid(), bookingId, 10m, "EGP", "dup", null, DateTime.UtcNow));
            await context.SaveChangesAsync();
        };

        var failure = await second.Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("23505");
    }

    // ----- no-show -----

    [Fact]
    public async Task AConfirmedBookingThatStarted_CanBeMarkedANoShowAsync()
    {
        var (bookingId, venueId) = await SaveBookingAsync(BookingStatus.Confirmed, startedHoursAgo: 1);
        var staffId = Guid.NewGuid();

        var result = await NoShowAsync(new MarkNoShowCommand(venueId, bookingId, null, staffId));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.Include(b => b.History).SingleAsync(b => b.Id == bookingId);
        booking.Status.Should().Be(BookingStatus.NoShow);
        booking.History.Single(h => h.ToStatus == BookingStatus.NoShow).ChangedByUserId.Should().Be(staffId.ToString("D"));
    }

    [Fact]
    public async Task ANoShow_CannotBeDeclaredBeforeKickoff_NorForAnotherVenuesBooking_NorWithAnOversizedReasonAsync()
    {
        var (upcomingId, venueId) = await SaveBookingAsync(BookingStatus.Confirmed, startedHoursAgo: -5);
        var (startedId, startedVenue) = await SaveBookingAsync(BookingStatus.Confirmed, startedHoursAgo: 1);

        (await NoShowAsync(new MarkNoShowCommand(venueId, upcomingId, "x", Guid.NewGuid()))).Error.Should().Be(BookingErrors.CannotMarkNoShowBeforeStart);
        (await NoShowAsync(new MarkNoShowCommand(Guid.NewGuid(), startedId, "x", Guid.NewGuid()))).Error.Should().Be(BookingErrors.NotFound);
        (await NoShowAsync(new MarkNoShowCommand(startedVenue, startedId, new string('x', MarkNoShowHandler.MaxReasonLength + 1), Guid.NewGuid()))).Error.Code.Should().Be("Booking.NoShowReasonTooLong");
    }

    [Fact]
    public async Task ABrokenLiveUpdate_DoesNotFailANoShow_AndTwoAtOnceLeaveOneWinnerAsync()
    {
        var (bookingId, venueId) = await SaveBookingAsync(BookingStatus.Confirmed, startedHoursAgo: 1);
        var (otherId, otherVenue) = await SaveBookingAsync(BookingStatus.Confirmed, startedHoursAgo: 1);

        var broken = await NoShowAsync(new MarkNoShowCommand(venueId, bookingId, "x", Guid.NewGuid()), throwOnBroadcast: true);
        var results = await Task.WhenAll(
            Task.Run(() => NoShowAsync(new MarkNoShowCommand(otherVenue, otherId, "a", Guid.NewGuid()))),
            Task.Run(() => NoShowAsync(new MarkNoShowCommand(otherVenue, otherId, "b", Guid.NewGuid()))));

        broken.IsSuccess.Should().BeTrue();
        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Type.Should().Be(ErrorType.Conflict);
    }

    // ----- helpers -----

    private async Task<(Guid BookingId, Guid VenueId)> SaveCancelledWithRefundAsync(decimal deposit = 150m)
    {
        var venueId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var actor = playerId.ToString("D");
        var booking = new BookingBuilder().ForVenue(venueId).ForPlayer(playerId).WithPrice(500m, deposit).WithTimeSlot(_slot, _slot.AddHours(1)).Build();
        var proofId = Guid.NewGuid();
        booking.SubmitProof(proofId, actor, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        booking.AddProof(PaymentProof.Create(proofId, booking.Id, Guid.NewGuid(), "k", "p.webp", "image/webp", 1, Convert.ToHexString(Guid.NewGuid().ToByteArray()), actor, DateTime.UtcNow));
        booking.Cancel("Cannot attend", actor, isStaff: false, DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        context.BookingRefunds.Add(BookingRefund.CreatePending(Guid.NewGuid(), booking.Id, deposit, "EGP", "Cannot attend", null, DateTime.UtcNow));
        await context.SaveChangesAsync();
        return (booking.Id, venueId);
    }

    private async Task<(Guid BookingId, Guid VenueId)> SaveBookingAsync(BookingStatus status, int startedHoursAgo = 3)
    {
        var venueId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var actor = playerId.ToString("D");
        var start = DateTime.UtcNow.AddHours(-startedHoursAgo);
        var booking = new BookingBuilder().ForVenue(venueId).ForPlayer(playerId).WithTimeSlot(start, start.AddHours(1)).Build();
        if (status != BookingStatus.PendingDeposit)
        {
            booking.SubmitProof(Guid.NewGuid(), actor, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            booking.Confirm("staff", DateTime.UtcNow).IsSuccess.Should().BeTrue();
        }

        if (status == BookingStatus.Cancelled)
        {
            booking.Cancel("Cancelled", actor, isStaff: true, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        }

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return (booking.Id, venueId);
    }

    private async Task<BookingRefund> RefundAsync(Guid bookingId)
    {
        await using var context = _database.CreateContext();
        return await context.BookingRefunds.AsNoTracking().SingleAsync(r => r.BookingId == bookingId);
    }

    private async Task<Result> RecordAsync(RecordManualRefundCommand command)
    {
        await using var context = _database.CreateContext();
        return await new RecordManualRefundHandler(context, new FakeClock(DateTime.UtcNow)).HandleAsync(command);
    }

    private async Task<Result> NoShowAsync(MarkNoShowCommand command, bool throwOnBroadcast = false)
    {
        await using var context = _database.CreateContext();
        var handler = new MarkNoShowHandler(context, new FakeClock(DateTime.UtcNow), new Broadcaster(throwOnBroadcast), NullLogger<MarkNoShowHandler>.Instance);
        return await handler.HandleAsync(command);
    }

    private sealed class Broadcaster(bool throwOnUpdate) : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) =>
            throwOnUpdate ? throw new InvalidOperationException("SignalR is down") : Task.CompletedTask;
    }
}
