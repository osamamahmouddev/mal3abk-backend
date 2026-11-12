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
