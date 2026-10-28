using FluentAssertions;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TimeSlotRangeTests
{
    private readonly DateTime _baseTimeUtc = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidUtcRange_ReturnsSuccess()
    {
        // Arrange
        var start = _baseTimeUtc;
        var end = _baseTimeUtc.AddHours(2);

        // Act
        var result = TimeSlotRange.Create(start, end);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.StartUtc.Should().Be(start);
        result.Value.EndUtc.Should().Be(end);
        result.Value.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Create_WithStartEqualOrAfterEnd_ReturnsInvalidRangeError()
    {
        // Act
        var equalResult = TimeSlotRange.Create(_baseTimeUtc, _baseTimeUtc);
        var reversedResult = TimeSlotRange.Create(_baseTimeUtc.AddHours(1), _baseTimeUtc);

        // Assert
        equalResult.IsFailure.Should().BeTrue();
        equalResult.Error.Should().Be(TimeSlotRangeErrors.InvalidRange);

        reversedResult.IsFailure.Should().BeTrue();
        reversedResult.Error.Should().Be(TimeSlotRangeErrors.InvalidRange);
    }

    [Fact]
    public void Create_WithDurationGreaterThan24Hours_ReturnsExceedsMaxDurationError()
    {
        // Act
        var result = TimeSlotRange.Create(_baseTimeUtc, _baseTimeUtc.AddHours(25));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TimeSlotRangeErrors.ExceedsMaxDuration);
    }

    [Fact]
    public void Create_WithStartAndDuration_ReturnsSuccess()
    {
        // Act
        var result = TimeSlotRange.Create(_baseTimeUtc, TimeSpan.FromHours(1.5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Duration.Should().Be(TimeSpan.FromHours(1.5));
        result.Value.EndUtc.Should().Be(_baseTimeUtc.AddHours(1.5));
    }

    [Fact]
    public void Overlaps_WhenRangesOverlap_ReturnsTrue()
    {
        // Arrange: [18:00, 20:00) and [19:00, 21:00)
        var slot1 = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(2));
        var slot2 = TimeSlotRange.From(_baseTimeUtc.AddHours(1), _baseTimeUtc.AddHours(3));

        // Act & Assert
        slot1.Overlaps(slot2).Should().BeTrue();
        slot2.Overlaps(slot1).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_WhenRangesAreAdjacentTouching_ReturnsFalse()
    {
        // Arrange: [18:00, 19:00) and [19:00, 20:00)
        var slot1 = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(1));
        var slot2 = TimeSlotRange.From(_baseTimeUtc.AddHours(1), _baseTimeUtc.AddHours(2));

        // Act & Assert
        slot1.Overlaps(slot2).Should().BeFalse();
        slot2.Overlaps(slot1).Should().BeFalse();
        slot1.Touches(slot2).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_WhenCompletelyDisjoint_ReturnsFalse()
    {
        // Arrange: [18:00, 19:00) and [21:00, 22:00)
        var slot1 = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(1));
        var slot2 = TimeSlotRange.From(_baseTimeUtc.AddHours(3), _baseTimeUtc.AddHours(4));

        // Act & Assert
        slot1.Overlaps(slot2).Should().BeFalse();
        slot1.Touches(slot2).Should().BeFalse();
    }

    [Fact]
    public void Contains_Timestamp_ReturnsTrueOnlyWithinRange()
    {
        // Arrange: [18:00, 20:00)
        var slot = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(2));

        // Act & Assert
        slot.Contains(_baseTimeUtc).Should().BeTrue(); // inclusive start
        slot.Contains(_baseTimeUtc.AddHours(1)).Should().BeTrue(); // middle
        slot.Contains(_baseTimeUtc.AddHours(2)).Should().BeFalse(); // exclusive end
        slot.Contains(_baseTimeUtc.AddHours(-1)).Should().BeFalse(); // before
    }

    [Fact]
    public void Contains_OtherRange_ReturnsTrueWhenSubset()
    {
        // Arrange: [18:00, 21:00) and [19:00, 20:00)
        var outer = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(3));
        var inner = TimeSlotRange.From(_baseTimeUtc.AddHours(1), _baseTimeUtc.AddHours(2));

        // Act & Assert
        outer.Contains(inner).Should().BeTrue();
        inner.Contains(outer).Should().BeFalse();
    }

    [Fact]
    public void MergeWith_WhenTouchingOrOverlapping_ReturnsMergedRange()
    {
        // Arrange: [18:00, 19:00) and [19:00, 20:00)
        var slot1 = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(1));
        var slot2 = TimeSlotRange.From(_baseTimeUtc.AddHours(1), _baseTimeUtc.AddHours(2));

        // Act
        var result = slot1.MergeWith(slot2);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.StartUtc.Should().Be(_baseTimeUtc);
        result.Value.EndUtc.Should().Be(_baseTimeUtc.AddHours(2));
    }

    [Fact]
    public void MergeWith_WhenDisjoint_ReturnsFailure()
    {
        // Arrange: [18:00, 19:00) and [21:00, 22:00)
        var slot1 = TimeSlotRange.From(_baseTimeUtc, _baseTimeUtc.AddHours(1));
        var slot2 = TimeSlotRange.From(_baseTimeUtc.AddHours(3), _baseTimeUtc.AddHours(4));

        // Act
        var result = slot1.MergeWith(slot2);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void FromDateAndTimes_WithCairoTimezone_ConvertsCorrectly()
    {
        // Arrange
        var date = new DateOnly(2026, 10, 24);
        var startTime = new TimeOnly(18, 0); // 6:00 PM Cairo
        var endTime = new TimeOnly(19, 30);   // 7:30 PM Cairo
        var cairoZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        // Act
        var result = TimeSlotRange.FromDateAndTimes(date, startTime, endTime, cairoZone);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Duration.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void FromDateAndTimes_OvernightSlotCrossingMidnight_SpansNextDay()
    {
        // Arrange: 23:00 to 01:00 (crosses midnight)
        var date = new DateOnly(2026, 10, 24);
        var startTime = new TimeOnly(23, 0);
        var endTime = new TimeOnly(1, 0);
        var cairoZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        // Act
        var result = TimeSlotRange.FromDateAndTimes(date, startTime, endTime, cairoZone);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Duration.Should().Be(TimeSpan.FromHours(2));
    }
}
