using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.CancelBooking;

public sealed record CancelBookingCommand(
    Guid BookingId,
    Guid PlayerId,
    string? Reason = null
) : ICommand<Result>;

public sealed record CancelPreviewResponse(
    bool CanCancel,
    DateTime? CancellationDeadlineUtc,
    decimal RefundPercentage,
    decimal EstimatedRefundAmount,
    string Currency,
    string? PolicySummary);
