using FluentAssertions;
using Malabak.SharedKernel.Reference;
using Xunit;

namespace Malabak.UnitTests;

public sealed class ReferenceDataTests
{
    [Fact]
    public void Governorates_AllTwentySeven_WithUniqueCodesAndBothLanguages()
    {
        EgyptReferenceData.Governorates.Should().HaveCount(27);
        EgyptReferenceData.Governorates.Select(g => g.Code).Should().OnlyHaveUniqueItems();
        EgyptReferenceData.Governorates.Should().OnlyContain(g => g.NameAr.Length > 0 && g.NameEn.Length > 0);
    }

    [Theory]
    [InlineData("cairo")]
    [InlineData("Cairo")]
    [InlineData("القاهرة")]
    [InlineData("  CAIRO ")]
    public void FindGovernorate_ByCodeOrName(string value)
    {
        EgyptReferenceData.FindGovernorate(value)!.Code.Should().Be("cairo");
    }

    [Fact]
    public void Areas_AcceptListedNamesAndTypedOnes_ButNotBlank()
    {
        var cairo = EgyptReferenceData.FindGovernorate("cairo")!;
        var luxor = EgyptReferenceData.FindGovernorate("luxor")!;

        EgyptReferenceData.IsValidArea(cairo, "مدينة نصر").Should().BeTrue();
        EgyptReferenceData.IsValidArea(cairo, "Nasr City").Should().BeTrue();
        EgyptReferenceData.IsValidArea(cairo, "التجمع الخامس").Should().BeTrue("a district missing from the list can be typed");
        EgyptReferenceData.IsValidArea(luxor, "Karnak").Should().BeTrue();
        EgyptReferenceData.IsValidArea(luxor, " ").Should().BeFalse();
        EgyptReferenceData.FindGovernorate("Narnia").Should().BeNull();
    }

    [Fact]
    public void Catalogs_HaveUniqueCodes()
    {
        EgyptReferenceData.Facilities.Select(f => f.Code).Should().OnlyHaveUniqueItems();
        EgyptReferenceData.PitchTypes.Select(p => p.Code).Should().Contain(["5v5", "7v7", "11v11"]);
        EgyptReferenceData.Surfaces.Select(s => s.Code).Should().Contain("ArtificialTurf");
        EgyptReferenceData.IsKnownFacility("Floodlights").Should().BeTrue();
        EgyptReferenceData.IsKnownFacility("helipad").Should().BeFalse();
    }

    [Theory]
    [InlineData(2024, 5, 5)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    public void OrthodoxEaster_MatchesPublishedDates(int year, int month, int day)
    {
        EgyptHolidayCalendar.OrthodoxEaster(year).Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void Holidays2026_IncludeFixedCopticAndEstimatedIslamicDates()
    {
        var holidays = EgyptHolidayCalendar.ForYear(2026);

        holidays.Should().Contain(h => h.Code == "revolution_jan25" && h.Date == new DateOnly(2026, 1, 25) && !h.IsEstimated);
        holidays.Should().Contain(h => h.Code == "sham_el_nessim" && h.Date == new DateOnly(2026, 4, 13));

        var eidFitr = holidays.Where(h => h.Code == "eid_al_fitr").ToList();
        eidFitr.Should().HaveCount(3).And.OnlyContain(h => h.IsEstimated);
        eidFitr[0].Date.Should().BeOnOrAfter(new DateOnly(2026, 3, 18)).And.BeOnOrBefore(new DateOnly(2026, 3, 22));

        var eidAdha = holidays.First(h => h.Code == "eid_al_adha");
        eidAdha.Date.Should().BeOnOrAfter(new DateOnly(2026, 5, 25)).And.BeOnOrBefore(new DateOnly(2026, 5, 29));

        holidays.Should().BeInAscendingOrder(h => h.Date);
        EgyptHolidayCalendar.IsHoliday(new DateOnly(2026, 10, 6)).Should().BeTrue();
        EgyptHolidayCalendar.IsHoliday(new DateOnly(2026, 10, 7)).Should().BeFalse();
    }

    [Fact]
    public void ForYear_RejectsOutOfRangeYears()
    {
        var act = () => EgyptHolidayCalendar.ForYear(1990);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsHoliday_AgreesWithTheFullList_ForEveryDayOfTheYear()
    {
        var holidays = EgyptHolidayCalendar.ForYear(2027).Select(h => h.Date).ToHashSet();

        for (var day = new DateOnly(2027, 1, 1); day.Year == 2027; day = day.AddDays(1))
        {
            EgyptHolidayCalendar.IsHoliday(day).Should().Be(holidays.Contains(day), day.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void IsHoliday_DoesNotRebuildTheCalendarPerCall()
    {
        // Performance guard: pricing calls this for every 15-minute slice. Rebuilding the list cost ~4 KB per call
        // (113 MB for one explore request); the cached lookup allocates nothing.
        var day = new DateOnly(2026, 11, 5);
        EgyptHolidayCalendar.IsHoliday(day);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 10_000; i++)
        {
            EgyptHolidayCalendar.IsHoliday(day.AddDays(i % 50));
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(64 * 1024);
    }
}
