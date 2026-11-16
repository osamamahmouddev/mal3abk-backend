using System.Globalization;
using System.Text.Json;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Quotes;

/// <summary>Signs and verifies quote tokens (HMAC with a secret key, implemented by the host).</summary>
public interface IQuoteTokenSigner
{
    string Protect(byte[] payload);

    /// <summary>The payload when the signature is valid; otherwise null.</summary>
    byte[]? Unprotect(string token);
}

public sealed record BookingQuoteDto(
    string QuoteToken,
    DateTime ExpiresAtUtc,
    string Currency,
    decimal Subtotal,
    decimal Discount,
    decimal Fees,
    decimal Total,
    decimal Deposit,
    decimal RemainingBalance,
    bool SubjectToConfirmation,
    string ConfirmationNote,
    IReadOnlyList<string> PolicySummary,
    BookingPolicySnapshot? Policy,
    PriceQuoteSnapshot Breakdown,
    decimal NetPitchPrice = 0m,
    decimal PlatformCommission = 0m,
    decimal OnlineDepositAmount = 0m,
    decimal VenuePayAmount = 0m)
{
    public decimal NetPitchPrice { get; init; } = NetPitchPrice != 0m ? NetPitchPrice : Breakdown.NetPitchPrice;
    public decimal PlatformCommission { get; init; } = PlatformCommission != 0m ? PlatformCommission : Breakdown.PlatformCommission;
    public decimal OnlineDepositAmount { get; init; } = OnlineDepositAmount != 0m ? OnlineDepositAmount : Breakdown.OnlineDepositAmount;
    public decimal VenuePayAmount { get; init; } = VenuePayAmount != 0m ? VenuePayAmount : Breakdown.VenuePayAmount;
}

public static class QuoteErrors
{
    public static readonly Error SlotUnavailable = new("Quote.SlotUnavailable", "This slot is already booked, held or blocked. Choose another time.");
    public static readonly Error InvalidToken = new("Quote.InvalidToken", "The quote is invalid or does not match this booking. Request a new quote.");
    public static readonly Error Expired = new("Quote.Expired", "The quote expired. Request a new quote.");
    public static readonly Error PriceChanged = new("Quote.PriceChanged", "The price changed since your quote. Review the new quote before booking.");
}

/// <summary>
/// Booking quote (TASK-305): server-side price, deposit, remaining balance and policy summary for a slot, plus a signed
/// quote token (15 minutes) that booking creation validates. Prices, availability and policy are always re-checked on
/// submit; the token only proves what the player saw and agreed to.
/// </summary>
public sealed class BookingQuoteService : IBookingQuoteValidator
{
    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly VenuesDbContext _dbContext;
    private readonly IPriceQuoteProvider _pricing;
    private readonly IPitchAvailabilityChecker _rules;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IBookingPolicyProvider _policies;
    private readonly IQuoteTokenSigner _signer;
    private readonly IClock _clock;

    public BookingQuoteService(
        VenuesDbContext dbContext,
        IPriceQuoteProvider pricing,
        IPitchAvailabilityChecker rules,
        IUpcomingBookingsReader bookings,
        IBookingPolicyProvider policies,
        IQuoteTokenSigner signer,
        IClock clock)
    {
        _dbContext = dbContext;
        _pricing = pricing;
        _rules = rules;
        _bookings = bookings;
        _policies = policies;
        _signer = signer;
        _clock = clock;
    }

    public async Task<Result<BookingQuoteDto>> QuoteAsync(Guid playerId, Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var check = await CheckSlotAsync(pitchId, startUtc, endUtc, cancellationToken);
        if (check is not null)
        {
            return Result.Failure<BookingQuoteDto>(check);
        }

        var price = await _pricing.QuoteAsync(pitchId, startUtc, endUtc, cancellationToken);
        if (price.IsFailure)
        {
            return Result.Failure<BookingQuoteDto>(price.Error);
        }

        var quote = price.Value;
        var policy = await _policies.GetCurrentSnapshotAsync(quote.VenueId, cancellationToken);
        var expiresAt = _clock.UtcNow + QuoteLifetime;
        var payload = new QuoteTokenPayload(1, playerId, pitchId, startUtc, endUtc, quote.Total, quote.Deposit, quote.Currency, policy?.Version, expiresAt);
        var token = _signer.Protect(JsonSerializer.SerializeToUtf8Bytes(payload, _json));

        return Result.Success(new BookingQuoteDto(
            token, expiresAt, quote.Currency, quote.Subtotal, quote.Discount, Fees: 0m, quote.Total, quote.Deposit, quote.Total - quote.Deposit,
            SubjectToConfirmation: true,
            ConfirmationNote: policy is null
                ? "The venue confirms the booking after reviewing your deposit transfer."
                : string.Create(CultureInfo.InvariantCulture,
                    $"Your slot is held after you start the booking. Upload the deposit proof within {policy.ProofSubmissionWindowMinutes} minutes; the venue reviews it within {policy.ManualReviewWindowMinutes} minutes."),
            PolicySummary(policy), policy, quote,
            quote.NetPitchPrice, quote.PlatformCommission, quote.OnlineDepositAmount, quote.VenuePayAmount));
    }

