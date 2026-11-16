using System.Text.Json;
using Malabak.Modules.Payments.Domain;
using Malabak.Modules.Payments.Infrastructure;
using Malabak.Modules.Payments.Infrastructure.Paymob;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Payments.Application.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly PaymentsDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingPaymentDetailsProvider _bookingDetailsProvider;
    private readonly IBookingPaymentConfirmationContract _bookingConfirmationContract;
    private readonly IClock _clock;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        PaymentsDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingPaymentDetailsProvider bookingDetailsProvider,
        IBookingPaymentConfirmationContract bookingConfirmationContract,
        IClock clock,
        ILogger<PaymentService> logger)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _bookingDetailsProvider = bookingDetailsProvider;
        _bookingConfirmationContract = bookingConfirmationContract;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<PaymentCheckoutDto>> InitiateBookingCommissionPaymentAsync(
        Guid bookingId, Guid playerId, string? firstName, string? lastName, string? phone, string? email, CancellationToken cancellationToken = default)
    {
        var bookingResult = await _bookingDetailsProvider.GetPayableBookingAsync(bookingId, playerId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return Result.Failure<PaymentCheckoutDto>(bookingResult.Error);
        }

        var booking = bookingResult.Value;
        if (booking.IsCommissionPaid)
        {
            return Result.Failure<PaymentCheckoutDto>(Error.Conflict("Payment.AlreadyPaid", "Commission for this booking has already been paid."));
        }

        var nowUtc = _clock.UtcNow;

        // If commission is zero (e.g. exempt venue or promotional zero-commission), confirm immediately
        if (booking.CommissionAmount <= 0)
        {
            await _bookingConfirmationContract.ConfirmCommissionPaymentAsync(new ConfirmBookingCommissionRequest(
                booking.BookingId,
                "EXEMPT",
                "EXEMPT",
                0m,
                booking.Currency,
                nowUtc), cancellationToken);

            return Result.Success(new PaymentCheckoutDto(
                string.Empty,
                string.Empty,
                0m,
                booking.Currency,
                RequiresRedirect: false));
        }

        // Call Paymob Intention API
        var intentionRequest = new PaymobIntentionRequest(
            booking.CommissionAmount,
            booking.Currency,
            booking.BookingId,
            booking.BookingReference,
            firstName ?? "Malabak",
            lastName ?? "Player",
            phone ?? "+201000000000",
            email ?? "player@malabak.app");

        var intentionResult = await _paymobClient.CreateIntentionAsync(intentionRequest, cancellationToken);
        if (intentionResult.IsFailure)
        {
            return Result.Failure<PaymentCheckoutDto>(intentionResult.Error);
        }

        var intention = intentionResult.Value;

        // Record pending transaction
        var transaction = PaymentTransaction.Create(
            IdGenerator.NewGuid(),
            booking.BookingId,
            booking.PlayerId,
            booking.CommissionAmount,
            booking.Currency,
            "Paymob",
            intention.OrderId ?? intention.IntentionId,
            nowUtc);

        _dbContext.Transactions.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new PaymentCheckoutDto(
            intention.ClientSecret,
            intention.CheckoutUrl,
            booking.CommissionAmount,
            booking.Currency,
            RequiresRedirect: true));
    }

    public async Task<Result> ProcessPaymobWebhookAsync(string rawBody, string? hmac, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Result.Failure(Error.Validation("Payment.EmptyPayload", "Webhook body is empty."));
        }

        // HMAC verification - deny by default if missing or invalid
        if (string.IsNullOrWhiteSpace(hmac))
        {
            _logger.LogWarning("Paymob webhook rejected: Missing HMAC signature.");
            return Result.Failure(Error.Unauthorized("Payment.MissingSignature", "Webhook signature is required."));
        }

        if (!_paymobClient.VerifyHmac(rawBody, hmac))
        {
            _logger.LogWarning("Paymob webhook HMAC validation failed.");
            return Result.Failure(Error.Unauthorized("Payment.InvalidHmac", "Invalid webhook signature."));
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            // Paymob webhook contains either "obj" (transaction callback) or root-level fields
            var data = root.TryGetProperty("obj", out var objElem) ? objElem : root;

            var success = false;
            if (data.TryGetProperty("success", out var successProp))
            {
                success = successProp.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(successProp.GetString(), out var s) && s,
                    _ => false
                };
            }

            string? transactionId = null;
            if (data.TryGetProperty("id", out var idProp))
            {
                transactionId = idProp.ToString();
            }

            string? orderId = null;
            if (data.TryGetProperty("order", out var orderProp) && orderProp.TryGetProperty("id", out var orderIdProp))
            {
                orderId = orderIdProp.ToString();
            }

            // Extract booking ID from extras if present
            Guid? bookingId = null;
            if (data.TryGetProperty("order", out var o) && o.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                // check items
            }

            if (data.TryGetProperty("extras", out var extrasProp) &&
                extrasProp.TryGetProperty("booking_id", out var bidProp) &&
                Guid.TryParse(bidProp.GetString(), out var bid))
            {
                bookingId = bid;
            }

            decimal? paidAmount = null;
            if (data.TryGetProperty("amount_cents", out var acProp))
            {
                if (acProp.TryGetInt64(out var cents))
                {
                    paidAmount = cents / 100m;
                }
                else if (acProp.TryGetDecimal(out var centsDec))
                {
                    paidAmount = centsDec / 100m;
                }
            }

            var nowUtc = _clock.UtcNow;

            // Find transaction by order id or transaction id or booking id
            PaymentTransaction? transaction = null;
            if (orderId is not null)
            {
                transaction = await _dbContext.Transactions
                    .FirstOrDefaultAsync(t => t.ProviderOrderId == orderId, cancellationToken);
            }

            if (transaction is null && bookingId.HasValue)
            {
                transaction = await _dbContext.Transactions
                    .Where(t => t.BookingId == bookingId.Value && t.Status == PaymentTransactionStatus.Pending)
                    .OrderByDescending(t => t.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var targetBookingId = bookingId ?? transaction?.BookingId;
            if (targetBookingId is null)
            {
                _logger.LogWarning("Webhook received but no matching booking or transaction found for order {OrderId}", orderId);
                return Result.Failure(Error.NotFound("Payment.TransactionNotFound", "No matching payment transaction found."));
            }

            string? method = null;
            if (data.TryGetProperty("source_data", out var sd) && sd.TryGetProperty("type", out var typeProp))
            {
                method = typeProp.GetString();
            }

            // Sanitize audit payload to avoid storing unnecessary customer/card PII
            var sanitizedAuditPayload = JsonSerializer.Serialize(new
            {
                transaction_id = transactionId,
                order_id = orderId,
                success,
                amount = paidAmount ?? transaction?.Amount ?? 0m,
                method
            });

            if (success)
            {
                if (transaction is not null && paidAmount.HasValue && paidAmount.Value < transaction.Amount)
                {
                    _logger.LogWarning(
                        "Paymob reported amount {ReportedAmount} is less than transaction expected amount {ExpectedAmount} for order {OrderId}",
                        paidAmount.Value, transaction.Amount, orderId);
                    return Result.Failure(Error.Validation("Payment.InsufficientAmount", "Paid amount is less than expected amount."));
                }

                if (transaction is not null)
                {
                    transaction.MarkSucceeded(transactionId, method, sanitizedAuditPayload, nowUtc);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                var amount = paidAmount ?? transaction?.Amount ?? 0m;
                var currency = transaction?.Currency ?? "EGP";

                var confirmResult = await _bookingConfirmationContract.ConfirmCommissionPaymentAsync(new ConfirmBookingCommissionRequest(
                    targetBookingId.Value,
                    orderId ?? "UNKNOWN",
                    transactionId ?? "UNKNOWN",
                    amount,
                    currency,
                    nowUtc), cancellationToken);

                if (confirmResult.IsFailure)
                {
                    _logger.LogWarning("Failed to confirm booking {BookingId} payment: {Error}", targetBookingId.Value, confirmResult.Error);
                    return confirmResult;
                }

                _logger.LogInformation("Successfully confirmed online commission payment for booking {BookingId}", targetBookingId.Value);
            }
            else
            {
                if (transaction is not null)
                {
                    transaction.MarkFailed(transactionId, sanitizedAuditPayload, nowUtc);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                _logger.LogWarning("Payment failed for transaction {TransactionId}, order {OrderId}", transactionId, orderId);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Paymob webhook");
            return Result.Failure(Error.Failure("Payment.WebhookProcessingError", "Failed to process webhook."));
        }
    }
}
