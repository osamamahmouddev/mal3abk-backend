using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Edge-case test suite for Venue Explore/Map Search, Query/Pagination Bounds,
/// Approval Visibility Filtering, and Cached Public Availability (P3).
/// </summary>
public sealed class VenueExploreAndAvailabilityEdgeCaseTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly FixedClock _clock = new();

    private static VenuesDbContext CreateVenuesDb()
    {
        var options = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new VenuesDbContext(options);
    }

    [Theory]
    [InlineData(0, 20)] // Page 0
    [InlineData(-1, 20)] // Negative page
    [InlineData(1, 0)] // Page size 0
    [InlineData(1, -5)] // Negative page size
    [InlineData(1, 51)] // Page size over max 50
    [InlineData(1, 100)] // Page size over max 50
    public async Task Search_RejectsInvalidPaginationBoundariesAsync(int page, int pageSize)
    {
        await using var db = CreateVenuesDb();
        var cache = new CachedVenueAvailability(
            new VenueAvailabilityQuery(db, new FakeBookingsReader([]), _clock),
            new InMemoryAvailabilityCacheStore(),
            _clock,
            NullLogger<CachedVenueAvailability>.Instance);
        var explore = new VenueExplore(db, cache, _clock);

        var query = new ExploreQuery(Page: page, PageSize: pageSize);
        var result = await explore.SearchAsync(query);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ExploreErrors.InvalidQuery.Code);
    }

    [Fact]
    public async Task Search_RejectsInvalidCoordinatesRadiusOrUnknownCodesAsync()
    {
        await using var db = CreateVenuesDb();
        var cache = new CachedVenueAvailability(
            new VenueAvailabilityQuery(db, new FakeBookingsReader([]), _clock),
            new InMemoryAvailabilityCacheStore(),
            _clock,
            NullLogger<CachedVenueAvailability>.Instance);
        var explore = new VenueExplore(db, cache, _clock);

        // 1. Latitude without Longitude
        var coordResult = await explore.SearchAsync(new ExploreQuery(Latitude: 30.0444, Longitude: null));
        coordResult.IsFailure.Should().BeTrue();
        coordResult.Error.Code.Should().Be(ExploreErrors.InvalidQuery.Code);

        // 2. Radius > 100 km
        var radiusResult = await explore.SearchAsync(new ExploreQuery(Latitude: 30.0444, Longitude: 31.2357, RadiusKm: 150));
        radiusResult.IsFailure.Should().BeTrue();
        radiusResult.Error.Code.Should().Be(ExploreErrors.InvalidQuery.Code);

        // 3. Unknown surface code
        var surfaceResult = await explore.SearchAsync(new ExploreQuery(Surface: "WoodenTiles"));
        surfaceResult.IsFailure.Should().BeTrue();
        surfaceResult.Error.Code.Should().Be(ExploreErrors.UnknownCode.Code);

        // 4. Unknown facility code
        var facilityResult = await explore.SearchAsync(new ExploreQuery(Facilities: ["Helipad"]));
        facilityResult.IsFailure.Should().BeTrue();
        facilityResult.Error.Code.Should().Be(ExploreErrors.UnknownCode.Code);
    }

    [Fact]
    public async Task Search_ReturnsOnlyApprovedAndPublishedVenuesAsync()
    {
        await using var db = CreateVenuesDb();

        // 1. Approved & Published venue (Searchable)
        var approvedPub = Venue.Create(Guid.NewGuid(), "Approved Club", "approved-club", null, _ownerId, "Addr 1", "Cairo", "Maadi", 30.0, 31.0, "+201000000001", null, null, _now);
        approvedPub.SubmitForApproval(_ownerId, null, _now);
        approvedPub.Approve(Guid.NewGuid(), null, _now);
        approvedPub.SetPublished(true, _now);
        var pitch1 = Pitch.Create(Guid.NewGuid(), approvedPub.Id, "P1", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        db.Venues.Add(approvedPub);
        db.Pitches.Add(pitch1);

        // 2. Draft venue (Not Searchable)
        var draft = Venue.Create(Guid.NewGuid(), "Draft Club", "draft-club", null, _ownerId, "Addr 2", "Cairo", "Maadi", 30.0, 31.0, "+201000000002", null, null, _now);
        db.Venues.Add(draft);

        // 3. Approved but Unpublished venue (Not Searchable)
        var unpub = Venue.Create(Guid.NewGuid(), "Unpublished Club", "unpub-club", null, _ownerId, "Addr 3", "Cairo", "Maadi", 30.0, 31.0, "+201000000003", null, null, _now);
        unpub.SubmitForApproval(_ownerId, null, _now);
        unpub.Approve(Guid.NewGuid(), null, _now);
        // IsPublished is false
        db.Venues.Add(unpub);

        // 4. Suspended venue (Not Searchable)
        var suspended = Venue.Create(Guid.NewGuid(), "Suspended Club", "suspended-club", null, _ownerId, "Addr 4", "Cairo", "Maadi", 30.0, 31.0, "+201000000004", null, null, _now);
        suspended.SubmitForApproval(_ownerId, null, _now);
        suspended.Approve(Guid.NewGuid(), null, _now);
        suspended.SetPublished(true, _now);
        suspended.Suspend(Guid.NewGuid(), "Investigation", _now);
        db.Venues.Add(suspended);

        await db.SaveChangesAsync();

        var cache = new CachedVenueAvailability(
            new VenueAvailabilityQuery(db, new FakeBookingsReader([]), _clock),
            new InMemoryAvailabilityCacheStore(),
            _clock,
            NullLogger<CachedVenueAvailability>.Instance);
        var explore = new VenueExplore(db, cache, _clock);

        var result = await explore.SearchAsync(new ExploreQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle()
            .Which.VenueId.Should().Be(approvedPub.Id);
    }

    [Fact]
    public async Task VenueAvailabilityQuery_FailsForUnapprovedOrUnpublishedVenueAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(Guid.NewGuid(), "Draft Arena", "draft-arena", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        db.Venues.Add(venue);
        await db.SaveChangesAsync();

        var query = new VenueAvailabilityQuery(db, new FakeBookingsReader([]), _clock);
        var result = await query.GetAsync(venue.Id, DateOnly.FromDateTime(_now), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(AvailabilityErrors.VenueNotFound.Code);
    }

    [Fact]
    public async Task VenueAvailabilityQuery_DateOutOfRange_IsRejectedAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(Guid.NewGuid(), "Valid Arena", "valid-arena", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venue.SubmitForApproval(_ownerId, null, _now);
        venue.Approve(Guid.NewGuid(), null, _now);
        venue.SetPublished(true, _now);
        var pitch = Pitch.Create(Guid.NewGuid(), venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(pitch);
        await db.SaveChangesAsync();

        var query = new VenueAvailabilityQuery(db, new FakeBookingsReader([]), _clock);

        // Date in the distant past (> 1 day before today)
        var pastDate = _clock.CairoToday.AddDays(-5);
        var pastResult = await query.GetAsync(venue.Id, pastDate, null);
        pastResult.IsFailure.Should().BeTrue();
        pastResult.Error.Code.Should().Be(AvailabilityErrors.DateOutOfRange.Code);

        // Date beyond maximum advance days (default 30 days)
        var futureDate = _clock.CairoToday.AddDays(45);
        var futureResult = await query.GetAsync(venue.Id, futureDate, null);
        futureResult.IsFailure.Should().BeTrue();
        futureResult.Error.Code.Should().Be(AvailabilityErrors.DateOutOfRange.Code);
    }

    [Fact]
    public async Task CachedVenueAvailability_CacheBusting_OnVersionIncrementAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(Guid.NewGuid(), "Club", "club", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venue.SubmitForApproval(_ownerId, null, _now);
        venue.Approve(Guid.NewGuid(), null, _now);
        venue.SetPublished(true, _now);
        var pitch = Pitch.Create(Guid.NewGuid(), venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(pitch);
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venue.Id, DayOfWeek.Tuesday, new TimeOnly(10, 0), new TimeOnly(22, 0), 60, _now));
        db.PriceRules.Add(PriceRule.Create(venue.Id, new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 200m, null, null, null, null, null, null, null), _now).Value);
        await db.SaveChangesAsync();

        var store = new InMemoryAvailabilityCacheStore();
        var cached = new CachedVenueAvailability(
            new VenueAvailabilityQuery(db, new FakeBookingsReader([]), _clock),
            store,
            _clock,
            NullLogger<CachedVenueAvailability>.Instance);

        var date = _clock.CairoToday;

        // First call caches the result under version 0
        var res1 = await cached.GetAsync(venue.Id, date, null);
        res1.IsSuccess.Should().BeTrue();
        var etag1 = res1.Value.ETag;

        // Block a slot and increment version (simulating slot blocked event invalidating cache)
        db.BlockedSlots.Add(BlockedSlot.Create(Guid.NewGuid(), venue.Id, pitch.Id, _now.AddHours(1), _now.AddHours(2), "Maintenance", Guid.NewGuid(), _now));
        await db.SaveChangesAsync();
        await store.IncrementVersionAsync(venue.Id);

        // Second call with bumped version bypasses v0 cache key and computes fresh availability
        var res2 = await cached.GetAsync(venue.Id, date, null);
        res2.IsSuccess.Should().BeTrue();
        res2.Value.ETag.Should().NotBe(etag1, "Cache version change must invalidate prior cached availability ETag");
    }

    private sealed class InMemoryAvailabilityCacheStore : IAvailabilityCacheStore
    {
        private readonly Dictionary<Guid, long> _versions = [];
        private readonly Dictionary<string, string> _values = [];

        public Task<long> GetVersionAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.GetValueOrDefault(venueId, 0L));

        public Task<long> IncrementVersionAsync(Guid venueId, CancellationToken cancellationToken = default)
        {
            var next = _versions.GetValueOrDefault(venueId, 0L) + 1;
            _versions[venueId] = next;
            return Task.FromResult(next);
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBookingsReader(IReadOnlyList<UpcomingBookingRef> bookings) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(bookings.Where(b => pitchIds.Contains(b.PitchId)).ToList());
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
