using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

public static class BookingErrors
{
    public static readonly Error NotFound = Error.NotFound("Booking.NotFound", "The booking was not found.");
    public static readonly Error InvalidTransition = Error.Conflict("Booking.InvalidTransition", "The requested booking state transition is invalid.");
    public static readonly Error ReasonRequired = Error.Validation("Booking.ReasonRequired", "A reason is mandatory for this state change.");
    public static readonly Error SlotOverlap = Error.Conflict("Booking.SlotOverlap", "The selected pitch time slot is already booked or held.");
    public static readonly Error ReferenceUnavailable = Error.Failure("Booking.ReferenceUnavailable", "The booking could not be created right now. Please try again.");
    public static readonly Error NoRefundDue = Error.Conflict("Refund.NoneDue", "No refund is due for this booking: only a cancelled booking with a submitted deposit has one.");
    public static readonly Error RefundAlreadyDecided = Error.Conflict("Refund.AlreadyDecided", "This refund has already been recorded or denied and cannot be changed.");
    public static readonly Error RefundAmountInvalid = Error.Validation("Refund.InvalidAmount", "The refunded amount must be greater than zero and cannot exceed the deposit.");
    public static readonly Error RefundNotesRequired = Error.Validation("Refund.NotesRequired", "Record how the refund was paid (for example the transfer reference).");
    public static readonly Error HoldExpired = Error.Conflict("Booking.HoldExpired", "The temporary hold on this booking slot has expired.");
    public static readonly Error CannotCancelPastBooking = Error.Conflict("Booking.CannotCancelPastBooking", "Cannot cancel a booking that has already started or completed.");
    public static readonly Error CancellationDeadlinePassed = Error.Conflict("Booking.CancellationDeadlinePassed", "The cancellation deadline for this booking has passed under venue policy.");
    public static readonly Error CannotCompleteBeforeEnd = Error.Conflict("Booking.CannotCompleteBeforeEnd", "Cannot complete a booking before its scheduled end time.");
    public static readonly Error CannotMarkNoShowBeforeStart = Error.Conflict("Booking.CannotMarkNoShowBeforeStart", "Cannot mark a booking as no-show before its scheduled kickoff time.");
    public static readonly Error MaxPendingBookingsExceeded = Error.Conflict("Booking.MaxPendingBookingsExceeded", "You already have the maximum allowed number (2) of pending booking holds.");
    public static readonly Error DuplicateProof = Error.Conflict("Booking.DuplicateProof", "This payment receipt has already been submitted for another booking.");
    public static readonly Error ProofNotFound = Error.NotFound("Booking.ProofNotFound", "The payment proof was not found.");
    public static readonly Error UnauthorizedAccess = Error.Unauthorized("Booking.Unauthorized", "You are not authorized to access or modify this booking.");
    public static readonly Error InvalidProofFile = Error.Validation("Booking.InvalidProofFile", "The uploaded file is invalid, exceeds 5MB, or is not an allowed image/PDF.");
    public static readonly Error VirusDetected = Error.Validation("Booking.VirusDetected", "Security scanner flagged the uploaded payment receipt as suspicious or infected.");
    public static readonly Error RejectionReasonRequired = ReasonRequired;
    public static readonly Error CancellationWindowClosed = CancellationDeadlinePassed;
}
