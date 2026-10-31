using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.ReviewProof;

public sealed record ReviewProofCommand(
    Guid BookingId,
    Guid VenueId,
    Guid ActorUserId,
    bool Approved,
    string? RejectionReason = null
) : ICommand<Result>;
