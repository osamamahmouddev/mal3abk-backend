/**
 * Where a notification takes the venue dashboard's user. The API sends app deep links ("malabak://booking/{id}") meant for
 * the phone; here they are turned into the dashboard page that handles the same thing, or null when there is none.
 */
export function notificationTarget(notification: { type: string; deepLink?: string | null }): string | null {
  const { type } = notification;
  const id = (notification.deepLink ?? "").match(/^malabak:\/\/[a-z-]+\/(?:venue\/|tickets\/)?([0-9a-f-]{36})/i)?.[1];

  // A payment proof waits for review: open that booking's proof.
  if (type === "payment.proof_submitted") return id ? `/reservations/proof-review?booking=${id}` : "/reservations/proof-review";
  if (type === "dispute.opened" || type === "dispute.resolved") return "/reservations";
  if (type.startsWith("booking.")) return "/reservations";
  if (type === "staff.invitation_accepted") return "/staff";
  if (type.startsWith("subscription.")) return "/subscription";
  // Support tickets are opened from the subscription page, so replies belong there.
  if (type.startsWith("support.")) return "/subscription";
  if (type === "venue.info_requested" || type === "venue.rejected") return "/setup";
  if (type === "venue.approved") return "/overview";
  if (type.startsWith("venue.")) return "/settings";
  if (type.startsWith("security.")) return "/settings";
  return null;
}
