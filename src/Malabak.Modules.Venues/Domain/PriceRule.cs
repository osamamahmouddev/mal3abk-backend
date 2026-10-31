using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

/// <summary>What a rule contributes, applied in this order (later kinds act on the result of earlier ones).</summary>
public enum PriceRuleKind
{
    /// <summary>The default hourly rate: venue-wide, or a pitch override when <see cref="PriceRule.PitchId"/> is set.</summary>
    Base = 0,

    /// <summary>Peak hours, e.g. 18:00-23:00.</summary>
    Peak = 1,

    /// <summary>Weekend days (in Egypt usually Friday and Saturday).</summary>
    Weekend = 2,

    /// <summary>Official public holidays from the reference calendar.</summary>
    Holiday = 3,

    /// <summary>A specific date or date range (Ramadan nights, tournaments). Wins over Holiday on the same day.</summary>
    SpecialDate = 4,

    /// <summary>A reduction of the total; the single best matching discount is applied.</summary>
    Discount = 5
}

public enum PriceAdjustment
{
    /// <summary>Sets the hourly rate to <see cref="PriceRule.Price"/>. The only option for Base rules.</summary>
    FixedHourlyRate = 0,

    /// <summary>Changes the current rate by <see cref="PriceRule.Percent"/> (e.g. +20, -15); for discounts, the percent taken off.</summary>
    Percentage = 1,

    /// <summary>Discounts only: a fixed amount taken off the total, never below zero.</summary>
    FixedAmountOff = 2
}

/// <summary>Validated values of a price rule, shared by create and update.</summary>
public sealed record PriceRuleTerms(
    PriceRuleKind Kind,
    PriceAdjustment Adjustment,
    Guid? PitchId,
    string Name,
    decimal? Amount,
    decimal? Percent,
    DaysOfWeekMask? Days,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? MinDurationMinutes,
    string Currency = Money.DefaultCurrency);

[Flags]
public enum DaysOfWeekMask
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    All = 127
}

public static class DaysOfWeekMaskExtensions
{
    public static bool Includes(this DaysOfWeekMask mask, DayOfWeek day) => (mask & (DaysOfWeekMask)(1 << (int)day)) != 0;
}

/// <summary>
/// One pricing rule of a venue (optionally limited to one pitch). Rules only ever affect new quotes: bookings keep the
/// price agreed when they were created.
/// </summary>
public sealed class PriceRule : Entity<Guid>
{
    public const int MaxNameLength = 100;
    public const int MaxRulesPerVenue = 200;
    public const decimal MaxHourlyRate = 100_000m;
    public const int MaxRangeDays = 366;

    private PriceRule()
    {
    }

    public Guid VenueId { get; private set; }

    /// <summary>Null = applies to every pitch of the venue; a pitch-specific rule wins over a venue-wide one of the same kind.</summary>
    public Guid? PitchId { get; private set; }

