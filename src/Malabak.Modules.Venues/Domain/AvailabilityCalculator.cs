using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed record UtcWindow(DateTime StartUtc, DateTime EndUtc);

/// <summary>Everything that decides when one pitch is open: venue week, pitch overrides, exceptions and rules.</summary>
public sealed record PitchScheduleState(
    Guid PitchId,
    IReadOnlyList<OperatingSchedule> VenueWeek,
    IReadOnlyList<PitchWeeklyHours> PitchWeek,
    IReadOnlyList<ScheduleOverride> Exceptions,
    BookingRules Rules);

/// <summary>
/// Pure schedule maths. Hours are resolved per Cairo business date (special hours &gt; pitch week &gt; venue week,
/// closures win, breaks are subtracted), built in local time and only then converted to UTC, so overnight hours and
/// DST transitions produce the real elapsed time. Bookable start times step through the UTC windows.
/// </summary>
public static class AvailabilityCalculator
{
    public static IReadOnlyList<UtcWindow> OpenWindows(PitchScheduleState state, DateOnly businessDate, TimeZoneInfo timeZone)
    {
        var exceptions = state.Exceptions.Where(e => e.Covers(businessDate) && e.AppliesTo(state.PitchId)).ToList();
        if (exceptions.Exists(e => e.Kind == ScheduleOverrideKind.Closure))
        {
            return [];
        }

        var hours = ResolveHours(state, businessDate, exceptions);
        if (hours is null)
        {
            return [];
        }

        var windows = new List<(DateTime Start, DateTime End)> { Local(businessDate, hours.Value.Open, hours.Value.Close) };
        foreach (var pause in exceptions.Where(e => e.Kind == ScheduleOverrideKind.Break))
        {
            // A break can fall in the after-midnight tail of overnight hours, so try it on the next calendar day too.
            windows = Subtract(windows, Local(businessDate, pause.FromTime!.Value, pause.ToTime!.Value));
            windows = Subtract(windows, Local(businessDate.AddDays(1), pause.FromTime!.Value, pause.ToTime!.Value));
        }

        return windows
            .Select(w => new UtcWindow(OperatingSchedule.ConvertToUtc(w.Start, timeZone), OperatingSchedule.ConvertToUtc(w.End, timeZone)))
            .Where(w => w.EndUtc > w.StartUtc)
            .ToList();
    }

    /// <summary>Start times a player may book (for the minimum duration), honouring notice and advance window.</summary>
    public static IReadOnlyList<DateTime> StartTimes(IReadOnlyList<UtcWindow> windows, BookingRules rules, DateTime nowUtc)
    {
        var earliest = nowUtc.AddMinutes(rules.MinNoticeMinutes);
        var latest = nowUtc.AddDays(rules.MaxAdvanceDays);
        var result = new List<DateTime>();
        foreach (var window in windows)
        {
            for (var start = window.StartUtc; start.AddMinutes(rules.MinDurationMinutes) <= window.EndUtc; start = start.AddMinutes(rules.IncrementMinutes))
            {
                if (start >= earliest && start <= latest)
                {
                    result.Add(start);
                }
            }
        }

        return result;
    }

    /// <summary>Server-side check of a requested slot against hours, increments, duration, notice and advance window.</summary>
    public static Error? CheckSlot(PitchScheduleState state, DateTime startUtc, DateTime endUtc, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        var rules = state.Rules;
        var duration = endUtc - startUtc;
        var increment = TimeSpan.FromMinutes(rules.IncrementMinutes);
        if (duration < TimeSpan.FromMinutes(rules.MinDurationMinutes) || duration > TimeSpan.FromMinutes(rules.MaxDurationMinutes)
            || duration.Ticks % increment.Ticks != 0)
        {
            return SpecialScheduleErrors.InvalidDuration;
        }

        if (startUtc < nowUtc.AddMinutes(rules.MinNoticeMinutes))
        {
            return SpecialScheduleErrors.TooSoon;
        }

        if (startUtc > nowUtc.AddDays(rules.MaxAdvanceDays))
        {
            return SpecialScheduleErrors.TooFarAhead;
        }

        var window = ContainingWindow(state, startUtc, endUtc, timeZone);
        if (window is null)
        {
            return SpecialScheduleErrors.Closed;
        }

        return (startUtc - window.StartUtc).Ticks % increment.Ticks == 0 ? null : SpecialScheduleErrors.InvalidDuration;
    }

    /// <summary>Whether an existing booking still fits the open hours (used for change-impact warnings).</summary>
    public static bool Fits(PitchScheduleState state, DateTime startUtc, DateTime endUtc, TimeZoneInfo timeZone) =>
        ContainingWindow(state, startUtc, endUtc, timeZone) is not null;

    private static UtcWindow? ContainingWindow(PitchScheduleState state, DateTime startUtc, DateTime endUtc, TimeZoneInfo timeZone)
    {
        // The slot belongs either to its own local date or to the previous date's overnight hours.
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone));
        return new[] { localDate, localDate.AddDays(-1) }
            .SelectMany(d => OpenWindows(state, d, timeZone))
            .FirstOrDefault(w => w.StartUtc <= startUtc && endUtc <= w.EndUtc);
    }

    private static (TimeOnly Open, TimeOnly Close)? ResolveHours(PitchScheduleState state, DateOnly date, List<ScheduleOverride> exceptions)
    {
        var special = exceptions
            .Where(e => e.Kind == ScheduleOverrideKind.SpecialHours)
            .OrderByDescending(e => e.PitchId.HasValue)
            .FirstOrDefault();
        if (special is not null)
        {
            return (special.FromTime!.Value, special.ToTime!.Value);
        }

        var pitchDay = state.PitchWeek.FirstOrDefault(d => !d.IsDeleted && d.DayOfWeek == date.DayOfWeek);
        if (pitchDay is not null)
        {
            return pitchDay.IsClosed ? null : (pitchDay.OpenTime, pitchDay.CloseTime);
        }

        var venueDay = state.VenueWeek.FirstOrDefault(d => d.IsActive && !d.IsDeleted && d.DayOfWeek == date.DayOfWeek);
        return venueDay is null ? null : (venueDay.OpenTime, venueDay.CloseTime);
    }

    /// <summary>Close at or before open means the hours run past midnight (equal times = 24 hours).</summary>
    private static (DateTime Start, DateTime End) Local(DateOnly date, TimeOnly from, TimeOnly to) =>
        (date.ToDateTime(from), to <= from ? date.AddDays(1).ToDateTime(to) : date.ToDateTime(to));

    private static List<(DateTime Start, DateTime End)> Subtract(List<(DateTime Start, DateTime End)> windows, (DateTime Start, DateTime End) cut)
    {
        var result = new List<(DateTime Start, DateTime End)>();
        foreach (var (start, end) in windows)
        {
            if (cut.End <= start || cut.Start >= end)
            {
                result.Add((start, end));
                continue;
            }

            if (cut.Start > start)
            {
                result.Add((start, cut.Start));
            }

            if (cut.End < end)
            {
                result.Add((cut.End, end));
            }
        }

        return result;
    }
}
