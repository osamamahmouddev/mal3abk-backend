namespace Malabak.Modules.Booking.Domain;

public enum BookingStatus
{
    Draft = 1,
    PendingDeposit = 2,
    ProofSubmitted = 3,
    UnderReview = 4,
    Confirmed = 5,
    Rejected = 6,
    Expired = 7,
    Cancelled = 8,
    Completed = 9,
    NoShow = 10
}
