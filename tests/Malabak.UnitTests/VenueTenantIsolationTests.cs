using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches.GetPitchById;
using Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;
using Malabak.Modules.Venues.Application.Venues.GetVenueById;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>Two owners, two venues: neither may see the other's data through any read path.</summary>
public sealed class VenueTenantIsolationTests
{
    private static readonly DateTime _now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Guid _ownerA = Guid.NewGuid();
    private readonly Guid _ownerB = Guid.NewGuid();

    private VenuesDbContext CreateContext(IVenueTenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(_databaseName).Options;
        return tenant is null ? new VenuesDbContext(options) : new VenuesDbContext(options, tenant);
    }

    // Optionally seed through a caller-supplied context so a test can keep working with the tracked entities.
    private async Task<(Venue A, Pitch PitchA, Venue B, Pitch PitchB)> SeedTwoVenuesAsync(VenuesDbContext? sharedContext = null)
    {
        var db = sharedContext ?? CreateContext();
        await using var owned = sharedContext is null ? db : null;
        var venueA = Venue.Create(Guid.NewGuid(), "Venue A", "venue-a", null, _ownerA, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        var venueB = Venue.Create(Guid.NewGuid(), "Venue B", "venue-b", null, _ownerB, "Addr", "Cairo", "Zamalek", null, null, "+201000000002", null, null, _now);
        var pitchA = Pitch.Create(Guid.NewGuid(), venueA.Id, "A1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now);
        var pitchB = Pitch.Create(Guid.NewGuid(), venueB.Id, "B1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now);
        var scheduleB = OperatingSchedule.Create(Guid.NewGuid(), venueB.Id, DayOfWeek.Friday, new TimeOnly(10, 0), new TimeOnly(22, 0), 60, _now);
        venueB.SetOperatingSchedules([scheduleB], _now);

        db.Venues.AddRange(venueA, venueB);
        db.Pitches.AddRange(pitchA, pitchB);
        db.BlockedSlots.AddRange(
            BlockedSlot.Create(Guid.NewGuid(), venueA.Id, pitchA.Id, _now.AddDays(1), _now.AddDays(1).AddHours(1), "A block", _ownerA, _now),
            BlockedSlot.Create(Guid.NewGuid(), venueB.Id, pitchB.Id, _now.AddDays(1), _now.AddDays(1).AddHours(1), "B block", _ownerB, _now));
        await db.SaveChangesAsync();
        return (venueA, pitchA, venueB, pitchB);
    }

    private async Task<VenueTenantContext> TenantForAsync(Guid userId)
    {
        await using var lookup = CreateContext();
        var accessible = await new VenueAccessService(lookup).GetAccessibleVenuesAsync(userId);
        var tenant = new VenueTenantContext();
        tenant.RestrictTo(accessible.Select(a => a.VenueId));
        return tenant;
    }

    [Fact]
    public async Task AccessService_Owner_GetsAllVenueScopedPermissionsAsync()
    {
        var (a, _, b, _) = await SeedTwoVenuesAsync();
        await using var db = CreateContext();
        var service = new VenueAccessService(db);

        var ownAccess = await service.GetAccessAsync(_ownerA, a.Id);
        var otherAccess = await service.GetAccessAsync(_ownerA, b.Id);

        ownAccess.Level.Should().Be(VenueAccessLevel.Owner);
        ownAccess.Permissions.Should().BeEquivalentTo(Permissions.VenueScoped);
        otherAccess.HasAccess.Should().BeFalse();
        otherAccess.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task AccessService_DeletedVenue_GrantsNoAccessAsync()
    {
        Guid venueAId;
        await using (var db = CreateContext())
        {
            var (venueA, _, _, _) = await SeedTwoVenuesAsync(db);
            venueA.MarkDeleted(_now);
            await db.SaveChangesAsync();
            venueAId = venueA.Id;
        }

        await using var readDb = CreateContext();
        var service = new VenueAccessService(readDb);

        (await service.GetAccessAsync(_ownerA, venueAId)).HasAccess.Should().BeFalse();
        (await service.GetAccessibleVenuesAsync(_ownerA)).Should().BeEmpty();
    }

    [Fact]
    public async Task QueryFilter_RestrictedToOwnerA_HidesEveryVenueOwnedRowOfOwnerBAsync()
    {
        var (a, pitchA, b, pitchB) = await SeedTwoVenuesAsync();
        await using var db = CreateContext(await TenantForAsync(_ownerA));

        (await db.Venues.Select(v => v.Id).ToListAsync()).Should().Equal(a.Id);
        (await db.Pitches.Select(p => p.Id).ToListAsync()).Should().Equal(pitchA.Id);
        (await db.BlockedSlots.AllAsync(s => s.VenueId == a.Id)).Should().BeTrue();
        (await db.OperatingSchedules.AnyAsync(s => s.VenueId == b.Id)).Should().BeFalse();

        // Repositories go through the same filter, so a handler that forgot its check still gets nothing.
        (await new VenueRepository(db).GetByIdAsync(b.Id)).Should().BeNull();
        (await new PitchRepository(db).GetByIdAsync(pitchB.Id)).Should().BeNull();
        (await new BlockedSlotRepository(db).GetByVenueIdAsync(b.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task QueryFilter_Unrestricted_SeesAllVenuesAsync()
    {
        await SeedTwoVenuesAsync();
        await using var db = CreateContext(new VenueTenantContext());

        (await db.Venues.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task QueryFilter_UserWithNoVenues_SeesNothingAsync()
    {
        await SeedTwoVenuesAsync();
        await using var db = CreateContext(await TenantForAsync(Guid.NewGuid()));

        (await db.Venues.AnyAsync()).Should().BeFalse();
        (await db.Pitches.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SlugUniquenessCheck_SeesOtherOwnersVenues_DespiteTenantFilterAsync()
    {
        await SeedTwoVenuesAsync();
        await using var db = CreateContext(await TenantForAsync(_ownerA));

        (await new VenueRepository(db).ExistsBySlugAsync("venue-b")).Should().BeTrue();
    }

    [Fact]
    public void TenantContext_CanOnlyNarrowOnceRestricted()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var tenant = new VenueTenantContext();

        tenant.RestrictTo([first]);
        tenant.RestrictTo([first, second]);

        tenant.IsRestricted.Should().BeTrue();
        tenant.VenueIds.Should().Equal(first);
    }

    [Fact]
    public async Task ReadHandlers_OtherOwnersVenue_ReturnNotFoundAsync()
    {
        await using var db = CreateContext();
        var (_, _, b, pitchB) = await SeedTwoVenuesAsync(db);
        var access = new VenueAccessService(db);
        var venueRepo = new VenueRepository(db);
        var pitchRepo = new PitchRepository(db);

        var venue = await new GetVenueByIdHandler(venueRepo, access).HandleAsync(new GetVenueByIdQuery(b.Id, _ownerA));
        var pitches = await new GetPitchesByVenueHandler(pitchRepo, access).HandleAsync(new GetPitchesByVenueQuery(b.Id, _ownerA));
        var pitch = await new GetPitchByIdHandler(pitchRepo, access).HandleAsync(new GetPitchByIdQuery(pitchB.Id, _ownerA));
        var schedules = await new GetVenueSchedulesHandler(venueRepo, access).HandleAsync(new GetVenueSchedulesQuery(b.Id, _ownerA));

        venue.Error.Code.Should().Be("Venue.NotFound");
        pitches.Error.Code.Should().Be("Venue.NotFound");
        pitch.Error.Code.Should().Be("Pitch.NotFound");
        schedules.Error.Code.Should().Be("Venue.NotFound");
    }

    [Fact]
    public async Task ReadHandlers_Owner_GetsOwnDataAsync()
    {
        await using var db = CreateContext();
        var (_, _, b, pitchB) = await SeedTwoVenuesAsync(db);
        var access = new VenueAccessService(db);

        var venue = await new GetVenueByIdHandler(new VenueRepository(db), access).HandleAsync(new GetVenueByIdQuery(b.Id, _ownerB));
        var pitch = await new GetPitchByIdHandler(new PitchRepository(db), access).HandleAsync(new GetPitchByIdQuery(pitchB.Id, _ownerB));
        var schedules = await new GetVenueSchedulesHandler(new VenueRepository(db), access).HandleAsync(new GetVenueSchedulesQuery(b.Id, _ownerB));

        venue.IsSuccess.Should().BeTrue();
        pitch.IsSuccess.Should().BeTrue();
        schedules.Value.Should().ContainSingle();
    }
}
