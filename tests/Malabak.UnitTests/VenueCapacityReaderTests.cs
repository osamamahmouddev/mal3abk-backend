using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueCapacityReaderTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _thursday = new(2026, 11, 5);

    [Fact]
    public async Task OpenMinutes_SumActivePitches_IncludingHoursPastMidnightAsync()
    {
        await using var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var venueId = Guid.NewGuid();
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venueId, "A", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venueId, "B", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        var closed = Pitch.Create(Guid.NewGuid(), venueId, "C", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now);
        closed.SetActive(false, _now);
        db.Pitches.Add(closed);
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venueId, DayOfWeek.Thursday, new TimeOnly(18, 0), new TimeOnly(2, 0), 60, _now));
        await db.SaveChangesAsync();

        var minutes = await new VenueCapacityReader(db, new FixedClock()).GetOpenMinutesAsync(venueId, _thursday, _thursday.AddDays(1));

        minutes[_thursday].Should().Be(2 * 8 * 60, "two active pitches open 18:00 to 02:00; the inactive one does not count");
        minutes[_thursday.AddDays(1)].Should().Be(0);
    }

    [Fact]
    public async Task InvertedOrHugeRanges_ReturnNothingAsync()
    {
        await using var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var reader = new VenueCapacityReader(db, new FixedClock());

        (await reader.GetOpenMinutesAsync(Guid.NewGuid(), _thursday, _thursday.AddDays(-1))).Should().BeEmpty();
        (await reader.GetOpenMinutesAsync(Guid.NewGuid(), _thursday, _thursday.AddDays(VenueCapacityReader.MaxDays))).Should().BeEmpty();
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
