using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application.Bookings.VenueQueries;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>What a venue manager reads (TASK-404): the reservations list and filters, the pending queue and the calendar.</summary>
public sealed class VenueBookingQueriesTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _day = new(DateTime.UtcNow.Year + 6, 2, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();

    public VenueBookingQueriesTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ThePendingQueue_HoldsEveryBookingWaitingOnSomeone_AndNothingFromAnotherVenueAsync()
    {
        var waiting = await SaveAsync(_day.AddHours(10));
        var underReview = await SaveAsync(_day.AddHours(12));
        await SetStatusAsync(underReview, BookingStatus.UnderReview);
        var confirmed = await SaveAsync(_day.AddHours(14));
        await SetStatusAsync(confirmed, BookingStatus.Confirmed);
        await SaveAsync(_day.AddHours(16), venueId: Guid.NewGuid());
        await using var context = _database.CreateContext();

        var result = await new VenueBookingQueriesHandler(context, new FakeVenueDirectory(), new FakeUserAccounts()).HandleAsync(new GetVenuePendingBookingsQuery(_venueId));

        result.Value.Select(b => b.Id).Should().BeEquivalentTo([waiting, underReview]);
    }

    [Fact]
    public async Task TheListSearch_FindsByReferenceCustomerNameOrPhone_AndTheEndDateKeepsABookingStartingThatDayAsync()
    {
        var phone = await SaveManualAsync(_day.AddDays(1).AddHours(9), "Mahmoud Ali", "01234567890");
        await SaveManualAsync(_day.AddDays(1).AddHours(11), "Someone Else", "01999999999");
        await using var context = _database.CreateContext();
        var handler = new VenueBookingQueriesHandler(context, new FakeVenueDirectory(), new FakeUserAccounts());

        var byName = await handler.HandleAsync(new GetVenueBookingsQuery(_venueId, null, null, null, null, "mahmoud"));
        var byPhone = await handler.HandleAsync(new GetVenueBookingsQuery(_venueId, null, null, null, null, "0123456"));
        var endsThatEvening = await handler.HandleAsync(new GetVenueBookingsQuery(_venueId, null, _day.AddDays(1), _day.AddDays(1).AddHours(9), null, null));

        byName.Value.Items.Should().ContainSingle().Which.Id.Should().Be(phone);
        byPhone.Value.Items.Should().ContainSingle().Which.Id.Should().Be(phone);
        endsThatEvening.Value.Items.Should().Contain(b => b.Id == phone, "a booking that starts within the range belongs to it even if it ends after");
    }

    [Fact]
    public async Task TheSourceFilter_SplitsBookingsEnteredByHandFromThoseMadeInTheAppAsync()
    {
        var day = _day.AddDays(5);
        var manual = await SaveManualAsync(day.AddHours(9), "By Phone", "01111111111");
        var inApp = await SaveAsync(day.AddHours(12));
        await using var context = _database.CreateContext();
        var handler = new VenueBookingQueriesHandler(context, new FakeVenueDirectory(), new FakeUserAccounts());
        GetVenueBookingsQuery Query(bool? isManual) => new(_venueId, null, day, day.AddDays(1), null, null, IsManual: isManual);

        (await handler.HandleAsync(Query(true))).Value.Items.Select(b => b.Id).Should().Equal(manual);
        (await handler.HandleAsync(Query(false))).Value.Items.Select(b => b.Id).Should().Equal(inApp);
        (await handler.HandleAsync(Query(null))).Value.Items.Select(b => b.Id).Should().BeEquivalentTo([manual, inApp]);
    }

    [Fact]
    public async Task TheBookingDetails_IncludeTheServerResolvedPitchNameAsync()
    {
        var bookingId = await SaveAsync(_day.AddDays(6).AddHours(10));
        await using var context = _database.CreateContext();

        var result = await new VenueBookingQueriesHandler(context, new FakeVenueDirectory(), new FakeUserAccounts())
            .HandleAsync(new GetVenueBookingDetailQuery(_venueId, bookingId));

        result.IsSuccess.Should().BeTrue();
        result.Value.PitchName.Should().Be(FakeVenueDirectory.PitchName);
    }

    [Fact]
    public async Task TheCalendar_ShowsBookingsAndBlockedPeriodsTogether_InTimeOrderAsync()
    {
        var day = _day.AddDays(2);
        var bookingId = await SaveAsync(day.AddHours(10));
        var blocked = new VenueBlockedPeriod(Guid.NewGuid(), _pitchId, day.AddHours(8), day.AddHours(9), "Maintenance");
        var directory = new FakeVenueDirectory().WithBlocked(blocked);
        await using var context = _database.CreateContext();

        var result = await new VenueBookingQueriesHandler(context, directory, new FakeUserAccounts()).HandleAsync(new GetVenueCalendarQuery(_venueId, day, day.AddDays(1), null));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(s => s.Status).Should().Equal("Blocked", nameof(BookingStatus.PendingDeposit));
        result.Value[0].Reason.Should().Be("Maintenance");
        result.Value[1].Id.Should().Be(bookingId);
    }

    [Fact]
    public async Task TheCalendar_SaysWhoBooked_AndWhichManualBookingsAreNotConfirmedYetAsync()
    {
        var day = _day.AddDays(3);
        var appBooking = await SaveAsync(day.AddHours(8));
        var tentative = Booking.CreateManual(
            Guid.NewGuid(), $"MLB-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), _venueId, _pitchId, Guid.NewGuid(), null, "01012345678",
            day.AddHours(10), day.AddHours(11), Money.Create(300m, "EGP").Value, Money.Create(0m, "EGP").Value,
            confirmedImmediately: false, holdExpiresAtUtc: day.AddHours(10), nowUtc: DateTime.UtcNow).Value;
        await using (var save = _database.CreateContext())
        {
            save.Bookings.Add(tentative);
            await save.SaveChangesAsync();
        }

        var walkIn = await SaveManualAsync(day.AddHours(12), "Captain Ali", "01099999999");
        await using var context = _database.CreateContext();

        var result = await new VenueBookingQueriesHandler(context, new FakeVenueDirectory(), new FakeUserAccounts())
            .HandleAsync(new GetVenueCalendarQuery(_venueId, day, day.AddDays(1), null));

        var slots = result.Value.ToDictionary(s => s.Id);
        slots[appBooking].CustomerName.Should().Be("Player", "an app booking shows the player's account name");
        slots[appBooking].CustomerPhone.Should().BeNull("a player's phone is not handed to the venue through the calendar");
        slots[appBooking].IsTentative.Should().BeFalse();
        slots[tentative.Id].IsTentative.Should().BeTrue();
        slots[tentative.Id].CustomerName.Should().BeNull();
        slots[tentative.Id].CustomerPhone.Should().Be("01012345678");
        slots[walkIn].IsTentative.Should().BeFalse();
        slots[walkIn].CustomerName.Should().Be("Captain Ali");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(46)]
    public async Task ACalendarRangeThatIsBackwardsOrHuge_IsRefusedAsync(int days)
    {
        await using var context = _database.CreateContext();

        var result = await new VenueBookingQueriesHandler(context, new FakeVenueDirectory(), new FakeUserAccounts())
            .HandleAsync(new GetVenueCalendarQuery(_venueId, _day, _day.AddDays(days), null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Calendar.InvalidRange");
    }

    private async Task<Guid> SaveAsync(DateTime start, Guid? venueId = null)
    {
        var booking = new BookingBuilder().ForVenue(venueId ?? _venueId).ForPitch(venueId is null ? _pitchId : Guid.NewGuid())
            .WithTimeSlot(start, start.AddHours(1)).Build();
        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<Guid> SaveManualAsync(DateTime start, string name, string phone)
    {
        var booking = Booking.CreateManual(
            Guid.NewGuid(), $"MLB-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), _venueId, _pitchId, Guid.NewGuid(), name, phone,
            start, start.AddHours(1), Money.Create(300m, "EGP").Value, Money.Create(300m, "EGP").Value,
            confirmedImmediately: true, holdExpiresAtUtc: null, nowUtc: DateTime.UtcNow).Value;
        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private async Task SetStatusAsync(Guid bookingId, BookingStatus status)
    {
        await using var context = _database.CreateContext();
        var booking = await context.Bookings.Include(b => b.History).SingleAsync(b => b.Id == bookingId);
        booking.SubmitProof(Guid.NewGuid(), "staff", DateTime.UtcNow).IsSuccess.Should().BeTrue();
        var next = status == BookingStatus.UnderReview ? booking.MarkUnderReview("staff", DateTime.UtcNow) : booking.Confirm("staff", DateTime.UtcNow);
        next.IsSuccess.Should().BeTrue();
        await context.SaveChangesAsync();
    }
}
