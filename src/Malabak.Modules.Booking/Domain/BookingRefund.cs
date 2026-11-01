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

    public void RecordManualRefund(
        decimal actualAmount,
        string notes,
        string recordedByUserId,
        DateTime nowUtc)
    {
        Status = RefundStatus.Refunded;
        ActualRefundAmount = actualAmount;
        Notes = notes;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void DenyRefund(
        string reason,
        string deniedByUserId,
        DateTime nowUtc)
    {
        Status = RefundStatus.Denied;
        Notes = reason;
        RecordedByUserId = deniedByUserId;
        RecordedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
