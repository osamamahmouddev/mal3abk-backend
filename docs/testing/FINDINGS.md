# Malabak Test Findings & Bug Catalog

## Known Documentation Conflicts (Decided)

| ID | Topic | Docs | Code (expected) |
|---|---|---|---|
| DOC-01 | Max pending holds per player | ADR-0008: 3; SECURITY.md §7.5 and §11: 2 | **2** (`BookingHoldRules`). SECURITY.md wins. |
| DOC-02 | Hold length | ADR-0008: 10 min; SECURITY.md §11: 15–30 min | Venue policy, else the `booking.default_hold_minutes` platform setting. Test the configured value. |
| DOC-03 | Error Code Catalog completeness | `docs/api/error-codes.md` & `error-codes.json`: lists only 21 codes in SCREAMING_SNAKE_CASE | Backend emits 175+ domain error codes using dotted PascalCase (`Booking.NotFound`, `Team.NotFound`, etc.) not documented in the catalog. |
| DOC-04 | Cancellation Refund Windows | ADR-0008 §2 specifies tiered cancellation ladder (>12h: 100%, 6-12h: 50%, <6h: 0%) | Backend implements flat policy-based model (`CancellationDeadlineHours`, default 24h, and flat `RefundPercentage`) where cancellations past the deadline are completely rejected with `Booking.CancellationDeadlinePassed`. |
| DOC-05 | Waitlist Domain Scope | Test plan references slot availability waitlist (`/waitlist` frees up and notifies) | Backend `/api/v1/waitlist` is exclusively the pre-launch landing page lead capture (`WaitlistService` in `IdentityDbContext`), not a pitch slot inventory waitlist. |

---

## Phase 0 (P0) — Inventory and Safety Nets Findings

### BUG-001 — Missing required query parameter in Minimal API returns 500 Internal Server Error instead of 400 Bad Request
- **Severity**: High (produces 500 server crash on client-correctable query parameter omission)
- **Area**: API / Parameter Binding & Global Exception Handling
- **Rule**: docs/api/conventions.md RFC 9457 ProblemDetails & RFC 9110
- **Test**: `tests/Malabak.IntegrationTests/Infrastructure/CrossTenantIdorSecurityTests.cs`
- **Repro**: Issue `GET /api/v1/venue-manager/venues/{venueId}/reports/` or `/slots` without query parameters (`from`, `to`, `date`).
- **Expected**: 400 Bad Request with RFC 9457 `application/problem+json` describing missing parameters in `invalidParams`.
- **Actual**: 500 Internal Server Error returning `Microsoft.AspNetCore.Http.BadHttpRequestException` from `GlobalExceptionHandler`.

### BUG-002 — docs/api/error-codes.json is missing 175 domain error codes returned by the API
- **Severity**: Medium (breaks programmatic frontend error contract for web & mobile)
- **Area**: API / Error Catalog Contract
- **Rule**: docs/api/error-codes.md §1 ("All Malabak API errors return an RFC 9457 application/problem+json response containing a stable string errorCode... Front-end applications MUST inspect errorCode rather than parsing error message strings")
- **Test**: `tests/Malabak.UnitTests/ErrorCodeCatalogTests.cs :: AllDomainErrorCodes_MustExistInCatalog`
- **Repro**: Domain handlers throughout `Malabak.Modules.*` return domain error codes (e.g. `Booking.NotFound`, `Booking.SlotOverlap`, `Team.NotFound`, `AdminVenues.NotFound`), but `docs/api/error-codes.json` contains only 21 generic/initial codes.
- **Expected**: Every error code returned by the API exists in `docs/api/error-codes.json` with localized English and Arabic descriptions.
- **Actual**: 175 domain error codes are completely absent from `docs/api/error-codes.json`.

---

## Phase 1 (P1) — Identity and Sessions Findings

### BUG-003 — TOTP authentication lacks replay protection within the same 30-second time-step
- **Severity**: Medium
- **Area**: `Malabak.Modules.Identity` / 2FA & TOTP Verification
- **Rule**: Test Plan §P1 ("TOTP enroll, then confirm: reject a wrong code, a reused code inside the same time step, and clock drift beyond the allowed window.") & RFC 6238 §5.2
- **Test**: `tests/Malabak.UnitTests/StaffPasswordAndStepUpEdgeCaseTests.cs :: Totp_ReplayInsideSameTimeStep_MustBeRejectedAsync`
- **Repro**: Successfully log in with a valid TOTP code; immediately replay the same code 5 seconds later within the same 30-second window.
- **Expected**: The second login attempt rejects the replayed code with an error indicating the code was already used.
- **Actual**: `TotpService.VerifyCode` and `PasswordLoginHandler` are completely stateless with respect to used time-steps; the replayed code succeeds again.

