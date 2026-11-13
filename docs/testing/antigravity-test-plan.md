# Malabak — Full Edge-Case Test Plan (brief for Antigravity)

You are writing **tests only** for Malabak (ملعبك), a football pitch booking and community platform for Egypt.
Three products share one backend: the Player app (Flutter), the Venue dashboard (Next.js) and the Admin dashboard (Next.js).

Your job is to find bugs by testing every business rule, including its edge cases.
Your job is **not** to fix bugs. A failing test that proves a real bug is a good result.

---

## 0. Ground rules (read before writing anything)

1. Read these files first, in full:
   - `AGENTS.md`, `SECURITY.md`, `rule.md`, `docs/PRD.md`
   - `docs/adr/0007-retention-and-deletion-policy.md`, `docs/adr/0008-hold-expiry-and-ranking-defaults.md`
   - `docs/api/error-codes.md`, `docs/api/conventions.md`
   - `docs/architecture/test-infrastructure-guide.md`
   - Mobile: `../Mal3abk-mobile/rules.md`
2. **Do not change production code** (`src/`, `web/apps/*/src`, `web/packages/*/src`, `Mal3abk-mobile/lib/`).
   The only exception is a missing test seam, such as an interface that cannot be faked. In that case, stop and write it in `FINDINGS.md` as `SEAM-xx`. Do not add the seam yourself.
3. **Never weaken an existing test.** Do not delete assertions, loosen expected values, add `Skip` to tests that already pass, or change test helpers so that more cases pass.
4. When a new test **fails because the code is wrong**:
   - keep the test and its correct expected value;
   - mark it skipped with the bug ID: `[Fact(Skip = "BUG-012: ...")]`, `test.fixme('BUG-012 ...')` or `skip: 'BUG-012 ...'` in Dart;
   - add the bug to `docs/testing/FINDINGS.md` (format in §9).
5. When the docs disagree with each other, follow the **stricter** rule (`SECURITY.md` wins). Test what the code does, and log the conflict as `DOC-xx`. Known conflicts are in §8.
6. Security:
   - No real phone numbers. Use the reserved test range from the existing fixtures (`TestSessions.cs`, `test/helpers/auth_fixtures.dart`).
   - No secrets, keys or tokens in test files. Use `TestSecrets.cs` and Testcontainers' random passwords.
   - Never print or log phone numbers, OTP codes, tokens or passwords from tests, including in assertion messages.
7. Tests must be deterministic:
   - use `FakeClock` (`SetUtcNow`, `AdvanceMinutes`, ...), never `Thread.Sleep` or `Task.Delay`;
   - use a fixed seed for any randomness;
   - do not depend on the order tests run in.
8. Naming and style:
   - .NET: `Method_Scenario_ExpectedResult`, Arrange-Act-Assert, xUnit + FluentAssertions.
   - Use the existing builders (`UserBuilder`, `VenueBuilder`, `PitchBuilder`, `BookingBuilder`) and auth helpers (`AsPlayer`, `AsVenueOwner`, `AsVenueStaff`, `AsAdmin`, `AsSupportAgent`, `AuthorizationMatrixHelper`).
   - Follow the style of the neighbouring test files. Don't add new test libraries without asking.
9. Git:
   - one branch and one PR **per phase** (§3), from `dev`, named `test/<phase>`, e.g. `test/p2-booking`;
   - Conventional Commits with type `test:`;
   - do not merge your own PRs.

---

## 1. Commands

Backend (from `mal3abk-backend/`; Docker must be running for Testcontainers):

```bash
dotnet build -warnaserror
dotnet test tests/Malabak.UnitTests
dotnet test tests/Malabak.ArchitectureTests
dotnet test tests/Malabak.IntegrationTests
dotnet format --verify-no-changes
dotnet test --collect:"XPlat Code Coverage"
```

Web (from `mal3abk-backend/web/`):

```bash
pnpm install
pnpm lint
pnpm typecheck
pnpm --filter venue exec playwright test
pnpm --filter admin exec playwright test
```

