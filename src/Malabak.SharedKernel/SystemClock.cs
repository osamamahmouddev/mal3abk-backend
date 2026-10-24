namespace Malabak.SharedKernel;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo CairoZone = ResolveCairoTimeZone();

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, CairoZone);

    public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);

    public TimeZoneInfo CairoTimeZone => CairoZone;

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
