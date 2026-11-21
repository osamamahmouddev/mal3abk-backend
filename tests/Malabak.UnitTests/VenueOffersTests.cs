using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueOffersTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);
    private static readonly Guid _venue = Guid.NewGuid();

    private static OfferRule Percent(decimal percent, DateOnly? end = null, DateOnly? start = null, Guid? pitch = null,
        DaysOfWeekMask? days = null, TimeOnly? from = null, int? minMinutes = null, string name = "Offer") =>
        new(_venue, pitch, name, PriceAdjustment.Percentage, percent, null, start, end, days, from, minMinutes);

    private static OfferRule AmountOff(decimal amount, string name = "Amount") =>
        new(_venue, null, name, PriceAdjustment.FixedAmountOff, null, amount, null, null, null, null, null);

    [Fact]
    public void NoRules_NoOffer() => VenueOffers.Pick([], _today, 300m).Should().BeNull();

    [Fact]
    public void PicksTheBiggestSaving_OnAnHourAtTheStartingPrice()
    {
        var offer = VenueOffers.Pick([Percent(10, name: "Ten"), AmountOff(50, "Fifty"), Percent(15, name: "Fifteen")], _today, 300m)!;

        offer.Name.Should().Be("Fifty", "50 off beats 15% (45) and 10% (30) of 300");
        offer.Type.Should().Be(VenueOffers.AmountType);
        offer.Value.Should().Be(50);
        offer.FromHourlyPriceAfter.Should().Be(250m);
    }

    [Fact]
    public void OffersOutsideTheirDates_AreIgnored()
    {
        VenueOffers.Pick([Percent(30, end: _today.AddDays(-1)), Percent(30, start: _today.AddDays(1))], _today, 300m)
            .Should().BeNull();
        VenueOffers.Pick([Percent(30, end: _today)], _today, 300m)!.EndsOn.Should().Be(_today, "the last day still counts");
    }

    [Fact]
    public void ConditionalOffers_AreShown_WithoutAnAfterPrice()
    {
        foreach (var rule in new[]
        {
            Percent(20, pitch: Guid.NewGuid()),
            Percent(20, days: DaysOfWeekMask.Friday),
            Percent(20, from: new TimeOnly(10, 0)),
            Percent(20, minMinutes: 120)
        })
        {
            var offer = VenueOffers.Pick([rule], _today, 300m)!;
            offer.HasConditions.Should().BeTrue();
            offer.FromHourlyPriceAfter.Should().BeNull();
        }

        VenueOffers.Pick([Percent(20, days: DaysOfWeekMask.All, minMinutes: 60)], _today, 300m)!.HasConditions.Should().BeFalse();
    }

    [Fact]
    public void AnAmountBiggerThanThePrice_NeverGoesBelowZero() =>
        VenueOffers.Pick([AmountOff(500)], _today, 300m)!.FromHourlyPriceAfter.Should().Be(0m);

    [Fact]
    public void WithoutAStartingPrice_TheOfferIsStillShown() =>
        VenueOffers.Pick([Percent(25)], _today, null)!.FromHourlyPriceAfter.Should().BeNull();
}