Mobile (from `Mal3abk-mobile/`, Flutter 3.38.5 via fvm):

```bash
fvm flutter analyze
fvm flutter test
fvm dart run build_runner build --force-jit --delete-conflicting-outputs
```

Load (optional, staging only, never production): `tests/load/*.js` with k6.

---

## 2. How to work through each rule

For every business rule, cover all of these that apply:

- **Happy path**
- **Boundaries:** exactly at the limit, one below and one above. For time, test `limit − 1s`, `limit` and `limit + 1s`.
- **Invalid input:** empty, null, whitespace, too long, wrong format, negative, zero, huge numbers, Arabic-Indic digits (`٠١٢٣`), mixed RTL/LTR text, emoji, SQL and script payloads.
- **Wrong state:** call each action from every state where it is not allowed.
- **Authorization:** anonymous → 401, wrong role → 403, another tenant's resource → 403 or 404 (never 200), owner → 2xx.
- **Concurrency:** two or more parallel requests on the same resource.
- **Repeats:** double submit, retry with the same `Idempotency-Key`, replay of a used token or code.
- **Time zones:** `Africa/Cairo`, including both Egypt DST transitions (spring forward and fall back) and slots that cross midnight.
- **Error shape:** a ProblemDetails body with the documented error code from `docs/api/error-codes.json`, no stack trace and no internal detail.

---

## 3. Phases

Run the phases in order. Each phase ends with a green build, its own PR, and an updated `FINDINGS.md`.

### P0 — Inventory and safety nets (do first)

1. Generate an **endpoint inventory**: every endpoint from the ASP.NET `EndpointDataSource` (about 234 routes in `src/Malabak.Api/Endpoints/*.cs`), with method, route, auth policy and rate limit policy. Save it as `docs/testing/endpoint-inventory.md`.
2. Add a **deny-by-default** integration test. It enumerates every endpoint and fails if one has neither an authorization policy nor an explicit `AllowAnonymous` that appears on a reviewed allowlist in the test. Health, ping, OTP send/verify, refresh, webhooks, public venues and reference data are the expected anonymous ones. Anything else that is anonymous is a finding.
3. Add an **authorization matrix** test for **every** endpoint in the inventory, using `AuthorizationMatrixHelper` across Anonymous, Player, VenueOwner, VenueStaff, Admin, SupportAgent and Finance.
4. Add a **cross-tenant (IDOR) test** for every route that has `{venueId}`, `{bookingId}`, `{id}`, `{teamId}`, `{ticketId}`, `{userId}`, `{customerKey}` or `{invoiceId}`. Create the resource as tenant A, call the route as tenant B, and expect 403 or 404 with no data in the body.
5. Add an **error-code catalog** test: every code the API returns must exist in `docs/api/error-codes.json`, and every catalog entry must be reachable or marked unused.

### P1 — Identity and sessions (`Malabak.Modules.Identity`)

**Player OTP sign-in** (`/auth/otp/send`, `/auth/otp/verify`), see SECURITY.md §4.3, §4.4 and §7.5:

- Phone normalization accepts `01xxxxxxxxx`, `+201xxxxxxxxx`, `00201xxxxxxxxx`, with spaces or dashes, and with Arabic-Indic digits. It rejects non-Egyptian numbers (+966, +1), landlines, prefixes other than 10/11/12/15, and wrong lengths.
- `send` returns the **same response** for a registered and an unregistered number (no enumeration). Compare status, body shape and timing to within a reasonable bound.
- Codes have 6 digits. Reject 5 or 7 digits, letters, and leading or trailing spaces.
- A code expires after 5 minutes: verify at 4:59 succeeds and at 5:01 fails.
- After 5 wrong attempts the code is locked (`Identity.OtpMaxAttemptsExceeded`), and the correct code fails afterwards too.
- A code works once: a second verify with the same code fails.
- Purpose binding: a `login` code cannot be used for `change_phone_old`, `change_phone_new` or step-up, and the other way round.
- A code for number A cannot be used for number B.
- Resend: 60-second cooldown, 3 per hour, 5 per hour and 10 per day per number. Check the per-IP limits too, and that a 429 has `Retry-After` (see `SendOtpRetryAfterTests.cs`).
- Turnstile: a missing or invalid token is rejected in non-test environments, and there is no bypass flag left in config.
- A new number creates a player and returns `isNewUser = true`. A known number returns `false`.
- **Account pending deletion** (ADR-0007): verify returns `Identity.AccountPendingDeletion` and does not sign in. Verify with `restoreAccount: true` restores the account and signs in. Day 29 can still be restored, day 31 cannot (the account is anonymized). After anonymization, the old number registers as a brand-new account with no link to the old history.
- Suspended players and disabled accounts cannot sign in. Check the error code.

