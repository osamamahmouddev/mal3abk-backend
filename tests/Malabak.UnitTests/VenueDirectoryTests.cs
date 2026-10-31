using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueDirectoryTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _ownerId = Guid.NewGuid();

    [Fact]
    public async Task StaffWhoMayReviewProofs_AreTheOwnerAndTheRolesThatHoldThePermission_EachOnceAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var manager = Guid.NewGuid();
        var accountant = Guid.NewGuid();
        var receptionist = Guid.NewGuid();
        db.StaffMembers.AddRange(
            VenueStaffMember.Create(venue.Id, manager, VenueStaffRole.Manager, _ownerId, _now),
            VenueStaffMember.Create(venue.Id, accountant, VenueStaffRole.Accountant, _ownerId, _now),
            VenueStaffMember.Create(venue.Id, receptionist, VenueStaffRole.Receptionist, _ownerId, _now));
        await db.SaveChangesAsync();

        var reviewers = await new VenueDirectory(db).GetStaffUserIdsAsync(venue.Id, Permissions.PaymentProofsReview);

        reviewers.Should().BeEquivalentTo([_ownerId, manager, accountant], "a receptionist may not review payment proofs");
    }

    [Fact]
    public async Task StaffOfAnotherVenueOrOfAVenueThatDoesNotExist_AreNeverReturnedAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var other = await SeedVenueAsync(db, Guid.NewGuid());
        db.StaffMembers.Add(VenueStaffMember.Create(other.Id, Guid.NewGuid(), VenueStaffRole.Manager, _ownerId, _now));
        await db.SaveChangesAsync();
        var directory = new VenueDirectory(db);

        (await directory.GetStaffUserIdsAsync(venue.Id, Permissions.PaymentProofsReview)).Should().Equal(_ownerId);
        (await directory.GetStaffUserIdsAsync(Guid.NewGuid(), Permissions.PaymentProofsReview)).Should().BeEmpty();
    }

    [Fact]
    public async Task VenueNames_AreReturnedForKnownVenuesOnlyAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var directory = new VenueDirectory(db);

        var names = await directory.GetVenueNamesAsync([venue.Id, Guid.NewGuid()]);

        names.Should().ContainSingle().Which.Should().Be(new KeyValuePair<Guid, string>(venue.Id, "Club"));
        (await directory.GetVenueNamesAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task BlockedPeriods_AreTheVenuesOwnThatOverlapTheRange_OptionallyForOnePitchAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var otherVenue = await SeedVenueAsync(db, Guid.NewGuid());
        var pitchA = Guid.NewGuid();
        var pitchB = Guid.NewGuid();
        db.BlockedSlots.AddRange(
            BlockedSlot.Create(Guid.NewGuid(), venue.Id, pitchA, _now.AddHours(1), _now.AddHours(2), "Maintenance", _ownerId, _now),
            BlockedSlot.Create(Guid.NewGuid(), venue.Id, pitchB, _now.AddHours(1), _now.AddHours(2), "Event", _ownerId, _now),
            BlockedSlot.Create(Guid.NewGuid(), venue.Id, pitchA, _now.AddDays(5), _now.AddDays(5).AddHours(1), "Later", _ownerId, _now),
            BlockedSlot.Create(Guid.NewGuid(), otherVenue.Id, pitchA, _now.AddHours(1), _now.AddHours(2), "Not ours", _ownerId, _now));
        await db.SaveChangesAsync();
        var directory = new VenueDirectory(db);

        var all = await directory.GetBlockedPeriodsAsync(venue.Id, _now, _now.AddDays(1), null);
        var onePitch = await directory.GetBlockedPeriodsAsync(venue.Id, _now, _now.AddDays(1), pitchB);

        all.Select(b => b.Reason).Should().BeEquivalentTo("Maintenance", "Event");
        onePitch.Should().ContainSingle().Which.Reason.Should().Be("Event");
    }

    [Fact]
    public async Task TheVenueSummary_CarriesNameAddressAndPlace_AndIsNullForAnUnknownVenueAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var directory = new VenueDirectory(db);

        var summary = await directory.GetVenueSummaryAsync(venue.Id);

        summary.Should().NotBeNull();
        summary!.Name.Should().Be("Club");
        summary.Address.Should().Be("Addr");
        (await directory.GetVenueSummaryAsync(Guid.NewGuid())).Should().BeNull();
    }

    private async Task<Venue> SeedVenueAsync(VenuesDbContext db, Guid? ownerId = null)
    {
        var venue = Venue.Create(Guid.NewGuid(), "Club", $"club-{Guid.NewGuid():N}", null, ownerId ?? _ownerId, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return venue;
    }

    private static VenuesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
