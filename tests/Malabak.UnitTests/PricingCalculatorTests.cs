using FluentAssertions;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel.Contracts;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PricingCalculatorTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();

    // 2026-11-05 is a Thursday (Cairo is UTC+2 in November).
    private static DateTime Local(int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified), _cairo);

    [Fact]
    public void BaseRate_IsCharged_PerMinutePlayed()
    {
        var quote = Quote([Base(400)], Local(5, 16), Local(5, 17, 30));

        quote.Subtotal.Should().Be(600m);
        quote.Total.Should().Be(600m);
        quote.DurationMinutes.Should().Be(90);
        quote.Lines.Should().ContainSingle();
    }

    [Fact]
    public void PitchBase_OverridesVenueBase()
    {
        var quote = Quote([Base(400), Base(500, pitch: true)], Local(5, 16), Local(5, 17));

        quote.Total.Should().Be(500m);
    }

    [Fact]
    public void NoBaseRate_IsAnError()
    {
        var result = PricingCalculator.Quote(_pitchId, [Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 20)], Local(5, 16), Local(5, 17), null, _cairo);

        result.Error.Should().Be(PriceRuleErrors.NoBasePrice);
    }

    [Fact]
    public void PeakPercentage_AppliesOnlyInsideItsWindow_SplittingLines()
    {
        var peak = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 25, start: new TimeOnly(18, 0), end: new TimeOnly(23, 0));

        var quote = Quote([Base(400), peak], Local(5, 17), Local(5, 19));

        quote.Total.Should().Be(400m + 500m);
        quote.Lines.Should().HaveCount(2);
        quote.Lines[1].AppliedRules.Should().Contain("peak");
    }

    [Fact]
    public void OvernightPeakWindow_CoversHoursAfterMidnight()
    {
        var latePeak = Rule(PriceRuleKind.Peak, PriceAdjustment.FixedHourlyRate, amount: 600, start: new TimeOnly(22, 0), end: new TimeOnly(2, 0));

        var quote = Quote([Base(400), latePeak], Local(5, 23), Local(6, 1));

        quote.Total.Should().Be(1200m);
    }

    [Fact]
    public void Weekend_StacksOnPeak_InPrecedenceOrder()
    {
        // Friday 2026-11-06: base 400 -> peak +25% = 500 -> weekend +10% = 550.
        var peak = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 25, start: new TimeOnly(18, 0), end: new TimeOnly(23, 0));
        var weekend = Rule(PriceRuleKind.Weekend, PriceAdjustment.Percentage, percent: 10, days: DaysOfWeekMask.Friday | DaysOfWeekMask.Saturday);

        Quote([Base(400), peak, weekend], Local(6, 19), Local(6, 20)).Total.Should().Be(550m);
        Quote([Base(400), peak, weekend], Local(5, 19), Local(5, 20)).Total.Should().Be(500m, "Thursday is not a weekend day");
    }

    [Fact]
    public void Holiday_AppliesOnOfficialHolidays_AndSpecialDateWinsOverIt()
    {
        // 2026-10-06 (Armed Forces Day) is a fixed public holiday in Egypt.
        var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Unspecified), _cairo);
        var holiday = Rule(PriceRuleKind.Holiday, PriceAdjustment.FixedHourlyRate, amount: 700);
        var special = Rule(PriceRuleKind.SpecialDate, PriceAdjustment.FixedHourlyRate, amount: 800,
            from: new DateOnly(2026, 10, 6), to: new DateOnly(2026, 10, 6));

        Quote([Base(400), holiday], start, start.AddHours(1)).Total.Should().Be(700m);
        Quote([Base(400), holiday, special], start, start.AddHours(1)).Total.Should().Be(800m);
        Quote([Base(400), holiday], Local(5, 16), Local(5, 17)).Total.Should().Be(400m, "not a holiday");
    }

    [Fact]
    public void BestSingleDiscount_Applies_WithMinimumDuration()
    {
        var tenPercent = Rule(PriceRuleKind.Discount, PriceAdjustment.Percentage, percent: 10, minDuration: 120);
        var fiftyOff = Rule(PriceRuleKind.Discount, PriceAdjustment.FixedAmountOff, amount: 50);

        var twoHours = Quote([Base(400), tenPercent, fiftyOff], Local(5, 16), Local(5, 18));
        twoHours.Subtotal.Should().Be(800m);
        twoHours.Discount.Should().Be(80m);
        twoHours.Total.Should().Be(720m);

        var oneHour = Quote([Base(400), tenPercent, fiftyOff], Local(5, 16), Local(5, 17));
        oneHour.Discount.Should().Be(50m, "the 10% discount needs 2 hours");
    }

    [Theory]
    [InlineData(DepositType.Percentage, 30, 240)]
    [InlineData(DepositType.FixedAmount, 150, 150)]
    [InlineData(DepositType.Full, 0, 800)]
    public void Deposit_IsCalculatedFromTheDiscountedTotal(DepositType type, decimal value, decimal expected)
    {
        var quote = PricingCalculator.Quote(_pitchId, [Base(400)], Local(5, 16), Local(5, 18), DepositRule.Create(type, value), _cairo).Value;

        quote.Deposit.Should().Be(expected);
    }

    [Theory]
    [InlineData(CommissionType.Percentage, 15, 120)]   // 15% of 800 = 120 > the 50 EGP deposit
    [InlineData(CommissionType.PerHourFixed, 40, 80)]  // 2 hours × 40 = 80 > 50
    [InlineData(CommissionType.Percentage, 5, 50)]     // 40 < 50: the venue's deposit already covers it
    public void Deposit_NeverFallsBelowTheCommission_ThePlatformKeepsOutOfIt(CommissionType type, decimal rate, decimal expectedDeposit)
    {
        var quote = PricingCalculator.Quote(_pitchId, [Base(400)], Local(5, 16), Local(5, 18), DepositRule.Create(DepositType.FixedAmount, 50), _cairo,
            type, rate).Value;

        quote.Deposit.Should().Be(expectedDeposit);
        quote.Deposit.Should().BeGreaterThanOrEqualTo(quote.PlatformCommission);
    }

    [Fact]
    public void InactiveDeletedOrOtherPitchRules_AreIgnored()
    {
        var otherPitch = PriceRule.Create(_venueId, Terms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, pitchId: Guid.NewGuid(), amount: 900), _now).Value;
        var deleted = Base(950, pitch: true);
        deleted.MarkDeleted(_now);

        Quote([Base(400), otherPitch, deleted], Local(5, 16), Local(5, 17)).Total.Should().Be(400m);
    }

    [Fact]
    public void InvalidRanges_AreRejected()
    {
        PricingCalculator.Quote(_pitchId, [Base(400)], Local(5, 17), Local(5, 16), null, _cairo).IsFailure.Should().BeTrue();
        PricingCalculator.Quote(_pitchId, [Base(400)], Local(5, 16), Local(5, 16, 10), null, _cairo).IsFailure.Should().BeTrue("10 minutes is not a 15-minute step");

        // Security: huge ranges used to spin through millions of slices (and overflow the minute count).
        PricingCalculator.Quote(_pitchId, [Base(400)], Local(5, 16), Local(5, 16).AddDays(1).AddMinutes(15), null, _cairo).IsFailure.Should().BeTrue("over 24 hours");
        PricingCalculator.Quote(_pitchId, [Base(400)], DateTime.MinValue, DateTime.MaxValue.AddTicks(-(DateTime.MaxValue.Ticks % TimeSpan.TicksPerHour)), null, _cairo)
            .IsFailure.Should().BeTrue();
        PricingCalculator.Quote(_pitchId, [Base(400)], Local(5, 16), Local(5, 16).AddDays(1), null, _cairo).IsSuccess.Should().BeTrue("exactly 24 hours is allowed");
    }

    [Fact]
    public void DstNight_ChargesOnlyTheMinutesActuallyPlayed()
    {
        // 2026-04-24 00:00 Cairo clocks jump to 01:00: a 23:00-02:00 booking lasts 2 real hours.
        var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 4, 23, 23, 0, 0, DateTimeKind.Unspecified), _cairo);
        var end = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 4, 24, 2, 0, 0, DateTimeKind.Unspecified), _cairo);
        var hours = (decimal)(end - start).TotalHours;

        Quote([Base(400)], start, end).Total.Should().Be(400m * hours);
    }

    [Theory]
    [InlineData(PriceRuleKind.Base, PriceAdjustment.Percentage, null, 10.0)]
    [InlineData(PriceRuleKind.Discount, PriceAdjustment.FixedHourlyRate, 50.0, null)]
    [InlineData(PriceRuleKind.Peak, PriceAdjustment.Percentage, null, 0.0)]
    [InlineData(PriceRuleKind.Discount, PriceAdjustment.Percentage, null, 150.0)]
    [InlineData(PriceRuleKind.Peak, PriceAdjustment.FixedHourlyRate, -5.0, null)]
    public void InvalidRuleTerms_AreRejected(PriceRuleKind kind, PriceAdjustment adjustment, double? amount, double? percent)
    {
        PriceRule.Create(_venueId, Terms(kind, adjustment, amount: (decimal?)amount, percent: (decimal?)percent), _now).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void BaseRule_CannotHaveConditions_AndSpecialDateNeedsDates()
    {
        PriceRule.Create(_venueId, Terms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, amount: 400, days: DaysOfWeekMask.Friday), _now)
            .Error.Should().Be(PriceRuleErrors.BaseMustBeUnconditional);
        PriceRule.Create(_venueId, Terms(PriceRuleKind.SpecialDate, PriceAdjustment.FixedHourlyRate, amount: 400), _now)
            .Error.Should().Be(PriceRuleErrors.InvalidDates);
    }

    [Fact]
    public void Overlap_IsDetected_ForSameKindAndScope()
    {
        var evening = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 20, start: new TimeOnly(18, 0), end: new TimeOnly(22, 0));
        var night = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 30, start: new TimeOnly(21, 0), end: new TimeOnly(1, 0));
        var morning = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 10, start: new TimeOnly(8, 0), end: new TimeOnly(10, 0));
        var fridayOnly = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 10, days: DaysOfWeekMask.Friday,
            start: new TimeOnly(18, 0), end: new TimeOnly(22, 0));
        var saturdayOnly = Rule(PriceRuleKind.Peak, PriceAdjustment.Percentage, percent: 10, days: DaysOfWeekMask.Saturday,
            start: new TimeOnly(18, 0), end: new TimeOnly(22, 0));

        evening.Overlaps(night).Should().BeTrue();
        evening.Overlaps(morning).Should().BeFalse();
        fridayOnly.Overlaps(saturdayOnly).Should().BeFalse();
    }

    [Fact]
    public void Commission_PlatformDefault_CalculatesPerHour()
    {
        // 2 hours at 400 EGP base rate = 800 EGP net pitch price. Default commission 30 EGP/hour = 60 EGP.
        var quote = PricingCalculator.Quote(
            _pitchId,
            [Base(400)],
            Local(5, 16),
            Local(5, 18),
            null,
            _cairo,
            CommissionType.PlatformDefault,
            commissionRate: null,
            defaultHourlyCommission: 30m).Value;

        quote.Subtotal.Should().Be(800m);
        quote.Discount.Should().Be(0m);
        quote.NetPitchPrice.Should().Be(800m);
        quote.PlatformCommission.Should().Be(60m);
        quote.Total.Should().Be(800m);
        quote.OnlineDepositAmount.Should().Be(800m);
        quote.VenuePayAmount.Should().Be(0m);
    }

    [Fact]
    public void Commission_PerHourFixed_UsesCustomVenueRate()
    {
        // 90 minutes (1.5 hours) at 400 EGP/hour = 600 EGP. Custom commission 40 EGP/hour = 60 EGP.
        var quote = PricingCalculator.Quote(
            _pitchId,
            [Base(400)],
            Local(5, 16),
            Local(5, 17, 30),
            null,
            _cairo,
            CommissionType.PerHourFixed,
            commissionRate: 40m,
            defaultHourlyCommission: 30m).Value;

        quote.Subtotal.Should().Be(600m);
        quote.NetPitchPrice.Should().Be(600m);
        quote.PlatformCommission.Should().Be(60m);
        quote.Total.Should().Be(600m);
        quote.OnlineDepositAmount.Should().Be(600m);
        quote.VenuePayAmount.Should().Be(0m);
    }

    [Fact]
    public void Commission_Percentage_CalculatesFromNetPitchPrice()
    {
        // 2 hours at 400 EGP = 800 EGP. 50 EGP discount -> Net = 750 EGP. 10% commission = 75 EGP.
        var fiftyOff = Rule(PriceRuleKind.Discount, PriceAdjustment.FixedAmountOff, amount: 50);
        var quote = PricingCalculator.Quote(
            _pitchId,
            [Base(400), fiftyOff],
            Local(5, 16),
            Local(5, 18),
            null,
            _cairo,
            CommissionType.Percentage,
            commissionRate: 10m,
            defaultHourlyCommission: 30m).Value;

        quote.Subtotal.Should().Be(800m);
        quote.Discount.Should().Be(50m);
        quote.NetPitchPrice.Should().Be(750m);
        quote.PlatformCommission.Should().Be(75m);
        quote.Total.Should().Be(750m);
        quote.OnlineDepositAmount.Should().Be(750m);
        quote.VenuePayAmount.Should().Be(0m);
    }

    [Fact]
    public void Commission_Exempt_ResultsInZeroCommission()
    {
        var quote = PricingCalculator.Quote(
            _pitchId,
            [Base(400)],
            Local(5, 16),
            Local(5, 18),
            null,
            _cairo,
            CommissionType.Exempt,
            commissionRate: null,
            defaultHourlyCommission: 50m).Value;

        quote.NetPitchPrice.Should().Be(800m);
        quote.PlatformCommission.Should().Be(0m);
        quote.Total.Should().Be(800m);
    }

    private PriceQuote Quote(IReadOnlyCollection<PriceRule> rules, DateTime start, DateTime end) =>
        PricingCalculator.Quote(_pitchId, rules, start, end, null, _cairo).Value;

    private PriceRule Base(decimal amount, bool pitch = false) =>
        PriceRule.Create(_venueId, Terms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, pitchId: pitch ? _pitchId : null, amount: amount), _now).Value;

    private PriceRule Rule(
        PriceRuleKind kind, PriceAdjustment adjustment, decimal? amount = null, decimal? percent = null, DaysOfWeekMask? days = null,
        TimeOnly? start = null, TimeOnly? end = null, DateOnly? from = null, DateOnly? to = null, int? minDuration = null) =>
        PriceRule.Create(_venueId, Terms(kind, adjustment, amount: amount, percent: percent, days: days, start: start, end: end, from: from, to: to, minDuration: minDuration), _now).Value;

    private static PriceRuleTerms Terms(
        PriceRuleKind kind, PriceAdjustment adjustment, Guid? pitchId = null, decimal? amount = null, decimal? percent = null,
        DaysOfWeekMask? days = null, TimeOnly? start = null, TimeOnly? end = null, DateOnly? from = null, DateOnly? to = null, int? minDuration = null) =>
        new(kind, adjustment, pitchId, kind.ToString().ToLowerInvariant(), amount, percent, days, start, end, from, to, minDuration);
}
