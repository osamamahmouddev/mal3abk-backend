using Malabak.SharedKernel;

namespace Malabak.Modules.Payments.Application.Services;

public sealed record PaymentCheckoutDto(
    string ClientSecret,
    string CheckoutUrl,
    decimal Amount,
    string Currency,
    bool RequiresRedirect);

public interface IPaymentService
{
    Task<Result<PaymentCheckoutDto>> InitiateBookingCommissionPaymentAsync(
        Guid bookingId, Guid playerId, string? firstName, string? lastName, string? phone, string? email, CancellationToken cancellationToken = default);

    Task<Result> ProcessPaymobWebhookAsync(string rawBody, string? hmac, CancellationToken cancellationToken = default);
}
