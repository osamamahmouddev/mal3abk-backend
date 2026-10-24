using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

public static class BookingErrors
{
    public static readonly Error NotFound = Error.NotFound("Booking.NotFound", "The booking was not found.");
    public static readonly Error InvalidTransition = Error.Conflict("Booking.InvalidTransition", "The requested booking state transition is invalid.");
    public static readonly Error ReasonRequired = Error.Validation("Booking.ReasonRequired", "A reason is mandatory for this state change.");
    public static readonly Error SlotOverlap = Error.Conflict("Booking.SlotOverlap", "The selected pitch time slot is already booked or held.");
    public static readonly Error HoldExpired = Error.Conflict("Booking.HoldExpired", "The temporary hold on this booking slot has expired.");
}
