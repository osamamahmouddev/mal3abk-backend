using System.Reflection;
using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 2 (P2) Edge-Case Tests: Full booking lifecycle transition table across all 10 states
/// (Draft, PendingDeposit, ProofSubmitted, UnderReview, Confirmed, Rejected, Expired, Cancelled, Completed, NoShow).
/// Every state-action pair is explicitly tested for valid transition or correct error code rejection.
/// </summary>
public sealed class BookingLifecycleTransitionMatrixTests
{
    private static readonly DateTime _baseTimeUtc = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();

    private Booking CreateTestBooking(BookingStatus status, DateTime? holdExpiresAtUtc = null, DateTime? startUtc = null, DateTime? endUtc = null)
    {
        var start = startUtc ?? _baseTimeUtc.AddHours(2);
        var end = endUtc ?? _baseTimeUtc.AddHours(3);
        var holdExp = holdExpiresAtUtc ?? _baseTimeUtc.AddMinutes(15);

        var booking = Booking.Create(
            Guid.NewGuid(),
            "MLB-TEST-REF",
            _venueId,
            _pitchId,
            _playerId,
            start,
            end,
            Money.Create(600m, "EGP").Value,
            Money.Create(150m, "EGP").Value,
            holdExp,
            null,
            null,
            _baseTimeUtc,
            _playerId.ToString("D")).Value;

        SetStatus(booking, status);
        return booking;
    }

    private static void SetStatus(Booking booking, BookingStatus status)
    {
        var prop = typeof(Booking).GetProperty(nameof(Booking.Status), BindingFlags.Public | BindingFlags.Instance);
        prop!.SetValue(booking, status);
    }

