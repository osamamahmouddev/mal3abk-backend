namespace Malabak.SharedKernel;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime CairoNow { get; }
    DateOnly CairoToday { get; }
    TimeZoneInfo CairoTimeZone { get; }
}
