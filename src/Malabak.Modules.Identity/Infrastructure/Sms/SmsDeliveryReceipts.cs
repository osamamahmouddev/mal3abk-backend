using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Sms;

public enum DeliveryReceiptResult
{
    Applied = 0,
    InvalidSignature = 1,
    UnknownAccount = 2,
    UnknownMessage = 3
}

/// <summary>Verifies and applies provider delivery receipts (only signed requests from a configured account are trusted).</summary>
public sealed class SmsDeliveryReceipts
{
    private readonly IdentityDbContext _dbContext;
    private readonly SmsOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<SmsDeliveryReceipts> _logger;

    public SmsDeliveryReceipts(IdentityDbContext dbContext, IOptions<SmsOptions> options, IClock clock, ILogger<SmsDeliveryReceipts> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DeliveryReceiptResult> ApplyTwilioAsync(
        string accountName, IReadOnlyDictionary<string, string> form, string? signature, CancellationToken cancellationToken = default)
    {
        if (!_options.Twilio.TryGetValue(accountName, out var account) || string.IsNullOrWhiteSpace(account.StatusCallbackUrl))
        {
            return DeliveryReceiptResult.UnknownAccount;
        }

        // Signed over the exact public callback URL we configured, not whatever host header the request carried.
        if (!TwilioSmsProvider.IsValidSignature(account.AuthToken, account.StatusCallbackUrl, form, signature))
        {
            _logger.LogWarning("Rejected SMS delivery receipt with an invalid signature for account {Account}", accountName);
            return DeliveryReceiptResult.InvalidSignature;
        }

        if (!form.TryGetValue("MessageSid", out var messageId) || !form.TryGetValue("MessageStatus", out var status))
        {
            return DeliveryReceiptResult.UnknownMessage;
        }

        var dispatch = await _dbContext.SmsDispatches.FirstOrDefaultAsync(d => d.ProviderMessageId == messageId, cancellationToken);
        if (dispatch is null)
        {
            return DeliveryReceiptResult.UnknownMessage;
        }

        dispatch.ApplyDeliveryStatus(MapTwilioStatus(status), _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return DeliveryReceiptResult.Applied;
    }

    internal static SmsDeliveryStatus MapTwilioStatus(string status) => status.ToLowerInvariant() switch
    {
        "delivered" or "read" => SmsDeliveryStatus.Delivered,
        "failed" or "undelivered" => SmsDeliveryStatus.Failed,
        "queued" or "accepted" or "scheduled" => SmsDeliveryStatus.Queued,
        _ => SmsDeliveryStatus.Sent
    };
}
