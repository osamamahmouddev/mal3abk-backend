using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Domain.Events;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Xunit;

namespace Malabak.UnitTests;

public sealed class BookingTests
{
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidParameters_InitializesAsPendingDepositAndEmitsHoldCreatedEvent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var startUtc = _now.AddHours(2);
        var endUtc = _now.AddHours(3);
        var priceSnapshot = CreateSamplePriceSnapshot(startUtc, endUtc);
        var policySnapshot = CreateSamplePolicySnapshot();

        // Act
        var result = Booking.Create(
            id,
            "MLB-123456",
            _venueId,
            _pitchId,
            _playerId,
            startUtc,
            endUtc,
            new Money(500m),
            new Money(100m),
            _now.AddMinutes(15),
            priceSnapshot,
            policySnapshot,
            _now,
            "player-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var booking = result.Value;
        booking.Status.Should().Be(BookingStatus.PendingDeposit);
        booking.VenueId.Should().Be(_venueId);
        booking.History.Should().HaveCount(1);
        booking.History.First().FromStatus.Should().Be(BookingStatus.Draft);
        booking.History.First().ToStatus.Should().Be(BookingStatus.PendingDeposit);
        booking.GetPriceSnapshot().Should().NotBeNull();
        booking.GetPolicySnapshot().Should().NotBeNull();

        booking.DomainEvents.Should().ContainSingle(e => e is BookingHoldCreatedDomainEvent);
    }

    [Fact]
    public void CreateManual_WhenConfirmedImmediately_InitializesAsConfirmed()
    {
        // Arrange
        var id = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var startUtc = _now.AddHours(2);
        var endUtc = _now.AddHours(3);

        // Act
        var result = Booking.CreateManual(
            id,
            "MLB-MANUAL",
            _venueId,
            _pitchId,
            staffId,
            "Ahmed Hassan",
            "+201012345678",
            startUtc,
            endUtc,
            new Money(600m),
            new Money(600m),
            confirmedImmediately: true,
            holdExpiresAtUtc: null,
            nowUtc: _now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var booking = result.Value;
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.IsManualBooking.Should().BeTrue();
        booking.CustomerName.Should().Be("Ahmed Hassan");
        booking.CustomerPhone.Should().Be("+201012345678");
        booking.ConfirmedAtUtc.Should().Be(_now);
        booking.History.First().ToStatus.Should().Be(BookingStatus.Confirmed);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingConfirmedDomainEvent);
    }

    [Fact]
    public void SubmitProof_WhenPendingDeposit_TransitionsToProofSubmitted()
    {
        // Arrange
        var booking = CreateSampleBooking();
        var proofId = Guid.NewGuid();

        // Act
        var result = booking.SubmitProof(proofId, "player-1", _now.AddMinutes(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.ProofSubmitted);
        booking.History.Should().HaveCount(2);
        booking.History.Last().ToStatus.Should().Be(BookingStatus.ProofSubmitted);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingProofSubmittedDomainEvent);
    }

