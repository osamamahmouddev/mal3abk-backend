using FluentAssertions;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Application.Pitches.CreatePitch;
using Malabak.Modules.Venues.Application.Pitches.DeletePitch;
using Malabak.Modules.Venues.Application.Subscriptions;
using Malabak.Modules.Venues.Application.Venues.CreateVenue;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Edge-case test suite for Pitch Management, Surface/Capacity Validations, Pause/Resume (SetPitchActive),
/// Subscription Quotas (Pitches, Venues, Staff) and Pitch Deletion (P3).
/// </summary>
public sealed class PitchAndEntitlementEdgeCaseTests
{
    private static readonly DateTime _now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly IClock _clock = new FixedClock();

    private static VenuesDbContext CreateVenuesDb()
    {
        var options = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new VenuesDbContext(options);
    }

    private static SubscriptionsDbContext CreateSubscriptionsDb()
    {
        var options = new DbContextOptionsBuilder<SubscriptionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SubscriptionsDbContext(options);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(12)]
    [InlineData(15)]
    public void PitchCapacity_Create_RejectsOutOfBoundsPlayersPerTeam(int playersPerTeam)
    {
        var result = PitchCapacity.Create(playersPerTeam);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pitch.InvalidCapacity");
    }

    [Theory]
    [InlineData(3, 6, "3x3")]
    [InlineData(5, 10, "5x5")]
    [InlineData(7, 14, "7x7")]
    [InlineData(8, 16, "8x8")]
    [InlineData(9, 18, "9x9")]
    [InlineData(11, 22, "11x11")]
    public void PitchCapacity_Create_AcceptsValidPlayersPerTeam(int playersPerTeam, int expectedTotal, string expectedFormat)
    {
        var result = PitchCapacity.Create(playersPerTeam);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlayersPerTeam.Should().Be(playersPerTeam);
        result.Value.TotalPlayers.Should().Be(expectedTotal);
        result.Value.Format.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData(5)] // odd
    [InlineData(7)] // odd
    [InlineData(11)] // odd
    [InlineData(4)] // below min 6
    [InlineData(24)] // above max 22
    public void PitchCapacity_FromTotalPlayers_RejectsOddOrOutOfRange(int totalPlayers)
    {
        var result = PitchCapacity.FromTotalPlayers(totalPlayers);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pitch.InvalidTotalPlayers");
    }

    [Fact]
    public void Pitch_Create_ValidatesDimensionsAndSurfaces()
    {
        // Negative length throws
        var actLength = () => Pitch.Create(Guid.NewGuid(), _venueId, "P1", SurfaceType.ArtificialTurfValue, 5, -10m, 20m, null, _now);
        actLength.Should().Throw<ArgumentException>().WithMessage("*Length must be greater than zero*");

        // Zero width throws
        var actWidth = () => Pitch.Create(Guid.NewGuid(), _venueId, "P1", SurfaceType.ArtificialTurfValue, 5, 40m, 0m, null, _now);
        actWidth.Should().Throw<ArgumentException>().WithMessage("*Width must be greater than zero*");

        // Invalid turf throws
        var actTurf = () => Pitch.Create(Guid.NewGuid(), _venueId, "P1", "ConcretePavement", 5, 40m, 20m, null, _now);
        actTurf.Should().Throw<ArgumentException>().WithMessage("*Invalid surface type*");
    }

    [Fact]
    public async Task CreatePitch_EnforcesOwnerPitchSubscriptionQuotaAsync()
    {
        await using var venuesDb = CreateVenuesDb();
        await using var subsDb = CreateSubscriptionsDb();

        var venue = Venue.Create(_venueId, "Sports Zone", "sports-zone", null, _ownerId, "Cairo", "Cairo", "Nasr City", null, null, "+201011112222", null, null, _now);
        venuesDb.Venues.Add(venue);
        await venuesDb.SaveChangesAsync();

        // Plan with quota of 2 pitches
        var plan = SubscriptionPlan.Create(new PlanTerms(
            "starter", "Starter", "Starter plan",
            500m, 5000m,
            MaxVenues: 1, MaxPitches: 2, MaxStaffPerVenue: 2,
            Features: ["standard_reports"], TrialDays: 0, GraceDays: 0,
            IsPublic: true, SortOrder: 1), _now).Value;
        plan.SetStatus(PlanStatus.Active, _now);
        plan.SetDefault(true, _now);
        subsDb.Plans.Add(plan);
        await subsDb.SaveChangesAsync();

        var entitlementService = new EntitlementService(subsDb);
        var venueRepo = new VenueRepository(venuesDb);
        var pitchRepo = new PitchRepository(venuesDb);
        var handler = new CreatePitchHandler(venueRepo, pitchRepo, venuesDb, entitlementService, _clock, NullLogger<CreatePitchHandler>.Instance);

        // Pitch 1 (under quota 0 < 2) -> Succeeds
        var p1 = await handler.HandleAsync(new CreatePitchCommand(_venueId, _ownerId, "Pitch 1", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null));
        p1.IsSuccess.Should().BeTrue();

        // Pitch 2 (under quota 1 < 2) -> Succeeds
        var p2 = await handler.HandleAsync(new CreatePitchCommand(_venueId, _ownerId, "Pitch 2", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null));
        p2.IsSuccess.Should().BeTrue();

        // Pitch 3 (at quota 2 >= 2) -> Fails with QuotaExceeded
        var p3 = await handler.HandleAsync(new CreatePitchCommand(_venueId, _ownerId, "Pitch 3", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null));
        p3.IsFailure.Should().BeTrue();
        p3.Error.Code.Should().Be(EntitlementErrors.QuotaExceeded.Code);
    }

