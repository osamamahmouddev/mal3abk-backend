# Admin authorization matrix

TASK-719. Generated from the live endpoint graph by `AdminAuthorizationMatrixTests` (unit tests). To refresh it, run the tests with `ADMIN_MATRIX_OUT` set to a file path and paste the table here.

Rules enforced by those tests:

- **Deny by default.** Every `/api/v1/admin/*` endpoint requires a platform-staff permission (admin token audience). The only exceptions are the four sign-in steps.
- **No customer roles.** Player, VenueOwner and VenueStaff can reach none of these endpoints. Admin can reach all of them.
- **Least privilege.**
  - SupportAgent gets users (read), bookings (read), support tickets and impersonation.
  - Finance gets plans, subscriptions and invoices.
- **Step-up.** Destructive actions also need a fresh TOTP step-up token (`X-Step-Up-Token`, scope `admin_destructive`).
- **Impersonation tokens** (`act` claim) are refused on `/auth/*`, phone change, data export, account deletion, consents, session revocation, payment methods and every admin endpoint (`ImpersonationRestrictionMiddleware`).

| ✓ | allowed |
|---|---|
| — | refused (403) |

| Endpoint | Admin | SupportAgent | Finance | Step-up |
|---|---|---|---|---|
| `GET /api/v1/admin/accounts/` | ✓ | — | — |  |
| `POST /api/v1/admin/accounts/` | ✓ | — | — | ✓ |
| `POST /api/v1/admin/accounts/{accountId:guid}/disable` | ✓ | — | — | ✓ |
| `POST /api/v1/admin/accounts/{accountId:guid}/enable` | ✓ | — | — |  |
| `PUT /api/v1/admin/accounts/{accountId:guid}/roles` | ✓ | — | — | ✓ |
| `GET /api/v1/admin/audit-logs` | ✓ | — | — |  |
| `POST /api/v1/admin/auth/login` (sign-in) | ✓ | ✓ | ✓ |  |
| `POST /api/v1/admin/auth/logout` | ✓ | ✓ | ✓ |  |
| `POST /api/v1/admin/auth/refresh` (sign-in) | ✓ | ✓ | ✓ |  |
| `POST /api/v1/admin/auth/totp/confirm` (sign-in) | ✓ | ✓ | ✓ |  |
| `POST /api/v1/admin/auth/totp/enroll` (sign-in) | ✓ | ✓ | ✓ |  |
| `GET /api/v1/admin/bookings/` | ✓ | ✓ | — |  |
| `GET /api/v1/admin/bookings/{bookingId:guid}` | ✓ | ✓ | — |  |
| `POST /api/v1/admin/bookings/{bookingId:guid}/force-cancel` | ✓ | — | — | ✓ |
| `GET /api/v1/admin/dashboard` | ✓ | ✓ | — |  |
| `GET /api/v1/admin/disputes/` | ✓ | — | — |  |
| `GET /api/v1/admin/disputes/{disputeId:guid}` | ✓ | — | — |  |
| `POST /api/v1/admin/disputes/{disputeId:guid}/notes` | ✓ | — | — |  |
| `POST /api/v1/admin/disputes/{disputeId:guid}/resolve` | ✓ | — | — | ✓ |
| `POST /api/v1/admin/disputes/{disputeId:guid}/review` | ✓ | — | — |  |
| `GET /api/v1/admin/feature-flags` | ✓ | — | — |  |
| `PUT /api/v1/admin/feature-flags/{key}` | ✓ | — | — |  |
| `GET /api/v1/admin/featured-venues/` | ✓ | — | — |  |
| `POST /api/v1/admin/featured-venues/` | ✓ | — | — |  |
| `PUT /api/v1/admin/featured-venues/{placementId:guid}` | ✓ | — | — |  |
| `POST /api/v1/admin/featured-venues/{placementId:guid}/cancel` | ✓ | — | — |  |
| `POST /api/v1/admin/impersonate` | ✓ | ✓ | — | ✓ |
| `GET /api/v1/admin/invoices/` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/invoices/reconciliation` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/invoices/{invoiceId:guid}` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/invoices/{invoiceId:guid}/pdf` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/invoices/{invoiceId:guid}/void` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/moderation/matches/disputes` | ✓ | — | — |  |
| `POST /api/v1/admin/moderation/matches/{id:guid}/disputes/resolve` | ✓ | — | — |  |
| `POST /api/v1/admin/moderation/matches/{matchId:guid}/correct-result` | ✓ | — | — | ✓ |
| `POST /api/v1/admin/moderation/rankings/recalculate` | ✓ | — | — |  |
| `GET /api/v1/admin/moderation/reports` | ✓ | — | — |  |
| `POST /api/v1/admin/moderation/reports/{id:guid}/appeal/resolve` | ✓ | — | — |  |
| `POST /api/v1/admin/moderation/reports/{id:guid}/review` | ✓ | — | — |  |
| `GET /api/v1/admin/moderation/teams` | ✓ | — | — |  |
| `POST /api/v1/admin/moderation/teams/{teamId:guid}/reactivate` | ✓ | — | — |  |
| `POST /api/v1/admin/moderation/teams/{teamId:guid}/suspend` | ✓ | — | — |  |
| `GET /api/v1/admin/navigation` | ✓ | ✓ | ✓ |  |
| `GET /api/v1/admin/plans/` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/plans/` | ✓ | — | ✓ |  |
| `PUT /api/v1/admin/plans/{planId:guid}` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/plans/{planId:guid}/default` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/plans/{planId:guid}/status` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/roles` | ✓ | — | — |  |
| `GET /api/v1/admin/sessions/` | ✓ | — | — |  |
| `DELETE /api/v1/admin/sessions/{sessionId:guid}` | ✓ | — | — |  |
| `GET /api/v1/admin/settings` | ✓ | — | — |  |
| `PUT /api/v1/admin/settings/{key}` | ✓ | — | — |  |
| `GET /api/v1/admin/subscriptions/` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/subscriptions/` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/subscriptions/owners/{ownerId:guid}` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/subscriptions/owners/{ownerId:guid}/cancel` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/subscriptions/owners/{ownerId:guid}/payments` | ✓ | — | ✓ |  |
| `PUT /api/v1/admin/subscriptions/owners/{ownerId:guid}/plan` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/subscriptions/owners/{ownerId:guid}/reactivate` | ✓ | — | ✓ |  |
| `POST /api/v1/admin/subscriptions/owners/{ownerId:guid}/suspend` | ✓ | — | ✓ |  |
| `GET /api/v1/admin/support/tickets/` | ✓ | ✓ | — |  |
| `GET /api/v1/admin/support/tickets/{ticketId:guid}` | ✓ | ✓ | — |  |
| `PUT /api/v1/admin/support/tickets/{ticketId:guid}/assignee` | ✓ | ✓ | — |  |
| `POST /api/v1/admin/support/tickets/{ticketId:guid}/close` | ✓ | ✓ | — |  |
| `POST /api/v1/admin/support/tickets/{ticketId:guid}/messages` | ✓ | ✓ | — |  |
| `PUT /api/v1/admin/support/tickets/{ticketId:guid}/priority` | ✓ | ✓ | — |  |
| `POST /api/v1/admin/support/tickets/{ticketId:guid}/resolve` | ✓ | ✓ | — |  |
| `GET /api/v1/admin/users/` | ✓ | ✓ | — |  |
| `GET /api/v1/admin/users/{userId:guid}` | ✓ | ✓ | — |  |
| `GET /api/v1/admin/users/{userId:guid}/history` | ✓ | ✓ | — |  |
| `POST /api/v1/admin/users/{userId:guid}/reactivate` | ✓ | — | — |  |
| `POST /api/v1/admin/users/{userId:guid}/sessions/revoke` | ✓ | — | — |  |
| `POST /api/v1/admin/users/{userId:guid}/suspend` | ✓ | — | — | ✓ |
| `GET /api/v1/admin/venues/` | ✓ | — | — |  |
| `GET /api/v1/admin/venues/approval-queue` | ✓ | — | — |  |
| `GET /api/v1/admin/venues/pending-approval` | ✓ | — | — |  |
| `GET /api/v1/admin/venues/{venueId:guid}` | ✓ | — | — |  |
| `PUT /api/v1/admin/venues/{venueId:guid}/admin-note` | ✓ | — | — |  |
| `POST /api/v1/admin/venues/{venueId:guid}/approve` | ✓ | — | — |  |
| `POST /api/v1/admin/venues/{venueId:guid}/ask-owner` | ✓ | — | — |  |
| `GET /api/v1/admin/venues/{venueId:guid}/complaints` | ✓ | — | — |  |
| `POST /api/v1/admin/venues/{venueId:guid}/ownership` | ✓ | — | — | ✓ |
| `POST /api/v1/admin/venues/{venueId:guid}/reactivate` | ✓ | — | — |  |
| `POST /api/v1/admin/venues/{venueId:guid}/reject` | ✓ | — | — |  |
| `POST /api/v1/admin/venues/{venueId:guid}/request-info` | ✓ | — | — |  |
| `POST /api/v1/admin/venues/{venueId:guid}/suspend` | ✓ | — | — | ✓ |
