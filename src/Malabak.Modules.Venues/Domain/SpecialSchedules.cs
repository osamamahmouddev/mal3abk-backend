using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public enum ScheduleOverrideKind
{
    /// <summary>Closed for the whole business day(s): temporary closure or holiday.</summary>
    Closure = 0,

    /// <summary>Replaces the regular hours for the date(s); may run past midnight.</summary>
    SpecialHours = 1,

    /// <summary>A daily window carved out of the open hours: break, prayer, maintenance.</summary>
    Break = 2
}

/// <summary>
/// A one-off (single occurrence or date range) change to the weekly schedule, venue-wide or for one pitch.
/// Dates are Cairo business dates: a closure on D also closes the after-midnight tail of D's overnight hours.
/// </summary>
public sealed class ScheduleOverride : Entity<Guid>
{
    public const int MaxReasonLength = 200;
    public const int MaxRangeDays = 366;

    private ScheduleOverride()
    {
    }

    public Guid VenueId { get; private set; }

    /// <summary>Null for the whole venue.</summary>
    public Guid? PitchId { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public ScheduleOverrideKind Kind { get; private set; }
    public TimeOnly? FromTime { get; private set; }
    public TimeOnly? ToTime { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }

    public static Result<ScheduleOverride> Create(
        Guid venueId, Guid? pitchId, DateOnly startDate, DateOnly endDate, ScheduleOverrideKind kind,
        TimeOnly? fromTime, TimeOnly? toTime, string reason, Guid userId, DateTime nowUtc)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (endDate < startDate || endDate.DayNumber - startDate.DayNumber >= MaxRangeDays)
        {
            return Result.Failure<ScheduleOverride>(SpecialScheduleErrors.InvalidDates);
        }

        if (!Enum.IsDefined(kind) || trimmed.Length is 0 or > MaxReasonLength)
        {
            return Result.Failure<ScheduleOverride>(SpecialScheduleErrors.InvalidException);
        }

        var needsTimes = kind != ScheduleOverrideKind.Closure;
        if (needsTimes != (fromTime is not null && toTime is not null) || (kind == ScheduleOverrideKind.Break && fromTime == toTime))
        {
            return Result.Failure<ScheduleOverride>(SpecialScheduleErrors.InvalidTimes);
        }

        return Result.Success(new ScheduleOverride
        {
            Id = Guid.CreateVersion7(),
            VenueId = venueId,
            PitchId = pitchId,
            StartDate = startDate,
            EndDate = endDate,
            Kind = kind,
            FromTime = needsTimes ? fromTime : null,
            ToTime = needsTimes ? toTime : null,
            Reason = trimmed,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc
        });
    }

    public bool Covers(DateOnly date) => !IsDeleted && date >= StartDate && date <= EndDate;

    public bool AppliesTo(Guid pitchId) => PitchId is null || PitchId == pitchId;

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>Recurring weekly hours for one pitch, overriding the venue's hours for that weekday.</summary>
public sealed class PitchWeeklyHours : Entity<Guid>
{
    private PitchWeeklyHours()
    {
    }

    public Guid VenueId { get; private set; }
    public Guid PitchId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    public bool IsClosed { get; private set; }

    public static PitchWeeklyHours Create(
        Guid venueId, Guid pitchId, DayOfWeek dayOfWeek, TimeOnly openTime, TimeOnly closeTime, bool isClosed, DateTime nowUtc) => new()
        {
            Id = Guid.CreateVersion7(),
            VenueId = venueId,
            PitchId = pitchId,
            DayOfWeek = dayOfWeek,
            OpenTime = openTime,
            CloseTime = closeTime,
            IsClosed = isClosed,
            CreatedAtUtc = nowUtc
        };
}

/// <summary>Duration, increment, notice and advance-window rules. Venue default (PitchId null) or a per-pitch override.</summary>
public sealed class BookingRules : Entity<Guid>
{
    public const int DefaultMinDurationMinutes = 60;
    public const int DefaultMaxDurationMinutes = 180;
    public const int DefaultIncrementMinutes = 30;
    public const int DefaultMinNoticeMinutes = 60;
    public const int DefaultMaxAdvanceDays = 30;

    private BookingRules()
    {
    }

    public Guid VenueId { get; private set; }
    public Guid? PitchId { get; private set; }
    public int MinDurationMinutes { get; private set; } = DefaultMinDurationMinutes;
    public int MaxDurationMinutes { get; private set; } = DefaultMaxDurationMinutes;
    public int IncrementMinutes { get; private set; } = DefaultIncrementMinutes;
    public int MinNoticeMinutes { get; private set; } = DefaultMinNoticeMinutes;
    public int MaxAdvanceDays { get; private set; } = DefaultMaxAdvanceDays;

    public static BookingRules Defaults(Guid venueId) => new() { VenueId = venueId };

    public static Result<BookingRules> Create(
        Guid venueId, Guid? pitchId, int minDuration, int maxDuration, int increment, int minNotice, int maxAdvanceDays, DateTime nowUtc)
    {
        var rules = new BookingRules { Id = Guid.CreateVersion7(), VenueId = venueId, PitchId = pitchId, CreatedAtUtc = nowUtc };
        var update = rules.Update(minDuration, maxDuration, increment, minNotice, maxAdvanceDays, nowUtc);
        return update.IsSuccess ? Result.Success(rules) : Result.Failure<BookingRules>(update.Error);
    }

    public Result Update(int minDuration, int maxDuration, int increment, int minNotice, int maxAdvanceDays, DateTime nowUtc)
    {
        var valid = increment is 15 or 30 or 60
            && minDuration is >= 30 and <= 480 && minDuration % increment == 0
            && maxDuration >= minDuration && maxDuration <= 720 && maxDuration % increment == 0
            && minNotice is >= 0 and <= 10080
            && maxAdvanceDays is >= 1 and <= 365;
        if (!valid)
        {
            return Result.Failure(SpecialScheduleErrors.InvalidRules);
        }

        MinDurationMinutes = minDuration;
        MaxDurationMinutes = maxDuration;
        IncrementMinutes = increment;
        MinNoticeMinutes = minNotice;
        MaxAdvanceDays = maxAdvanceDays;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

public static class SpecialScheduleErrors
{
    public static readonly Error InvalidDates = new("Schedule.InvalidDates", "End date must be on or after the start date, within one year.");
    public static readonly Error InvalidException = new("Schedule.InvalidOverride", "Choose a valid type and give a reason (1–200 characters).");
    public static readonly Error InvalidTimes = new("Schedule.InvalidTimes", "Special hours and breaks need from/to times; closures must not have them.");
    public static readonly Error InvalidRules = new("Schedule.InvalidRules",
        "Increment must be 15/30/60; durations 30–720 minutes and multiples of the increment; notice 0–10080 minutes; advance window 1–365 days.");
    public static readonly Error InvalidWeeklyHours = new("Schedule.InvalidWeeklyHours", "Give each weekday at most once.");
    public static readonly Error OverrideNotFound = new("Schedule.OverrideNotFound", "Schedule override not found.");
    public static readonly Error Closed = new("Schedule.Closed", "The pitch is not open for the whole requested time.");
    public static readonly Error InvalidDuration = new("Schedule.InvalidDuration", "Duration or start time does not match the pitch's booking increments.");
    public static readonly Error TooSoon = new("Schedule.TooSoon", "This slot starts sooner than the venue's minimum notice.");
    public static readonly Error TooFarAhead = new("Schedule.TooFarAhead", "This slot is beyond the venue's advance booking window.");
}
