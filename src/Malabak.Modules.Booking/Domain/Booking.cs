using System.Text.Json;
using Malabak.Modules.Booking.Domain.Events;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Booking.Domain;

public sealed class Booking : Entity<Guid>
{
    private readonly List<BookingStatusHistory> _history = [];
    private readonly List<PaymentProof> _proofs = [];
    private readonly List<BookingRefund> _refunds = [];

    public string BookingReference { get; private set; } = string.Empty;
    public Guid VenueId { get; private set; }
    public Guid PitchId { get; private set; }
    public Guid PlayerId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public Money TotalPrice { get; private set; } = default!;
    public Money DepositAmount { get; private set; } = default!;
    public Money PlatformCommission { get; private set; } = default!;
    public Money VenueNetAmount { get; private set; } = default!;
    public Money OnlinePaidAmount { get; private set; } = default!;
    public Money VenuePayAmount { get; private set; } = default!;
    public bool IsCommissionPaid { get; private set; }
    public string? PaymobOrderId { get; private set; }
    public string? PaymobTransactionId { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTime? HoldExpiresAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? RejectionReason { get; private set; }
    public bool IsManualBooking { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string? Notes { get; private set; }
    public string? PriceSnapshotJson { get; private set; }
    public string? PolicySnapshotJson { get; private set; }

    public bool IsManual => IsManualBooking;
    public BookingPolicySnapshot? PolicySnapshot => PolicySnapshotJson is null ? null : JsonSerializer.Deserialize<BookingPolicySnapshot>(PolicySnapshotJson);
    public PriceQuoteSnapshot? PriceSnapshot => PriceSnapshotJson is null ? null : JsonSerializer.Deserialize<PriceQuoteSnapshot>(PriceSnapshotJson);

    public IReadOnlyCollection<BookingStatusHistory> History => _history.AsReadOnly();
    public IReadOnlyCollection<PaymentProof> Proofs => _proofs.AsReadOnly();
    public IReadOnlyCollection<BookingRefund> Refunds => _refunds.AsReadOnly();

    private Booking()
    {
    }

    public static Result<Booking> Create(
        Guid id,
        string bookingReference,
        Guid venueId,
        Guid pitchId,
        Guid playerId,
        DateTime startUtc,
        DateTime endUtc,
        Money totalPrice,
        Money depositAmount,
        DateTime holdExpiresAtUtc,
        PriceQuoteSnapshot? priceSnapshot,
        BookingPolicySnapshot? policySnapshot,
        DateTime nowUtc,
        string actorUserId)
    {
        if (startUtc >= endUtc)
        {
            return Result.Failure<Booking>(Error.Validation("Booking.InvalidTimeRange", "Start time must precede end time."));
        }

        if (totalPrice.Amount <= 0)
        {
            return Result.Failure<Booking>(Error.Validation("Booking.InvalidPrice", "Total price must be greater than zero."));
        }

        if (depositAmount.Amount < 0 || depositAmount.Amount > totalPrice.Amount)
        {
            return Result.Failure<Booking>(Error.Validation("Booking.InvalidDeposit", "Deposit amount cannot be negative or exceed total price."));
        }

        var commission = priceSnapshot is not null && priceSnapshot.PlatformCommission > 0
            ? new Money(priceSnapshot.PlatformCommission, totalPrice.Currency)
            : new Money(0m, totalPrice.Currency);

        var venueNet = priceSnapshot is not null && priceSnapshot.NetPitchPrice > 0
            ? new Money(priceSnapshot.NetPitchPrice, totalPrice.Currency)
            : new Money(Math.Max(0m, totalPrice.Amount - commission.Amount), totalPrice.Currency);

        var venuePay = priceSnapshot is not null && priceSnapshot.VenuePayAmount > 0
            ? new Money(priceSnapshot.VenuePayAmount, totalPrice.Currency)
            : new Money(venueNet.Amount, venueNet.Currency);

        var booking = new Booking
        {
            Id = id,
            BookingReference = bookingReference,
            VenueId = venueId,
            PitchId = pitchId,
            PlayerId = playerId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            TotalPrice = totalPrice,
            DepositAmount = depositAmount,
            PlatformCommission = commission,
            VenueNetAmount = venueNet,
            OnlinePaidAmount = new Money(0m, totalPrice.Currency),
            VenuePayAmount = venuePay,
            IsCommissionPaid = false,
            Status = BookingStatus.PendingDeposit,
            HoldExpiresAtUtc = holdExpiresAtUtc,
            PriceSnapshotJson = priceSnapshot is null ? null : JsonSerializer.Serialize(priceSnapshot),
            PolicySnapshotJson = policySnapshot is null ? null : JsonSerializer.Serialize(policySnapshot),
            CreatedAtUtc = nowUtc
        };

        booking.AddHistory(BookingStatus.Draft, BookingStatus.PendingDeposit, "Booking hold created", actorUserId, nowUtc);
        booking.RaiseDomainEvent(new BookingHoldCreatedDomainEvent(
            booking.Id, venueId, pitchId, playerId, bookingReference, startUtc, endUtc, holdExpiresAtUtc));

        return Result.Success(booking);
    }

    public static Result<Booking> CreateManual(
        Guid id,
        string bookingReference,
        Guid venueId,
        Guid pitchId,
        Guid staffUserId,
        string customerName,
        string customerPhone,
        DateTime startUtc,
        DateTime endUtc,
        Money totalPrice,
        Money depositAmount,
        bool confirmedImmediately,
        DateTime? holdExpiresAtUtc,
        DateTime nowUtc,
        string? notes = null)
    {
        if (startUtc >= endUtc)
        {
            return Result.Failure<Booking>(Error.Validation("Booking.InvalidTimeRange", "Start time must precede end time."));
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            return Result.Failure<Booking>(Error.Validation("Booking.CustomerNameRequired", "Customer name is required for manual bookings."));
        }

        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return Result.Failure<Booking>(Error.Validation("Booking.CustomerPhoneRequired", "Customer phone is required for manual bookings."));
        }

        var targetStatus = confirmedImmediately ? BookingStatus.Confirmed : BookingStatus.PendingDeposit;

        var booking = new Booking
        {
            Id = id,
            BookingReference = bookingReference,
            VenueId = venueId,
            PitchId = pitchId,
            PlayerId = staffUserId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            TotalPrice = totalPrice,
            DepositAmount = depositAmount,
            PlatformCommission = new Money(0m, totalPrice.Currency),
            VenueNetAmount = new Money(totalPrice.Amount, totalPrice.Currency),
            OnlinePaidAmount = new Money(0m, totalPrice.Currency),
            VenuePayAmount = new Money(Math.Max(0m, totalPrice.Amount - depositAmount.Amount), totalPrice.Currency),
            IsCommissionPaid = false,
            Status = targetStatus,
            HoldExpiresAtUtc = confirmedImmediately ? null : holdExpiresAtUtc,
            ConfirmedAtUtc = confirmedImmediately ? nowUtc : null,
            IsManualBooking = true,
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAtUtc = nowUtc
        };

        var reason = confirmedImmediately ? "Manual booking created and confirmed by venue staff" : "Manual booking hold created by venue staff";
        booking.AddHistory(BookingStatus.Draft, targetStatus, reason, staffUserId.ToString(), nowUtc);

        if (confirmedImmediately)
        {
            booking.RaiseDomainEvent(new BookingConfirmedDomainEvent(
                booking.Id, venueId, pitchId, staffUserId, bookingReference, startUtc, endUtc, staffUserId.ToString(), nowUtc));
        }
        else
        {
            booking.RaiseDomainEvent(new BookingHoldCreatedDomainEvent(
                booking.Id, venueId, pitchId, staffUserId, bookingReference, startUtc, endUtc, holdExpiresAtUtc ?? nowUtc.AddMinutes(15)));
        }

        return Result.Success(booking);
    }

    public Result SubmitProof(Guid proofId, string actorUserId, DateTime nowUtc)
    {
        if (Status != BookingStatus.PendingDeposit && Status != BookingStatus.Rejected)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        if (HoldExpiresAtUtc.HasValue && nowUtc > HoldExpiresAtUtc.Value)
        {
            return Result.Failure(BookingErrors.HoldExpired);
        }

        TransitionTo(BookingStatus.ProofSubmitted, "Payment proof uploaded by player", actorUserId, nowUtc);
        RaiseDomainEvent(new BookingProofSubmittedDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, proofId, nowUtc));
        return Result.Success();
    }

