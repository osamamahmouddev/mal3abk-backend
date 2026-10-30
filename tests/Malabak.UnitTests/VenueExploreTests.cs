using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueExploreTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _thursday = new(2026, 11, 5);

    // Maadi (Cairo) is the search origin.
    private const double _originLat = 29.9602;
    private const double _originLng = 31.2569;

    private Guid _maadi;
    private Guid _zamalek;
    private Guid _alexandria;

    [Fact]
    public void Haversine_MatchesKnownDistances()
    {
        GeoMath.HaversineKm(30.0444, 31.2357, 31.2001, 29.9187).Should().BeApproximately(179, 2, "Cairo to Alexandria");
        GeoMath.HaversineKm(_originLat, _originLng, _originLat, _originLng).Should().Be(0);

        var box = GeoMath.BoundingBox(_originLat, _originLng, 10);
        GeoMath.HaversineKm(_originLat, _originLng, box.MaxLatitude, _originLng).Should().BeApproximately(10, 0.1);
        GeoMath.HaversineKm(_originLat, _originLng, _originLat, box.MaxLongitude).Should().BeApproximately(10, 0.1);
    }

    [Fact]
    public async Task NearbySearch_KeepsVenuesInsideTheRadius_NearestFirst_AndHidesUnpublishedAsync()
    {
        await using var db = await SeedAsync();

        var result = (await Explore(db).SearchAsync(new ExploreQuery(_originLat, _originLng, RadiusKm: 25))).Value;

        result.Items.Select(v => v.VenueId).Should().Equal(_maadi, _zamalek);
        result.Items[0].DistanceKm.Should().BeLessThan(1);
        result.Items[1].DistanceKm.Should().BeInRange(10, 13);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Filters_PitchType_Surface_Facilities_Budget_CityAndAreaAsync()
    {
        await using var db = await SeedAsync();
        var explore = Explore(db);

        async Task<IEnumerable<Guid>> Ids(ExploreQuery q) => (await explore.SearchAsync(q)).Value.Items.Select(v => v.VenueId);

        (await Ids(new ExploreQuery(PitchType: "7v7"))).Should().Equal(_zamalek);
        (await Ids(new ExploreQuery(Surface: "ArtificialTurf"))).Should().BeEquivalentTo([_maadi, _alexandria]);
        (await Ids(new ExploreQuery(Facilities: ["wifi", "Parking"]))).Should().Equal(_zamalek);
        (await Ids(new ExploreQuery(MaxPrice: 450))).Should().BeEquivalentTo([_maadi, _alexandria]);
        (await Ids(new ExploreQuery(City: "الإسكندرية"))).Should().Equal(_alexandria);
        (await Ids(new ExploreQuery(City: "Cairo", Area: "maadi"))).Should().Equal(_maadi);
    }

    [Fact]
    public async Task DateAndTimeRange_KeepOnlyVenuesWithAFreeSlot_AndShowTheFirstOneAsync()
    {
        await using var db = await SeedAsync();

        var items = (await Explore(db).SearchAsync(new ExploreQuery(Date: _thursday, From: new TimeOnly(19, 0), To: new TimeOnly(21, 0)))).Value.Items;

        items.Should().ContainSingle().Which.VenueId.Should().Be(_maadi);
        items[0].NextAvailableStartUtc.Should().Be(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 5, 19, 0, 0, DateTimeKind.Unspecified), _cairo));
    }

    [Fact]
    public async Task Sorting_AndPaginationAsync()
    {
        await using var db = await SeedAsync();
        var explore = Explore(db);

        (await explore.SearchAsync(new ExploreQuery(Sort: ExploreSort.Price))).Value.Items.Select(v => v.FromHourlyPrice).Should().Equal(300m, 400m, 700m);
        (await explore.SearchAsync(new ExploreQuery(Sort: ExploreSort.Popularity))).Value.Items[0].VenueId.Should().Be(_zamalek, "it has the most favorites");

        var page2 = (await explore.SearchAsync(new ExploreQuery(Sort: ExploreSort.Price, Page: 2, PageSize: 1))).Value;
        page2.Items.Should().ContainSingle().Which.FromHourlyPrice.Should().Be(400m);
        page2.HasMore.Should().BeTrue();
        page2.TotalCount.Should().Be(3);
    }

    [Theory]
    [InlineData(30.0, null, null, null, null)]
    [InlineData(30.0, 31.0, 500.0, null, null)]
    [InlineData(null, null, null, "4v4", null)]
    [InlineData(null, null, null, null, "sauna")]
    public async Task InvalidOrUnknownFilters_AreRejectedAsync(double? lat, double? lng, double? radius, string? pitchType, string? facility)
    {
        await using var db = await SeedAsync();

        var result = await Explore(db).SearchAsync(new ExploreQuery(lat, lng, radius, PitchType: pitchType, Facilities: facility is null ? null : [facility]));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task TimeRangeWithoutDate_AndHugePages_AreRejectedAsync()
    {
        await using var db = await SeedAsync();

        (await Explore(db).SearchAsync(new ExploreQuery(From: new TimeOnly(18, 0)))).Error.Should().Be(ExploreErrors.InvalidQuery);
        (await Explore(db).SearchAsync(new ExploreQuery(PageSize: 500))).Error.Should().Be(ExploreErrors.InvalidQuery);
    }

    [Fact]
    public async Task Map_ReturnsPinsInsideTheBounds_WithFavoriteFlagAsync()
    {
        await using var db = await SeedAsync();
        var player = Guid.NewGuid();
        db.FavoriteVenues.Add(FavoriteVenue.Create(player, _maadi, _now));
        await db.SaveChangesAsync();

        var southCairo = new MapBounds(29.90, 31.20, 30.00, 31.30);
        var result = (await Explore(db).MapAsync(new ExploreQuery(Bounds: southCairo), player)).Value;

        result.Pins.Should().ContainSingle().Which.Should().Match<MapVenuePin>(p => p.VenueId == _maadi && p.IsFavorite);
        result.Truncated.Should().BeFalse();

        var cairo = new MapBounds(29.90, 31.10, 30.20, 31.40);
        (await Explore(db).MapAsync(new ExploreQuery(Bounds: cairo))).Value.Pins.Select(p => p.VenueId).Should().BeEquivalentTo([_zamalek, _maadi]);
    }

    [Theory]
    [InlineData(30.0, 31.0, 29.0, 32.0)]
    [InlineData(29.0, 30.0, 32.0, 31.0)]
    [InlineData(29.0, 31.0, 30.0, 31.0)]
    public async Task Map_RejectsMissingInvertedOrHugeBoundsAsync(double south, double west, double north, double east)
    {
        await using var db = await SeedAsync();

        (await Explore(db).MapAsync(new ExploreQuery(Bounds: new MapBounds(south, west, north, east)))).Error.Should().Be(ExploreErrors.InvalidQuery);
        (await Explore(db).MapAsync(new ExploreQuery())).Error.Should().Be(ExploreErrors.InvalidQuery);
    }

    [Fact]
    public async Task Cards_CarryFavoriteFlag_AndNextFreeSlotTodayOrTomorrowAsync()
    {
        await using var db = await SeedAsync();
        var player = Guid.NewGuid();
        db.FavoriteVenues.Add(FavoriteVenue.Create(player, _zamalek, _now));
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), _maadi, DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(23, 0), 60, _now));
        await db.SaveChangesAsync();

        var items = (await Explore(db).SearchAsync(new ExploreQuery(Sort: ExploreSort.Name), player)).Value.Items;

        items.Single(v => v.VenueId == _zamalek).IsFavorite.Should().BeTrue();
        items.Single(v => v.VenueId == _maadi).IsFavorite.Should().BeFalse();
        items.Single(v => v.VenueId == _maadi).NextAvailableStartUtc
            .Should().Be(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Unspecified), _cairo), "Monday is tomorrow");
        items.Single(v => v.VenueId == _alexandria).NextAvailableStartUtc.Should().BeNull("no hours today or tomorrow");
    }

    [Fact]
    public async Task EmptyResult_SuggestsWhichFilterToRelaxAsync()
    {
        await using var db = await SeedAsync();

        var result = (await Explore(db).SearchAsync(new ExploreQuery(_originLat, _originLng, RadiusKm: 5, PitchType: "7v7"))).Value;

        result.Items.Should().BeEmpty();
        result.Suggestions.Should().BeEquivalentTo([
            new ExploreSuggestion("widen_radius", "Search a wider area (up to 100 km).", 1),
            new ExploreSuggestion("any_pitch", "Try any pitch size and surface.", 1)]);

        (await Explore(db).SearchAsync(new ExploreQuery(_originLat, _originLng))).Value.Suggestions.Should().BeEmpty("there were results");
    }

    private static VenueExplore Explore(VenuesDbContext db)
    {
        var clock = new FixedClock();
        var query = new VenueAvailabilityQuery(db, new NoBookings(), clock);
        return new VenueExplore(db, new CachedVenueAvailability(query, new NoCache(), clock, NullLogger<CachedVenueAvailability>.Instance), clock);
    }

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        _maadi = AddVenue(db, "Maadi Arena", "Cairo", "Maadi", 29.9610, 31.2580, ["parking"], "ArtificialTurf", 5, 400, schedule: true);
        _zamalek = AddVenue(db, "Zamalek Club", "Cairo", "Zamalek", 30.0626, 31.2197, ["parking", "wifi"], "NaturalGrass", 7, 700, schedule: false);
        _alexandria = AddVenue(db, "Alex Pitch", "Alexandria", "Smouha", null, null, [], "ArtificialTurf", 5, 300, schedule: false);
        AddVenue(db, "Hidden", "Cairo", "Maadi", 29.9605, 31.2570, ["parking"], "ArtificialTurf", 5, 100, schedule: true, publish: false);

        db.FavoriteVenues.Add(FavoriteVenue.Create(Guid.NewGuid(), _zamalek, _now));
        db.FavoriteVenues.Add(FavoriteVenue.Create(Guid.NewGuid(), _zamalek, _now));
        db.FavoriteVenues.Add(FavoriteVenue.Create(Guid.NewGuid(), _maadi, _now));
        await db.SaveChangesAsync();
        return db;
    }

    private static Guid AddVenue(
        VenuesDbContext db, string name, string city, string area, double? lat, double? lng, string[] facilities,
        string surface, int capacity, decimal price, bool schedule, bool publish = true)
    {
        var venue = Venue.Create(Guid.NewGuid(), name, $"v-{Guid.NewGuid():N}", null, Guid.NewGuid(), "Addr", city, area, lat, lng, "+201000000001", facilities, null, _now);
        if (publish)
        {
            venue.SubmitForApproval(venue.OwnerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venue.Id, "P1", surface, capacity, null, null, null, _now));
        if (schedule)
        {
            db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venue.Id, DayOfWeek.Thursday, new TimeOnly(18, 0), new TimeOnly(23, 0), 60, _now));
        }

        db.PriceRules.Add(PriceRule.Create(venue.Id,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", price, null, null, null, null, null, null, null), _now).Value);
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
