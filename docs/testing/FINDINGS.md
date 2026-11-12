# Malabak Test Findings & Bug Catalog

## Known Documentation Conflicts (Decided)

| ID | Topic | Docs | Code (expected) |
|---|---|---|---|
| DOC-01 | Max pending holds per player | ADR-0008: 3; SECURITY.md §7.5 and §11: 2 | **2** (`BookingHoldRules`). SECURITY.md wins. |
| DOC-02 | Hold length | ADR-0008: 10 min; SECURITY.md §11: 15–30 min | Venue policy, else the `booking.default_hold_minutes` platform setting. Test the configured value. |
| DOC-03 | Error Code Catalog completeness | `docs/api/error-codes.md` & `error-codes.json`: lists only 21 codes in SCREAMING_SNAKE_CASE | Backend emits 175+ domain error codes using dotted PascalCase (`Booking.NotFound`, `Team.NotFound`, etc.) not documented in the catalog. |

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