    public Result MarkUnderReview(string actorUserId, DateTime nowUtc)
    {
        if (Status != BookingStatus.ProofSubmitted)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        TransitionTo(BookingStatus.UnderReview, "Venue staff opened proof for review", actorUserId, nowUtc);
        RaiseDomainEvent(new BookingUnderReviewDomainEvent(Id, VenueId, BookingReference, actorUserId, nowUtc));
        return Result.Success();
    }

    public Result ConfirmOnlinePayment(string paymobOrderId, string paymobTransactionId, Money paidAmount, DateTime nowUtc, string actorUserId = "Paymob")
    {
        if (IsCommissionPaid && Status == BookingStatus.Confirmed)
        {
            return Result.Success();
        }

        if (Status != BookingStatus.PendingDeposit && Status != BookingStatus.ProofSubmitted && Status != BookingStatus.UnderReview)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        if (PlatformCommission.Amount > 0 && paidAmount.Amount < PlatformCommission.Amount)
        {
            return Result.Failure(Error.Validation("Booking.InsufficientPayment", "Paid amount is less than the required commission."));
        }

        PaymobOrderId = paymobOrderId;
        PaymobTransactionId = paymobTransactionId;
        IsCommissionPaid = true;
        OnlinePaidAmount = new Money(paidAmount.Amount, paidAmount.Currency);
        ConfirmedAtUtc = nowUtc;
        HoldExpiresAtUtc = null;
        TransitionTo(BookingStatus.Confirmed, "Commission paid online via Paymob", actorUserId, nowUtc);
        RaiseDomainEvent(new BookingConfirmedDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, StartUtc, EndUtc, actorUserId, nowUtc));
        return Result.Success();
    }

    public Result Confirm(string actorUserId, DateTime nowUtc)
    {
        if (Status != BookingStatus.ProofSubmitted && Status != BookingStatus.UnderReview)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        ConfirmedAtUtc = nowUtc;
        HoldExpiresAtUtc = null; // Clear hold expiry upon confirmation
        TransitionTo(BookingStatus.Confirmed, "Payment verified and booking confirmed by venue staff", actorUserId, nowUtc);
        RaiseDomainEvent(new BookingConfirmedDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, StartUtc, EndUtc, actorUserId, nowUtc));
        return Result.Success();
    }

    public Result Reject(string reason, string actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BookingErrors.ReasonRequired);
        }

        if (Status != BookingStatus.ProofSubmitted && Status != BookingStatus.UnderReview)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        RejectionReason = reason.Trim();
        var canResubmit = HoldExpiresAtUtc.HasValue && nowUtc < HoldExpiresAtUtc.Value;
        TransitionTo(BookingStatus.Rejected, reason.Trim(), actorUserId, nowUtc);
        RaiseDomainEvent(new BookingRejectedDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, reason.Trim(), canResubmit, nowUtc));
        return Result.Success();
    }

    public Result Expire(DateTime nowUtc)
    {
        if (Status != BookingStatus.PendingDeposit && Status != BookingStatus.Rejected)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        TransitionTo(BookingStatus.Expired, "Hold duration elapsed without approved payment", "System", nowUtc);
        RaiseDomainEvent(new BookingExpiredDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, StartUtc, EndUtc, nowUtc));
        return Result.Success();
    }

    public Result Cancel(string reason, string actorUserId, bool isStaff, DateTime nowUtc, int? policyCancellationDeadlineHours = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BookingErrors.ReasonRequired);
        }

        if (Status != BookingStatus.Confirmed && Status != BookingStatus.PendingDeposit
            && Status != BookingStatus.ProofSubmitted && Status != BookingStatus.UnderReview)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        if (Status == BookingStatus.Confirmed && !isStaff)
        {
            if (nowUtc >= StartUtc)
            {
                return Result.Failure(BookingErrors.CannotCancelPastBooking);
            }

            if (policyCancellationDeadlineHours.HasValue)
            {
                var deadlineUtc = StartUtc.AddHours(-policyCancellationDeadlineHours.Value);
                if (nowUtc > deadlineUtc)
                {
                    return Result.Failure(BookingErrors.CancellationDeadlinePassed);
                }
            }
        }

        CancelledAtUtc = nowUtc;
        CancellationReason = reason.Trim();
        HoldExpiresAtUtc = null;
        TransitionTo(BookingStatus.Cancelled, reason.Trim(), actorUserId, nowUtc);
        RaiseDomainEvent(new BookingCancelledDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, StartUtc, EndUtc, actorUserId, reason.Trim(), nowUtc));
        return Result.Success();
    }

    public Result Complete(DateTime nowUtc)
    {
        if (Status != BookingStatus.Confirmed)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        if (nowUtc < EndUtc)
        {
            return Result.Failure(BookingErrors.CannotCompleteBeforeEnd);
        }

        TransitionTo(BookingStatus.Completed, "Match completed", "System", nowUtc);
        RaiseDomainEvent(new BookingCompletedDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, nowUtc));
        return Result.Success();
    }

    public Result MarkNoShow(string reason, string actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BookingErrors.ReasonRequired);
        }

        if (Status != BookingStatus.Confirmed)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        if (nowUtc < StartUtc)
        {
            return Result.Failure(BookingErrors.CannotMarkNoShowBeforeStart);
        }

        TransitionTo(BookingStatus.NoShow, reason.Trim(), actorUserId, nowUtc);
        RaiseDomainEvent(new BookingNoShowDomainEvent(Id, VenueId, PitchId, PlayerId, BookingReference, reason.Trim(), actorUserId, nowUtc));
        return Result.Success();
    }

    public void AddProof(PaymentProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        _proofs.Add(proof);
    }

    public void AddRefund(BookingRefund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        _refunds.Add(refund);
    }

    public BookingPolicySnapshot? GetPolicySnapshot()
    {
        return string.IsNullOrWhiteSpace(PolicySnapshotJson)
            ? null
            : JsonSerializer.Deserialize<BookingPolicySnapshot>(PolicySnapshotJson);
    }

    public PriceQuoteSnapshot? GetPriceSnapshot()
    {
        return string.IsNullOrWhiteSpace(PriceSnapshotJson)
            ? null
            : JsonSerializer.Deserialize<PriceQuoteSnapshot>(PriceSnapshotJson);
    }

    private void TransitionTo(BookingStatus newStatus, string reason, string actorUserId, DateTime nowUtc)
    {
        var oldStatus = Status;
        Status = newStatus;
        UpdatedAtUtc = nowUtc;
        AddHistory(oldStatus, newStatus, reason, actorUserId, nowUtc);
    }

    private void AddHistory(BookingStatus from, BookingStatus to, string reason, string actorUserId, DateTime timestampUtc)
    {
        _history.Add(new BookingStatusHistory
        {
            BookingId = Id,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            ChangedByUserId = actorUserId,
            ChangedAtUtc = timestampUtc
        });
    }
}
