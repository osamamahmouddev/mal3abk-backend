/**
 * Where a notification takes the venue dashboard's user. The API sends app deep links ("malabak://booking/{id}") meant for
 * the phone; here they are turned into the dashboard page that handles the same thing, or null when there is none.
 */
export function notificationTarget(notification: { type: string; deepLink?: string | null }): string | null {
  const { type } = notification;

  // Deposits are paid to the platform and reviewed by its team; the venue just follows the booking.
  if (type === "payment.proof_submitted") return "/reservations";
  if (type === "dispute.opened" || type === "dispute.resolved") return "/reservations";
  if (type.startsWith("booking.")) return "/reservations";
  if (type === "staff.invitation_accepted") return "/staff";
  if (type === "payout.recorded") return "/payouts";
  // Subscriptions are retired (commission model); old subscription and support notices just open the overview.
  if (type.startsWith("subscription.") || type.startsWith("support.")) return "/overview";
  if (type === "venue.info_requested" || type === "venue.rejected") return "/setup";
  if (type === "venue.approved") return "/overview";
  if (type.startsWith("venue.")) return "/settings";
  if (type.startsWith("security.")) return "/settings";
  return null;
}
