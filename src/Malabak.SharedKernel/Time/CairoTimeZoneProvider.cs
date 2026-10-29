namespace Malabak.SharedKernel.Time;

public sealed class CairoTimeZoneProvider : ITimeZoneProvider
{
    private static readonly TimeZoneInfo _cairoTimeZone = ResolveCairoTimeZone();
    private readonly IClock _clock;

    public CairoTimeZoneProvider(IClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
    }

    public TimeZoneInfo TimeZone => _cairoTimeZone;

    public DateTime ConvertToCairo(DateTime utcDateTime)
    {
        var normalizedUtc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, _cairoTimeZone);
    }

    public DateTime ConvertToUtc(DateTime cairoDateTime)
    {
        if (cairoDateTime.Kind == DateTimeKind.Utc)
        {
            return cairoDateTime;
        }

        var unspecified = DateTime.SpecifyKind(cairoDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, _cairoTimeZone);
    }

    public DateOnly GetCairoToday()
    {
        return DateOnly.FromDateTime(_clock.CairoNow);
    }

    public TimeOnly GetCairoTime()
    {
        return TimeOnly.FromDateTime(_clock.CairoNow);
    }

    public bool IsDaylightSavingTime(DateTime dateTime)
    {
        return _cairoTimeZone.IsDaylightSavingTime(dateTime);
    }

    public TimeSpan GetUtcOffset(DateTime dateTime)
    {
        return _cairoTimeZone.GetUtcOffset(dateTime);
    }

    private static TimeZoneInfo ResolveCairoTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
    }
}