**Tokens** (SECURITY.md §3):

- The access token is ES256. Reject `alg: none`, HS256 signed with the public key (algorithm confusion), a wrong `kid`, a tampered payload, an expired token, `nbf` in the future, a wrong `iss` or `aud`, and a missing `sub`.
- Refresh rotation: a new pair is issued, and the old refresh token is now invalid.
- **Reuse detection:** using an old refresh token again revokes the **whole token family**, so the newest token stops working too.
- Two parallel refreshes with the same token: exactly one succeeds.
- `logout` revokes the current session only. `logout-all` and `sessions/revoke-others` behave as named. Revoking another user's session ID returns 403 or 404.
- After account deletion, a security-stamp change or a phone change, the old access token is rejected within its documented lifetime.
- The database stores refresh tokens as SHA-256 hashes, never in plain text.

**Staff and admin password auth, 2FA and step-up:**

- Login lockout at 5 failures in 15 minutes per account and 20 per IP. The response must not reveal whether the email exists.
- `password/forgot` always returns 200. A reset token works once, expires, and is rejected for another account.
- `password/change` requires the current password and revokes other sessions.
- TOTP enroll, then confirm: reject a wrong code, a reused code inside the same time step, and clock drift beyond the allowed window.
- Email verify and resend: 60-second cooldown.
- Step-up token: right scope only, valid for 5 minutes, bound to the same user and session.

**Phone change** (SECURITY.md §5, full state machine):

- `start` without a step-up token is rejected. `start` with the current number is rejected. `start` with a number used by another account gives the **generic** error (no enumeration).
- The order `verify-new` before `verify-old` is rejected.
- Too many wrong codes → `Failed`. After 15 minutes → `Expired`. `cancel` works from each pending state.
- The 7-day cooldown after a completed change (`Identity.PhoneChangeCooldownActive`) and 3 starts per day.
- On completion: other sessions are revoked, the current one stays, an audit entry exists without the plain phone number, and a notification to the old number is queued in the outbox.
- Race: two users complete a change to the same new number at the same time. One succeeds and the other gets the generic error, not a 500.
- A pending booking stays attached to the user after the change.

**Impersonation:**

- Only allowed roles can start it. The exchange token works once and is short-lived.
- Actions are audited with both the real admin and the impersonated user.
- An impersonated session cannot start another impersonation, change the phone or password, or delete the account.

### P2 — Booking (`Malabak.Modules.Booking`) — highest business risk

**Lifecycle:** `Draft → PendingDeposit → ProofSubmitted → UnderReview → Confirmed / Rejected / Expired / Cancelled / Completed / NoShow`.

- Build a **full transition table** test: for every state and every action (submit proof, review approve, review reject, cancel, expire, no-show, complete, refund record, force-cancel), assert allowed or rejected with the correct error. Every pair must be listed explicitly.

**Quote and pricing** (`/quote`, rule.md §6):

- The client price is ignored. Send a tampered `price` or `amount` in the body and expect the server value.
- Pricing precedence: default hourly → pitch override → peak / weekend / holiday / special date → discounts. Test each layer alone and all layers together, plus two rules that overlap.
- A price snapshot is stored on the booking. Changing the price rule afterwards does not change existing bookings.
- Money in EGP: no float rounding errors (for example 3 × 33.33), and zero or negative prices are rejected.