    [Fact]
    public void SubmitProof_WhenHoldExpired_ReturnsFailure()
    {
        // Arrange
        var booking = CreateSampleBooking();

        // Act (after 15 minutes hold)
        var result = booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(20));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.HoldExpired);
    }

    [Fact]
    public void SubmitProof_WhenRejectedAndHoldStillValid_AllowsResubmission()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(2));
        booking.Reject("Unclear receipt photo", "venue-staff-1", _now.AddMinutes(5));
        booking.Status.Should().Be(BookingStatus.Rejected);

        // Act (player re-uploads within 15 min hold at minute 8)
        var newProofId = Guid.NewGuid();
        var result = booking.SubmitProof(newProofId, "player-1", _now.AddMinutes(8));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.ProofSubmitted);
    }

    [Fact]
    public void MarkUnderReview_WhenProofSubmitted_TransitionsToUnderReview()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));

        // Act
        var result = booking.MarkUnderReview("staff-1", _now.AddMinutes(7));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.UnderReview);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingUnderReviewDomainEvent);
    }

    [Fact]
    public void Confirm_WhenProofSubmittedOrUnderReview_TransitionsToConfirmedAndClearsHold()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.MarkUnderReview("staff-1", _now.AddMinutes(7));

        // Act
        var result = booking.Confirm("venue-staff-1", _now.AddMinutes(10));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ConfirmedAtUtc.Should().Be(_now.AddMinutes(10));
        booking.HoldExpiresAtUtc.Should().BeNull();
        booking.DomainEvents.Should().ContainSingle(e => e is BookingConfirmedDomainEvent);
    }

    [Fact]
    public void Reject_WithoutReason_ReturnsValidationFailure()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));

        // Act
        var result = booking.Reject(string.Empty, "venue-staff-1", _now.AddMinutes(10));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.ReasonRequired);
    }

    [Fact]
    public void Expire_WhenPendingDepositAndHoldElapsed_TransitionsToExpired()
    {
        // Arrange
        var booking = CreateSampleBooking();

        // Act
        var result = booking.Expire(_now.AddMinutes(16));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Expired);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingExpiredDomainEvent);
    }

    [Fact]
    public void Cancel_WhenConfirmedBeforePolicyDeadline_Succeeds()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // Kickoff is in 2 hours (_now + 2h). Policy requires 1 hour cancellation notice.
        // Player cancels at _now + 30m (1.5 hours before kickoff, > 1h deadline).
        var result = booking.Cancel("Change of plans", "player-1", isStaff: false, _now.AddMinutes(30), policyCancellationDeadlineHours: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancelledAtUtc.Should().Be(_now.AddMinutes(30));
        booking.CancellationReason.Should().Be("Change of plans");
        booking.DomainEvents.Should().ContainSingle(e => e is BookingCancelledDomainEvent);
    }

    [Fact]
    public void Cancel_WhenConfirmedAfterPolicyDeadlinePassed_FailsForPlayer()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // Kickoff is at _now + 2h. Policy requires 24h cancellation deadline.
        // Player cancels at _now + 30m (only 1.5h before kickoff).
        var result = booking.Cancel("Too late", "player-1", isStaff: false, _now.AddMinutes(30), policyCancellationDeadlineHours: 24);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.CancellationDeadlinePassed);
    }

    [Fact]
    public void Cancel_WhenConfirmedAfterPolicyDeadlinePassed_SucceedsForStaffOverride()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // Venue manager staff override
        var result = booking.Cancel("Weather emergency", "venue-manager-1", isStaff: true, _now.AddMinutes(30), policyCancellationDeadlineHours: 24);

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    public void Complete_WhenKickoffEnded_TransitionsToCompleted()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // EndUtc is _now.AddHours(3).
        // Complete after end time:
        var result = booking.Complete(_now.AddHours(3).AddMinutes(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Completed);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingCompletedDomainEvent);
    }

    [Fact]
    public void Complete_BeforeEndTime_ReturnsFailure()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // Complete before match ends
        var result = booking.Complete(_now.AddHours(2));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.CannotCompleteBeforeEnd);
    }

    [Fact]
    public void MarkNoShow_WhenStartPassed_TransitionsToNoShow()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // Kickoff is _now.AddHours(2). Marking no-show at kickoff + 20m.
        var result = booking.MarkNoShow("Player did not arrive", "staff-1", _now.AddHours(2).AddMinutes(20));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.NoShow);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingNoShowDomainEvent);
    }

    [Fact]
    public void MarkNoShow_BeforeKickoff_ReturnsFailure()
    {
        // Arrange
        var booking = CreateSampleBooking();
        booking.SubmitProof(Guid.NewGuid(), "player-1", _now.AddMinutes(5));
        booking.Confirm("staff-1", _now.AddMinutes(6));

        // Attempting to mark no-show before kickoff
        var result = booking.MarkNoShow("Premature no-show", "staff-1", _now.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.CannotMarkNoShowBeforeStart);
    }

    private Booking CreateSampleBooking()
    {
        var startUtc = _now.AddHours(2);
        var endUtc = _now.AddHours(3);
        return Booking.Create(
            Guid.NewGuid(),
            "MLB-ABCDEF",
            _venueId,
            _pitchId,
            _playerId,
            startUtc,
            endUtc,
            new Money(500m),
            new Money(100m),
            _now.AddMinutes(15),
            CreateSamplePriceSnapshot(startUtc, endUtc),
            CreateSamplePolicySnapshot(),
            _now,
            "player-1").Value;
    }

    private static PriceQuoteSnapshot CreateSamplePriceSnapshot(DateTime startUtc, DateTime endUtc)
    {
        return new PriceQuoteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startUtc,
            endUtc,
            60,
            "EGP",
            500m,
            0m,
            null,
            500m,
            100m,
            "Fixed",
            [new PriceLineSnapshot(startUtc, endUtc, 500m, 500m, [])],
            DateTime.UtcNow);
    }

    private static BookingPolicySnapshot CreateSamplePolicySnapshot()
    {
        return new BookingPolicySnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "Fixed",
            100m,
            15,
            120,
            24,
            "FullRefund",
            100m,
            "ForfeitDeposit",
            15,
            true,
            24,
            true,
            "Standard policy",
            DateTime.UtcNow);
    }
}