    // =========================================================================
    // 1. SubmitProof Action Matrix
    // Allowed: PendingDeposit, Rejected (when hold not expired)
    // Rejected: Draft, ProofSubmitted, UnderReview, Confirmed, Expired, Cancelled, Completed, NoShow
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.PendingDeposit, true)]
    [InlineData(BookingStatus.Rejected, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.ProofSubmitted, false)]
    [InlineData(BookingStatus.UnderReview, false)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void SubmitProof_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var booking = CreateTestBooking(initialState, holdExpiresAtUtc: _baseTimeUtc.AddMinutes(15));
        var result = booking.SubmitProof(Guid.NewGuid(), _playerId.ToString("D"), _baseTimeUtc.AddMinutes(5));

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.ProofSubmitted);
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    [Fact]
    public void SubmitProof_WhenHoldExpired_RejectsWithHoldExpiredError()
    {
        var booking = CreateTestBooking(BookingStatus.PendingDeposit, holdExpiresAtUtc: _baseTimeUtc.AddMinutes(10));
        // Calling at minute 11 (after expiry)
        var result = booking.SubmitProof(Guid.NewGuid(), _playerId.ToString("D"), _baseTimeUtc.AddMinutes(11));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.HoldExpired");
    }

    // =========================================================================
    // 2. MarkUnderReview Action Matrix
    // Allowed: ProofSubmitted
    // Rejected: All 9 other states
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.ProofSubmitted, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.PendingDeposit, false)]
    [InlineData(BookingStatus.UnderReview, false)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void MarkUnderReview_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var booking = CreateTestBooking(initialState);
        var result = booking.MarkUnderReview("staff-1", _baseTimeUtc.AddMinutes(5));

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.UnderReview);
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    // =========================================================================
    // 3. Confirm (Approve) Action Matrix
    // Allowed: ProofSubmitted, UnderReview
    // Rejected: All 8 other states
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.ProofSubmitted, true)]
    [InlineData(BookingStatus.UnderReview, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.PendingDeposit, false)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Confirm_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var booking = CreateTestBooking(initialState);
        var result = booking.Confirm("staff-1", _baseTimeUtc.AddMinutes(5));

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.Confirmed);
            booking.ConfirmedAtUtc.Should().Be(_baseTimeUtc.AddMinutes(5));
            booking.HoldExpiresAtUtc.Should().BeNull();
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    // =========================================================================
    // 4. Reject Action Matrix
    // Allowed: ProofSubmitted, UnderReview
    // Rejected: All 8 other states
    // Edge case: Empty / whitespace reason rejected with ReasonRequired
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.ProofSubmitted, true)]
    [InlineData(BookingStatus.UnderReview, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.PendingDeposit, false)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Reject_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var booking = CreateTestBooking(initialState);
        var result = booking.Reject("Receipt illegible", "staff-1", _baseTimeUtc.AddMinutes(5));

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.Rejected);
            booking.RejectionReason.Should().Be("Receipt illegible");
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithEmptyOrWhitespaceReason_ReturnsReasonRequired(string? emptyReason)
    {
        var booking = CreateTestBooking(BookingStatus.ProofSubmitted);
        var result = booking.Reject(emptyReason!, "staff-1", _baseTimeUtc.AddMinutes(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.ReasonRequired");
    }

    // =========================================================================
    // 5. Expire Action Matrix
    // Allowed: PendingDeposit, Rejected
    // Rejected: All 8 other states
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.PendingDeposit, true)]
    [InlineData(BookingStatus.Rejected, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.ProofSubmitted, false)]
    [InlineData(BookingStatus.UnderReview, false)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Expire_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var booking = CreateTestBooking(initialState);
        var result = booking.Expire(_baseTimeUtc.AddMinutes(16));

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.Expired);
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    // =========================================================================
    // 6. Cancel Action Matrix
    // Allowed: Confirmed, PendingDeposit, ProofSubmitted, UnderReview
    // Rejected: Draft, Rejected, Expired, Cancelled, Completed, NoShow
    // Edge case: Empty / whitespace reason rejected with ReasonRequired
    // Edge case: Confirmed booking cancelled by player after kickoff rejected with CannotCancelPastBooking
    // Edge case: Confirmed booking cancelled by player after policy deadline rejected with CancellationDeadlinePassed
    // Edge case: Confirmed booking cancelled by staff permitted even after policy deadline
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.PendingDeposit, true)]
    [InlineData(BookingStatus.ProofSubmitted, true)]
    [InlineData(BookingStatus.UnderReview, true)]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Cancel_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var booking = CreateTestBooking(initialState, startUtc: _baseTimeUtc.AddHours(48), endUtc: _baseTimeUtc.AddHours(49));
        var result = booking.Cancel("User request", _playerId.ToString("D"), isStaff: false, _baseTimeUtc);

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.Cancelled);
            booking.CancellationReason.Should().Be("User request");
            booking.CancelledAtUtc.Should().Be(_baseTimeUtc);
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithEmptyOrWhitespaceReason_ReturnsReasonRequired(string? emptyReason)
    {
        var booking = CreateTestBooking(BookingStatus.PendingDeposit);
        var result = booking.Cancel(emptyReason!, _playerId.ToString("D"), isStaff: false, _baseTimeUtc);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.ReasonRequired");
    }

    [Fact]
    public void Cancel_ConfirmedBooking_ByPlayerAfterKickoff_ReturnsCannotCancelPastBooking()
    {
        var kickoff = _baseTimeUtc.AddHours(1);
        var booking = CreateTestBooking(BookingStatus.Confirmed, startUtc: kickoff, endUtc: kickoff.AddHours(1));

        // Player tries to cancel at kickoff + 1 minute
        var result = booking.Cancel("Late cancel", _playerId.ToString("D"), isStaff: false, kickoff.AddMinutes(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.CannotCancelPastBooking");
    }

    [Fact]
    public void Cancel_ConfirmedBooking_ByPlayerAfterPolicyDeadline_ReturnsCancellationDeadlinePassed()
    {
        var kickoff = _baseTimeUtc.AddHours(20);
        var booking = CreateTestBooking(BookingStatus.Confirmed, startUtc: kickoff, endUtc: kickoff.AddHours(1));

        // Policy requires 24h notice; 20h is past the deadline
        var result = booking.Cancel("Notice too short", _playerId.ToString("D"), isStaff: false, _baseTimeUtc, policyCancellationDeadlineHours: 24);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.CancellationDeadlinePassed");
    }

    [Fact]
    public void Cancel_ConfirmedBooking_ByStaff_BypassesPolicyCancellationDeadline()
    {
        var kickoff = _baseTimeUtc.AddHours(2);
        var booking = CreateTestBooking(BookingStatus.Confirmed, startUtc: kickoff, endUtc: kickoff.AddHours(1));

        // Venue staff can cancel even 2h before kickoff despite 24h policy
        var result = booking.Cancel("Pitch maintenance emergency", "staff-1", isStaff: true, _baseTimeUtc, policyCancellationDeadlineHours: 24);

        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
    }

    // =========================================================================
    // 7. Complete Action Matrix
    // Allowed: Confirmed (when nowUtc >= EndUtc)
    // Rejected: All 9 other states
    // Edge case: Confirmed before match end rejected with CannotCompleteBeforeEnd
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.PendingDeposit, false)]
    [InlineData(BookingStatus.ProofSubmitted, false)]
    [InlineData(BookingStatus.UnderReview, false)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Complete_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var start = _baseTimeUtc.AddHours(1);
        var end = _baseTimeUtc.AddHours(2);
        var booking = CreateTestBooking(initialState, startUtc: start, endUtc: end);

        // Calling at end time
        var result = booking.Complete(end);

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.Completed);
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    [Fact]
    public void Complete_WhenMatchNotYetEnded_ReturnsCannotCompleteBeforeEnd()
    {
        var start = _baseTimeUtc.AddHours(1);
        var end = _baseTimeUtc.AddHours(2);
        var booking = CreateTestBooking(BookingStatus.Confirmed, startUtc: start, endUtc: end);

        // Calling 1 minute before end
        var result = booking.Complete(end.AddMinutes(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.CannotCompleteBeforeEnd");
    }

    // =========================================================================
    // 8. MarkNoShow Action Matrix
    // Allowed: Confirmed (when nowUtc >= StartUtc)
    // Rejected: All 9 other states
    // Edge case: Confirmed before kickoff rejected with CannotMarkNoShowBeforeStart
    // Edge case: Empty / whitespace reason rejected with ReasonRequired
    // =========================================================================

    [Theory]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Draft, false)]
    [InlineData(BookingStatus.PendingDeposit, false)]
    [InlineData(BookingStatus.ProofSubmitted, false)]
    [InlineData(BookingStatus.UnderReview, false)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void MarkNoShow_StateMatrix_EnforcesStrictTransitions(BookingStatus initialState, bool expectedSuccess)
    {
        var start = _baseTimeUtc.AddHours(1);
        var end = _baseTimeUtc.AddHours(2);
        var booking = CreateTestBooking(initialState, startUtc: start, endUtc: end);

        // Calling after kickoff
        var result = booking.MarkNoShow("Player did not arrive", "staff-1", start.AddMinutes(15));

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.NoShow);
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Booking.InvalidTransition");
        }
    }

    [Fact]
    public void MarkNoShow_BeforeKickoff_ReturnsCannotMarkNoShowBeforeStart()
    {
        var start = _baseTimeUtc.AddHours(1);
        var booking = CreateTestBooking(BookingStatus.Confirmed, startUtc: start);

        // Calling 1 minute before kickoff
        var result = booking.MarkNoShow("Player absent", "staff-1", start.AddMinutes(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.CannotMarkNoShowBeforeStart");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkNoShow_WithEmptyOrWhitespaceReason_ReturnsReasonRequired(string? emptyReason)
    {
        var start = _baseTimeUtc.AddHours(1);
        var booking = CreateTestBooking(BookingStatus.Confirmed, startUtc: start);

        var result = booking.MarkNoShow(emptyReason!, "staff-1", start.AddMinutes(10));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.ReasonRequired");
    }

    // =========================================================================
    // 9. Manual Refund Action Matrix
    // Allowed: Pending refund on Cancelled booking
    // Rejected: Status == None (no refund due), or already decided (Refunded / Denied)
    // Edge case: Amount <= 0 or Amount > Deposit rejected with RefundAmountInvalid
    // Edge case: Empty / whitespace notes rejected with RefundNotesRequired
    // =========================================================================

    [Fact]
    public void RecordManualRefund_WhenPending_SuccessfullyRecordsRefund()
    {
        var refund = BookingRefund.CreatePending(Guid.NewGuid(), Guid.NewGuid(), 150m, "EGP", "Player cancelled within free window", null, _baseTimeUtc);

        var result = refund.RecordManualRefund(150m, 150m, "InstaPay ref #987654321", "staff-1", _baseTimeUtc.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        refund.Status.Should().Be(RefundStatus.Refunded);
        refund.ActualRefundAmount.Should().Be(150m);
        refund.Notes.Should().Be("InstaPay ref #987654321");
    }

    [Fact]
    public void RecordManualRefund_WhenAlreadyRefunded_RejectsWithRefundAlreadyDecided()
    {
        var refund = BookingRefund.CreatePending(Guid.NewGuid(), Guid.NewGuid(), 150m, "EGP", "Player cancelled", null, _baseTimeUtc);
        refund.RecordManualRefund(150m, 150m, "Initial refund", "staff-1", _baseTimeUtc.AddHours(1));

        // Replaying record
        var secondRecord = refund.RecordManualRefund(150m, 150m, "Second refund attempt", "staff-1", _baseTimeUtc.AddHours(2));

        secondRecord.IsFailure.Should().BeTrue();
        secondRecord.Error.Code.Should().Be("Refund.AlreadyDecided");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(150.01)]
    [InlineData(200)]
    public void RecordManualRefund_WithInvalidAmount_ReturnsRefundAmountInvalid(decimal invalidAmount)
    {
        var refund = BookingRefund.CreatePending(Guid.NewGuid(), Guid.NewGuid(), 150m, "EGP", "Player cancelled", null, _baseTimeUtc);

        var result = refund.RecordManualRefund(invalidAmount, 150m, "Transfer notes", "staff-1", _baseTimeUtc.AddHours(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Refund.InvalidAmount");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordManualRefund_WithEmptyOrWhitespaceNotes_ReturnsRefundNotesRequired(string? emptyNotes)
    {
        var refund = BookingRefund.CreatePending(Guid.NewGuid(), Guid.NewGuid(), 150m, "EGP", "Player cancelled", null, _baseTimeUtc);

        var result = refund.RecordManualRefund(150m, 150m, emptyNotes!, "staff-1", _baseTimeUtc.AddHours(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Refund.NotesRequired");
    }
}
