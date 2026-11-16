using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 2 (P2) Edge-Case Tests: Pricing engine, quote snapshots, precedence, rounding,
/// overnight slots, Africa/Cairo DST transitions, and tamper resistance (rule.md §6).
/// </summary>
public sealed class BookingPricingAndQuoteEdgeCaseTests
{
    private static readonly DateTime _nowUtc = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly TimeZoneInfo _cairoTz = new FakeClock(_nowUtc).CairoTimeZone;
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _otherPitchId = Guid.NewGuid();

    // Helper to create price rules
    private PriceRule CreateRule(
        PriceRuleKind kind,
        PriceAdjustment adjustment,
        string name,
        decimal? amount = null,
        decimal? percent = null,
        Guid? pitchId = null,
        DaysOfWeekMask? days = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int? minDurationMinutes = null)
    {
        var terms = new PriceRuleTerms(
            kind,
            adjustment,
            pitchId,
            name,
            amount,
            percent,
            days,
            startTime,
            endTime,
            startDate,
            endDate,
            minDurationMinutes);

        return PriceRule.Create(_venueId, terms, _nowUtc).Value;
    }

    // =========================================================================
    // 1. Pricing Precedence
    // Order: Base -> Pitch Override -> Peak -> Weekend -> Holiday -> SpecialDate -> Discounts
    // =========================================================================

    [Fact]
    public void Pricing_PitchOverride_BeatsVenueDefaultBasePrice()
    {
        var venueBase = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Venue Base", amount: 400m);
        var pitchOverride = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Pitch 1 Base", amount: 600m, pitchId: _pitchId);

        var start = new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);

        // Quote for _pitchId: gets 600
        var quotePitch1 = PricingCalculator.Quote(_pitchId, [venueBase, pitchOverride], start, end, null, _cairoTz);
        quotePitch1.IsSuccess.Should().BeTrue();
        quotePitch1.Value.Total.Should().Be(600m);

