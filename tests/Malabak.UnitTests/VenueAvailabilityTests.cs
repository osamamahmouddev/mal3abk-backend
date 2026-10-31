using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueAvailabilityTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _thursday = new(2026, 11, 5);

    private readonly Guid _pitchId = Guid.NewGuid();
    private Guid _venueId;

    private static DateTime Local(int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified), _cairo);

    [Fact]
    public async Task OvernightHours_ProduceSlotsOnTheIncrementGrid_WithPricesAsync()
    {
        await using var db = await SeedAsync();

        var result = await Query(db).GetAsync(_venueId, _thursday, null);

        var pitch = result.Value.Pitches.Should().ContainSingle().Subject;
        pitch.SlotMinutes.Should().Be(60);
        pitch.IncrementMinutes.Should().Be(30);
        pitch.Slots.Should().HaveCount(15, "18:00..01:00 every 30 minutes, each lasting 60 minutes, closing at 02:00");
        pitch.Slots[^1].StartLocal.Should().Be(new DateTime(2026, 11, 6, 1, 0, 0, DateTimeKind.Unspecified));
        pitch.Slots.Should().OnlyContain(s => s.State == SlotState.Available && s.Price == 400m);
    }

    [Fact]
    public async Task Bookings_Holds_AndBlocks_AreOverlaid_AndLapsedHoldsFreeTheSlotAsync()
    {
        await using var db = await SeedAsync();
        db.BlockedSlots.Add(BlockedSlot.Create(Guid.NewGuid(), _venueId, _pitchId, Local(5, 23), Local(6, 0), "Maintenance", Guid.NewGuid(), _now));
        await db.SaveChangesAsync();
        var bookings = new[]
        {
            Booking(Local(5, 19), Local(5, 20), "Confirmed"),
            Booking(Local(5, 21), Local(5, 22), "PendingDeposit", holdExpires: _now.AddMinutes(10)),
            Booking(Local(6, 0), Local(6, 1), "PendingDeposit", holdExpires: _now.AddMinutes(-1)),
            Booking(Local(5, 18), Local(5, 19), "UnderReview")
        };

        var slots = (await Query(db, bookings).GetAsync(_venueId, _thursday, null)).Value.Pitches[0].Slots;

        State(slots, 19, 0).Should().Be(SlotState.Booked);
        State(slots, 18, 30).Should().Be(SlotState.Booked, "18:30-19:30 overlaps the confirmed 19:00 booking");
        State(slots, 21, 0).Should().Be(SlotState.Held);
        State(slots, 18, 0).Should().Be(SlotState.Held, "proof under review still holds the slot");
        State(slots, 23, 0).Should().Be(SlotState.Blocked);
        slots.Single(s => s.StartLocal.Day == 6 && s.StartLocal.Hour == 0 && s.StartLocal.Minute == 0)
            .State.Should().Be(SlotState.Available, "the hold lapsed");
    }

    [Fact]
    public async Task SlotsInsideTheMinimumNotice_AreUnavailableAsync()
    {
        await using var db = await SeedAsync(scheduleDay: DayOfWeek.Sunday, open: 0, close: 0);

        var slots = (await Query(db).GetAsync(_venueId, new DateOnly(2026, 11, 1), null)).Value.Pitches[0].Slots;

        // Now is 12:00 Cairo; default notice is 60 minutes.
        State(slots, 12, 30, day: 1).Should().Be(SlotState.Unavailable);
        State(slots, 13, 0, day: 1).Should().Be(SlotState.Available);
    }

    [Fact]
    public async Task UnpublishedVenue_UnknownPitch_AndFarDates_AreRejectedAsync()
    {
        await using var db = await SeedAsync(publish: false);
        (await Query(db).GetAsync(_venueId, _thursday, null)).Error.Should().Be(AvailabilityErrors.VenueNotFound);

        await using var published = await SeedAsync();
        (await Query(published).GetAsync(_venueId, _thursday, Guid.NewGuid())).IsFailure.Should().BeTrue();
        (await Query(published).GetAsync(_venueId, _thursday.AddDays(90), null)).Error.Should().Be(AvailabilityErrors.DateOutOfRange);
    }

    private static SlotState State(IReadOnlyList<SlotDto> slots, int hour, int minute, int day = 5) =>
        slots.Single(s => s.StartLocal.Day == day && s.StartLocal.Hour == hour && s.StartLocal.Minute == minute).State;

    private UpcomingBookingRef Booking(DateTime start, DateTime end, string status, DateTime? holdExpires = null) =>
        new(Guid.NewGuid(), "MLB-1", _pitchId, start, end, status, holdExpires);

    private static VenueAvailabilityQuery Query(VenuesDbContext db, params UpcomingBookingRef[] bookings) =>
        new(db, new FakeBookings(bookings), new FixedClock());

    private async Task<VenuesDbContext> SeedAsync(bool publish = true, DayOfWeek scheduleDay = DayOfWeek.Thursday, int open = 18, int close = 2)
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var venue = Venue.Create(Guid.NewGuid(), "Club", $"club-{Guid.NewGuid():N}", null, Guid.NewGuid(), "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        _venueId = venue.Id;
        if (publish)
        {
            venue.SubmitForApproval(venue.OwnerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(_pitchId, venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venue.Id, scheduleDay, new TimeOnly(open, 0), new TimeOnly(close, 0), 60, _now));
        db.PriceRules.Add(PriceRule.Create(venue.Id,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 400, null, null, null, null, null, null, null), _now).Value);
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class FakeBookings(IReadOnlyList<UpcomingBookingRef> bookings) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(bookings.Where(b => pitchIds.Contains(b.PitchId) && b.EndUtc > fromUtc && b.StartUtc < toUtc).ToList());
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