**Create (hold):**

- Hold length is the venue policy value, or the platform default `booking.default_hold_minutes`. Check `ExpiresAtUtc` exactly.
- **Maximum 2 pending holds per player** (`BookingHoldRules.MaxPendingBookingsPerPlayer`): the third returns 409 `Booking.MaxPendingBookingsExceeded`. When one expires or is cancelled, a new hold is allowed.
- Penalty: 3 expired holds within 24 hours → a 6-hour booking lock. Test 3 within 23:59, 3 spread over 24:01, and that the lock lifts at exactly 6 hours.
- Rejected cases:
  - a slot in the past, or starting now;
  - outside operating hours, on a blocked slot, or on a special closed day or holiday;
  - an inactive pitch, an unapproved, unpublished or suspended venue, or a suspended owner subscription;
  - a duration that is not a multiple of the slot length, zero, negative, or longer than the maximum;
  - a start time that is not on the slot grid;
  - beyond the venue's booking window.
- **Overnight slots:** a venue open 18:00–02:00 accepts 23:00–01:00 and rejects 01:30–02:30. The booking date is correct after midnight.
- **DST in Africa/Cairo:** a slot in the missing hour on spring-forward day, and a slot in the repeated hour on fall-back day. Prices and durations must still be right. Never hard-code +2 or +3.
- **Overlap and concurrency** (PostgreSQL `btree_gist` exclusion):
  - 20 parallel creates for the same slot → exactly 1 succeeds and 19 get 409, with no 500;
  - partial overlaps are rejected;
  - **adjacent** slots (10–11 and 11–12) are allowed, because the end is exclusive;
  - an expired or cancelled booking frees the slot right away.
- **Idempotency-Key:**
  - the same key with the same body returns the same response and creates no second booking;
  - the same key with a different body → 422;
  - a key reused by another user does not leak the first user's response;
  - the key expires after 24 hours;
  - parallel requests with the same key create one booking only.

**Proof upload and submit** (SECURITY.md §12):

- Allowed: JPEG, PNG, WebP, PDF, checked by **magic bytes**. Reject a `.jpg` that holds PHP or HTML, an SVG, a polyglot file, a zero-byte file, 5 MB + 1 byte, and a correct type sent with the wrong Content-Type.
- EXIF and GPS are stripped from stored images (read the stored file back).
- The stored name is random. Path traversal in the file name (`../../x`) has no effect.
- The upload URL is pre-signed, expires after about 5 minutes, and only the booking owner can get it.
- Submitting proof **pauses** the hold timer, so the reaper never expires a `ProofSubmitted` booking.
- Proof after the hold expired → rejected. Proof on another player's booking → 403 or 404.
- The same image hash used on two bookings is flagged as a duplicate receipt.
- 5 uploads per hour and 2 at the same time.

**Review (venue):**

- Approve → `Confirmed`. Reject needs a reason and gives `Rejected`. Reviewing twice → rejected.
- Staff of venue B cannot review a booking at venue A. A staff role without the review permission → 403.

**Hold reaper job** (`ExpireBookingHoldsJob`):

- Expires only `PendingDeposit` with `ExpiresAtUtc < now`. A booking at exactly `now` is not expired yet (check what the code does and log it).
- Raises `BookingExpiredDomainEvent` and a SignalR slot release.
- Running twice in a row, or two runs at the same time, gives no double events.

**Cancellation and refunds** (ADR-0008 §2):

- Player cancel windows, tested at exactly 12:00:00, 11:59:59, 6:00:00 and 5:59:59 before kick-off: more than 12 h → 100% refunded, 6–12 h → 50%, less than 6 h → 0%. Write down which window the exact boundary falls in, and log it if the docs leave it unclear.
- A venue's own cancellation policy overrides the defaults.
- `cancel/preview` gives the same amount as the real cancel.
- A venue-initiated cancel gives a 100% refund at any time and queues an apology notification.
- Cancel after kick-off → rejected. Cancelling twice → rejected.
- The server clock decides: a client timestamp in the body is ignored.
- `refund/record` cannot exceed the paid amount, and recording a refund twice is rejected.

