namespace Malabak.SharedKernel.Contracts;

/// <summary>Where a player sends a booking's deposit, with the account details decrypted.</summary>
public sealed record PayableVenuePaymentMethod(Guid Id, string MethodType, string RecipientName, string AccountDetails, string? Instructions);

/// <summary>
/// Venues-module contract for revealing a venue's payment details to the player of a booking. The Booking module decides
/// whether the caller may pay; this only decrypts and audits.
/// </summary>
public interface IVenuePaymentDirectory
{
    /// <summary>The venue's active payment methods, decrypted. Every reveal is audited against the booking and player.</summary>
    Task<IReadOnlyList<PayableVenuePaymentMethod>> RevealForBookingAsync(Guid venueId, Guid bookingId, Guid playerId, CancellationToken cancellationToken = default);
}
