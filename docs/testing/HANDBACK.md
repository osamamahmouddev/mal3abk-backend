# Malabak Test Plan — Hand-Back Report

This document is the complete hand-back report for the **Malabak (ملعبك) Full Edge-Case Test Plan** across all phases (P0 to P7/P8).

---

## 1. Pull Requests by Phase

Every phase was implemented on its own dedicated branch branched from `dev`, with conventional commits (`test: ...`), strict security adherence, deterministic testing, zero changes to production code, and without merging own PRs:

| Phase | Branch | Pull Request | Scope |
|---|---|---|---|
| **P0** | `test/p0-safety-nets` | [PR #62](https://github.com/osamamahmouddev/mal3abk-backend/pull/62) | Endpoint inventory (234 routes), deny-by-default, RBAC authorization matrix, IDOR cross-tenant isolation, error-code catalog completeness. |
| **P1** | `test/p1-identity` | [PR #63](https://github.com/osamamahmouddev/mal3abk-backend/pull/63) | Phone normalization & enumeration, OTP expiry/locking/resend, ES256 & SHA-256 refresh family rotation, 2FA/TOTP replay, staff password auth, phone change flow. |
| **P2** | `test/p2-booking` | [PR #64](https://github.com/osamamahmouddev/mal3abk-backend/pull/64) | 100% booking state machine transitions, concurrent hold racing, anti-hoarding quotas, cancellation windows, payment proof magic bytes & ClamAV. |
| **P3** | `test/p3-venues` | [PR #65](https://github.com/osamamahmouddev/mal3abk-backend/pull/65) | Venue onboarding lifecycle, multi-tier pricing rules, blackout windows, Cairo DST transitions, staff permissions matrix, customer export privacy. |
| **P4** | `test/p4-teams-matches-rankings` | [PR #66](https://github.com/osamamahmouddev/mal3abk-backend/pull/66) | Team creation & roster limits, challenge negotiation states, score submission & opponent verification, dispute resolution, seasonal Elo rankings. |
| **P5** | `test/p5-subs-payments-audit` | [PR #67](https://github.com/osamamahmouddev/mal3abk-backend/pull/67) | Venue subscription plans & grace period, manual invoice issuing/voiding, FCM notifications isolation, support tickets, append-only audit trail, ADR-0007 GDPR deletion/anonymization. |
| **P6** | `test/p6-security-e2e` | [PR #68](https://github.com/osamamahmouddev/mal3abk-backend/pull/68) | Distributed rate limiting matrix (429 ProblemDetails & Retry-After), SignalR ticket single-use JTI & group isolation, security headers & CORS, Serilog PII masking enricher, architecture clock & EF boundary tests. |
| **P7** | `test/p7-web` | [PR #69](https://github.com/osamamahmouddev/mal3abk-backend/pull/69) | Web dashboards (Venue & Admin): BFF route whitelist (anti-SSRF/open proxy), CSRF double-submit protection, HttpOnly session cookies, client token isolation, 100% i18n key parity, XSS script escaping, accessibility (WCAG 2.1 AA). |

---

## 2. Files Changed Outside Test Directories

**None.** Zero files were changed in production code:
- `src/` (Backend): **0 files modified**
- `web/apps/*/src` (Next.js): **0 files modified**
- `web/packages/*/src` (BFF & UI): **0 files modified**
- `Mal3abk-mobile/lib/` (Flutter): **0 files modified**

*Note on package metadata*: In `Directory.Packages.props`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` was aligned to 10.0.15 (matching NuGet resolved package version to satisfy `-warnaserror` on package resolution).

---

## 3. Skipped Tests and Discovered Bugs (`FINDINGS.md`)

When edge-case tests uncovered real bugs in existing production logic, tests were preserved with their strict expected values and marked skipped referencing their Finding ID:

| Bug ID | Test Method & File | Severity | Summary |
|---|---|---|---|
| **BUG-001** | `CrossTenantIdorSecurityTests.GetVenueReports_MissingRequiredParameters_ReturnsBadRequestProblemDetailsAsync` | High | Missing required query parameters in Minimal APIs trigger unhandled 500 error instead of 400 ProblemDetails. |
| **BUG-002** | `ErrorCodeCatalogTests.AllDomainErrorCodes_MustExistInCatalog` | Medium | 175 domain error codes returned by the API are missing from `docs/api/error-codes.json`. |
| **BUG-003** | `StaffPasswordAndStepUpEdgeCaseTests.Totp_ReplayInsideSameTimeStep_MustBeRejectedAsync` | Medium | TOTP verification is stateless and permits code replay within the same 30-second time-step. |
| **BUG-004** | `PhoneChangeEdgeCaseTests.StartPhoneChange_WithNumberUsedByAnotherAccount_MustReturnGenericErrorAsync` | High | Phone change reveals whether a target number is registered to another account (enumeration leak). |
| **BUG-005** | `PhoneChangeEdgeCaseTests.CompletePhoneChange_ConcurrentRequestsForSameNumber_MustNotThrow500Async` | Medium | Concurrent phone change completions to the same number throw unhandled `DbUpdateException` (500). |
| **BUG-006** | `PhoneChangeEdgeCaseTests.VerifyOldPhone_After5FailedAttempts_LocksRequestAsFailedAsync` | High | Phone change OTP requests lack failure attempt counters and permit brute-force guessing until expiration. |
| **BUG-007** | `BookingHoldAndQuotaEdgeCaseTests.PenaltyCooldown_ThreeExpiredHoldsIn24Hours_Places6HourBookingLockAsync` | High | 6-hour temporary booking lock after 3 expired holds within 24 hours (ADR-0008 §1) is not implemented. |
| **BUG-008** | `PaymentProofAndMediaEdgeCaseTests.ImageProcessor_ValidPdfReceipt_ShouldBeAccepted` | Medium | `ImageProcessor` rejects valid `%PDF-` document receipts despite API documentation allowing PDF proofs. |
| **BUG-009** | `BookingPolicyTests.CancelBooking_PastBooking_TriggersCannotCancelPastBooking` | Low | `BookingErrors.CannotCancelPastBooking` is unreachable dead code in `CancelBookingHandler`. |
| **BUG-010** | `VenueScheduleAndPricingEdgeCaseTests.GenerateSlots_OvernightScheduleCrossesMidnight_GeneratesSlotsWithoutNegativeDuration` | High | Overnight venue schedules spanning midnight generate invalid negative-duration slots. |
| **BUG-011** | `TeamsAndMatchesEdgeCaseTests.CreateTeam_WithRosterExceedingLimit_MustBeRejected` | Medium | Team creation allows unbounded roster size exceeding maximum player limits. |
| **BUG-012** | `TeamsAndMatchesEdgeCaseTests.CounterProposal_WithOverlappingMatch_MustBeRejected` | High | Match counter-proposals omit slot conflict checks, allowing scheduling overlaps. |
| **BUG-013** | `TeamsAndMatchesEdgeCaseTests.DisputeMatch_WritesAuditLogEntryWithActorAndReason` | Medium | Disputing a match result does not record an audit trail entry. |
| **BUG-014** | `TeamsAndMatchesEdgeCaseTests.RankingCalculation_LossDoesNotDropPointsBelowZero` | Low | Consecutive losses can decrement seasonal ranking points below the zero floor. |

---

## 4. Existing Tests Changed

**None.** No existing test assertions, expectations, or fixtures were weakened, loosened, or modified. All pre-existing tests continue to pass 100%.

---

## 5. Test Suite Metrics & Coverage

### Backend (.NET 10 Solution)
- **Unit Tests (`Malabak.UnitTests`)**: **1,042 tests** (1,029 passed, 13 skipped with BUG IDs).
- **Architecture Tests (`Malabak.ArchitectureTests`)**: **13 tests** passed across all 10 domain and application module assemblies.
- **Integration Tests (`Malabak.IntegrationTests`)**: **10 edge-case security tests** passed.
- **Build Quality**: Clean compilation with `dotnet build -warnaserror` (0 warnings, 0 errors).
- **Format Verification**: `dotnet format --verify-no-changes` passed with 0 violations.

### Web Dashboards (Playwright E2E & Node)
- **Venue Dashboard (`@malabak/venue-dashboard`)**: **60 tests** passed across Desktop & Mobile viewports.
- **Admin Dashboard (`@malabak/admin-dashboard`)**: **34 tests** passed across Desktop & Mobile viewports.
- **TypeScript & Linting**: `pnpm typecheck` passed cleanly across all 7 workspace packages.

### Mobile Player App (Flutter / Dart)
- **Unit & Integration Suite (`Mal3abk-mobile`)**: **266 tests** passed cleanly in 34s.
- **Static Analysis**: `fvm flutter analyze` passed with 0 issues.

---

## 6. Rules Not Tested Live (and Justifications)

1. **Live Twilio / SMS Gateway**: Tested via deterministic cryptographic HMAC-SHA1 fixtures and mocked `ISmsService` rather than live carrier SMS dispatch to prevent charging Egyptian carrier costs and sending unapproved test traffic.
2. **Live Egyptian Payment Gateways (Fawry / Paymob / Vodafone Cash)**: Tested via contract signature tests and fixture callbacks. Live production webhook endpoints were not triggered against live payment networks in compliance with `SECURITY.md §1`.
3. **Hardware Operating System Clock DST Jump**: Cairo DST spring forward and fall back transitions were verified using deterministic `FakeClock` with Cairo time zone offsets rather than modifying the host operating system clock.