### BUG-004 — Phone change reveals account existence when target number is already registered (Enumeration Vulnerability)
- **Severity**: High
- **Area**: `Malabak.Modules.Identity` / Phone Change
- **Rule**: SECURITY.md §5.2 rule 6 ("لو الرقم الجديد مسجل على حساب تاني → ما تفضحش. ارجع رسالة عامة "تعذر استخدام هذا الرقم" علشان ما يبقاش وسيلة لتخمين الأرقام")
- **Test**: `tests/Malabak.UnitTests/PhoneChangeEdgeCaseTests.cs :: StartPhoneChange_WithNumberUsedByAnotherAccount_MustReturnGenericErrorAsync`
- **Repro**: Call `StartPhoneChangeCommand` with a phone number that is registered to another user.
- **Expected**: The API returns a generic error (e.g. `Identity.UnableToUsePhoneNumber`) without revealing whether the phone number is registered.
- **Actual**: Handler returns `Identity.PhoneNumberAlreadyInUse` with description `"The requested phone number is already registered to another account."`, allowing account enumeration.

### BUG-005 — Concurrent phone changes to the same number trigger unhandled DbUpdateException (500)
- **Severity**: Medium
- **Area**: `Malabak.Modules.Identity` / Phone Change Concurrency
- **Rule**: Test Plan §P1 ("Race: two users complete a change to the same new number at the same time. One succeeds and the other gets the generic error, not a 500.")
- **Test**: `tests/Malabak.UnitTests/PhoneChangeEdgeCaseTests.cs`
- **Repro**: Two users reach `VerifyNewPhoneCommand` for the same new number and submit simultaneously.
- **Expected**: One transaction commits; the other catches the unique constraint violation on `identity.users(phone_number)` and returns a generic failure result.
- **Actual**: `VerifyNewPhoneHandler` does not catch `DbUpdateException` from `_dbContext.SaveChangesAsync()`, crashing the request with an unhandled 500 error.

### BUG-006 — Phone change requests do not limit or track wrong OTP attempts, permitting brute force
- **Severity**: High
- **Area**: `Malabak.Modules.Identity` / Phone Change Security
- **Rule**: SECURITY.md §4.3 & §5.3 ("المحاولات: 5 محاولات فقط لكل كود - منع التخمين... جدول phone_change_requests: attempts_old, attempts_new... Expired / Cancelled / TooManyAttempts -> Failed")
- **Test**: `tests/Malabak.UnitTests/PhoneChangeEdgeCaseTests.cs :: VerifyOldPhone_After5FailedAttempts_LocksRequestAsFailedAsync`
- **Repro**: Submit more than 5 incorrect OTP codes to `VerifyOldPhoneHandler` or `VerifyNewPhoneHandler`.
- **Expected**: After 5 wrong attempts, the request transitions to `Failed` and subsequent attempts (even with the correct code) fail.
- **Actual**: `PhoneChangeRequest` has no attempt counters (`attempts_old`, `attempts_new`) and `PhoneChangeStatus` lacks `Failed` and `Cancelled` states. The code can be guessed repeatedly until the 15-minute expiration.

### BUG-015 — Concurrent refresh token requests trigger unhandled DbUpdateConcurrencyException (500)
- **Severity**: Medium
- **Area**: `Malabak.Modules.Identity` / Refresh Token Rotation Concurrency
- **Rule**: Test Plan §P1 ("Race: two concurrent refreshes with the same token. Exactly one succeeds, the other gets 401 (token reuse / invalid), not a 500.") & SECURITY.md §4.4
- **Test**: `tests/Malabak.IntegrationTests/Infrastructure/IdentityHttpPipelineIntegrationTests.cs :: RefreshToken_ConcurrentRefreshesWithSameToken_ExactlyOneSucceedsAsync`
- **Repro**: Issue two concurrent HTTP POST requests to `/api/v1/auth/refresh` using the same valid refresh token via `Task.WhenAll`.
- **Expected**: Exactly one request succeeds with 200 OK and rotates the token; the other request receives 401 Unauthorized (`Identity.RefreshTokenReused` or invalid token).
- **Actual**: Both requests attempt to update the token row concurrently, causing PostgreSQL concurrency conflict that throws an unhandled `Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException`, returning a 500 Internal Server Error.

---

## Phase 2 (P2) — Booking Findings