    public PriceRuleKind Kind { get; private set; }
    public PriceAdjustment Adjustment { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Hourly rate (FixedHourlyRate) or amount off (FixedAmountOff); null for percentage rules.</summary>
    public decimal? Amount { get; private set; }

    public string Currency { get; private set; } = Money.DefaultCurrency;

    /// <summary><see cref="Amount"/> as money, or null for percentage rules.</summary>
    public Money? Price => Amount is { } amount ? new Money(amount, Currency) : null;

    public decimal? Percent { get; private set; }

    /// <summary>Null = every day.</summary>
    public DaysOfWeekMask? Days { get; private set; }

    /// <summary>Local (Cairo) time window; end at or before start means it runs past midnight. Null = all day.</summary>
    public TimeOnly? StartTime { get; private set; }

    public TimeOnly? EndTime { get; private set; }

    /// <summary>Validity in local dates, inclusive. Required for SpecialDate; optional for others (seasonal rules).</summary>
    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    /// <summary>Discounts only: the booking must last at least this long.</summary>
    public int? MinDurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<PriceRule> Create(Guid venueId, PriceRuleTerms terms, DateTime nowUtc)
    {
        var rule = new PriceRule { Id = Guid.CreateVersion7(), VenueId = venueId, IsActive = true, CreatedAtUtc = nowUtc };
        var applied = rule.Apply(terms, nowUtc);
        return applied.IsSuccess ? Result.Success(rule) : Result.Failure<PriceRule>(applied.Error);
    }

    public Result Update(PriceRuleTerms terms, bool isActive, DateTime nowUtc)
    {
        var applied = Apply(terms, nowUtc);
        if (applied.IsSuccess)
        {
            IsActive = isActive;
        }

        return applied;
    }

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Whether the rule's day/time/date conditions match a local moment (duration conditions are checked by the calculator).</summary>
    public bool MatchesMoment(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        if (!IsActive || IsDeleted)
        {
            return false;
        }

        if ((StartDate is { } from && date < from) || (EndDate is { } to && date > to))
        {
            return false;
        }

        if (Days is { } days && !days.Includes(local.DayOfWeek))
        {
            return false;
        }

        return InTimeWindow(TimeOnly.FromDateTime(local));
    }

    public bool AppliesToPitch(Guid pitchId) => PitchId is null || PitchId == pitchId;

    /// <summary>Whether two rules of the same kind and scope could apply to the same moment (used to reject ambiguous setups).</summary>
    public bool Overlaps(PriceRule other)
    {
        if (other.Kind != Kind || other.PitchId != PitchId || !other.IsActive || other.IsDeleted || !IsActive || other.Id == Id)
        {
            return false;
        }

        var datesOverlap = (StartDate ?? DateOnly.MinValue) <= (other.EndDate ?? DateOnly.MaxValue)
            && (other.StartDate ?? DateOnly.MinValue) <= (EndDate ?? DateOnly.MaxValue);
        var daysOverlap = ((Days ?? DaysOfWeekMask.All) & (other.Days ?? DaysOfWeekMask.All)) != 0;
        return datesOverlap && daysOverlap && TimeWindowsOverlap(other);
    }

    private bool InTimeWindow(TimeOnly time)
    {
        if (StartTime is not { } start || EndTime is not { } end)
        {
            return true;
        }

        return end > start ? time >= start && time < end : time >= start || time < end;
    }

    private bool TimeWindowsOverlap(PriceRule other)
    {
        if (StartTime is null || other.StartTime is null)
        {
            return true;
        }

        // Compare minute sets over a day; windows are small enough that a 15-minute scan is exact for our increments.
        for (var minute = 0; minute < 24 * 60; minute += 15)
        {
            var time = new TimeOnly(minute / 60, minute % 60);
            if (InTimeWindow(time) && other.InTimeWindow(time))
            {
                return true;
            }
        }

        return false;
    }

    private Result Apply(PriceRuleTerms terms, DateTime nowUtc)
    {
        var error = Validate(terms);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        PitchId = terms.PitchId;
        Kind = terms.Kind;
        Adjustment = terms.Adjustment;
        Name = terms.Name.Trim();
        Amount = terms.Adjustment == PriceAdjustment.Percentage ? null : terms.Amount;
        Currency = new Money(0, terms.Currency).Currency;
        Percent = terms.Adjustment == PriceAdjustment.Percentage ? terms.Percent : null;
        Days = terms.Days is DaysOfWeekMask.All ? null : terms.Days;
        StartTime = terms.StartTime;
        EndTime = terms.EndTime;
        StartDate = terms.StartDate;
        EndDate = terms.EndDate;
        MinDurationMinutes = terms.Kind == PriceRuleKind.Discount ? terms.MinDurationMinutes : null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Error? Validate(PriceRuleTerms t)
    {
        if (!Enum.IsDefined(t.Kind) || !Enum.IsDefined(t.Adjustment) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Trim().Length > MaxNameLength)
        {
            return PriceRuleErrors.Invalid;
        }

        var adjustmentAllowed = t.Kind switch
        {
            PriceRuleKind.Base => t.Adjustment == PriceAdjustment.FixedHourlyRate,
            PriceRuleKind.Discount => t.Adjustment is PriceAdjustment.Percentage or PriceAdjustment.FixedAmountOff,
            _ => t.Adjustment is PriceAdjustment.FixedHourlyRate or PriceAdjustment.Percentage
        };
        if (!adjustmentAllowed)
        {
            return PriceRuleErrors.AdjustmentNotAllowed;
        }

        var valueOk = t.Adjustment switch
        {
            PriceAdjustment.FixedHourlyRate or PriceAdjustment.FixedAmountOff => t.Amount is > 0 and <= MaxHourlyRate && decimal.Round(t.Amount.Value, 2) == t.Amount,
            _ => t.Kind == PriceRuleKind.Discount ? t.Percent is > 0 and <= 100 : t.Percent is >= -90 and <= 300 and not 0
        };
        if (!valueOk || (t.Adjustment == PriceAdjustment.Percentage && t.Amount is not null))
        {
            return PriceRuleErrors.InvalidValue;
        }

        if ((t.StartTime is null) != (t.EndTime is null) || (t.StartTime is { } s && s == t.EndTime))
        {
            return PriceRuleErrors.InvalidTimeWindow;
        }

        if (t.Days is DaysOfWeekMask.None || (t.Days is { } d && (d & ~DaysOfWeekMask.All) != 0))
        {
            return PriceRuleErrors.InvalidDays;
        }

        var needsDates = t.Kind == PriceRuleKind.SpecialDate;
        if ((needsDates && (t.StartDate is null || t.EndDate is null))
            || (t.StartDate is { } from && t.EndDate is { } to && (to < from || to.DayNumber - from.DayNumber >= MaxRangeDays)))
        {
            return PriceRuleErrors.InvalidDates;
        }

        if (t.Kind == PriceRuleKind.Base && (t.Days is not null || t.StartTime is not null || t.StartDate is not null))
        {
            return PriceRuleErrors.BaseMustBeUnconditional;
        }

        if (t.MinDurationMinutes is { } min && (t.Kind != PriceRuleKind.Discount || min is < 15 or > 24 * 60))
        {
            return PriceRuleErrors.InvalidValue;
        }

        return null;
    }
}

public static class PriceRuleErrors
{
    public static readonly Error Invalid = new("PriceRule.Invalid", "Choose a valid rule type and give it a name (1-100 characters).");
    public static readonly Error AdjustmentNotAllowed = new("PriceRule.AdjustmentNotAllowed",
        "Base prices are fixed hourly rates; discounts are a percentage or a fixed amount off; other rules set a rate or change it by a percentage.");
    public static readonly Error InvalidValue = new("PriceRule.InvalidValue",
        "Amounts must be positive with at most 2 decimals; percentages: discounts 1 to 100, modifiers -90 to +300 (not 0); minimum duration 15 minutes to 24 hours, discounts only.");
    public static readonly Error InvalidTimeWindow = new("PriceRule.InvalidTimeWindow", "Give both start and end time (different), or neither.");
    public static readonly Error InvalidDays = new("PriceRule.InvalidDays", "Choose at least one valid day.");
    public static readonly Error InvalidDates = new("PriceRule.InvalidDates", "Special dates need a start and end date; end must be on or after start, within one year.");
    public static readonly Error BaseMustBeUnconditional = new("PriceRule.BaseMustBeUnconditional",
        "A base price applies at all times; use peak, weekend or special-date rules for conditions.");
    public static readonly Error Overlap = new("PriceRule.Overlap", "Another active rule of the same type and pitch already covers some of these days and times.");
    public static readonly Error NotFound = new("PriceRule.NotFound", "Price rule not found.");
    public static readonly Error NoBasePrice = new("Pricing.NoBasePrice", "This pitch has no base price yet.");
    public static readonly Error TooMany = new("PriceRule.TooMany", "A venue can have at most 200 price rules. Delete unused rules first.");
    public static readonly Error CurrencyMismatch = new("Pricing.CurrencyMismatch", "All price rules of a venue must use the same currency.");
}
