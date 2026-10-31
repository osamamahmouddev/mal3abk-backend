using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class BookingTests
{
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidParameters_InitializesAsPendingDeposit()
    {
        // Arrange
        var id = Guid.NewGuid();
        var startUtc = _now.AddHours(2);
        var endUtc = _now.AddHours(3);

        // Act
        var result = Booking.Create(
            id,
            "MLB-123456",
            _pitchId,
            _playerId,
            startUtc,
            endUtc,
            new Money(500m),
            new Money(100m),
            _now.AddMinutes(15),
            _now,
            "player-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(BookingStatus.PendingDeposit);
        result.Value.History.Should().HaveCount(1);
    }

    [Fact]
    public void HistoryRows_GetTimeOrderedGuidV7Ids()
    {
        var booking = CreateSampleBooking();
        booking.SubmitProof("player-1", _now.AddMinutes(1)).IsSuccess.Should().BeTrue();

        booking.History.Should().HaveCount(2).And.OnlyContain(h => h.Id.Version == 7, "ids are Guid v7 (rule.md section 5)");
        booking.History.Select(h => h.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SubmitProof_WhenPendingDeposit_TransitionsToProofSubmitted()
    {
        // Arrange
        var booking = CreateSampleBooking();

        // Act
        var result = booking.SubmitProof("player-1", _now.AddMinutes(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.ProofSubmitted);
    }

    [Fact]
    public void SubmitProof_WhenHoldExpired_ReturnsFailure()
    {
        // Arrange
        var booking = CreateSampleBooking();

        // Act (after 15 minutes hold)
        var result = booking.SubmitProof("player-1", _now.AddMinutes(20));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.HoldExpired);
    }

    [Fact]
    public void Confirm_WhenProofSubmitted_TransitionsToConfirmed()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof("player-1", _now.AddMinutes(5));

        // Act
        var result = booking.Confirm("venue-staff-1", _now.AddMinutes(10));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public void Reject_WithoutReason_ReturnsValidationFailure()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof("player-1", _now.AddMinutes(5));

        // Act
        var result = booking.Reject(string.Empty, "venue-staff-1", _now.AddMinutes(10));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.ReasonRequired);
    }

    private Booking CreateSampleBooking()
    {
        return Booking.Create(
            Guid.NewGuid(),
            "MLB-ABCDEF",
            _pitchId,
            _playerId,
            _now.AddHours(2),
            _now.AddHours(3),
            new Money(500m),
            new Money(100m),
            _now.AddMinutes(15),
            _now,
            "player-1").Value;
    }
}