**No-show and complete:** no-show is only allowed after kick-off and only for confirmed bookings. Completion happens after the end time.

**Manual booking (staff):** follows the same overlap, operating-hours and pricing rules. The staff member's venue must match.

**Payment disputes:** open, add notes, resolve. Only the parties involved and admins can see a dispute.

**Waitlist** (`/waitlist`): duplicate join, notify when the slot frees up, and no notification after the slot was taken again.

### P3 — Venues and schedules (`Malabak.Modules.Venues`)

- Venue onboarding → submit → approval queue → approve, reject, request info or ask owner → publish. Test every invalid transition. Players cannot see or book an unapproved venue.
- Pitches: create, update, deactivate (`SetPitchActive`) and delete. Deleting a pitch that has future confirmed bookings is rejected or handled as documented.
- The subscription quota on pitches and staff (`IEntitlementService`): at the limit, and one over it.
- Weekly hours:
  - overlapping ranges on the same day, open equal to close, 24-hour days, closed days, overnight ranges;
  - `schedules/impact` correctly lists the bookings a change would affect.
- Special schedules and holidays override the weekly hours. Deleting an override restores them.
- Blocked slots: overlap with an existing booking, removing a block, and other-venue IDs.
- Price rules: create, update and delete. Overlapping rules use the documented priority. The time range crosses midnight.
- Availability (`/availability`, `/slots`):
  - matches the schedule + blocks + bookings + holds;
  - the cache is invalidated after each change;
  - no hidden internal fields are returned.
- Explore, map, featured, favorites, recently viewed:
  - pagination limits (page size 0, negative, very large);
  - filters by governorate, area, surface and pitch type;
  - distance sort;
  - only approved and published venues appear.
- Gallery uploads: same file rules as proofs (§P2), plus the maximum images per venue.
- Staff: invitation accept, resend and expiry. A removed staff member loses access right away. Role permissions are enforced.
- Customers and notes: notes stay inside the venue. Deleting another venue's note → 403 or 404.
- Reports and `bookings.csv`: the date range is in the venue's time zone. **CSV injection:** a customer name starting with `=`, `+`, `-` or `@` is escaped.

### P4 — Teams, matches and rankings

- Teams:
  - create; the name is unique where required; Arabic names work;
  - invite, accept, join request, approve, decline;
  - leave; the **last captain cannot leave** without transferring captaincy;
  - transfer captaincy to a non-member is rejected;
  - remove a member;
  - maximum squad size.
- Challenges: you cannot challenge your own team. Respond, cancel, and link a booking (the booking must be confirmed and must belong to one of the two teams).
- Results (ADR-0008 §3):
  - both captains submit within 6 hours; matching scores → `Confirmed`, and Glicko-2 updates once, in one transaction;
  - different scores, or a dispute → `Disputed`, and ratings **do not change**;
  - submit after 6 hours, a non-captain submitting, and submitting twice are all rejected.