### BUG-007 — 6-hour temporary booking lock after 3 expired holds within 24 hours is not implemented
- **Severity**: High (permits repeated slot denial-of-inventory attacks across rolling 24-hour windows)
- **Area**: `Malabak.Modules.Booking` / Anti-Hoarding & Hold Lifecycle
- **Rule**: ADR-0008 §1 ("Slot Hoarding & Abuse Controls: If a player triggers 3 expired holds within 24 hours without submitting payment proof, a 6-hour temporary booking lock is placed on the player account.")
- **Test**: `tests/Malabak.IntegrationTests/Infrastructure/BookingHoldAndQuotaEdgeCaseTests.cs :: PenaltyCooldown_ThreeExpiredHoldsIn24Hours_Places6HourBookingLockAsync`
- **Repro**: As a single player, initiate and let 3 holds expire across different pitches/slots within 24 hours; immediately attempt to hold a 4th slot.
- **Expected**: The 4th hold request is blocked with a 6-hour temporary booking lock penalty.
- **Actual**: `CreateBookingHandler` only counts active pending holds (`b.Status == PendingDeposit && b.HoldExpiresAtUtc > nowUtc`), with zero tracking or penalty cooldown for historically expired holds within 24 hours.

### BUG-008 — ImageProcessor rejects standard PDF payment receipts
- **Severity**: Medium
- **Area**: `Malabak.Api.Infrastructure.Media` / Payment Proof Validation
- **Rule**: Test Plan §P2 ("Allowed: JPEG, PNG, WebP, PDF, checked by magic bytes.") & `BookingErrors.InvalidProofFile` ("The uploaded file is invalid, exceeds 5MB, or is not an allowed image/PDF.")
- **Test**: `tests/Malabak.UnitTests/PaymentProofAndMediaEdgeCaseTests.cs :: ImageProcessor_ValidPdfReceipt_ShouldBeAccepted`
- **Repro**: Attempt to upload a valid `%PDF-` document receipt as payment proof.
- **Expected**: Magic byte validation recognizes the PDF header and allows valid receipts to process.
- **Actual**: `ImageProcessor.HasAllowedSignature` only recognizes JPEG, PNG, and WebP signatures; any valid PDF file is rejected with `"unsupported_format"`.

### BUG-009 — BookingErrors.CannotCancelPastBooking is unreachable dead code in CancelBookingHandler
- **Severity**: Low
- **Area**: `Malabak.Modules.Booking` / Cancellation Handler Validation
- **Rule**: Test Plan §P2 ("Cancel after kick-off → rejected.")
- **Test**: `tests/Malabak.UnitTests/BookingCancellationAndReaperEdgeCaseTests.cs :: Cancel_ByPlayer_AfterKickoff_RejectsCancellationAsync`
- **Repro**: Attempt to cancel a confirmed booking after match kickoff.
- **Expected**: Handler returns `BookingErrors.CannotCancelPastBooking` (`Booking.CannotCancelPastBooking`).
- **Actual**: `CancelBookingHandler` evaluates policy deadline (`nowUtc > deadlineUtc`) before delegating to `Booking.Cancel`. Because `deadlineUtc = StartUtc - leadHours`, any time after kickoff is already past `deadlineUtc`, so the handler returns `Booking.CancellationDeadlinePassed` and the domain code `CannotCancelPastBooking` is unreachable via the API.

---

## Phase 3 (P3) — Venues, Pitches and Schedules Findings

### BUG-010 — DeletePitchHandler soft-deletes pitches with future confirmed bookings without validation
- **Severity**: High
- **Area**: `Malabak.Modules.Venues` / Pitch Management
- **Rule**: Test Plan §P3 ("Pitches: create, update, deactivate (`SetPitchActive`) and delete. Deleting a pitch that has future confirmed bookings is rejected or handled as documented.")
- **Test**: `tests/Malabak.UnitTests/PitchAndEntitlementEdgeCaseTests.cs :: DeletePitch_WithFutureConfirmedBookings_ShouldBeRejectedAsync`
- **Repro**: Send a `DeletePitchCommand` for a pitch that currently has future confirmed player reservations.
- **Expected**: Deletion is rejected (e.g. `Pitch.HasActiveBookings`) or blocked until existing bookings are cancelled or reassigned.
- **Actual**: `DeletePitchHandler` has no dependency on `IBookingConflictChecker` or `IUpcomingBookingsReader` and immediately marks the pitch deleted (`pitch.MarkDeleted(_clock.UtcNow); venue.RemovePitch(pitch.Id)`), leaving orphaned bookings on a deleted pitch.

