/** Venue-scoped permission names, as the API grants them per role (see Permissions.cs). */
export const P = {
  venueView: "venue.view",
  venueEdit: "venue.edit",
  venuePublish: "venue.publish",
  pitchesManage: "venue.pitches.manage",
  schedulesManage: "venue.schedules.manage",
  blockedSlotsManage: "venue.blocked_slots.manage",
  policiesManage: "venue.policies.manage",
  paymentMethodsManage: "venue.payment_methods.manage",
  bookingsView: "venue.bookings.view",
  bookingsApprove: "venue.bookings.approve",
  proofsReview: "venue.payment_proofs.review",
  customersView: "venue.customers.view",
  reportsView: "venue.reports.view",
  staffManage: "venue.staff.manage",
} as const;
