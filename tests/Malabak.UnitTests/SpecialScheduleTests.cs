using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SpecialScheduleTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _thursday = new(2026, 11, 5);

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();

    // --- Calculator: overnight, DST, precedence ---

    [Fact]
    public void Overnight_Hours_RunPastMidnight_AndStartTimesStepByIncrement()
    {
        var state = State(Week(DayOfWeek.Thursday, 18, 2));

        var windows = AvailabilityCalculator.OpenWindows(state, _thursday, _cairo);
        var starts = AvailabilityCalculator.StartTimes(windows, state.Rules, _now);

        windows.Should().ContainSingle();
        (windows[0].EndUtc - windows[0].StartUtc).Should().Be(TimeSpan.FromHours(8));
        starts.Should().HaveCount(15, "18:00..01:00 every 30 minutes for a 60-minute minimum");
        TimeZoneInfo.ConvertTimeFromUtc(starts[^1], _cairo).Should().Be(new DateTime(2026, 11, 6, 1, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void Dst_SpringForwardNight_IsAnHourShorter()
    {
        var date = new DateOnly(2026, 4, 23);
        var expectedHours = _cairo.IsInvalidTime(new DateTime(2026, 4, 24, 0, 30, 0, DateTimeKind.Unspecified)) ? 7 : 8;

        var windows = AvailabilityCalculator.OpenWindows(State(Week(DayOfWeek.Thursday, 18, 2)), date, _cairo);

        (windows[0].EndUtc - windows[0].StartUtc).Should().Be(TimeSpan.FromHours(expectedHours));
    }

    [Fact]
    public void Dst_FallBackNight_IsAnHourLonger()
    {
        var date = new DateOnly(2026, 10, 29);
        var expectedHours = _cairo.IsAmbiguousTime(new DateTime(2026, 10, 29, 23, 30, 0, DateTimeKind.Unspecified)) ? 9 : 8;

        var windows = AvailabilityCalculator.OpenWindows(State(Week(DayOfWeek.Thursday, 18, 2)), date, _cairo);

        (windows[0].EndUtc - windows[0].StartUtc).Should().Be(TimeSpan.FromHours(expectedHours));
    }

    [Fact]
    public void PitchWeeklyHours_OverrideVenueHours_AndCanCloseTheDay()
    {
        var open = State(Week(DayOfWeek.Thursday, 16, 23)) with
        {
            PitchWeek = [PitchWeeklyHours.Create(_venueId, _pitchId, DayOfWeek.Thursday, new TimeOnly(20, 0), new TimeOnly(22, 0), false, _now)]
        };
        var closed = open with
        {
            PitchWeek = [PitchWeeklyHours.Create(_venueId, _pitchId, DayOfWeek.Thursday, default, default, true, _now)]
        };

        Hours(AvailabilityCalculator.OpenWindows(open, _thursday, _cairo)).Should().Be(2);
        AvailabilityCalculator.OpenWindows(closed, _thursday, _cairo).Should().BeEmpty();
    }

    [Fact]
    public void PitchSpecialHours_BeatVenueSpecialHours_AndClosureBeatsEverything()
    {
        var state = State(Week(DayOfWeek.Thursday, 16, 23)) with
        {
            Exceptions =
            [
                Override(null, ScheduleOverrideKind.SpecialHours, new TimeOnly(10, 0), new TimeOnly(12, 0)),
                Override(_pitchId, ScheduleOverrideKind.SpecialHours, new TimeOnly(14, 0), new TimeOnly(18, 0))
            ]
        };

        Hours(AvailabilityCalculator.OpenWindows(state, _thursday, _cairo)).Should().Be(4);

        var closed = state with { Exceptions = [.. state.Exceptions, Override(null, ScheduleOverrideKind.Closure, null, null)] };
        AvailabilityCalculator.OpenWindows(closed, _thursday, _cairo).Should().BeEmpty();
        AvailabilityCalculator.OpenWindows(closed, _thursday.AddDays(7), _cairo).Should().NotBeEmpty("the closure covers one date only");
    }

    [Fact]
    public void Breaks_AreSubtracted_IncludingAfterMidnight()
    {
        var state = State(Week(DayOfWeek.Thursday, 18, 2)) with
        {
            Exceptions =
            [
                Override(null, ScheduleOverrideKind.Break, new TimeOnly(20, 0), new TimeOnly(20, 30)),
                Override(null, ScheduleOverrideKind.Break, new TimeOnly(0, 0), new TimeOnly(0, 30))
            ]
        };

        var windows = AvailabilityCalculator.OpenWindows(state, _thursday, _cairo);

        windows.Should().HaveCount(3);
        Hours(windows).Should().Be(7);
    }

    [Theory]
    [InlineData(18, 0, 60, null)]
    [InlineData(18, 15, 60, "Schedule.InvalidDuration")]
    [InlineData(18, 0, 45, "Schedule.InvalidDuration")]
    [InlineData(18, 0, 240, "Schedule.InvalidDuration")]
    [InlineData(1, 30, 60, "Schedule.Closed")]
    [InlineData(1, 0, 60, null)]
    public void CheckSlot_EnforcesHoursIncrementsAndDuration(int hour, int minute, int durationMinutes, string? expected)
    {
        var state = State(Week(DayOfWeek.Thursday, 18, 2));
        var day = hour < 12 ? _thursday.AddDays(1) : _thursday;
        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), _cairo);

        AvailabilityCalculator.CheckSlot(state, start, start.AddMinutes(durationMinutes), _now, _cairo)?.Code.Should().Be(expected);
        if (expected is null)
        {
            AvailabilityCalculator.CheckSlot(state, start, start.AddMinutes(durationMinutes), _now, _cairo).Should().BeNull();
        }
    }

    [Fact]
    public void CheckSlot_EnforcesNoticeAndAdvanceWindow()
    {
        var state = State(Week(DayOfWeek.Sunday, 0, 0));
        var tooSoon = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);

        AvailabilityCalculator.CheckSlot(state, tooSoon, tooSoon.AddHours(1), _now, _cairo)!.Code.Should().Be("Schedule.TooSoon");
        var tooFar = new DateTime(2026, 12, 6, 10, 0, 0, DateTimeKind.Utc);
        AvailabilityCalculator.CheckSlot(state, tooFar, tooFar.AddHours(1), _now, _cairo)!.Code.Should().Be("Schedule.TooFarAhead");
    }

    [Fact]
    public void BookingRules_RejectInconsistentValues()
    {
        BookingRules.Create(_venueId, null, 60, 120, 30, 60, 30, _now).IsSuccess.Should().BeTrue();
        BookingRules.Create(_venueId, null, 45, 120, 30, 60, 30, _now).Error.Should().Be(SpecialScheduleErrors.InvalidRules);
        BookingRules.Create(_venueId, null, 120, 60, 30, 60, 30, _now).Error.Should().Be(SpecialScheduleErrors.InvalidRules);
        BookingRules.Create(_venueId, null, 60, 120, 20, 60, 30, _now).Error.Should().Be(SpecialScheduleErrors.InvalidRules);
    }

    // --- Handlers: impact warning, confirmation, permissions, holidays ---

    [Fact]
    public async Task Closure_OverABooking_NeedsConfirmation_ThenSavesAsync()
    {
        await using var db = await SeedAsync();
        var booking = Booking(_thursday, 19);
        var handlers = Handlers(db, VenueAccess.Owner(_venueId), booking);
        var command = new AddScheduleOverrideCommand(_venueId, Guid.NewGuid(), null, _thursday, _thursday, ScheduleOverrideKind.Closure, null, null, "Maintenance", false);

        var warning = await handlers.HandleAsync(command);

        warning.Value.Applied.Should().BeFalse();
        warning.Value.AffectedBookings.Should().ContainSingle(b => b.Reference == "MLB-1");
        (await db.ScheduleOverrides.CountAsync()).Should().Be(0);

        var confirmed = await handlers.HandleAsync(command with { Confirm = true });

        confirmed.Value.Applied.Should().BeTrue();
        (await db.ScheduleOverrides.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SpecialHours_ThatStillCoverTheBooking_ApplyWithoutWarningAsync()
    {
        await using var db = await SeedAsync();
        var handlers = Handlers(db, VenueAccess.Owner(_venueId), Booking(_thursday, 19));

        var result = await handlers.HandleAsync(new AddScheduleOverrideCommand(
            _venueId, Guid.NewGuid(), _pitchId, _thursday, _thursday, ScheduleOverrideKind.SpecialHours, new TimeOnly(19, 0), new TimeOnly(21, 0), "Short day", false));

        result.Value.Applied.Should().BeTrue();
        result.Value.AffectedBookings.Should().BeEmpty();
    }

    [Fact]
    public async Task RecurringPitchHours_ThatDropABookedSlot_ListItAsync()
    {
        await using var db = await SeedAsync();
        var handlers = Handlers(db, VenueAccess.Owner(_venueId), Booking(_thursday, 23));

        var result = await handlers.HandleAsync(new SetPitchWeeklyHoursCommand(
            _venueId, Guid.NewGuid(), _pitchId, [new WeeklyHoursInput(DayOfWeek.Thursday, new TimeOnly(18, 0), new TimeOnly(22, 0), false)], false));

        result.Value.Applied.Should().BeFalse();
        result.Value.AffectedBookings.Should().ContainSingle();
    }

    [Fact]
    public async Task StaffWithoutSchedulePermission_IsRejectedAsync()
    {
        await using var db = await SeedAsync();
        var handlers = Handlers(db, VenueAccess.None(_venueId));

        var result = await handlers.HandleAsync(new SetBookingRulesCommand(_venueId, Guid.NewGuid(), null, 60, 120, 30, 60, 30));

        result.Error.Should().Be(VenueAccessErrors.VenueNotFound);
    }

    [Fact]
    public async Task HolidayClosures_AreCreatedOnce_ForRemainingHolidaysAsync()
    {
        await using var db = await SeedAsync();
        var handlers = Handlers(db, VenueAccess.Owner(_venueId));

        var first = await handlers.HandleAsync(new ApplyHolidayClosuresCommand(_venueId, Guid.NewGuid(), 2027, null, false));
        var count = await db.ScheduleOverrides.CountAsync();
        var second = await handlers.HandleAsync(new ApplyHolidayClosuresCommand(_venueId, Guid.NewGuid(), 2027, null, false));

        first.Value.Applied.Should().BeTrue();
        count.Should().BeGreaterThan(5);
        second.Value.Applied.Should().BeTrue();
        (await db.ScheduleOverrides.CountAsync()).Should().Be(count);
    }

    [Fact]
    public async Task PitchAvailability_UsesPitchRuleOverrideAsync()
    {
        await using var db = await SeedAsync();
        var handlers = Handlers(db, VenueAccess.Owner(_venueId));
        await handlers.HandleAsync(new SetBookingRulesCommand(_venueId, Guid.NewGuid(), _pitchId, 120, 120, 60, 0, 30));

        var queries = new SpecialScheduleQueries(db, new FakeAccess(VenueAccess.Owner(_venueId)), new FakeBookings([]), new FixedClock());
        var availability = await queries.GetPitchAvailabilityAsync(_venueId, _pitchId, _thursday, Guid.NewGuid());

        availability.Value.Rules.IncrementMinutes.Should().Be(60);
        availability.Value.StartTimes.Should().HaveCount(7, "18:00..00:00 hourly for a 2-hour game ending by 02:00");
    }

    private PitchScheduleState State(IReadOnlyList<OperatingSchedule> week) =>
        new(_pitchId, week, [], [], BookingRules.Defaults(_venueId));

    private List<OperatingSchedule> Week(DayOfWeek day, int open, int close) =>
        [OperatingSchedule.Create(Guid.NewGuid(), _venueId, day, new TimeOnly(open, 0), new TimeOnly(close, 0), 60, _now)];

    private ScheduleOverride Override(Guid? pitchId, ScheduleOverrideKind kind, TimeOnly? from, TimeOnly? to) =>
        ScheduleOverride.Create(_venueId, pitchId, _thursday, _thursday, kind, from, to, "test", Guid.NewGuid(), _now).Value;

    private static double Hours(IEnumerable<UtcWindow> windows) => windows.Sum(w => (w.EndUtc - w.StartUtc).TotalHours);

    private UpcomingBookingRef Booking(DateOnly date, int hour)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0)), _cairo);
        return new UpcomingBookingRef(Guid.NewGuid(), "MLB-1", _pitchId, start, start.AddHours(1), "Confirmed");
    }

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Pitches.Add(Pitch.Create(_pitchId, _venueId, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        db.OperatingSchedules.AddRange(Week(DayOfWeek.Thursday, 18, 2));
        await db.SaveChangesAsync();
        return db;
    }

    private static SpecialScheduleHandlers Handlers(VenuesDbContext db, VenueAccess access, params UpcomingBookingRef[] bookings) =>
        new(db, new FakeAccess(access), new FakeBookings(bookings), new FixedClock());

    private sealed class FakeAccess(VenueAccess access) : IVenueAccessService
    {
        public Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(access);

        public Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VenueAccess>>([access]);
    }

    private sealed class FakeBookings(IReadOnlyList<UpcomingBookingRef> bookings) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(bookings.Where(b => b.EndUtc > fromUtc && b.StartUtc < toUtc).ToList());
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
