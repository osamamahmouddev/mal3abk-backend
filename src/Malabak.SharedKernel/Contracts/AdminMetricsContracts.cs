namespace Malabak.SharedKernel.Contracts;

/// <summary>Reporting period for the admin dashboard: [FromUtc, ToUtc), with daily buckets on Cairo calendar days.</summary>
public sealed record MetricsPeriod(DateTime FromUtc, DateTime ToUtc, TimeZoneInfo TimeZone)
{
    /// <summary>The Cairo calendar day a UTC instant falls on (used to bucket daily series).</summary>
    public DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone));

    /// <summary>Counts per day for every day of the period (days without events are zero), oldest first.</summary>
    public IReadOnlyList<SeriesPoint> Daily(IEnumerable<DateTime> instantsUtc, Func<DateTime, decimal>? weight = null)
    {
        var first = DayOf(FromUtc);
        var last = DayOf(ToUtc.AddTicks(-1));
        var buckets = new SortedDictionary<DateOnly, decimal>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            buckets[day] = 0;
        }

        foreach (var instant in instantsUtc)
        {
            var day = DayOf(instant);
            if (buckets.ContainsKey(day))
            {
                buckets[day] += weight?.Invoke(instant) ?? 1;
            }
        }

        return buckets.Select(b => new SeriesPoint(b.Key, b.Value)).ToList();
    }
}

public sealed record SeriesPoint(DateOnly Day, decimal Value);

/// <summary>
/// One module's share of the admin dashboard (TASK-710): headline figures, daily series and distributions, each under a
/// stable key (e.g. <c>bookings.created</c>). The meaning of each key is documented where it is computed.
/// </summary>
public sealed record AdminMetricsPart(
    IReadOnlyDictionary<string, decimal> Figures,
    IReadOnlyDictionary<string, IReadOnlyList<SeriesPoint>> Series,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Distributions);

/// <summary>Implemented by each module that contributes to the admin dashboard; the host merges all parts.</summary>
public interface IAdminMetricsProvider
{
    Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default);
}
