using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

public sealed class Booking : Entity<Guid>
{
    private readonly List<BookingStatusHistory> _history = [];

    public string BookingReference { get; private set; } = string.Empty;
    public Guid PitchId { get; private set; }
    public Guid PlayerId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public Money TotalPrice { get; private set; } = default!;
    public Money DepositAmount { get; private set; } = default!;
    public BookingStatus Status { get; private set; }
    public DateTime? HoldExpiresAtUtc { get; private set; }
    public IReadOnlyCollection<BookingStatusHistory> History => _history.AsReadOnly();

    private Booking()
    {
    }

    public static Result<Booking> Create(
        Guid id,
        string bookingReference,
        Guid pitchId,
        Guid playerId,
        DateTime startUtc,
        DateTime endUtc,
        Money totalPrice,
        Money depositAmount,
        DateTime holdExpiresAtUtc,
        DateTime nowUtc,
        string actorUserId)
    {
        if (startUtc >= endUtc)
        {
            return Result.Failure<Booking>(Error.Validation("Booking.InvalidTimeRange", "Start time must precede end time."));
        }

        var booking = new Booking
        {
            Id = id,
            BookingReference = bookingReference,
            PitchId = pitchId,
            PlayerId = playerId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            TotalPrice = totalPrice,
            DepositAmount = depositAmount,
            Status = BookingStatus.PendingDeposit,
            HoldExpiresAtUtc = holdExpiresAtUtc,
            CreatedAtUtc = nowUtc
        };

        booking.AddHistory(BookingStatus.Draft, BookingStatus.PendingDeposit, "Booking created", actorUserId, nowUtc);
        return Result.Success(booking);
    }

    public Result SubmitProof(string actorUserId, DateTime nowUtc)
    {
        if (Status != BookingStatus.PendingDeposit)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        if (HoldExpiresAtUtc.HasValue && nowUtc > HoldExpiresAtUtc.Value)
        {
            return Result.Failure(BookingErrors.HoldExpired);
        }

        TransitionTo(BookingStatus.ProofSubmitted, "Payment proof uploaded by player", actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkUnderReview(string actorUserId, DateTime nowUtc)
    {
        if (Status != BookingStatus.ProofSubmitted)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        TransitionTo(BookingStatus.UnderReview, "Venue staff opened proof for review", actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Confirm(string actorUserId, DateTime nowUtc)
    {
        if (Status != BookingStatus.ProofSubmitted && Status != BookingStatus.UnderReview)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        TransitionTo(BookingStatus.Confirmed, "Payment verified and booking confirmed by venue staff", actorUserId, nowUtc);
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

        TransitionTo(BookingStatus.Rejected, reason, actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Expire(DateTime nowUtc)
    {
        if (Status != BookingStatus.PendingDeposit)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        TransitionTo(BookingStatus.Expired, "Hold duration elapsed without proof submission", "System", nowUtc);
        return Result.Success();
    }

    public Result Cancel(string reason, string actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BookingErrors.ReasonRequired);
        }

        if (Status != BookingStatus.Confirmed && Status != BookingStatus.PendingDeposit)
        {
            return Result.Failure(BookingErrors.InvalidTransition);
        }

        TransitionTo(BookingStatus.Cancelled, reason, actorUserId, nowUtc);
        return Result.Success();
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
