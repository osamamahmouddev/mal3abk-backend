using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

/// <summary>
/// The offer a player sees on a venue: the best discount rule running today, how much it takes off, until when, and the
/// starting hourly price after it. <see cref="FromHourlyPriceAfter"/> is only given when the discount applies to every
/// one-hour booking on every pitch; a discount limited to some days, hours, pitches or longer bookings is still shown,
/// flagged with <see cref="HasConditions"/>, and the exact price comes from the quote.
/// </summary>
/// <param name="Type"><c>percentage</c> (Value is the percent off) or <c>amount</c> (Value is taken off a booking).</param>
public sealed record VenueOffer(
    string Name,
    string Type,
    decimal Value,
    DateOnly? EndsOn,
    bool HasConditions,
    decimal? FromHourlyPriceAfter);

/// <summary>The fields of a discount rule needed to describe it as an offer.</summary>
public sealed record OfferRule(
    Guid VenueId,
    Guid? PitchId,
    string Name,
    PriceAdjustment Adjustment,
    decimal? Percent,
    decimal? Amount,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DaysOfWeekMask? Days,
    TimeOnly? StartTime,
    int? MinDurationMinutes);

public static class VenueOffers
{
    public const string PercentageType = "percentage";
    public const string AmountType = "amount";

    /// <summary>Active, undeleted discount rules of <paramref name="venueIds"/> that have not ended before <paramref name="today"/>.</summary>
    public static IQueryable<OfferRule> CurrentDiscounts(this IQueryable<PriceRule> rules, IReadOnlyCollection<Guid> venueIds, DateOnly today) =>
        rules
            .Where(r => venueIds.Contains(r.VenueId) && r.Kind == PriceRuleKind.Discount && r.IsActive && !r.IsDeleted
                && (r.StartDate == null || r.StartDate <= today) && (r.EndDate == null || r.EndDate >= today))
            .Select(r => new OfferRule(
                r.VenueId, r.PitchId, r.Name, r.Adjustment, r.Percent, r.Amount, r.StartDate, r.EndDate, r.Days, r.StartTime,
                r.MinDurationMinutes));

    /// <summary>
    /// The biggest discount running on <paramref name="today"/> (Cairo), judged on a one-hour booking at
    /// <paramref name="fromHourlyPrice"/>; null when none runs.
    /// </summary>
    public static VenueOffer? Pick(IEnumerable<OfferRule> rules, DateOnly today, decimal? fromHourlyPrice)
    {
        var best = rules
            .Where(r => (r.StartDate is null || r.StartDate <= today) && (r.EndDate is null || r.EndDate >= today))
            .Where(r => r.Adjustment == PriceAdjustment.Percentage ? r.Percent is > 0 : r.Amount is > 0)
            .Select(r => (Rule: r, Off: AmountOff(r, fromHourlyPrice ?? 100m)))
            .OrderByDescending(x => x.Off)
            .ThenBy(x => x.Rule.EndDate ?? DateOnly.MaxValue)
            .Select(x => x.Rule)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var percentage = best.Adjustment == PriceAdjustment.Percentage;
        var conditional = best.PitchId is not null
            || (best.Days is { } days && days != DaysOfWeekMask.All)
            || best.StartTime is not null
            || (best.MinDurationMinutes ?? 0) > 60;
        decimal? after = conditional || fromHourlyPrice is not { } from
            ? null
            : Math.Max(0m, Math.Round(from - AmountOff(best, from), 2, MidpointRounding.AwayFromZero));

        return new VenueOffer(
            best.Name,
            percentage ? PercentageType : AmountType,
            percentage ? best.Percent!.Value : best.Amount!.Value,
            best.EndDate,
            conditional,
            after);
    }

    private static decimal AmountOff(OfferRule rule, decimal price) =>
        rule.Adjustment == PriceAdjustment.Percentage
            ? price * (rule.Percent ?? 0m) / 100m
            : Math.Min(rule.Amount ?? 0m, price);
}
