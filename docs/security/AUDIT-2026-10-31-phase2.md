# Security audit: Phase 2 (availability, pricing and search)

- **Date:** 2026-10-31
- **Scope:** branch `backend_phase_2` compared with `dev`. This covers:
  - the pricing engine and the price rules API (TASK-020, TASK-219)
  - the availability engine and its Redis cache (TASK-021, TASK-301)
  - booking quote tokens (TASK-305)
  - the public explore, map, areas, venue details and home endpoints (TASK-022, TASK-302, TASK-303, TASK-023, TASK-304)
- **Method:** recon of the new entry points, then a manual line-by-line review of every handler and what it calls. Each finding is reproduced with a failing test before it is fixed.
- **Standards:** OWASP API Security Top 10 and the project's `docs/SECURITY.md`.

## Summary

There are no Critical or High findings. Authorization on the price rules API is correct: every rule lookup is scoped by `VenueId` after a venue permission check, and pitches are checked to belong to the venue. Quote tokens are HMAC-signed, bound to the player, the slot, the amounts and the policy version, and they expire after 15 minutes. Prices and availability are re-checked on submit.

The four findings are all resource exhaustion (OWASP API4) or key strength. All of them are fixed, and each has a regression test.

| ID | Severity | Title | Status |
|---|---|---|---|
| SEC-01 | Medium | Price preview and quote accept unbounded time ranges | Fixed |
| SEC-02 | Medium | One public request could trigger hundreds of availability computations | Fixed |
| SEC-03 | Low | Unlimited price rules per venue amplify public availability cost | Fixed |
| SEC-04 | Low | Quote signing key length not enforced | Fixed |
| SEC-05 | Low | Explore text filters had no length limit | Fixed |

## Findings

### SEC-01: Unbounded time range in pricing (Medium)

- **Location:** `Domain/PricingCalculator.cs` `Quote`, reached through `POST /venue-manager/venues/{id}/price-rules/preview`.
- **Issue:** the calculator walks the range in 15-minute slices with no upper bound. A user with only `VenueView` permission could send a range spanning years. That spins the CPU through millions of slices, and the minute count `(int)TotalMinutes` overflows.
- **Fix:** ranges longer than 24 hours (`MaxQuoteMinutes`) are rejected before any work is done. The minute count is computed only after validation.
- **Test:** `PricingCalculatorTests.InvalidRanges_AreRejected`, covering 24h plus 15 minutes and `DateTime.MinValue` to `MaxValue`.

### SEC-02: Availability amplification on public explore (Medium)

- **Location:** `Application/Explore/VenueExplore.cs`.
- **Issue:** an anonymous explore with a date checks availability for up to 60 venues. When nothing matches, the empty-result suggestions re-run the search once per relaxed filter. A crafted query that matches many venues but has no free slot produced **300 availability lookups** in one request, each one a full slot computation with several database queries on a cache miss. At 60 requests per minute per client, that is a cheap way to load the database.
- **Fix:** a per-request budget (`AvailabilityBudgetPerRequest = 120`) is shared by the date filter, the next-free-slot enrichment and the suggestions.
- **Test:** `VenueExploreTests.OneRequest_CannotTriggerUnboundedAvailabilityWorkAsync`. It fails with 300 lookups without the fix and passes with 120 or fewer.

### SEC-03: Unlimited price rules per venue (Low)

- **Issue:** every active rule is evaluated for every slice of every public availability response. A compromised or careless manager account could create thousands of rules and slow every public availability call for that venue.
- **Fix:** at most 200 rules per venue (`PriceRule.MaxRulesPerVenue`; error `PriceRule.TooMany`).
- **Test:** `PriceRuleManagementTests.RulesPerVenue_AreCappedAsync`.

### SEC-04: Weak quote signing key accepted (Low)

- **Issue:** `Booking:QuoteSigningKey` of any length was accepted, so a short key would make HMAC forgery by brute force feasible.
- **Fix:** keys shorter than 32 bytes stop startup (`HmacQuoteTokenSigner.MinKeyBytes`).
- **Test:** `HmacQuoteTokenSignerTests.ShortKey_IsRefused`.

### SEC-05: Unbounded explore text filters (Low)

- **Fix:** city, area, pitch type, surface and facility values are limited to 100 characters.
- **Test:** `VenueExploreTests.OverlongTextFilters_AreRejectedAsync`.

## Verified controls (no finding)

- **Price rules:** list, create, update, delete and preview check `VenueView` or `VenueEdit` through the venue access service. Rule and pitch lookups are scoped by venue, so there is no IDOR across venues. Changes are audit-logged.
- **Quote tokens:**
  - HMAC-SHA256 with constant-time comparison.
  - The payload binds the player, pitch, start, end, total, deposit, currency, policy version and expiry.
  - Tampered, foreign, expired, price-changed and taken-slot cases are all tested.
  - The key comes from secrets; outside Development the app refuses to start without it.
- **Public endpoints:**
  - Only approved, published and non-deleted venues are returned. Others answer 404 without revealing that they exist.
  - The owner's phone and internal fields are never in public DTOs (tested).
  - All public routes are rate limited.
- **Location privacy (TASK-303):**
  - Player coordinates are never stored. An architecture test fails if any domain type other than `Venue` gains a coordinate property.
  - Distances are returned approximately.
  - Coordinates are rounded to 2 decimals in request logs and OpenTelemetry `url.query`.
  - Location-based responses are sent with `Cache-Control: no-store`.
- **Availability cache:** keys are built from validated ids, failures are never cached, and the store fails open without exposing errors.
- **Injection:** all filters are parameterized LINQ. The pitch-type regex runs only on whitelisted values and has a 100 ms timeout.

## Systemic recommendations

1. Run the integration suite in CI with Docker. The quote-signer and query-redaction tests live there and could not run locally during this audit.
2. Secret scanning already runs in CI ("Gitleaks Secret Detection", which passed on this branch). Consider adding `dotnet list package --vulnerable` as a CI step.
4. The OpenAPI contract (`docs/openapi/v1.json`) and the generated web client were stale for the new endpoints, which failed the "Web Monorepo Verification" check. Both are now regenerated, and the change only adds the 11 new paths and 6 schemas.
3. Consider a global per-IP budget for anonymous search endpoints on top of the per-route limits.
