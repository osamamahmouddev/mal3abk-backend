namespace Malabak.SharedKernel.Contracts;

public enum CommissionType
{
    PlatformDefault = 0,
    PerHourFixed = 1,
    Percentage = 2,
    Exempt = 3
}

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
    DateTime QuotedAtUtc,
    decimal NetPitchPrice = 0m,
    decimal PlatformCommission = 0m,
    decimal OnlineDepositAmount = 0m,
    decimal VenuePayAmount = 0m)
{
    public decimal NetPitchPrice { get; init; } = NetPitchPrice != 0m ? NetPitchPrice : Subtotal - Discount;
    public decimal PlatformCommission { get; init; } = PlatformCommission;
    public decimal OnlineDepositAmount { get; init; } = OnlineDepositAmount != 0m ? OnlineDepositAmount : Deposit;
    public decimal VenuePayAmount { get; init; } = VenuePayAmount != 0m ? VenuePayAmount : (Total - Deposit);
}

/// <summary>Venues-module contract: the only place prices are calculated. Clients never send prices.</summary>
public interface IPriceQuoteProvider
{
    Task<Result<PriceQuoteSnapshot>> QuoteAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Venues-module contract the Booking module calls when a player submits a booking: verifies the quote token issued by
/// the quote endpoint (signature, owner, slot, expiry), re-checks the slot, and re-prices it. A changed price fails with
/// <c>Quote.PriceChanged</c> so the player confirms the new price instead of being charged silently.
/// </summary>
public interface IBookingQuoteValidator
{
    Task<Result<PriceQuoteSnapshot>> ValidateAsync(
        string quoteToken, Guid playerId, Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
