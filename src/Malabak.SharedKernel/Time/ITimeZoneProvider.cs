namespace Malabak.SharedKernel.Time;

public interface ITimeZoneProvider
{
    TimeZoneInfo TimeZone { get; }
    DateTime ConvertToCairo(DateTime utcDateTime);
    DateTime ConvertToUtc(DateTime cairoDateTime);
    DateOnly GetCairoToday();
    TimeOnly GetCairoTime();
    bool IsDaylightSavingTime(DateTime dateTime);
    TimeSpan GetUtcOffset(DateTime dateTime);
}