- Glicko-2:
  - starting values μ=1500, φ=350, σ=0.06;
  - compare against a reference calculation (Glickman's paper example) to within 1e-6;
  - inactivity over 30 days raises φ.
- Leaderboard and fair play: suspended teams are hidden and disputed matches are excluded. `rankings/recalculate` is admin-only and idempotent.
- Moderation: reports, review, appeal, appeal resolve. Admin `correct-result` recalculates the ratings and writes an audit entry.

### P5 — Subscriptions, payments, notifications, support, audit, privacy

- **Subscriptions:**
  - plans CRUD, default plan, plan status;
  - owner suspend, reactivate and cancel; record payments;
  - invoices: generate, void, PDF;
  - voiding twice is rejected; a paid invoice cannot be voided;
  - money totals are correct;
  - a suspended owner's venue cannot take bookings.
- **Payment methods:** InstaPay, Vodafone Cash and bank transfer formats are validated. Players see only an active venue's methods.
- **Notifications:**
  - the outbox delivers exactly once, and handlers are idempotent on retry;
  - preferences per category are respected;
  - push token register and unregister;
  - read, read-all, unread count;
  - a user cannot read another user's notifications.
- **Support tickets:** create, message, assign, priority, resolve, close. Messages are rejected on a closed ticket. Players see only their own tickets.
- **Audit:**
  - append-only: no update or delete is possible, even through EF;
  - every admin and staff decision writes an entry;
  - **no PII** in audit payloads (only hashes or IDs).
- **Privacy:**
  - consents;
  - `data-export` contains only the user's own data and no other users' data;
  - delete account → soft delete + revoke all sessions + 30-day grace (ADR-0007);
  - the day-31 anonymization job wipes the name, phone and blind index, and keeps the anonymized bookings and match history.

### P6 — Cross-cutting security

- **Rate limits:** one test per row of the SECURITY.md §7.5 table, at the limit and limit + 1. A 429 includes `Retry-After`.
- **Security headers:** HSTS, `X-Content-Type-Options`, CSP on the web apps, and no `Server` or `X-Powered-By` header.
- **CORS:** only the allowlisted origins. No `*` together with credentials.
- **Errors:** no stack traces, SQL or internal paths in any 4xx or 5xx response.
- **Logging:** a test with `CapturingLogger` runs the OTP, phone-change, booking and proof flows. It fails if a phone number, OTP, token or password appears in any log line (see `OtpLogMaskingTests.cs`, `PiiMaskingEnricherTests.cs`).
- **Field encryption:**
  - AES-256-GCM: the AAD is bound to the row, so ciphertext copied to another row fails to decrypt;
  - the HMAC blind index is the same for the same input and different for other inputs.
- **SignalR** (SECURITY.md §8):
  - connecting needs a valid ticket, and the ticket works once and expires;
  - joining another venue's group is rejected;
  - a player only gets updates for their own bookings;
  - message size and connect rate limits.
- **Webhooks:**
  - Twilio: requests without a valid signature are rejected; the `{account}` route value is validated;
  - email bounce: authenticated and idempotent.
- **Hangfire dashboard:** admin-only, and not reachable anonymously.
- **Architecture tests:** no `DateTime.UtcNow` or `DateTime.Now` in Domain or Application, no EF in Domain, and no cross-module references (extend `tests/Malabak.ArchitectureTests`).

### P7 — Web dashboards (`web/apps/venue`, `web/apps/admin`, `web/packages/bff`)

- **BFF:**
  - session cookies are `HttpOnly`, `Secure` and `SameSite`;
  - tokens never reach browser JavaScript or `localStorage`;
  - CSRF protection on every state-changing route;
  - the BFF does not proxy arbitrary paths (no SSRF or open proxy).
- **Playwright flows** (extend the existing `e2e/` specs):
  - venue: login + 2FA, proof review, manual booking, schedule edit with an overnight range, price rules, staff invite, reports and CSV, subscription states;
  - admin: approval queue, moderation, user suspend, impersonation banner, audit log filters.
- **RTL / LTR:**
  - Arabic is the default;
  - the layout mirrors;
  - numbers, dates and EGP are formatted per locale;
  - **i18n key parity** between `ar` and `en` (write a test that fails on any missing key).
- **Forms:** Zod validation matches the server rules. Server errors are shown, not swallowed.
- **XSS:** Arabic or script payloads in venue names, customer notes and ticket messages are rendered as text.
- **Accessibility:** axe has no serious or critical violations on every main page.

### P8 — Mobile player app (`Mal3abk-mobile`, flutter_test + mocktail + bloc_test)

- Every cubit in `lib/features/*/presentation/cubit`: initial, loading, success, and each error branch. A double tap while loading does not send a second request. Calls after `close()` emit nothing.
- Every use case: validation failures come back as `ApiResult.failure` with a localization key, and the repo is not called after a validation failure.
- Every repo impl: Dio errors are mapped (timeout, no connection, 401, 403, 404, 409, 422, 429 with `Retry-After`, 500, a ProblemDetails body with a backend error code).
- Auth:
  - phone input formatter (Arabic-Indic digits, paste with spaces, `+20` prefix);
  - OTP field;
  - resend countdown;
  - pending-deletion restore dialog and retry with `restoreAccount: true`;
  - refresh-on-401 with a single refresh while several calls run in parallel;
  - logout clears secure storage.
- Booking:
  - the hold countdown matches the server's `expiresAt` (not the device clock);
  - proof upload states;
  - cancel preview;
  - the max-pending error is shown;
  - the idempotency key is reused on retry.
- Translations: `assets/translations/ar.json` and `en.json` have the same keys, and no hard-coded user-facing strings remain (grep test).
- No logging of phone numbers, OTPs or tokens (grep test over `lib/` for `print` / `debugPrint` / `log(` near those values).
- `test/integration/backend_e2e_test.dart`: extend only against a local or staging backend, never production.

---

## 4. Things that must never happen (fail the test on sight)

- A 500 from any endpoint for any input. Every bad input must give a 4xx with a documented code.
- A 200 or 2xx with another tenant's data.
- A booking overlap stored in the database.
- A price that differs from the server quote.
- A phone number, OTP, token or password in a log, audit row, error body or test output.
- A refresh token that still works after its family was revoked.

---

## 5. Coverage targets

- Domain and Application projects: ≥ 80% line coverage (rule.md §10). Report the numbers before and after per module.
- 100% of endpoints in the authorization matrix and the IDOR suite.
- 100% of booking state × action pairs.

---

## 6. Done for each phase

- `dotnet build -warnaserror`, all test projects and `dotnet format --verify-no-changes` are green (bug tests are skipped with an ID, not failing).
- Web: `pnpm lint`, `pnpm typecheck` and the Playwright specs are green. Mobile: `fvm flutter analyze` is clean and `fvm flutter test` is green.
- `FINDINGS.md` is updated, and the PR description lists the tests added, the bugs found and the coverage change.

---

## 7. Hand-back (what to give the reviewer)

When all phases are done, write `docs/testing/HANDBACK.md` with:

1. The PR link for each phase.
2. Every file you changed outside `tests/`, `test/`, `e2e/` and `docs/testing/` (this should be **none**), with the reason.
3. Every test you skipped, with its bug ID.
4. Every existing test you changed, with the reason.
5. Coverage before and after.
6. Any rule you could not test, and why.

---

## 8. Known doc conflicts (already decided — test the code, don't "fix" it)

| ID | Topic | Docs | Code (expected) |
|---|---|---|---|
| DOC-01 | Max pending holds per player | ADR-0008: 3; SECURITY.md §7.5 and §11: 2 | **2** (`BookingHoldRules`). SECURITY.md wins. |
| DOC-02 | Hold length | ADR-0008: 10 min; SECURITY.md §11: 15–30 min | Venue policy, else the `booking.default_hold_minutes` platform setting. Test the configured value. |

Add any new conflict you find as `DOC-03`, `DOC-04`, and so on.

---

## 9. `FINDINGS.md` format

```markdown
### BUG-012 — Cancel at exactly 6h before kick-off forfeits 100% instead of 50%
- Severity: High | Medium | Low   (High = money, security, data leak or 500s)
- Area: Booking / Cancellation
- Rule: ADR-0008 §2 Window B
- Test: tests/Malabak.UnitTests/BookingPolicyTests.cs :: Cancel_AtExactly6Hours_Refunds50Percent
- Repro: steps or request (no real numbers, no tokens)
- Expected / Actual
```

Also use `DOC-xx` for doc conflicts and `SEAM-xx` for code you could not test without changing production code.
