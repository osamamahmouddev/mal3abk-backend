using Malabak.SharedKernel;

namespace Malabak.Modules.Payments.Domain;

public enum PaymentTransactionStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

public sealed class PaymentTransaction : Entity<Guid>
{
    public Guid BookingId { get; private set; }
    public Guid PlayerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Provider { get; private set; } = "Paymob";
    public string? ProviderTransactionId { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public PaymentTransactionStatus Status { get; private set; }
    public string? PaymentMethod { get; private set; }
    public string? RawWebhookPayload { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private PaymentTransaction()
    {
    }

    public static PaymentTransaction Create(
        Guid id,
        Guid bookingId,
        Guid playerId,
        decimal amount,
        string currency,
        string provider,
        string? providerOrderId,
        DateTime createdAtUtc)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return new PaymentTransaction
        {
            Id = id,
            BookingId = bookingId,
            PlayerId = playerId,
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            Provider = provider.Trim(),
            ProviderOrderId = providerOrderId?.Trim(),
            Status = PaymentTransactionStatus.Pending,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void MarkSucceeded(string? providerTransactionId, string? paymentMethod, string? rawWebhookPayload, DateTime completedAtUtc)
    {
        if (Status == PaymentTransactionStatus.Succeeded)
        {
            return;
        }

        Status = PaymentTransactionStatus.Succeeded;
        ProviderTransactionId = providerTransactionId?.Trim() ?? ProviderTransactionId;
        PaymentMethod = paymentMethod?.Trim() ?? PaymentMethod;
        RawWebhookPayload = rawWebhookPayload;
        CompletedAtUtc = completedAtUtc;
        UpdatedAtUtc = completedAtUtc;
    }

    public void MarkFailed(string? providerTransactionId, string? rawWebhookPayload, DateTime completedAtUtc)
    {
        if (Status == PaymentTransactionStatus.Succeeded)
        {
            // Do not overwrite an already succeeded transaction
            return;
        }

        Status = PaymentTransactionStatus.Failed;
        ProviderTransactionId = providerTransactionId?.Trim() ?? ProviderTransactionId;
        RawWebhookPayload = rawWebhookPayload;
        CompletedAtUtc = completedAtUtc;
        UpdatedAtUtc = completedAtUtc;
    }
}
