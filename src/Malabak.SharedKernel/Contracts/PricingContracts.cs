namespace Malabak.SharedKernel.Contracts;

public sealed record PriceLineSnapshot(DateTime StartUtc, DateTime EndUtc, decimal HourlyRate, decimal Amount, IReadOnlyList<string> AppliedRules);

/// <summary>
/// A server-calculated price for one pitch and time range. The Booking module stores the total and deposit it receives
/// here as the agreed price; later rule changes never alter an existing booking.
/// </summary>
public sealed record PriceQuoteSnapshot(
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    int DurationMinutes,
    string Currency,
    decimal Subtotal,
    decimal Discount,
    string? DiscountRule,
    decimal Total,
    decimal Deposit,
    string DepositType,
    IReadOnlyList<PriceLineSnapshot> Lines,
    DateTime QuotedAtUtc);

/// <summary>Venues-module contract: the only place prices are calculated. Clients never send prices.</summary>
public interface IPriceQuoteProvider
{
    Task<Result<PriceQuoteSnapshot>> QuoteAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
