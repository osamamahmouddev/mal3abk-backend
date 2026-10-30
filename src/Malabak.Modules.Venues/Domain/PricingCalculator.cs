using Malabak.SharedKernel;
using Malabak.SharedKernel.Reference;

namespace Malabak.Modules.Venues.Domain;

/// <summary>A run of consecutive slices priced at the same hourly rate with the same rules.</summary>
public sealed record PriceLine(DateTime StartUtc, DateTime EndUtc, decimal HourlyRate, decimal Amount, IReadOnlyList<string> AppliedRules);

public sealed record PriceQuote(
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    int DurationMinutes,
    string Currency,
    decimal Subtotal,
    decimal Discount,
    string? DiscountRule,
    decimal Total,
    decimal Deposit,
    DepositType DepositType,
    IReadOnlyList<PriceLine> Lines);

/// <summary>
/// Server-side price calculation. Precedence per 15-minute slice: base (pitch override over venue default) -> peak ->
/// weekend -> holiday or special date (special date wins) ; then the single best discount on the subtotal; then the deposit.
/// Slices step through real UTC time and are matched on Cairo local time, so overnight bookings and DST days price the
/// minutes actually played. Amounts are rounded to 2 decimals once, at the end (no per-slice rounding drift).
/// </summary>
public static class PricingCalculator
{
    public const int SliceMinutes = 15;

    /// <summary>Longest range that can be priced in one call (a day); bookings are far shorter.</summary>
    public const int MaxQuoteMinutes = 24 * 60;

    private static readonly PriceRuleKind[] _modifierOrder =
        [PriceRuleKind.Peak, PriceRuleKind.Weekend, PriceRuleKind.Holiday, PriceRuleKind.SpecialDate];

    public static Result<PriceQuote> Quote(
        Guid pitchId,
        IReadOnlyCollection<PriceRule> venueRules,
        DateTime startUtc,
        DateTime endUtc,
        DepositRule? deposit,
        TimeZoneInfo timeZone)
    {
        if (endUtc <= startUtc || endUtc - startUtc > TimeSpan.FromMinutes(MaxQuoteMinutes)
            || (endUtc - startUtc).Ticks % TimeSpan.FromMinutes(SliceMinutes).Ticks != 0)
        {
            return Result.Failure<PriceQuote>(SpecialScheduleErrors.InvalidDuration);
        }

        var minutes = (int)(endUtc - startUtc).TotalMinutes;

        var rules = venueRules.Where(r => r.IsActive && !r.IsDeleted && r.AppliesToPitch(pitchId)).ToList();
        var currencies = rules.Where(r => r.Price is not null).Select(r => r.Price!.Currency).Distinct().ToList();
        if (currencies.Count > 1)
        {
            return Result.Failure<PriceQuote>(PriceRuleErrors.CurrencyMismatch);
        }

        var baseRule = MostSpecific(rules.Where(r => r.Kind == PriceRuleKind.Base));
        if (baseRule is null)
        {
            return Result.Failure<PriceQuote>(PriceRuleErrors.NoBasePrice);
        }

        var currency = baseRule.Price!.Currency;
        var lines = new List<PriceLine>();
        var subtotal = 0m;
        for (var sliceStart = startUtc; sliceStart < endUtc; sliceStart = sliceStart.AddMinutes(SliceMinutes))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(sliceStart, timeZone);
            var (rate, applied) = HourlyRate(baseRule, rules, local);
            var amount = rate * SliceMinutes / 60m;
            subtotal += amount;
            AddToLines(lines, sliceStart, rate, amount, applied);
        }

        var (discount, discountRule) = BestDiscount(rules, startUtc, minutes, subtotal, timeZone);
        var roundedSubtotal = Round(subtotal);
        var roundedDiscount = Math.Min(Round(discount), roundedSubtotal);
        var total = roundedSubtotal - roundedDiscount;
        var depositRule = deposit ?? DepositRule.Create(DepositType.Full, 0);

        return Result.Success(new PriceQuote(
            pitchId, startUtc, endUtc, minutes, currency, roundedSubtotal, roundedDiscount, discountRule?.Name, total,
            depositRule.CalculateDeposit(total), depositRule.Type,
            lines.Select(l => l with { Amount = Round(l.Amount) }).ToList()));
    }

    private static (decimal Rate, List<string> Applied) HourlyRate(PriceRule baseRule, List<PriceRule> rules, DateTime local)
    {
        var rate = baseRule.Price!.Amount;
        var applied = new List<string> { baseRule.Name };
        var date = DateOnly.FromDateTime(local);

        foreach (var kind in _modifierOrder)
        {
            if (kind == PriceRuleKind.Holiday
                && (!EgyptHolidayCalendar.IsHoliday(date) || HasMatching(rules, PriceRuleKind.SpecialDate, local)))
            {
                continue; // holiday rules apply only on official holidays, and a special-date rule on that day wins
            }

            var rule = MostSpecific(rules.Where(r => r.Kind == kind && r.MatchesMoment(local)));
            if (rule is null)
            {
                continue;
            }

            rate = rule.Adjustment == PriceAdjustment.FixedHourlyRate
                ? rule.Price!.Amount
                : Math.Max(0, rate * (1 + rule.Percent!.Value / 100m));
            applied.Add(rule.Name);
        }

        return (rate, applied);
    }

    private static (decimal Amount, PriceRule? Rule) BestDiscount(List<PriceRule> rules, DateTime startUtc, int minutes, decimal subtotal, TimeZoneInfo timeZone)
    {
        // A discount qualifies when the booking starts inside its conditions and is long enough; only the best one applies.
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone);
        return rules
            .Where(r => r.Kind == PriceRuleKind.Discount && r.MatchesMoment(startLocal) && (r.MinDurationMinutes ?? 0) <= minutes)
            .Select(r => (Amount: r.Adjustment == PriceAdjustment.Percentage ? subtotal * r.Percent!.Value / 100m : Math.Min(r.Price!.Amount, subtotal), Rule: (PriceRule?)r))
            .OrderByDescending(d => d.Amount)
            .FirstOrDefault();
    }

    private static bool HasMatching(List<PriceRule> rules, PriceRuleKind kind, DateTime local) =>
        rules.Exists(r => r.Kind == kind && r.MatchesMoment(local));

    /// <summary>Pitch-specific beats venue-wide; ties go to the newest rule (overlap validation keeps ties rare).</summary>
    private static PriceRule? MostSpecific(IEnumerable<PriceRule> candidates) =>
        candidates.OrderByDescending(r => r.PitchId.HasValue).ThenByDescending(r => r.Id).FirstOrDefault();

    private static void AddToLines(List<PriceLine> lines, DateTime sliceStart, decimal rate, decimal amount, List<string> applied)
    {
        var sliceEnd = sliceStart.AddMinutes(SliceMinutes);
        if (lines.Count > 0 && lines[^1].HourlyRate == rate && lines[^1].EndUtc == sliceStart && lines[^1].AppliedRules.SequenceEqual(applied))
        {
            lines[^1] = lines[^1] with { EndUtc = sliceEnd, Amount = lines[^1].Amount + amount };
            return;
        }

        lines.Add(new PriceLine(sliceStart, sliceEnd, rate, amount, applied));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
