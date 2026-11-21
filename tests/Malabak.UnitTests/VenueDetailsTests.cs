using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Details;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueDetailsTests
{
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _mainPitch = Guid.NewGuid();
    private readonly Guid _smallPitch = Guid.NewGuid();
    private readonly Guid _closedPitch = Guid.NewGuid();
    private Guid _venueId;

    [Fact]
    public async Task Profile_MapsGalleryFacilitiesHoursPriceAndPolicyAsync()
    {
        await using var db = await SeedAsync();

        var venue = (await Query(db).GetAsync(_venueId)).Value;

        venue.Name.Should().Be("Maadi Arena");
        venue.Photos.Should().Equal("/media/a/large", "/media/b/large");
        venue.Facilities.Select(f => f.NameEn).Should().Equal("Parking", "Floodlights");
        venue.Facilities[0].NameAr.Should().NotBeNullOrWhiteSpace();
        venue.FromHourlyPrice.Should().Be(350m, "the small pitch overrides the 400 venue rate");
        venue.PitchCount.Should().Be(2, "inactive pitches are hidden");
        venue.PitchTypes.Should().Equal("5v5", "7v7");
        venue.FavoritesCount.Should().Be(1);
        venue.DepositPolicy!.DepositType.Should().Be("Percentage");
        venue.DepositPolicy.DepositValue.Should().Be(30);
        venue.DepositPolicy.CancellationDeadlineHours.Should().Be(24);

        venue.OpeningHours.Should().HaveCount(7);
        venue.OpeningHours[0].Day.Should().Be(DayOfWeek.Saturday);
        var thursday = venue.OpeningHours.Single(d => d.Day == DayOfWeek.Thursday);
        thursday.Closed.Should().BeFalse();
        thursday.Windows.Should().Equal(new OpeningWindow(new TimeOnly(18, 0), new TimeOnly(2, 0)));
        venue.OpeningHours.Single(d => d.Day == DayOfWeek.Friday).Closed.Should().BeTrue();
    }

    [Fact]
    public async Task Pitches_ShowSpecs_Prices_AndCustomHours_ButNotInactivePitchesAsync()
    {
        await using var db = await SeedAsync();

        var pitches = (await Query(db).GetPitchesAsync(_venueId)).Value;

        pitches.Select(p => p.PitchId).Should().Equal(_mainPitch, _smallPitch);
        var main = pitches[0];
        main.PitchType.Should().Be("7v7");
        main.PlayersPerTeam.Should().Be(7);
        main.Surface.Code.Should().Be("NaturalGrass");
        main.Surface.NameEn.Should().Be("Natural grass");
        main.LengthMeters.Should().Be(60);
        main.BaseHourlyPrice.Should().Be(400m);
        main.CustomHours.Should().BeNull();

        var small = pitches[1];
        small.BaseHourlyPrice.Should().Be(350m);
        small.CustomHours!.Single(d => d.Day == DayOfWeek.Monday).Windows.Should().ContainSingle();
        small.CustomHours!.Single(d => d.Day == DayOfWeek.Tuesday).Closed.Should().BeTrue("marked closed that day");
    }

    [Fact]
    public async Task UnknownOrUnpublishedVenues_AreNotFoundAsync()
    {
        await using var db = await SeedAsync(publish: false);

        (await Query(db).GetAsync(_venueId)).Error.Should().Be(VenueDetailsErrors.NotFound);
        (await Query(db).GetPitchesAsync(_venueId)).Error.Should().Be(VenueDetailsErrors.NotFound);
        (await Query(db).GetAsync(Guid.NewGuid())).Error.Should().Be(VenueDetailsErrors.NotFound);
    }

    [Fact]
    public void PublicProfile_NeverExposesOwnerOrContactFields()
    {
        var properties = typeof(VenueDetailsDto).GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain(["PhoneNumber", "OwnerId", "ApprovalStatus", "IsPublished"]);
    }

    private static VenueDetailsQuery Query(VenuesDbContext db) => new(db, new FakePolicies(), new Clock());

    private async Task<VenuesDbContext> SeedAsync(bool publish = true)
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var venue = Venue.Create(Guid.NewGuid(), "Maadi Arena", $"maadi-{Guid.NewGuid():N}", "Two pitches", Guid.NewGuid(), "Road 9", "Cairo", "Maadi",
            29.96, 31.25, "+201000000001", ["parking", "lighting"], ["/media/a/large", "/media/b/large"], _now);
        _venueId = venue.Id;
        if (publish)
        {
            venue.SubmitForApproval(venue.OwnerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(_mainPitch, venue.Id, "A Main", "NaturalGrass", 7, 60, 40, null, _now));
        db.Pitches.Add(Pitch.Create(_smallPitch, venue.Id, "B Small", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        var closed = Pitch.Create(_closedPitch, venue.Id, "C Closed", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now);
        closed.SetActive(false, _now);
        db.Pitches.Add(closed);

        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), venue.Id, DayOfWeek.Thursday, new TimeOnly(18, 0), new TimeOnly(2, 0), 60, _now));
        db.PitchWeeklyHours.Add(PitchWeeklyHours.Create(venue.Id, _smallPitch, DayOfWeek.Monday, new TimeOnly(16, 0), new TimeOnly(22, 0), false, _now));
        db.PitchWeeklyHours.Add(PitchWeeklyHours.Create(venue.Id, _smallPitch, DayOfWeek.Tuesday, new TimeOnly(16, 0), new TimeOnly(22, 0), true, _now));
        db.PriceRules.Add(PriceRule.Create(venue.Id,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 400, null, null, null, null, null, null, null), _now).Value);
        db.PriceRules.Add(PriceRule.Create(venue.Id,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, _smallPitch, "Small", 350, null, null, null, null, null, null, null), _now).Value);
        db.FavoriteVenues.Add(FavoriteVenue.Create(Guid.NewGuid(), venue.Id, _now));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Profile_ShowsTheRunningOffer_WithItsEndAndThePriceAfterItAsync()
    {
        await using var db = await SeedAsync();
        var today = DateOnly.FromDateTime(_now);
        db.PriceRules.Add(PriceRule.Create(_venueId, new PriceRuleTerms(PriceRuleKind.Discount, PriceAdjustment.Percentage, null,
            "Opening week", null, 20, null, null, null, today.AddDays(-1), today.AddDays(6), null), _now).Value);
        db.PriceRules.Add(PriceRule.Create(_venueId, new PriceRuleTerms(PriceRuleKind.Discount, PriceAdjustment.Percentage, null,
            "Finished", null, 50, null, null, null, today.AddDays(-9), today.AddDays(-2), null), _now).Value);
        await db.SaveChangesAsync();

        var offer = (await Query(db).GetAsync(_venueId)).Value.Offer;

        offer.Should().NotBeNull();
        offer!.Name.Should().Be("Opening week", "an offer that already ended is not shown");
        offer.Type.Should().Be(VenueOffers.PercentageType);
        offer.Value.Should().Be(20);
        offer.EndsOn.Should().Be(today.AddDays(6));
        offer.HasConditions.Should().BeFalse();
        offer.FromHourlyPriceAfter.Should().Be(280m, "20% off the 350 starting price");
    }

    [Fact]
    public async Task Profile_HasNoOffer_WhenNoDiscountRunsAsync()
    {
        await using var db = await SeedAsync();

        (await Query(db).GetAsync(_venueId)).Value.Offer.Should().BeNull();
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => _now.AddHours(2);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }

    private sealed class FakePolicies : IBookingPolicyProvider
    {
        public Task<BookingPolicySnapshot?> GetCurrentSnapshotAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BookingPolicySnapshot?>(new BookingPolicySnapshot(
                Guid.NewGuid(), venueId, 1, "Percentage", 30, 30, 60, 24, "FullRefund", 100, "ForfeitDeposit", 15, false, 0, true, null, _now));
    }
}
