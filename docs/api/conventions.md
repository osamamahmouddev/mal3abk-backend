# Malabak REST API Conventions & Standards

## 1. Scope & Core Principles

This specification governs all public and internal HTTP APIs for the Malabak platform across backend services, the web monorepo (`web/packages/api-client`), and the mobile application (`packages/mobile-client`).

### Core Design Rules
1. **RESTful Resource Orientation:** Endpoints represent nouns in the plural form (e.g. `/api/v1/venues`, `/api/v1/bookings`).
2. **Kebab-Case URLs:** Multi-word path segments use kebab-case (`/api/v1/booking-slots`, `/api/v1/pitch-schedules`).
3. **CamelCase JSON Payloads:** Request and response property keys strictly use `camelCase`.
4. **Deny-by-Default Security:** All endpoints require authenticated bearer tokens unless explicitly annotated with `[AllowAnonymous]`.
5. **Standardized RFC 9457 Problem Details:** All errors (4xx and 5xx) return machine-readable ProblemDetails JSON structures.

---

## 2. HTTP Methods & Semantics

| Method | Idempotent | Safe | Typical Usage | Success Status Codes |
|---|---|---|---|---|
| `GET` | Yes | Yes | Retrieve resource representation or collection | `200 OK` |
| `POST` | No (unless with `Idempotency-Key`) | No | Create new resource, perform state transition, or trigger actions | `201 Created`, `200 OK`, `202 Accepted` |
| `PUT` | Yes | No | Replace resource representation completely | `200 OK`, `204 No Content` |
| `PATCH` | No | No | Apply partial modifications to a resource | `200 OK`, `204 No Content` |
| `DELETE` | Yes | No | Remove or soft-delete a resource | `200 OK`, `204 No Content` |

---

## 3. URL Structure & Versioning

All endpoints follow strict path-based versioning:
```
https://api.malabak.app/api/v{major}/{resource}[/{id}][/{sub-resource}]
```

### Examples:
- `GET /api/v1/venues` (List venues)
- `GET /api/v1/venues/0192d4f8-1111-7000-8000-000000000001` (Get venue by UUID)
- `POST /api/v1/venues/0192d4f8-1111-7000-8000-000000000001/pitches` (Create pitch in venue)
- `POST /api/v1/bookings` (Create a match booking hold)

---

## 4. Idempotency (`Idempotency-Key`)

All financial and state-altering `POST` endpoints require an `Idempotency-Key` HTTP header:

```http
POST /api/v1/bookings HTTP/1.1
Host: api.malabak.app
Idempotency-Key: 9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d
Content-Type: application/json
Authorization: Bearer <access_token>
```

### Idempotency Behavior
- **First Request:** Processed normally, cached in Redis distributed lock with SHA-256 payload fingerprint.
- **Identical Replay:** Returns cached response with header `X-Cache-Lookup: HIT`.
- **Concurrent Request:** Returns `HTTP 409 Conflict` with error code `IDEMPOTENCY_CONFLICT`.
- **Mismatched Payload:** Returns `HTTP 422 Unprocessable Entity` with error code `IDEMPOTENCY_PAYLOAD_MISMATCH`.

---

## 5. Optimistic Concurrency & ETags

Endpoints updating mutable resources (pitch prices, venue operating hours, booking states) support HTTP `ETag` and conditional headers:
- Responses include `ETag: W/"<version_guid_or_hash>"`.
- Update requests (`PUT`/`PATCH`) include `If-Match: W/"<version_guid_or_hash>"`.
- If the resource changed concurrently, the API returns `HTTP 412 Precondition Failed` or `HTTP 409 Conflict`.

---

## 6. Standard Error Format (RFC 9457)

All non-2xx responses conform to RFC 9457 `application/problem+json`:

```json
{
  "type": "https://malabak.app/errors/booking-slot-unavailable",
  "title": "تعارض في حجز الملعب",
  "status": 409,
  "errorCode": "BOOKING_SLOT_UNAVAILABLE",
  "detail": "الفترة الزمنية المختارة محجوزة بالفعل من قبل لاعب آخر.",
  "instance": "/api/v1/bookings",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "invalidParams": [
    {
      "name": "slotTime",
      "reason": "الفترة متداخلة مع حجز مؤكد برقم #1042"
    }
  ]
}
```

### Localization Headers
Clients specify their preferred response language via `Accept-Language` or `X-Locale`:
- `Accept-Language: ar-EG` (Default)
- `Accept-Language: en-US`
