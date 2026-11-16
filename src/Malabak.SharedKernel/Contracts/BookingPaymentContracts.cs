namespace Malabak.SharedKernel.Contracts;

public sealed record PayableBookingDetails(
    Guid BookingId,
    Guid PlayerId,
    string BookingReference,
    decimal CommissionAmount,
    string Currency,
    string Status,
    bool IsCommissionPaid);

public interface IBookingPaymentDetailsProvider
{
    Task<Result<PayableBookingDetails>> GetPayableBookingAsync(Guid bookingId, Guid playerId, CancellationToken cancellationToken = default);
}

public sealed record ConfirmBookingCommissionRequest(
    Guid BookingId,
    string ProviderOrderId,
    string ProviderTransactionId,
    decimal AmountPaid,
    string Currency,
    DateTime PaidAtUtc);

public interface IBookingPaymentConfirmationContract
{
    Task<Result> ConfirmCommissionPaymentAsync(ConfirmBookingCommissionRequest request, CancellationToken cancellationToken = default);
}
