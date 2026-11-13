using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pricing;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Edge-case test suite for Price Rules Management, Overlap Detection, Overnight Pricing Windows,
/// and Multi-Layer Pricing Calculator Precedence (P3).
/// </summary>
public sealed class VenuePriceRulesEdgeCaseTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitch1 = Guid.NewGuid();
    private readonly Guid _pitch2 = Guid.NewGuid();

    [Fact]
    public void OvernightTimeWindow_CrossesMidnight_MatchesCorrectLocalMoments()
    {
        // Rule runs 22:00 to 02:00
        var terms = new PriceRuleTerms(
            PriceRuleKind.Peak, PriceAdjustment.Percentage, null, "Late Night Peak",
            Amount: null, Percent: 25m, Days: null,
            StartTime: new TimeOnly(22, 0), EndTime: new TimeOnly(2, 0),
            StartDate: null, EndDate: null, MinDurationMinutes: null);

        var ruleResult = PriceRule.Create(_venueId, terms, _now);
        ruleResult.IsSuccess.Should().BeTrue();
        var rule = ruleResult.Value;

        // 23:00 local matches
        rule.MatchesMoment(new DateTime(2026, 11, 10, 23, 0, 0, DateTimeKind.Unspecified)).Should().BeTrue();

        // 01:30 local matches (after midnight)
        rule.MatchesMoment(new DateTime(2026, 11, 11, 1, 30, 0, DateTimeKind.Unspecified)).Should().BeTrue();

        // Exactly 02:00 local does NOT match (end is exclusive)
        rule.MatchesMoment(new DateTime(2026, 11, 11, 2, 0, 0, DateTimeKind.Unspecified)).Should().BeFalse();

        // 14:00 local does NOT match
        rule.MatchesMoment(new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Unspecified)).Should().BeFalse();
    }

    [Fact]
    public void PriceRules_OverlapDetection_RejectsAmbiguousSameKindRules()
    {
        // Rule A: Peak 18:00 to 22:00 on Thursdays
        var termsA = new PriceRuleTerms(
            PriceRuleKind.Peak, PriceAdjustment.Percentage, _pitch1, "Thursday Evening Peak A",
            Amount: null, Percent: 20m, Days: DaysOfWeekMask.Thursday,
            StartTime: new TimeOnly(18, 0), EndTime: new TimeOnly(22, 0),
            StartDate: null, EndDate: null, MinDurationMinutes: null);
        var ruleA = PriceRule.Create(_venueId, termsA, _now).Value;

        // Rule B: Peak 20:00 to 23:00 on Thursdays on same pitch -> Overlaps!
        var termsB = new PriceRuleTerms(
            PriceRuleKind.Peak, PriceAdjustment.Percentage, _pitch1, "Thursday Evening Peak B",
            Amount: null, Percent: 30m, Days: DaysOfWeekMask.Thursday,
            StartTime: new TimeOnly(20, 0), EndTime: new TimeOnly(23, 0),
            StartDate: null, EndDate: null, MinDurationMinutes: null);
        var ruleB = PriceRule.Create(_venueId, termsB, _now).Value;

        ruleA.Overlaps(ruleB).Should().BeTrue();

        // Rule C: Peak on different pitch (_pitch2) -> Does NOT overlap
        var termsC = termsB with { PitchId = _pitch2 };
        var ruleC = PriceRule.Create(_venueId, termsC, _now).Value;
        ruleA.Overlaps(ruleC).Should().BeFalse();

        // Rule D: Peak on different day (Friday) -> Does NOT overlap
        var termsD = termsB with { Days = DaysOfWeekMask.Friday };
        var ruleD = PriceRule.Create(_venueId, termsD, _now).Value;
        ruleA.Overlaps(ruleD).Should().BeFalse();

        // Rule E: Different Kind (Base vs Peak) -> Does NOT overlap (they layer/stack)
        var termsE = new PriceRuleTerms(
            PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, _pitch1, "Base Price",
            Amount: 300m, Percent: null, Days: null,
            StartTime: null, EndTime: null,
            StartDate: null, EndDate: null, MinDurationMinutes: null);
        var ruleE = PriceRule.Create(_venueId, termsE, _now).Value;
        ruleA.Overlaps(ruleE).Should().BeFalse();
    }

    [Fact]
    public void PricingCalculator_LayersRulesCorrectly_Base_Peak_Weekend_Discount()
    {
        var baseTerms = new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base Rate", 200m, null, null, null, null, null, null, null);
        var baseRule = PriceRule.Create(_venueId, baseTerms, _now).Value;

        // Peak rule: +20% on 18:00..22:00
        var peakTerms = new PriceRuleTerms(PriceRuleKind.Peak, PriceAdjustment.Percentage, null, "Peak +20%", null, 20m, null, new TimeOnly(18, 0), new TimeOnly(22, 0), null, null, null);
        var peakRule = PriceRule.Create(_venueId, peakTerms, _now).Value;

        // Weekend rule: +10% on Friday
        var weekendTerms = new PriceRuleTerms(PriceRuleKind.Weekend, PriceAdjustment.Percentage, null, "Weekend +10%", null, 10m, DaysOfWeekMask.Friday, null, null, null, null, null);
        var weekendRule = PriceRule.Create(_venueId, weekendTerms, _now).Value;

        // Discount rule: 50 EGP off if booking is at least 120 minutes
        var discountTerms = new PriceRuleTerms(PriceRuleKind.Discount, PriceAdjustment.FixedAmountOff, null, "Long Game 50 EGP Off", 50m, null, null, null, null, null, null, 120);
        var discountRule = PriceRule.Create(_venueId, discountTerms, _now).Value;

        // Quote a 2-hour game on Friday evening (2026-11-13) 19:00 to 21:00 Cairo time
        // Local start = 2026-11-13 19:00, Friday, within peak (18-22)
        // Rate = 200 base + 20% peak (40) + 10% weekend (20) = 260 EGP/hour
        // For 2 hours: 264 * 2 = 528 EGP subtotal
        // Discount = 50 EGP
        // Total = 478 EGP
        var startLocal = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, _cairo);
        var endUtc = startUtc.AddHours(2);

        var quote = PricingCalculator.Quote(
            _pitch1,
            [baseRule, peakRule, weekendRule, discountRule],
            startUtc,
            endUtc,
            deposit: null,
            _cairo);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.Subtotal.Should().Be(528.00m);
        quote.Value.Discount.Should().Be(50.00m);
        quote.Value.DiscountRule.Should().Be("Long Game 50 EGP Off");
        quote.Value.Total.Should().Be(478.00m);
        quote.Value.DurationMinutes.Should().Be(120);
    }

    [Fact]
    public void PricingCalculator_RejectsInvalidDurationOrMissingBasePrice()
    {
        var startUtc = _now;
        var endUtc = _now.AddMinutes(40); // 40 min is not a multiple of 15 min!

        var baseTerms = new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 200m, null, null, null, null, null, null, null);
        var baseRule = PriceRule.Create(_venueId, baseTerms, _now).Value;

        // 1. Duration not multiple of SliceMinutes (15)
        var quote1 = PricingCalculator.Quote(_pitch1, [baseRule], startUtc, endUtc, null, _cairo);
        quote1.IsFailure.Should().BeTrue();
        quote1.Error.Code.Should().Be("Schedule.InvalidDuration");

        // 2. Missing base price
        var quote2 = PricingCalculator.Quote(_pitch1, [], startUtc, startUtc.AddHours(1), null, _cairo);
        quote2.IsFailure.Should().BeTrue();
        quote2.Error.Code.Should().Be("Pricing.NoBasePrice");
    }
}
