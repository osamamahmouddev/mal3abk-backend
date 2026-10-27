namespace Malabak.SharedKernel;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo _cairoZone = ResolveCairoTimeZone();
    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairoZone);

    public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);

    public TimeZoneInfo CairoTimeZone => _cairoZone;

    private static TimeZoneInfo ResolveCairoTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows fallback ID
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
    }
}
