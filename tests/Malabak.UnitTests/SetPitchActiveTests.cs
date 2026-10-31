using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SetPitchActiveTests
{
    private static readonly DateTime _now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();

    [Fact]
    public async Task Owner_CanPauseAndResume_AndIsToldAboutBookingsAheadAsync()
    {
        await using var db = await SeedAsync();
        var service = new SetPitchActive(db, new TwoBookings(), new FixedClock());

        var paused = (await service.HandleAsync(_pitchId, _ownerId, isActive: false)).Value;

        paused.Pitch.IsActive.Should().BeFalse();
        paused.UpcomingBookings.Should().Be(2, "existing bookings are kept, so staff must handle them");
        (await db.Pitches.SingleAsync()).IsActive.Should().BeFalse();

        (await service.HandleAsync(_pitchId, _ownerId, isActive: true)).Value.Pitch.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SomeoneWithoutAccess_GetsNotFound_AndNothingChangesAsync()
    {
        await using var db = await SeedAsync();
        var service = new SetPitchActive(db, new TwoBookings(), new FixedClock());

        (await service.HandleAsync(_pitchId, Guid.NewGuid(), isActive: false)).Error.Should().Be(VenueAccessErrors.PitchNotFound);
        (await service.HandleAsync(Guid.NewGuid(), _ownerId, isActive: false)).Error.Should().Be(VenueAccessErrors.PitchNotFound);
        (await db.Pitches.SingleAsync()).IsActive.Should().BeTrue();
    }

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var venue = Venue.Create(Guid.NewGuid(), "Club", $"club-{Guid.NewGuid():N}", null, _ownerId, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(_pitchId, venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class TwoBookings : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(
            [
                new UpcomingBookingRef(Guid.NewGuid(), "A", pitchIds.First(), fromUtc.AddDays(1), fromUtc.AddDays(1).AddHours(1), "Confirmed"),
                new UpcomingBookingRef(Guid.NewGuid(), "B", pitchIds.First(), fromUtc.AddDays(2), fromUtc.AddDays(2).AddHours(1), "Confirmed"),
            ]);
    }

    private sealed class FixedClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
