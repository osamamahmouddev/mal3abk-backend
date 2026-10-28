using FluentAssertions;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class ClockTests
{
    [Fact]
    public void SystemClock_WithDefaultProvider_ReturnsValidUtcNowAndCairoNow()
    {
        // Arrange
        IClock clock = new SystemClock();

        // Act
        var utcNow = clock.UtcNow;
        var cairoNow = clock.CairoNow;
        var cairoToday = clock.CairoToday;

        // Assert
        utcNow.Kind.Should().Be(DateTimeKind.Utc);
        cairoNow.Should().BeAfter(utcNow.AddHours(1)); // Cairo is at least UTC+2 or UTC+3
        cairoToday.Year.Should().Be(cairoNow.Year);
        cairoToday.Month.Should().Be(cairoNow.Month);
        cairoToday.Day.Should().Be(cairoNow.Day);
    }

    [Fact]
    public void SystemClock_WithCustomTimeProvider_ReturnsConfiguredTime()
    {
        // Arrange
        var fixedUtc = new DateTimeOffset(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);
        var fakeProvider = new FixedTimeProvider(fixedUtc);
        IClock clock = new SystemClock(fakeProvider);

        // Act
        var utcNow = clock.UtcNow;
        var cairoNow = clock.CairoNow;

        // Assert
        utcNow.Should().Be(fixedUtc.UtcDateTime);
        cairoNow.Hour.Should().BeOneOf(20, 21); // UTC+2 or UTC+3 depending on Egyptian DST
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
