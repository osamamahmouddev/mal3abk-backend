namespace Malabak.SharedKernel.Contracts;

/// <summary>
/// Immutable copy of a venue's booking policy at the moment a booking is created (rule.md §5: later policy
/// edits must not change existing bookings). Booking stores this as-is; Venues produces it.
/// </summary>
public sealed record BookingPolicySnapshot(
    Guid PolicyId,
    Guid VenueId,
    int Version,
    string DepositType,
    decimal DepositValue,
    int ProofSubmissionWindowMinutes,
    int ManualReviewWindowMinutes,
    int CancellationDeadlineHours,
    string RefundEligibility,
    decimal RefundPercentage,
    string NoShowRule,
    int LateArrivalGraceMinutes,
    bool AllowReschedule,
    int RescheduleDeadlineHours,
    bool PendingBookingsBlockSlots,
    string? Terms,
    DateTime EffectiveFromUtc);

/// <summary>Venues-module contract the Booking module calls when creating a booking.</summary>
public interface IBookingPolicyProvider
{
    /// <summary>The policy in force for the venue, or null when the venue has not configured one.</summary>
    Task<BookingPolicySnapshot?> GetCurrentSnapshotAsync(Guid venueId, CancellationToken cancellationToken = default);
}
