using FluentAssertions;
using Malabak.SharedKernel.Reference;
using Xunit;

namespace Malabak.UnitTests;

public sealed class FacilityNamesTests
{
    [Theory]
    [InlineData("lighting", "إضاءة ليلية")]
    [InlineData("Floodlights", "إضاءة ليلية")]
    [InlineData("Locker Rooms", "غرف تغيير ملابس")]
    [InlineData("Shower", "دُش")]
    [InlineData("Bibs / Pinnies", "بِبات (صديريات)")]
    [InlineData("First Aid Station", "إسعافات أولية")]
    [InlineData("Referee On-Demand", "حَكَم عند الطلب")]
    [InlineData("  wi-fi ", "واي فاي")]
    [InlineData("كافيتريا", "كافيتريا")]
    public void OlderFreeTextNames_ResolveToTheCatalogFacility(string stored, string arabic) =>
        EgyptReferenceData.FindFacility(stored)!.NameAr.Should().Be(arabic);

    [Fact]
    public void UnknownValues_StayUnknown()
    {
        EgyptReferenceData.FindFacility("Sauna").Should().BeNull();
        EgyptReferenceData.FindFacility(" ").Should().BeNull();
        EgyptReferenceData.IsKnownFacility("Floodlights").Should().BeTrue();
    }
}
