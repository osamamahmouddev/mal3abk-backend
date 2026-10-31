using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

public enum RefundStatus
{
    None = 0,
    Pending = 1,
    Refunded = 2,
    Denied = 3
}

public sealed class BookingRefund : Entity<Guid>
{
    public Guid BookingId { get; private set; }
    public RefundStatus Status { get; private set; }
    public decimal ExpectedRefundAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal? ActualRefundAmount { get; private set; }
    public string? Reason { get; private set; }
    public string? PolicySnapshotApplied { get; private set; }
    public string? RecordedByUserId { get; private set; }
    public DateTime? RecordedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    private BookingRefund()
    {
    }

    public static BookingRefund CreatePending(
        Guid id,
        Guid bookingId,
        decimal expectedAmount,
        string currency,
        string? reason,
        string? policySnapshotApplied,
        DateTime nowUtc)
    {
        return new BookingRefund
        {
            Id = id,
            BookingId = bookingId,
            Status = expectedAmount > 0 ? RefundStatus.Pending : RefundStatus.None,
            ExpectedRefundAmount = expectedAmount,
            Currency = currency,
            Reason = reason,
            PolicySnapshotApplied = policySnapshotApplied,
            CreatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Records the refund the venue paid by hand. Only a pending refund can be recorded, once: a recorded or denied refund is a
    /// financial record and is never overwritten. The amount must be positive and no more than <paramref name="maxAmount"/> (the deposit).
    /// </summary>
    public Result RecordManualRefund(
        decimal actualAmount,
        decimal maxAmount,
        string notes,
        string recordedByUserId,
        DateTime nowUtc)
    {
        if (Status != RefundStatus.Pending)
        {
            return Result.Failure(Status == RefundStatus.None ? BookingErrors.NoRefundDue : BookingErrors.RefundAlreadyDecided);
        }

        if (actualAmount <= 0 || actualAmount > maxAmount)
        {
            return Result.Failure(BookingErrors.RefundAmountInvalid);
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            return Result.Failure(BookingErrors.RefundNotesRequired);
        }

        Status = RefundStatus.Refunded;
        ActualRefundAmount = actualAmount;
        Notes = notes.Trim();
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result DenyRefund(
        string reason,
        string deniedByUserId,
        DateTime nowUtc)
    {
        if (Status != RefundStatus.Pending)
        {
            return Result.Failure(Status == RefundStatus.None ? BookingErrors.NoRefundDue : BookingErrors.RefundAlreadyDecided);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BookingErrors.ReasonRequired);
        }

        Status = RefundStatus.Denied;
        Notes = reason.Trim();
        RecordedByUserId = deniedByUserId;
        RecordedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
