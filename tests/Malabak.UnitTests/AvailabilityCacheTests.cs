using System.Collections.Concurrent;
using FluentAssertions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AvailabilityCacheTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _thursday = new(2026, 11, 5);

    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly InMemoryStore _store = new();
    private readonly CountingBookings _bookings = new();
    private Guid _venueId;

    private static DateTime Local(int day, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, hour, 0, 0, DateTimeKind.Unspecified), _cairo);

    [Fact]
    public async Task SecondRead_IsServedFromCache_WithTheSameETagAsync()
    {
        await using var db = await SeedAsync();
        var cache = Cache(db);

        var first = await cache.GetAsync(_venueId, _thursday, null);
        var second = await cache.GetAsync(_venueId, _thursday, null);

        _bookings.Calls.Should().Be(1);
        second.Value.ETag.Should().Be(first.Value.ETag);
    }

    [Fact]
    public async Task PrefetchedVersions_ReplacePerVenueRoundTripsAsync()
    {
        await using var db = await SeedAsync();
        var cache = Cache(db);

        await cache.PrefetchVersionsAsync([_venueId, Guid.NewGuid(), Guid.NewGuid()]);
        await cache.GetAsync(_venueId, _thursday, null);
        await cache.GetAsync(_venueId, _thursday.AddDays(1), null);

        _store.BatchVersionReads.Should().Be(1);
        _store.SingleVersionReads.Should().Be(0, "the prefetched version is reused");
    }

    [Fact]
    public async Task PrefetchFailure_FallsBackToPerVenueReadsAsync()
    {
        await using var db = await SeedAsync();
        var cache = Cache(db);
        _store.Broken = true;
        await cache.PrefetchVersionsAsync([_venueId]);
        _store.Broken = false;

        (await cache.GetAsync(_venueId, _thursday, null)).IsSuccess.Should().BeTrue();
        _store.SingleVersionReads.Should().Be(1);
    }

    [Fact]
    public async Task VenueChanges_InvalidateOnSave_SoABlockIsNeverShownAsFreeAsync()
    {
        await using var db = await SeedAsync();
        var cache = Cache(db);
        var before = await cache.GetAsync(_venueId, _thursday, null);

        db.BlockedSlots.Add(BlockedSlot.Create(Guid.NewGuid(), _venueId, _pitchId, Local(5, 19), Local(5, 20), "Maintenance", Guid.NewGuid(), _now));
        await db.SaveChangesAsync();
        var after = await cache.GetAsync(_venueId, _thursday, null);

        after.Value.ETag.Should().NotBe(before.Value.ETag);
        after.Value.Availability.Pitches[0].Slots.Single(s => s.StartUtc == Local(5, 19)).State.Should().Be(SlotState.Blocked);
    }

    [Fact]
    public async Task BookingInvalidation_ShowsTheBookedSlotImmediatelyAsync()
    {
        await using var db = await SeedAsync();
        var cache = Cache(db);
        (await cache.GetAsync(_venueId, _thursday, null)).Value.Availability.Pitches[0].Slots
            .Single(s => s.StartUtc == Local(5, 20)).State.Should().Be(SlotState.Available);

        _bookings.Items.Add(new UpcomingBookingRef(Guid.NewGuid(), "MLB-1", _pitchId, Local(5, 20), Local(5, 21), "Confirmed"));
        await new AvailabilityInvalidator(_store, db, NullLogger<AvailabilityInvalidator>.Instance).InvalidatePitchAsync(_pitchId);

        (await cache.GetAsync(_venueId, _thursday, null)).Value.Availability.Pitches[0].Slots
            .Single(s => s.StartUtc == Local(5, 20)).State.Should().Be(SlotState.Booked);
    }

    [Fact]
    public async Task ConcurrentMisses_AreComputedOnceAsync()
    {
        await using var db = await SeedAsync();
        var cache = Cache(db);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(_venueId, _thursday, null)));

        results.Should().OnlyContain(r => r.IsSuccess);
        _bookings.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Ttl_NeverOutlivesTheEarliestHoldExpiryAsync()
    {
        await using var db = await SeedAsync();
        _bookings.Items.Add(new UpcomingBookingRef(Guid.NewGuid(), "MLB-2", _pitchId, Local(5, 21), Local(5, 22), "PendingDeposit", _now.AddSeconds(8)));

        await Cache(db).GetAsync(_venueId, _thursday, null);

        _store.LastTtl.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task BrokenStore_FailsOpen_ToADirectComputationAsync()
    {
        await using var db = await SeedAsync();
        _store.Broken = true;

        var result = await Cache(db).GetAsync(_venueId, _thursday, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Availability.Pitches.Should().ContainSingle();
    }

    private CachedVenueAvailability Cache(VenuesDbContext db) =>
        new(new VenueAvailabilityQuery(db, _bookings, new FixedClock()), _store, new FixedClock(), NullLogger<CachedVenueAvailability>.Instance);

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(
            new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, availabilityCache: _store);
        var venue = Venue.Create(Guid.NewGuid(), "Club", $"club-{Guid.NewGuid():N}", null, Guid.NewGuid(), "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        _venueId = venue.Id;
        venue.SubmitForApproval(venue.OwnerId, null, _now);
        venue.Approve(Guid.NewGuid(), null, _now);
        venue.SetPublished(true, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(_pitchId, venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venue.Id, DayOfWeek.Thursday, new TimeOnly(18, 0), new TimeOnly(23, 0), 60, _now));
        db.PriceRules.Add(PriceRule.Create(venue.Id,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 400, null, null, null, null, null, null, null), _now).Value);
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class InMemoryStore : IAvailabilityCacheStore
    {
        private readonly ConcurrentDictionary<Guid, long> _versions = new();
        private readonly ConcurrentDictionary<string, string> _entries = new();

        private int _singleVersionReads;
        private int _batchVersionReads;

        public bool Broken { get; set; }
        public TimeSpan? LastTtl { get; private set; }
        public int SingleVersionReads => _singleVersionReads;
        public int BatchVersionReads => _batchVersionReads;

        public Task<long> GetVersionAsync(Guid venueId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _singleVersionReads);
            return Broken ? throw new InvalidOperationException("store down") : Task.FromResult(_versions.GetValueOrDefault(venueId));
        }

        public Task<IReadOnlyDictionary<Guid, long>> GetVersionsAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _batchVersionReads);
            return Broken
                ? throw new InvalidOperationException("store down")
                : Task.FromResult<IReadOnlyDictionary<Guid, long>>(venueIds.ToDictionary(id => id, id => _versions.GetValueOrDefault(id)));
        }

        public Task<long> IncrementVersionAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Broken ? throw new InvalidOperationException("store down") : Task.FromResult(_versions.AddOrUpdate(venueId, 1, (_, v) => v + 1));

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken); // simulate network latency so concurrent misses overlap
            return _entries.TryGetValue(key, out var value) ? value : null;
        }

        public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _entries[key] = value;
            LastTtl = ttl;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingBookings : IUpcomingBookingsReader
    {
        private int _calls;

        public List<UpcomingBookingRef> Items { get; } = [];
        public int Calls => _calls;

        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(Items.Where(b => b.EndUtc > fromUtc && b.StartUtc < toUtc).ToList());
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
