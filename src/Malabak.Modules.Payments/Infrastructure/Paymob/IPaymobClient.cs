using Malabak.SharedKernel;

namespace Malabak.Modules.Payments.Infrastructure.Paymob;

public sealed record PaymobIntentionRequest(
    decimal Amount,
    string Currency,
    Guid BookingId,
    string BookingReference,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string Email);

public sealed record PaymobIntentionResult(
    string ClientSecret,
    string CheckoutUrl,
    string? IntentionId,
    string? OrderId);

public interface IPaymobClient
{
    Task<Result<PaymobIntentionResult>> CreateIntentionAsync(PaymobIntentionRequest request, CancellationToken cancellationToken = default);
    bool VerifyHmac(string rawBody, string receivedHmac);
    bool VerifyCallbackHmac(IDictionary<string, string?> fields, string receivedHmac);
}
