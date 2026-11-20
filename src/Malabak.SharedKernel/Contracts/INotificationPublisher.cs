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
    public const string VenueSuspended = "venue.suspended";
    public const string VenueReactivated = "venue.reactivated";
    public const string VenueOwnershipReceived = "venue.ownership_received";
    public const string VenueInfoRequested = "venue.info_requested";
    public const string StaffInvitationAccepted = "staff.invitation_accepted";
    public const string NewDeviceSignIn = "security.new_device";
    public const string ImpersonationStarted = "security.impersonation_started";
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
    public const string TeamSuspended = "team.suspended";
    public const string TeamReactivated = "team.reactivated";
    public const string MatchResultCorrected = "match.result_corrected";
    public const string SubscriptionExpiring = "subscription.expiring";
    public const string SubscriptionGraceStarted = "subscription.grace_started";
    public const string SubscriptionExpired = "subscription.expired";
    public const string SubscriptionPaymentRecorded = "subscription.payment_recorded";
    public const string SubscriptionSuspended = "subscription.suspended";
    public const string SubscriptionRequestApproved = "subscription.request_approved";
    public const string SubscriptionRequestRejected = "subscription.request_rejected";
    public const string SubscriptionRequestInstructions = "subscription.request_instructions";
    public const string SubscriptionPaymentReported = "subscription.payment_reported";
    public const string SupportTicketReplied = "support.ticket_replied";
    public const string SupportTicketResolved = "support.ticket_resolved";
    public const string PaymentDisputeOpened = "dispute.opened";
    public const string PaymentDisputeResolved = "dispute.resolved";
    public const string VenuePayoutRecorded = "payout.recorded";
}
