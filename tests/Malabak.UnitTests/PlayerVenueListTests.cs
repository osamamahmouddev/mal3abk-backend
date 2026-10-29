using FluentAssertions;
using Malabak.Modules.Venues.Application.PlayerLists;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PlayerVenueListTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    // Sunday 2026-11-01 12:00 Cairo.
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly MutableClock _clock = new();

    [Fact]
    public async Task Favorites_AddIsIdempotent_RemoveWorks_AndOnlySearchableVenuesAllowedAsync()
    {
        await using var db = NewDb();
        var live = await SeedVenueAsync(db, searchable: true);
        var draft = await SeedVenueAsync(db, searchable: false);
        var lists = Lists(db);

        (await lists.AddFavoriteAsync(_userId, live.Id)).IsSuccess.Should().BeTrue();
        (await lists.AddFavoriteAsync(_userId, live.Id)).IsSuccess.Should().BeTrue();
        (await lists.AddFavoriteAsync(_userId, draft.Id)).Error.Should().Be(PlayerListErrors.VenueNotFound);
        (await db.FavoriteVenues.CountAsync()).Should().Be(1);

        await lists.RemoveFavoriteAsync(_userId, live.Id);
        (await lists.GetFavoritesAsync(_userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Favorites_AreScopedToTheUser_AndHideVenuesUnpublishedLaterAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db, searchable: true);
        var lists = Lists(db);
        await lists.AddFavoriteAsync(_userId, venue.Id);

        (await lists.GetFavoritesAsync(Guid.NewGuid())).Should().BeEmpty("another player's favorites are private");
        (await lists.GetFavoritesAsync(_userId)).Should().ContainSingle().Which.IsFavorite.Should().BeTrue();

        venue.SetPublished(false, _now);
        await db.SaveChangesAsync();
        (await lists.GetFavoritesAsync(_userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task FavoriteHint_IsNextFreeStart_SkippingBookedAndBlockedSlotsAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db, searchable: true);
        var pitch = db.Pitches.Local.Single(p => p.VenueId == venue.Id);
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venue.Id, DayOfWeek.Sunday, new TimeOnly(16, 0), new TimeOnly(23, 0), 60, _now));
        db.BlockedSlots.Add(BlockedSlot.Create(Guid.NewGuid(), venue.Id, pitch.Id, Utc(16), Utc(17), "Maintenance", Guid.NewGuid(), _now));
        await db.SaveChangesAsync();
        var booking = new UpcomingBookingRef(Guid.NewGuid(), "MLB-1", pitch.Id, Utc(17), Utc(18), "Confirmed");
        var lists = Lists(db, booking);
        await lists.AddFavoriteAsync(_userId, venue.Id);

        var card = (await lists.GetFavoritesAsync(_userId)).Single();

        card.NextAvailableStartUtc.Should().Be(Utc(18));
    }

    [Fact]
    public async Task RecentlyViewed_MovesToTop_AndIsBoundedAsync()
    {
        await using var db = NewDb();
        var venues = new List<Venue>();
        for (var i = 0; i < RecentlyViewedVenue.MaxPerUser + 3; i++)
        {
            venues.Add(await SeedVenueAsync(db, searchable: true));
        }

        var lists = Lists(db);
        foreach (var venue in venues)
        {
            _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
            await lists.RecordViewAsync(_userId, venue.Id);
        }

        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await lists.RecordViewAsync(_userId, venues[^5].Id);
        var recent = await lists.GetRecentlyViewedAsync(_userId);

        recent.Should().HaveCount(RecentlyViewedVenue.MaxPerUser);
        recent[0].VenueId.Should().Be(venues[^5].Id);
        recent.Select(r => r.VenueId).Should().NotContain(venues[0].Id, "the oldest views are dropped");
        (await db.RecentlyViewedVenues.CountAsync()).Should().Be(RecentlyViewedVenue.MaxPerUser);

        await lists.ClearRecentlyViewedAsync(_userId);
        (await lists.GetRecentlyViewedAsync(_userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Flags_MarkFavoritesAndViewsForExploreResultsAsync()
    {
        await using var db = NewDb();
        var favorite = await SeedVenueAsync(db, searchable: true);
        var viewed = await SeedVenueAsync(db, searchable: true);
        var other = await SeedVenueAsync(db, searchable: true);
        var lists = Lists(db);
        await lists.AddFavoriteAsync(_userId, favorite.Id);
        await lists.RecordViewAsync(_userId, viewed.Id);

        var flags = await lists.GetFlagsAsync(_userId, [favorite.Id, viewed.Id, other.Id]);

        flags[favorite.Id].Should().Be(new VenueEngagementFlags(true, false));
        flags[viewed.Id].Should().Be(new VenueEngagementFlags(false, true));
        flags[other.Id].Should().Be(new VenueEngagementFlags(false, false));
    }

    private static DateTime Utc(int cairoHour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 1, cairoHour, 0, 0, DateTimeKind.Unspecified), _cairo);

    private static async Task<Venue> SeedVenueAsync(VenuesDbContext db, bool searchable)
    {
        var id = Guid.NewGuid();
        var venue = Venue.Create(id, "Club", $"club-{id:N}", null, Guid.NewGuid(), "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        if (searchable)
        {
            venue.SubmitForApproval(venue.OwnerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        await db.SaveChangesAsync();
        return venue;
    }

    private PlayerVenueListHandlers Lists(VenuesDbContext db, params UpcomingBookingRef[] bookings) => new(db, new FakeBookings(bookings), _clock);

    private static VenuesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeBookings(IReadOnlyList<UpcomingBookingRef> bookings) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(bookings.Where(b => pitchIds.Contains(b.PitchId)).ToList());
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
