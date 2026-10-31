using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Application.Home;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class HomeFeedTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _player = Guid.NewGuid();
    private Guid _maadi;
    private Guid _zamalek;
    private Guid _hidden;

    [Fact]
    public async Task AnonymousWithLocation_GetsNearbyAndPopular_AndPlaceholdersAsync()
    {
        await using var db = await SeedAsync();

        var feed = (await Feed(db).GetAsync(new HomeFeedQuery(29.9602, 31.2569), null)).Value;

        feed.Nearby.Status.Should().Be(HomeSectionStatus.Ok);
        feed.Nearby.Items.Select(v => v.VenueId).Should().Equal(_maadi, _zamalek);
        feed.PopularNearYou.Items.Select(v => v.VenueId).Should().Equal(_zamalek, _maadi);
        feed.RecentlyViewed.Status.Should().Be(HomeSectionStatus.SignInRequired);
        feed.UpcomingBooking.Status.Should().Be(HomeSectionStatus.SignInRequired);
        feed.TeamActivity.Status.Should().Be(HomeSectionStatus.ComingSoon);
        feed.Sponsored.Should().BeNull("the sponsored flag is off");
    }

    [Fact]
    public async Task WithoutLocationOrArea_NearbyAsksForLocation_PopularStillShowsAsync()
    {
        await using var db = await SeedAsync();

        var feed = (await Feed(db).GetAsync(new HomeFeedQuery(), null)).Value;

        feed.Nearby.Status.Should().Be(HomeSectionStatus.LocationRequired);
        feed.PopularNearYou.Items.Should().NotBeEmpty();
        (await Feed(db).GetAsync(new HomeFeedQuery(City: "Cairo", Area: "Maadi"), null)).Value.Nearby.Items.Select(v => v.VenueId).Should().Equal(_maadi);
    }

    [Fact]
    public async Task SignedInPlayer_SeesRecentlyViewedNewestFirst_WithoutUnpublishedVenues_AndFavoriteFlagsAsync()
    {
        await using var db = await SeedAsync();

        var feed = (await Feed(db).GetAsync(new HomeFeedQuery(29.9602, 31.2569), _player)).Value;

        feed.RecentlyViewed.Items.Select(v => v.VenueId).Should().Equal(_zamalek, _maadi);
        feed.RecentlyViewed.Items.Should().NotContain(v => v.VenueId == _hidden);
        feed.Nearby.Items.Single(v => v.VenueId == _maadi).IsFavorite.Should().BeTrue();
        feed.UpcomingBooking.Status.Should().Be(HomeSectionStatus.ComingSoon);
    }

    [Fact]
    public async Task AFailingSection_IsReportedUnavailable_WhileTheRestStillLoadsAsync()
    {
        await using var db = await SeedAsync();
        var broken = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await broken.DisposeAsync();

        var feed = (await Feed(db, recentDb: broken).GetAsync(new HomeFeedQuery(29.9602, 31.2569), _player)).Value;

        feed.RecentlyViewed.Status.Should().Be(HomeSectionStatus.Unavailable);
        feed.Nearby.Status.Should().Be(HomeSectionStatus.Ok);
    }

    [Fact]
    public async Task SponsoredSection_AppearsOnlyWhenTheFlagIsOnAsync()
    {
        await using var db = await SeedAsync();

        var feed = (await Feed(db, sponsored: true).GetAsync(new HomeFeedQuery(), null)).Value;

        feed.Sponsored.Should().NotBeNull();
        feed.Sponsored!.Status.Should().Be(HomeSectionStatus.Empty);
    }

    [Fact]
    public async Task HalfACoordinate_IsRejectedAsync()
    {
        await using var db = await SeedAsync();

        (await Feed(db).GetAsync(new HomeFeedQuery(Latitude: 30), null)).Error.Should().Be(ExploreErrors.InvalidQuery);
    }

    private static HomeFeed Feed(VenuesDbContext db, VenuesDbContext? recentDb = null, bool sponsored = false)
    {
        var clock = new FixedClock();
        var availability = new CachedVenueAvailability(new VenueAvailabilityQuery(db, new NoBookings(), clock), new NoCache(), clock, NullLogger<CachedVenueAvailability>.Instance);
        return new HomeFeed(new VenueExplore(db, availability, clock), recentDb ?? db, new HomeFeedSettings(sponsored), clock, NullLogger<HomeFeed>.Instance);
    }

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _maadi = AddVenue(db, "Maadi Arena", "Maadi", 29.9610, 31.2580);
        _zamalek = AddVenue(db, "Zamalek Club", "Zamalek", 30.0626, 31.2197);
        _hidden = AddVenue(db, "Hidden", "Maadi", 29.9605, 31.2570, publish: false);

        db.FavoriteVenues.Add(FavoriteVenue.Create(_player, _maadi, _now));
        db.FavoriteVenues.Add(FavoriteVenue.Create(Guid.NewGuid(), _zamalek, _now));
        db.FavoriteVenues.Add(FavoriteVenue.Create(Guid.NewGuid(), _zamalek, _now));
        db.RecentlyViewedVenues.Add(RecentlyViewedVenue.Create(_player, _maadi, _now.AddHours(-3)));
        db.RecentlyViewedVenues.Add(RecentlyViewedVenue.Create(_player, _hidden, _now.AddHours(-2)));
        db.RecentlyViewedVenues.Add(RecentlyViewedVenue.Create(_player, _zamalek, _now.AddHours(-1)));
        await db.SaveChangesAsync();
        return db;
    }

    private static Guid AddVenue(VenuesDbContext db, string name, string area, double lat, double lng, bool publish = true)
    {
        var venue = Venue.Create(Guid.NewGuid(), name, $"v-{Guid.NewGuid():N}", null, Guid.NewGuid(), "Addr", "Cairo", area, lat, lng, "+201000000001", null, null, _now);
        if (publish)
        {
            venue.SubmitForApproval(venue.OwnerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        db.PriceRules.Add(PriceRule.Create(venue.Id,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 400, null, null, null, null, null, null, null), _now).Value);
        return venue.Id;
    }

    private sealed class NoBookings : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>([]);
    }

    private sealed class NoCache : IAvailabilityCacheStore
    {
        public Task<long> GetVersionAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task<long> IncrementVersionAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(1L);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