        // Quote for _otherPitchId: falls back to 400
        var quoteOther = PricingCalculator.Quote(_otherPitchId, [venueBase, pitchOverride], start, end, null, _cairoTz);
        quoteOther.IsSuccess.Should().BeTrue();
        quoteOther.Value.Total.Should().Be(400m);
    }

    [Fact]
    public void Pricing_FullLayerHierarchy_AppliesInCorrectPrecedence()
    {
        // 1. Base rate: 400 EGP/h
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 400m);
        // 2. Peak modifier: +25% (400 * 1.25 = 500 EGP/h)
        var peakRule = CreateRule(PriceRuleKind.Peak, PriceAdjustment.Percentage, "Peak +25%", percent: 25m,
            startTime: new TimeOnly(18, 0), endTime: new TimeOnly(23, 0));
        // 3. Weekend modifier: +10% on top of peak (500 * 1.10 = 550 EGP/h)
        var weekendRule = CreateRule(PriceRuleKind.Weekend, PriceAdjustment.Percentage, "Weekend +10%", percent: 10m,
            days: DaysOfWeekMask.Friday | DaysOfWeekMask.Saturday);
        // 4. Special date rule: overrides rate to 700 EGP/h
        var specialDateRule = CreateRule(PriceRuleKind.SpecialDate, PriceAdjustment.FixedHourlyRate, "Tournament Night", amount: 700m,
            startDate: new DateOnly(2026, 11, 20), endDate: new DateOnly(2026, 11, 20));
        // 5. Discount: 100 EGP off on 2-hour matches
        var discountRule = CreateRule(PriceRuleKind.Discount, PriceAdjustment.FixedAmountOff, "2-Hour Deal", amount: 100m,
            minDurationMinutes: 120);

        // Scenario A: Friday 18:00-19:00 without special date rule (Base 400 + Peak 25% = 500, + Weekend 10% = 550 EGP)
        var cairoLocalStartA = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Unspecified); // Friday
        var startUtcA = TimeZoneInfo.ConvertTimeToUtc(cairoLocalStartA, _cairoTz);
        var endUtcA = startUtcA.AddHours(1);

        var quoteA = PricingCalculator.Quote(_pitchId, [baseRule, peakRule, weekendRule], startUtcA, endUtcA, null, _cairoTz);
        quoteA.IsSuccess.Should().BeTrue();
        quoteA.Value.Total.Should().Be(550m);

        // Scenario B: With Special Date rule active (wins over peak & weekend, setting rate to 700 EGP/h)
        var quoteB = PricingCalculator.Quote(_pitchId, [baseRule, peakRule, weekendRule, specialDateRule], startUtcA, endUtcA, null, _cairoTz);
        quoteB.IsSuccess.Should().BeTrue();
        quoteB.Value.Total.Should().Be(700m);

        // Scenario C: 2-hour booking with discount applied on total (700 * 2 = 1400 - 100 = 1300 EGP)
        var endUtc2H = startUtcA.AddHours(2);
        var quoteC = PricingCalculator.Quote(_pitchId, [baseRule, peakRule, weekendRule, specialDateRule, discountRule], startUtcA, endUtc2H, null, _cairoTz);
        quoteC.IsSuccess.Should().BeTrue();
        quoteC.Value.Subtotal.Should().Be(1400m);
        quoteC.Value.Discount.Should().Be(100m);
        quoteC.Value.Total.Should().Be(1300m);
    }

    [Fact]
    public void Pricing_MultiDiscounts_PicksSingleBestDiscount()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 1000m);
        var smallPercent = CreateRule(PriceRuleKind.Discount, PriceAdjustment.Percentage, "10% Off", percent: 10m);
        var bigPercent = CreateRule(PriceRuleKind.Discount, PriceAdjustment.Percentage, "25% Off", percent: 25m);
        var fixedOff = CreateRule(PriceRuleKind.Discount, PriceAdjustment.FixedAmountOff, "150 EGP Off", amount: 150m);

        var start = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);

        // 25% off 1000 = 250 EGP (better than 10% = 100 EGP and 150 EGP off)
        var quote = PricingCalculator.Quote(_pitchId, [baseRule, smallPercent, bigPercent, fixedOff], start, end, null, _cairoTz);
        quote.IsSuccess.Should().BeTrue();
        quote.Value.Discount.Should().Be(250m);
        quote.Value.Total.Should().Be(750m);
        quote.Value.DiscountRule.Should().Be("25% Off");
    }

    // =========================================================================
    // 2. Money Precision, Rounding, and Invalid Price Bounds
    // =========================================================================

    [Fact]
    public void Pricing_MidpointRoundingAwayFromZero_EliminatesFloatDrift()
    {
        // 33.33 EGP/h -> 3 hours = 99.99 EGP; 1 slice (15m) = 33.33 * 0.25 = 8.3325 -> 8.33
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Tricky Fractions", amount: 33.33m);

        var start = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(3); // 12 slices of 15 min

        var quote = PricingCalculator.Quote(_pitchId, [baseRule], start, end, null, _cairoTz);
        quote.IsSuccess.Should().BeTrue();
        quote.Value.Total.Should().Be(99.99m, "subtotal is 33.33 * 3 = 99.99, without float rounding distortion");
    }

    [Fact]
    public void Booking_Create_RejectsZeroOrNegativePrice()
    {
        var start = _nowUtc.AddHours(2);
        var end = _nowUtc.AddHours(3);

        // Money constructor and Money.Create reject negative amounts at the value-object boundary
        var negativeMoneyResult = Money.Create(-100m, "EGP");
        negativeMoneyResult.IsFailure.Should().BeTrue();

        var act = () => new Money(-100m, "EGP");
        act.Should().Throw<ArgumentException>();

        // Zero price rejected by Booking.Create domain check
        var zeroPriceResult = Booking.Create(
            Guid.NewGuid(), "MLB-ZERO", _venueId, _pitchId, Guid.NewGuid(),
            start, end, new Money(0m, "EGP"), new Money(0m, "EGP"),
            _nowUtc.AddMinutes(15), null, null, _nowUtc, "actor");

        zeroPriceResult.IsFailure.Should().BeTrue();
        zeroPriceResult.Error.Code.Should().Be("Booking.InvalidPrice");
    }

    [Fact]
    public void Booking_Create_RejectsDepositExceedingTotalPrice()
    {
        var start = _nowUtc.AddHours(2);
        var end = _nowUtc.AddHours(3);

        var result = Booking.Create(
            Guid.NewGuid(), "MLB-OVERDEP", _venueId, _pitchId, Guid.NewGuid(),
            start, end, Money.Create(500m, "EGP").Value, Money.Create(501m, "EGP").Value,
            _nowUtc.AddMinutes(15), null, null, _nowUtc, "actor");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.InvalidDeposit");
    }

    [Fact]
    public void Booking_SnapshotImmutability_RuleChangeDoesNotAffectCreatedBooking()
    {
        var start = _nowUtc.AddHours(2);
        var end = _nowUtc.AddHours(3);
        var snapshot = new PriceQuoteSnapshot(
            _venueId, _pitchId, start, end, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed",
            [new PriceLineSnapshot(start, end, 500m, 500m, ["Initial Base"])],
            _nowUtc);

        var booking = Booking.Create(
            Guid.NewGuid(), "MLB-SNAP", _venueId, _pitchId, Guid.NewGuid(),
            start, end, Money.Create(500m, "EGP").Value, Money.Create(100m, "EGP").Value,
            _nowUtc.AddMinutes(15), snapshot, null, _nowUtc, "actor").Value;

        // Even if the price rules are changed/updated to 800m later:
        var newRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "New Base", amount: 800m);
        newRule.Amount.Should().Be(800m);

        // The existing booking retains its original agreed price and snapshot
        booking.TotalPrice.Amount.Should().Be(500m);
        booking.DepositAmount.Amount.Should().Be(100m);
        booking.GetPriceSnapshot()!.Total.Should().Be(500m);
    }

    // =========================================================================
    // 3. Overnight Slots Crossing Midnight
    // =========================================================================

    [Fact]
    public void Pricing_OvernightSlot_PricesBeforeAndAfterMidnightAccurately()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base Day", amount: 400m);
        // Late night peak (23:00 to 02:00) gets +50% = 600 EGP/h
        var lateNightPeak = CreateRule(PriceRuleKind.Peak, PriceAdjustment.Percentage, "Late Night", percent: 50m,
            startTime: new TimeOnly(23, 0), endTime: new TimeOnly(2, 0));

        // Slot: 22:00 to 01:00 Cairo local time (3 hours):
        // 22:00 - 23:00: Base 400 EGP
        // 23:00 - 00:00: Peak 600 EGP
        // 00:00 - 01:00 (Next calendar day): Peak 600 EGP
        // Expected total = 400 + 600 + 600 = 1600 EGP
        var localStart = new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, _cairoTz);
        var endUtc = startUtc.AddHours(3);

        var quote = PricingCalculator.Quote(_pitchId, [baseRule, lateNightPeak], startUtc, endUtc, null, _cairoTz);
        quote.IsSuccess.Should().BeTrue();
        quote.Value.Total.Should().Be(1600m);
        quote.Value.Lines.Should().HaveCount(2, "first line at 400/h, second continuous line at 600/h across midnight");
    }

    // =========================================================================
    // 4. Africa/Cairo DST Transitions (Spring Forward & Fall Back)
    // =========================================================================

    [Fact]
    public void Pricing_DuringCairoDstSpringForward_CalculatesAccuratelyWithoutCrashing()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 500m);

        // In 2026, Egypt DST spring forward occurs on last Friday of April (April 24, 2026).
        // At 00:00 Cairo local, the clock jumps forward to 01:00 (skipping hour 00:00-01:00).
        // 23:00 local (April 23) in UTC is 21:00 UTC.
        // A 2-hour UTC booking starting at 21:00 UTC:
        var startUtc = new DateTime(2026, 4, 23, 21, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddHours(2);

        var quote = PricingCalculator.Quote(_pitchId, [baseRule], startUtc, endUtc, null, _cairoTz);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.DurationMinutes.Should().Be(120);
        quote.Value.Total.Should().Be(1000m, "2 UTC hours played at 500/h");
    }

    [Fact]
    public void Pricing_DuringCairoDstFallBack_CalculatesAccuratelyWithoutDuplicateHours()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 500m);

        // In 2026, Egypt DST fall back occurs late October (October 29/30, 2026).
        // Clock turns back 1 hour from 00:00 to 23:00.
        var startUtc = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddHours(3);

        var quote = PricingCalculator.Quote(_pitchId, [baseRule], startUtc, endUtc, null, _cairoTz);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.DurationMinutes.Should().Be(180);
        quote.Value.Total.Should().Be(1500m, "3 real UTC hours played at 500/h");
    }

    [Fact]
    public void Pricing_DuringCairoDstSpringForward_WithPeakRuleAtTransitionBoundary_CalculatesAccurately()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 400m);
        // Peak rule whose boundary is the transition hour (23:00 to 00:00 midnight)
        var peakRule = CreateRule(PriceRuleKind.Peak, PriceAdjustment.Percentage, "Midnight Boundary Peak",
            percent: 50m, startTime: new TimeOnly(23, 0), endTime: new TimeOnly(0, 0));

        // April 23 21:00 UTC is 23:00 Cairo local (UTC+2).
        // 21:00 - 22:00 UTC: Cairo local is 23:00 - 00:00 (matches peak window -> 600 EGP/h).
        // At 22:00 UTC: DST spring-forward jumps 00:00 -> 01:00 Cairo local (UTC+3).
        // 22:00 - 23:00 UTC: Cairo local is 01:00 - 02:00 (past boundary -> base rate 400 EGP/h).
        var startUtc = new DateTime(2026, 4, 23, 21, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddHours(2);

        var quote = PricingCalculator.Quote(_pitchId, [baseRule, peakRule], startUtc, endUtc, null, _cairoTz);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.DurationMinutes.Should().Be(120);
        quote.Value.Total.Should().Be(1000m, "1h peak at 600 + 1h base at 400 across spring-forward jump = 1000 EGP");
        quote.Value.Lines.Should().HaveCount(2);
        quote.Value.Lines[0].HourlyRate.Should().Be(600m);
        quote.Value.Lines[0].AppliedRules.Should().Contain("Midnight Boundary Peak");
        quote.Value.Lines[1].HourlyRate.Should().Be(400m);
        quote.Value.Lines[1].AppliedRules.Should().NotContain("Midnight Boundary Peak");
    }

    [Fact]
    public void Pricing_DuringCairoDstFallBack_WithPeakRuleAtTransitionBoundary_CalculatesAccurately()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 400m);
        // Peak rule whose boundary is the transition hour (23:00 to 00:00 midnight)
        var peakRule = CreateRule(PriceRuleKind.Peak, PriceAdjustment.Percentage, "Midnight Boundary Peak",
            percent: 50m, startTime: new TimeOnly(23, 0), endTime: new TimeOnly(0, 0));

        // October 29 20:00 UTC is 23:00 Cairo local (UTC+3).
        // 20:00 - 21:00 UTC: Cairo local 23:00 - 00:00 (first pass -> peak 600 EGP/h).
        // At 21:00 UTC: DST fall-back turns clock back from 00:00 -> 23:00 Cairo local (UTC+2).
        // 21:00 - 22:00 UTC: Cairo local repeats 23:00 - 00:00 (second pass -> peak 600 EGP/h).
        // 22:00 - 23:00 UTC: Cairo local is 00:00 - 01:00 (past midnight boundary -> base rate 400 EGP/h).
        var startUtc = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddHours(3);

        var quote = PricingCalculator.Quote(_pitchId, [baseRule, peakRule], startUtc, endUtc, null, _cairoTz);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.DurationMinutes.Should().Be(180);
        quote.Value.Total.Should().Be(1600m, "2h peak at 600 across repeated local hour + 1h base at 400 = 1600 EGP");
        quote.Value.Lines.Should().HaveCount(2);
        quote.Value.Lines[0].HourlyRate.Should().Be(600m);
        quote.Value.Lines[0].EndUtc.Should().Be(startUtc.AddHours(2), "consecutive peak slices merge across the repeated DST hour");
        quote.Value.Lines[1].HourlyRate.Should().Be(400m);
    }

    // =========================================================================
    // 5. Duration Boundaries & Slot Multiples
    // =========================================================================

    [Fact]
    public void Pricing_NonMultipleOfSlice_RejectsWithInvalidDuration()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 400m);

        var start = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        // 17 minutes is not a multiple of 15-minute slice
        var end = start.AddMinutes(17);

        var quote = PricingCalculator.Quote(_pitchId, [baseRule], start, end, null, _cairoTz);
        quote.IsFailure.Should().BeTrue();
        quote.Error.Code.Should().Be("Schedule.InvalidDuration");
    }

    [Fact]
    public void Pricing_ZeroOrNegativeDuration_RejectsWithInvalidDuration()
    {
        var baseRule = CreateRule(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, "Base", amount: 400m);
        var start = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

        // Zero duration
        var zeroQuote = PricingCalculator.Quote(_pitchId, [baseRule], start, start, null, _cairoTz);
        zeroQuote.IsFailure.Should().BeTrue();

        // Negative duration (end before start)
        var negQuote = PricingCalculator.Quote(_pitchId, [baseRule], start, start.AddMinutes(-30), null, _cairoTz);
        negQuote.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Booking_ConfirmOnlinePayment_WhenInsufficientAmount_FailsValidation()
    {
        var start = DateTime.UtcNow.AddDays(1);
        var end = start.AddHours(2);
        var priceSnapshot = new PriceQuoteSnapshot(
            _venueId,
            _pitchId,
            start,
            end,
            120,
            "EGP",
            800m,
            0m,
            null,
            860m,
            60m,
            "FixedAmount",
            [],
            DateTime.UtcNow,
            NetPitchPrice: 800m,
            PlatformCommission: 60m,
            OnlineDepositAmount: 60m,
            VenuePayAmount: 800m);

        var booking = Booking.Create(
            Guid.NewGuid(),
            "MLB-TEST",
            _venueId,
            _pitchId,
            Guid.NewGuid(),
            start,
            end,
            Money.From(860m),
            Money.From(60m),
            DateTime.UtcNow.AddMinutes(15),
            priceSnapshot,
            null,
            DateTime.UtcNow,
            "test-actor").Value;

        // Attempting to confirm with less than the 60 EGP platform commission (e.g. 50 EGP)
        var result = booking.ConfirmOnlinePayment("order-1", "tx-1", Money.From(50m), DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.InsufficientPayment");
    }

    [Fact]
    public void Venue_SetCommission_ExceedingLimits_ThrowsArgumentException()
    {
        var venue = Venue.Create(
            _venueId,
            "Test Venue",
            "test-venue",
            "Description",
            Guid.NewGuid(),
            "Address",
            "City",
            "Area",
            null,
            null,
            "+201000000000",
            null,
            null,
            DateTime.UtcNow);

        // Percentage cannot exceed 100%
        var actPercentage = () => venue.SetCommission(true, CommissionType.Percentage, 105m, DateTime.UtcNow);
        actPercentage.Should().Throw<ArgumentException>()
            .WithMessage("*Percentage commission rate cannot exceed 100%*");

        // Hourly fixed cannot exceed 100,000 EGP
        var actHourly = () => venue.SetCommission(true, CommissionType.PerHourFixed, 150_000m, DateTime.UtcNow);
        actHourly.Should().Throw<ArgumentException>()
            .WithMessage("*Hourly commission rate cannot exceed 100,000*");

        // Valid rates succeed
        venue.SetCommission(true, CommissionType.Percentage, 15m, DateTime.UtcNow);
        venue.CommissionRate.Should().Be(15m);
        venue.CommissionType.Should().Be(CommissionType.Percentage);
    }
}
