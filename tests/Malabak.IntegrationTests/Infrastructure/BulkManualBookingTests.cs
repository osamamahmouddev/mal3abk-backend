using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Bookings.ManualBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Many hours marked from the calendar at once, on a real PostgreSQL (the overlap constraint included): back-to-back hours are
/// one booking, a taken hour fails alone, and only the venue's own bookings can be freed again.
/// </summary>
public sealed class BulkManualBookingTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _day = new(DateTime.UtcNow.Year + 6, 4, 1, 14, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();

    public BulkManualBookingTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task BackToBackHours_BecomeOneBooking_AndATakenHourFailsAloneAsync()
    {
        // 18:00–19:00 is already taken by a player.
        var taken = new BookingBuilder().ForVenue(_venueId).ForPitch(_pitchId).WithTimeSlot(_day.AddHours(4), _day.AddHours(5)).Build();
        await using (var save = _database.CreateContext())
        {
            save.Bookings.Add(taken);
            await save.SaveChangesAsync();
        }

        var result = await BookAsync([Hour(0), Hour(1), Hour(2), Hour(4), Hour(6)], ManualPaymentStatus.Unconfirmed);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(2, "14–17 is one booking and 20–21 another");
        result.Value.Failed.Should().Be(1);
        result.Value.Slots.Single(s => !s.Succeeded).ErrorCode.Should().Be(BookingErrors.SlotOverlap.Code);
        await using var verify = _database.CreateContext();
        var mine = await verify.Bookings.Where(b => b.PitchId == _pitchId && b.IsManualBooking).OrderBy(b => b.StartUtc).ToListAsync();
        mine.Select(b => (b.StartUtc, b.EndUtc)).Should().Equal((_day, _day.AddHours(3)), (_day.AddHours(6), _day.AddHours(7)));
        mine.Should().OnlyContain(b => b.Status == BookingStatus.PendingDeposit && b.HoldExpiresAtUtc == b.StartUtc && b.CustomerName == null);
        mine[0].TotalPrice.Amount.Should().Be(900, "the venue's price for each hour, three hours");
    }

    [Fact]
    public async Task Freeing_CancelsTheVenuesOwnBookings_ButNeverAPlayersAsync()
    {
        var booked = await BookAsync([Hour(10), Hour(12)], ManualPaymentStatus.FullyPaid);
        var players = new BookingBuilder().ForVenue(_venueId).ForPitch(_pitchId).WithTimeSlot(_day.AddHours(14), _day.AddHours(15)).Build();
        await using (var save = _database.CreateContext())
        {
            save.Bookings.Add(players);
            await save.SaveChangesAsync();
        }

        var ids = booked.Value.Slots.Select(s => s.BookingId!.Value).Append(players.Id).ToList();
        Result<BulkReleaseResult> released;
        await using (var context = _database.CreateContext())
        {
            released = await Service(context).ReleaseAsync(_venueId, _staff, ids);
        }

        released.Value.Succeeded.Should().Be(2);
        released.Value.Items.Single(i => i.BookingId == players.Id).ErrorCode.Should().Be("Booking.NotReleasable");
        await using var verify = _database.CreateContext();
        (await verify.Bookings.Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Status))
            .Should().Contain(players.Id, players.Status).And.ContainValues(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task ADepositStatus_OrTooManyHours_IsRefusedAsync()
    {
        (await BookAsync([Hour(16)], ManualPaymentStatus.AwaitingDeposit)).Error.Code.Should().Be("Booking.BulkInvalidStatus");
        (await BookAsync(Enumerable.Range(0, 121).Select(i => Hour(24 + i)).ToList(), ManualPaymentStatus.FullyPaid)).Error.Code
            .Should().Be("Booking.BulkInvalidSlots");
    }

    private BulkSlot Hour(int offset) => new(_pitchId, _day.AddHours(offset), _day.AddHours(offset + 1));

    private async Task<Result<BulkManualBookingResult>> BookAsync(IReadOnlyList<BulkSlot> slots, ManualPaymentStatus status)
    {
        await using var context = _database.CreateContext();
        return await Service(context).BookAsync(_venueId, _staff, slots, status, null, null, null, null);
    }

    private BulkManualBookingService Service(BookingDbContext context)
    {
        var clock = new SystemClock();
        var create = new CreateManualBookingHandler(context, clock, new BookingReferenceGenerator(), new Quiet(), new Quiet(),
            new FakeVenueDirectory().WithPitch(_pitchId, _venueId), NullLogger<CreateManualBookingHandler>.Instance);
        var cancel = new VenueCancelBookingHandler(context, clock, new Quiet(), new Quiet(), NullLogger<VenueCancelBookingHandler>.Instance);
        return new BulkManualBookingService(create, cancel, context, new HourlyPrice(300m));
    }

    private sealed class HourlyPrice(decimal perHour) : IPriceQuoteProvider
    {
        public Task<Result<PriceQuoteSnapshot>> QuoteAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        {
            var total = perHour * (decimal)(endUtc - startUtc).TotalHours;
            return Task.FromResult(Result.Success(new PriceQuoteSnapshot(Guid.Empty, pitchId, startUtc, endUtc, (int)(endUtc - startUtc).TotalMinutes, "EGP",
                total, 0, null, total, total, "Full", [], startUtc)));
        }
    }

    private sealed class Quiet : IAvailabilityInvalidator, IBookingRealtimeBroadcaster
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
