namespace Malabak.SharedKernel.Time;

public sealed class FakeClock : IClock
{
    private static readonly TimeZoneInfo _cairoZone = ResolveCairoTimeZone();
    private DateTime _utcNow;

    public FakeClock(DateTime? initialUtcNow = null)
    {
        _utcNow = initialUtcNow ?? DateTime.UtcNow;
    }

    public DateTime UtcNow => _utcNow;

    public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_utcNow, _cairoZone);

    public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);

    public TimeZoneInfo CairoTimeZone => _cairoZone;

    public void SetUtcNow(DateTime utcNow)
    {
        _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public void Advance(TimeSpan timeSpan)
    {
        _utcNow = _utcNow.Add(timeSpan);
    }

    public void AdvanceDays(int days) => Advance(TimeSpan.FromDays(days));
    public void AdvanceHours(int hours) => Advance(TimeSpan.FromHours(hours));
    public void AdvanceMinutes(int minutes) => Advance(TimeSpan.FromMinutes(minutes));
    public void AdvanceSeconds(int seconds) => Advance(TimeSpan.FromSeconds(seconds));

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