    public async Task<Result<PriceQuoteSnapshot>> ValidateAsync(
        string quoteToken, Guid playerId, Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var raw = string.IsNullOrWhiteSpace(quoteToken) ? null : _signer.Unprotect(quoteToken);
        QuoteTokenPayload? payload = null;
        try
        {
            payload = raw is null ? null : JsonSerializer.Deserialize<QuoteTokenPayload>(raw, _json);
        }
        catch (JsonException)
        {
            // treated as an invalid token below
        }

        if (payload is null || payload.V != 1 || payload.PlayerId != playerId || payload.PitchId != pitchId
            || payload.StartUtc != startUtc || payload.EndUtc != endUtc)
        {
            return Result.Failure<PriceQuoteSnapshot>(QuoteErrors.InvalidToken);
        }

        if (payload.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Result.Failure<PriceQuoteSnapshot>(QuoteErrors.Expired);
        }

        var check = await CheckSlotAsync(pitchId, startUtc, endUtc, cancellationToken);
        if (check is not null)
        {
            return Result.Failure<PriceQuoteSnapshot>(check);
        }

        var current = await _pricing.QuoteAsync(pitchId, startUtc, endUtc, cancellationToken);
        if (current.IsFailure)
        {
            return current;
        }

        var policy = await _policies.GetCurrentSnapshotAsync(current.Value.VenueId, cancellationToken);
        var changed = current.Value.Total != payload.Total || current.Value.Deposit != payload.Deposit
            || current.Value.Currency != payload.Currency || policy?.Version != payload.PolicyVersion;
        return changed ? Result.Failure<PriceQuoteSnapshot>(QuoteErrors.PriceChanged) : current;
    }

    /// <summary>Hours, increments, notice and advance window, then bookings, live holds and venue blocks.</summary>
    private async Task<Error?> CheckSlotAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var rules = await _rules.CheckSlotAsync(pitchId, startUtc, endUtc, cancellationToken);
        if (rules.IsFailure)
        {
            return rules.Error;
        }

        var nowUtc = _clock.UtcNow;
        var bookings = await _bookings.GetActiveAsync([pitchId], startUtc, endUtc, cancellationToken);
        if (bookings.Any(b => b.HoldExpiresAtUtc is null || b.HoldExpiresAtUtc > nowUtc))
        {
            return QuoteErrors.SlotUnavailable;
        }

        var blocked = await _dbContext.BlockedSlots.AsNoTracking()
            .AnyAsync(b => b.PitchId == pitchId && !b.IsDeleted && b.StartUtc < endUtc && b.EndUtc > startUtc, cancellationToken);
        return blocked ? QuoteErrors.SlotUnavailable : null;
    }

    private static List<string> PolicySummary(BookingPolicySnapshot? policy)
    {
        if (policy is null)
        {
            return ["The venue has not published a cancellation policy yet."];
        }

        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Free cancellation up to {policy.CancellationDeadlineHours} hours before the start time."),
            policy.RefundEligibility switch
            {
                nameof(RefundEligibility.FullRefund) => "Cancelling in time refunds the full deposit.",
                nameof(RefundEligibility.PartialRefund) => string.Create(CultureInfo.InvariantCulture, $"Cancelling in time refunds {policy.RefundPercentage}% of the deposit."),
                _ => "The deposit is not refundable."
            },
            policy.NoShowRule switch
            {
                nameof(NoShowRule.ForfeitFullAmount) => "If you don't show up, the full booking amount is due.",
                nameof(NoShowRule.NoPenalty) => "No penalty for no-shows.",
                _ => "If you don't show up, you lose the deposit."
            },
            string.Create(CultureInfo.InvariantCulture, $"Late arrival grace period: {policy.LateArrivalGraceMinutes} minutes.")
        };
        if (policy.AllowReschedule)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Rescheduling allowed up to {policy.RescheduleDeadlineHours} hours before the start time."));
        }

        return lines;
    }

    private sealed record QuoteTokenPayload(
        int V, Guid PlayerId, Guid PitchId, DateTime StartUtc, DateTime EndUtc, decimal Total, decimal Deposit, string Currency, int? PolicyVersion, DateTime ExpiresAtUtc);
}
