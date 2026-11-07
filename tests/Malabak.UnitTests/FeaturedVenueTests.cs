using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Application.Featured;
using Malabak.Modules.Venues.Application.Home;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class FeaturedVenueTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Flags _flags = new();
    private readonly Audits _audit = new();
    private readonly Clock _clock = new();

    [Fact]
    public void Placements_NeedAValidScheduleOfAtMost90Days()
    {
        var venue = Guid.NewGuid();
        FeaturedPlacement.Schedule(venue, FeaturedPlacementSlot.Home, null, _now, _now, 0, _admin, _now).Error.Should().Be(FeaturedErrors.InvalidSchedule);
        FeaturedPlacement.Schedule(venue, FeaturedPlacementSlot.Home, null, _now, _now.AddDays(91), 0, _admin, _now).Error.Should().Be(FeaturedErrors.InvalidSchedule);
        FeaturedPlacement.Schedule(venue, FeaturedPlacementSlot.Home, new string('x', 81), _now, _now.AddDays(1), 0, _admin, _now).Error.Should().Be(FeaturedErrors.Invalid);

        var placement = FeaturedPlacement.Schedule(venue, FeaturedPlacementSlot.Explore, "  Weekend offer ", _now.AddDays(-1), _now.AddDays(1), 2, _admin, _now).Value;
        placement.Title.Should().Be("Weekend offer");
        placement.IsLiveAt(_now).Should().BeTrue();
        placement.Cancel(_now).IsSuccess.Should().BeTrue();
        placement.IsLiveAt(_now).Should().BeFalse();
    }

    [Fact]
    public async Task OnlyLiveVenues_CanBeFeatured_AndEachSlotHasALimitAsync()
    {
        await using var db = NewDb();
        var live = AddVenue(db, "Live Arena", publish: true);
        var hidden = AddVenue(db, "Draft Arena", publish: false);
        await db.SaveChangesAsync();
        var service = Service(db);

        (await service.CreateAsync(new FeaturedPlacementInput(hidden, "Explore", null, _now, _now.AddDays(7), 0), _admin)).Error.Should().Be(FeaturedErrors.VenueNotLive);
        (await service.CreateAsync(new FeaturedPlacementInput(live, "Banner", null, _now, _now.AddDays(7), 0), _admin)).Error.Should().Be(FeaturedErrors.Invalid);

        for (var i = 0; i < FeaturedVenueService.MaxConcurrentPerSlot; i++)
        {
            (await service.CreateAsync(new FeaturedPlacementInput(live, "Explore", null, _now, _now.AddDays(7), i), _admin)).IsSuccess.Should().BeTrue();
        }

        (await service.CreateAsync(new FeaturedPlacementInput(live, "explore", null, _now.AddDays(1), _now.AddDays(2), 0), _admin)).Error.Should().Be(FeaturedErrors.SlotFull);
        (await service.CreateAsync(new FeaturedPlacementInput(live, "Home", null, _now, _now.AddDays(7), 0), _admin)).IsSuccess.Should().BeTrue("other slots are separate");
        _audit.Entries.Should().HaveCount(FeaturedVenueService.MaxConcurrentPerSlot + 1).And.OnlyContain(a => a.Action == "admin.featured.created");
    }

    [Fact]
    public async Task PlayersSeeFeaturedVenues_OnlyWhileTheFlagIsOn_InAdminOrder_WithTheSponsoredLabelAsync()
    {
        await using var db = NewDb();
        var first = AddVenue(db, "Alpha Arena", publish: true);
        var second = AddVenue(db, "Beta Arena", publish: true);
        await db.SaveChangesAsync();
        var service = Service(db);
        await service.CreateAsync(new FeaturedPlacementInput(first, "Explore", null, _now.AddHours(-1), _now.AddDays(3), 5), _admin);
        await service.CreateAsync(new FeaturedPlacementInput(second, "Explore", "Ramadan nights", _now.AddHours(-1), _now.AddDays(3), 1), _admin);
        var future = (await service.CreateAsync(new FeaturedPlacementInput(first, "Explore", null, _now.AddDays(1), _now.AddDays(3), 0), _admin)).Value;

        (await service.LiveAsync(FeaturedPlacementSlot.Explore, null)).Should().BeEmpty("the venues.featured flag is off");

        _flags.On.Add(FeatureFlagKeys.FeaturedVenues);
        var items = await service.LiveAsync(FeaturedPlacementSlot.Explore, null);

        items.Select(i => i.Venue.VenueId).Should().Equal(second, first);
        items.Should().OnlyContain(i => i.Label == FeaturedPlacement.SponsoredLabel);
        items[0].Title.Should().Be("Ramadan nights");
        items[1].Title.Should().Be("Alpha Arena", "without a title the venue name is used");
        (await service.ListAsync(includeEnded: false)).Should().Contain(p => p.Id == future.Id && !p.IsLive);
    }

    [Fact]
    public async Task HomeFeed_FillsTheSponsoredSection_FromHomePlacementsAsync()
    {
        await using var db = NewDb();
        var venue = AddVenue(db, "Home Arena", publish: true);
        await db.SaveChangesAsync();
        var service = Service(db);
        await service.CreateAsync(new FeaturedPlacementInput(venue, "Home", null, _now.AddHours(-1), _now.AddDays(1), 0), _admin);
        _flags.On.Add(FeatureFlagKeys.SponsoredHomeItems);

        var feed = new HomeFeed(Explore(db), db, new HomeFeedSettings(false), _flags, _clock, NullLogger<HomeFeed>.Instance, service);
        var result = (await feed.GetAsync(new HomeFeedQuery(), null)).Value;

        result.Sponsored!.Items.Should().ContainSingle(i => i.Venue.VenueId == venue && i.Label == FeaturedPlacement.SponsoredLabel);
    }

    private FeaturedVenueService Service(VenuesDbContext db) => new(db, Explore(db), _flags, _audit, _clock);

    private VenueExplore Explore(VenuesDbContext db) =>
        new(db, new CachedVenueAvailability(new VenueAvailabilityQuery(db, new NoBookings(), _clock), new NoCache(), _clock, NullLogger<CachedVenueAvailability>.Instance), _clock);

    private static Guid AddVenue(VenuesDbContext db, string name, bool publish)
    {
        var venue = Venue.Create(Guid.NewGuid(), name, $"v-{Guid.NewGuid():N}", null, Guid.NewGuid(), "Addr", "Cairo", "Maadi", 29.96, 31.25, "+201000000001", null, null, _now);
        if (publish)
        {
            venue.SubmitForApproval(venue.OwnerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        return venue.Id;
    }

    private static VenuesDbContext NewDb() => new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Flags : IFeatureFlags
    {
        public HashSet<string> On { get; } = [];

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(On.Contains(key));
    }

    private sealed class Audits : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
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

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
