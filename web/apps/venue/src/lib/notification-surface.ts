/**
 * The notifications this dashboard shows. One account can also be a player, so the API is asked for the venue
 * dashboard's own notifications (payment proofs, venue status, staff, subscription) plus the account-wide ones
 * (security, support), not everything the account ever received. 0 = Player, 1 = Venue, 2 = Admin.
 */
export const VENUE_SURFACE = 1 as const;