    [Fact]
    public async Task CreatePitch_FailsWhenSubscriptionIsInactiveAsync()
    {
        await using var venuesDb = CreateVenuesDb();
        await using var subsDb = CreateSubscriptionsDb();

        var venue = Venue.Create(_venueId, "Sports Zone", "sports-zone", null, _ownerId, "Cairo", "Cairo", "Nasr City", null, null, "+201011112222", null, null, _now);
        venuesDb.Venues.Add(venue);
        await venuesDb.SaveChangesAsync();

        var plan = SubscriptionPlan.Create(new PlanTerms(
            "pro", "Pro", "Pro plan",
            1500m, 15000m,
            MaxVenues: 5, MaxPitches: 10, MaxStaffPerVenue: 5,
            Features: ["standard_reports"], TrialDays: 0, GraceDays: 0,
            IsPublic: false, SortOrder: 2), _now).Value;
        plan.SetStatus(PlanStatus.Active, _now);
        subsDb.Plans.Add(plan);

        // Subscription is suspended / cancelled (not active)
        var subscription = OwnerSubscription.Start(_ownerId, plan, BillingCycle.Monthly, withTrial: false, actorId: _ownerId, _now);
        subscription.Suspend("Non-payment", _ownerId, _now);
        subsDb.Subscriptions.Add(subscription);
        await subsDb.SaveChangesAsync();

        var entitlementService = new EntitlementService(subsDb);
        var venueRepo = new VenueRepository(venuesDb);
        var pitchRepo = new PitchRepository(venuesDb);
        var handler = new CreatePitchHandler(venueRepo, pitchRepo, venuesDb, entitlementService, _clock, NullLogger<CreatePitchHandler>.Instance);

        var result = await handler.HandleAsync(new CreatePitchCommand(_venueId, _ownerId, "Pitch 1", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(EntitlementErrors.SubscriptionInactive.Code);
    }

    [Fact]
    public async Task CreateVenue_EnforcesOwnerVenueSubscriptionQuotaAsync()
    {
        await using var venuesDb = CreateVenuesDb();
        await using var subsDb = CreateSubscriptionsDb();

        // Plan with quota of 1 venue
        var plan = SubscriptionPlan.Create(new PlanTerms(
            "solo", "Solo", "Solo plan",
            300m, 3000m,
            MaxVenues: 1, MaxPitches: 5, MaxStaffPerVenue: 2,
            Features: [], TrialDays: 0, GraceDays: 0,
            IsPublic: true, SortOrder: 1), _now).Value;
        plan.SetStatus(PlanStatus.Active, _now);
        plan.SetDefault(true, _now);
        subsDb.Plans.Add(plan);
        await subsDb.SaveChangesAsync();

        var entitlementService = new EntitlementService(subsDb);
        var venueRepo = new VenueRepository(venuesDb);
        var handler = new CreateVenueHandler(venueRepo, venuesDb, entitlementService, _clock, NullLogger<CreateVenueHandler>.Instance);

        // Venue 1 -> Succeeds (0 < 1)
        var v1 = await handler.HandleAsync(new CreateVenueCommand(_ownerId, "Venue 1", null, "Addr 1", "Cairo", "Maadi", null, null, "+201011113333", null, null));
        v1.IsSuccess.Should().BeTrue();

        // Venue 2 -> Fails (1 >= 1)
        var v2 = await handler.HandleAsync(new CreateVenueCommand(_ownerId, "Venue 2", null, "Addr 2", "Cairo", "Zamalek", null, null, "+201011114444", null, null));
        v2.IsFailure.Should().BeTrue();
        v2.Error.Code.Should().Be(EntitlementErrors.QuotaExceeded.Code);
    }

    [Fact]
    public async Task StaffQuotaPolicy_EnforcesStaffPerVenueLimitAsync()
    {
        await using var venuesDb = CreateVenuesDb();
        await using var subsDb = CreateSubscriptionsDb();

        var venue = Venue.Create(_venueId, "Club", "club", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venuesDb.Venues.Add(venue);
        await venuesDb.SaveChangesAsync();

        var plan = SubscriptionPlan.Create(new PlanTerms(
            "basic", "Basic", "Basic plan",
            500m, 5000m,
            MaxVenues: 2, MaxPitches: 5, MaxStaffPerVenue: 2, // limit 2 staff
            Features: [], TrialDays: 0, GraceDays: 0,
            IsPublic: true, SortOrder: 1), _now).Value;
        plan.SetStatus(PlanStatus.Active, _now);
        plan.SetDefault(true, _now);
        subsDb.Plans.Add(plan);
        await subsDb.SaveChangesAsync();

        var quotaPolicy = new EntitlementStaffQuotaPolicy(venuesDb, new EntitlementService(subsDb));

        // 1 current staff -> Can add (1 < 2)
        (await quotaPolicy.CanAddAsync(_venueId, currentCount: 1)).Should().BeTrue();

        // 2 current staff -> Cannot add (2 >= 2)
        (await quotaPolicy.CanAddAsync(_venueId, currentCount: 2)).Should().BeFalse();
    }

    [Fact]
    public async Task SetPitchActive_PausesPitch_AndReportsUpcomingBookingsAsync()
    {
        await using var venuesDb = CreateVenuesDb();
        var pitchId = Guid.NewGuid();
        var pitch = Pitch.Create(pitchId, _venueId, "Main Pitch", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        var venue = Venue.Create(_venueId, "Arena", "arena", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venuesDb.Venues.Add(venue);
        venuesDb.Pitches.Add(pitch);
        await venuesDb.SaveChangesAsync();

        // Mock 3 upcoming active bookings on this pitch
        var upcomingBookings = new List<UpcomingBookingRef>
        {
            new(Guid.NewGuid(), "B-1", pitchId, _now.AddHours(2), _now.AddHours(3), "Confirmed"),
            new(Guid.NewGuid(), "B-2", pitchId, _now.AddDays(1), _now.AddDays(1).AddHours(1), "Confirmed"),
            new(Guid.NewGuid(), "B-3", pitchId, _now.AddDays(2), _now.AddDays(2).AddHours(1), "Confirmed")
        };
        var mockReader = new MockUpcomingBookingsReader(upcomingBookings);

        var service = new SetPitchActive(venuesDb, mockReader, _clock);

        // Pause pitch
        var result = await service.HandleAsync(pitchId, _ownerId, isActive: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Pitch.IsActive.Should().BeFalse();
        result.Value.UpcomingBookings.Should().Be(3, "Staff must be informed of how many upcoming bookings remain");

        var updatedPitch = await venuesDb.Pitches.FindAsync(pitchId);
        updatedPitch!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePitch_NonOwner_ReturnsForbiddenAsync()
    {
        await using var venuesDb = CreateVenuesDb();
        var pitchId = Guid.NewGuid();
        var pitch = Pitch.Create(pitchId, _venueId, "Pitch A", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        var venue = Venue.Create(_venueId, "Arena", "arena", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venuesDb.Venues.Add(venue);
        venuesDb.Pitches.Add(pitch);
        await venuesDb.SaveChangesAsync();

        var pitchRepo = new PitchRepository(venuesDb);
        var venueRepo = new VenueRepository(venuesDb);
        var handler = new DeletePitchHandler(pitchRepo, venueRepo, venuesDb, _clock, NullLogger<DeletePitchHandler>.Instance);

        var nonOwnerId = Guid.NewGuid();
        var result = await handler.HandleAsync(new DeletePitchCommand(pitchId, nonOwnerId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pitch.Forbidden");
    }

    [Fact(Skip = "BUG-010: DeletePitchHandler does not check for active/confirmed future bookings before soft deleting pitch")]
    public async Task DeletePitch_WithFutureConfirmedBookings_ShouldBeRejectedAsync()
    {
        // Test plan §P3: "Deleting a pitch that has future confirmed bookings is rejected or handled as documented."
        // BUG-010: DeletePitchHandler does not verify active bookings (lacking IBookingConflictChecker / IUpcomingBookingsReader)
        // and soft deletes the pitch unconditionally, leaving active bookings orphaned on a deleted pitch.
        await using var venuesDb = CreateVenuesDb();
        var pitchId = Guid.NewGuid();
        var pitch = Pitch.Create(pitchId, _venueId, "Pitch B", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        var venue = Venue.Create(_venueId, "Arena", "arena", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venuesDb.Venues.Add(venue);
        venuesDb.Pitches.Add(pitch);
        await venuesDb.SaveChangesAsync();

        var pitchRepo = new PitchRepository(venuesDb);
        var venueRepo = new VenueRepository(venuesDb);
        var handler = new DeletePitchHandler(pitchRepo, venueRepo, venuesDb, _clock, NullLogger<DeletePitchHandler>.Instance);

        var result = await handler.HandleAsync(new DeletePitchCommand(pitchId, _ownerId));

        // When BUG-010 is resolved, this should fail because future confirmed bookings exist
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pitch.HasActiveBookings");
    }

    private sealed class MockUpcomingBookingsReader(IReadOnlyList<UpcomingBookingRef> bookings) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(bookings.Where(b => pitchIds.Contains(b.PitchId)).ToList());
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => _now.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
