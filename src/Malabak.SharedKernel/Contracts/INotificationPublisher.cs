namespace Malabak.SharedKernel.Contracts;

/// <summary>
/// Raise a user notification from any module. The request is written to the Notifications outbox and turned into
/// an in-app notification (and later push) by the dispatcher, applying templates, preferences and dedupe.
/// </summary>
public interface INotificationPublisher
{
    Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Type">A value from <see cref="NotificationTypes"/>.</param>
/// <param name="Data">Template placeholders (no secrets or tokens — notifications are visible in the app).</param>
/// <param name="DedupeKey">Same key for the same user is delivered once (e.g. "venue-approved:{venueId}").</param>
public sealed record NotificationRequest(Guid UserId, string Type, IReadOnlyDictionary<string, string> Data, string DedupeKey);

public static class NotificationTypes
{
    public const string BookingConfirmed = "booking.confirmed";
    public const string BookingRejected = "booking.rejected";
    public const string BookingExpired = "booking.expired";
    public const string PaymentProofSubmitted = "payment.proof_submitted";
    public const string BookingReminder24h = "booking.reminder_24h";
    public const string BookingReminder2h = "booking.reminder_2h";
    public const string VenueApproved = "venue.approved";
    public const string VenueRejected = "venue.rejected";
    public const string VenueInfoRequested = "venue.info_requested";
    public const string StaffInvitationAccepted = "staff.invitation_accepted";
    public const string NewDeviceSignIn = "security.new_device";
    public const string TeamInvitationReceived = "team.invitation_received";
    public const string TeamChallengeReceived = "team.challenge_received";
    public const string TeamChallengeAccepted = "team.challenge_accepted";
    public const string TeamChallengeDeclined = "team.challenge_declined";
    public const string TeamChallengeCountered = "team.challenge_countered";
    public const string TeamRankingUpdated = "team.ranking_updated";
    public const string MatchScheduled = "match.scheduled";
    public const string MatchResultSubmitted = "match.result_submitted";
    public const string MatchResultConfirmed = "match.result_confirmed";
    public const string MatchResultDisputed = "match.result_disputed";
    public const string MatchDisputeResolved = "match.dispute_resolved";
}
