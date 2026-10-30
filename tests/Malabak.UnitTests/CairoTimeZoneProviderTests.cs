using FluentAssertions;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Xunit;

namespace Malabak.UnitTests;

public sealed class CairoTimeZoneProviderTests
{
    private readonly CairoTimeZoneProvider _provider = new(new SystemClock());

    [Fact]
    public void TimeZone_ShouldBeCairoOrEgyptStandardTime()
    {
        // Act
        var zone = _provider.TimeZone;

        // Assert
        zone.Should().NotBeNull();
        zone.Id.Should().Match(id => id == "Africa/Cairo" || id == "Egypt Standard Time");
    }

    [Fact]
    public void ConvertToCairo_DuringWinter_HasTwoHoursOffset()
    {
        // Arrange (January 15th - Winter: UTC+2)
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var cairoTime = _provider.ConvertToCairo(utc);
        var offset = _provider.GetUtcOffset(cairoTime);

        // Assert
        cairoTime.Hour.Should().Be(14);
        cairoTime.Day.Should().Be(15);
        offset.Should().Be(TimeSpan.FromHours(2));
        _provider.IsDaylightSavingTime(cairoTime).Should().BeFalse();
    }

    [Fact]
    public void ConvertToCairo_DuringSummer_HasThreeHoursOffset()
    {
        // Arrange (July 15th - Summer: UTC+3)
        var utc = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var cairoTime = _provider.ConvertToCairo(utc);
        var offset = _provider.GetUtcOffset(cairoTime);

        // Assert
        cairoTime.Hour.Should().Be(15);
        cairoTime.Day.Should().Be(15);
        offset.Should().Be(TimeSpan.FromHours(3));
        _provider.IsDaylightSavingTime(cairoTime).Should().BeTrue();
    }

    [Fact]
    public void RoundTripConversion_UtcToCairoAndBack_PreservesOriginalUtc()
    {
        // Arrange
        var originalUtc = new DateTime(2026, 6, 20, 18, 30, 0, DateTimeKind.Utc);

        // Act
        var cairoTime = _provider.ConvertToCairo(originalUtc);
        var convertedBackUtc = _provider.ConvertToUtc(cairoTime);

        // Assert
        convertedBackUtc.Should().Be(originalUtc);
    }

    [Fact]
    public void GetCairoTodayAndNow_ShouldAlignWithClock()
    {
        // Arrange
        var testClock = new TestClock(new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc));
        var provider = new CairoTimeZoneProvider(testClock);

        // Act
        var today = provider.GetCairoToday();
        var time = provider.GetCairoTime();

        // Assert
        // In late October, Egypt is UTC+3 (or UTC+2 depending on exact Thursday transition)
        today.Year.Should().Be(2026);
        today.Month.Should().Be(10);
        today.Day.Should().Be(29);
        time.Minute.Should().Be(0);
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; }
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, CairoTimeZone);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone { get; }

        public TestClock(DateTime utcNow)
        {
            UtcNow = utcNow;
            try
            {
                CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
            }
            catch (TimeZoneNotFoundException)
            {
                CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
            }
        }
    }
}
