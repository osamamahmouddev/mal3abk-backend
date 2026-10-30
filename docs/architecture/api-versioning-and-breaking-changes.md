# API Versioning and Breaking Changes Policy

## 1. Overview and Core Philosophy

Malabak adheres to strict Semantic Versioning (`MAJOR.MINOR.PATCH`) principles across its REST APIs and generated client SDKs. The Malabak mobile application (Flutter/Dart) and web applications (Next.js/TypeScript) must remain resilient across backend deployments without breaking user sessions, ongoing pitch reservations, or live tactical matches.

---

## 2. API Versioning Scheme

Malabak adopts **URI Path Versioning** for all client-facing and partner endpoints:

```http
https://api.malabak.app/api/v{major}/[resource]
```

- Current Stable Version: `/api/v1`
- Experimental or Sandbox Features: Flagged under dedicated sub-routes or feature headers without mutating public schemas.
- Minor and patch updates to the API that preserve backward compatibility do not alter the path.
- Only breaking schema changes trigger a major version increment (e.g. `/api/v2`).

---

## 3. What Constitutes a Breaking Change?

| Change Type | Breaking? | Allowed in Existing Major Version? | Required Action |
| :--- | :---: | :---: | :--- |
| Adding a new endpoint | **No** | **Yes** | Client generation & CI check update |
| Adding an optional request parameter/field | **No** | **Yes** | Backward compatible fallback in handler |
| Adding a new response field | **No** | **Yes** | Mobile & Web DTOs safely ignore unmapped keys |
| Adding a new enum variant | **No** | **Yes** | Client deserializers must implement fallback/unknown variant |
| **Removing or renaming an endpoint** | **YES** | **NO** | Major version bump (`/api/v{N+1}`) |
| **Removing or renaming a response field** | **YES** | **NO** | Major version bump (`/api/v{N+1}`) |
| **Adding a mandatory request parameter/field** | **YES** | **NO** | Major version bump (`/api/v{N+1}`) |
| **Changing the data type or format of a field** | **YES** | **NO** | Major version bump (`/api/v{N+1}`) |
| **Altering error semantics or status codes** | **YES** | **NO** | Major version bump (`/api/v{N+1}`) |

---

## 4. Deprecation and Sunset Lifecycle

When an API endpoint or major version is scheduled for retirement, Malabak enforces an automated deprecation workflow following **RFC 8594** standard HTTP headers:

### Deprecation Timeline Policy
1. **Notice Period:** A minimum notice period of **180 days (6 months)** is guaranteed prior to retiring any deprecated endpoint or version.
2. **Standard Deprecation Headers:**
   - `Deprecation: @<unix-timestamp>`: Indicates the date when the endpoint was officially marked deprecated.
   - `Sunset: <HTTP-date>`: Indicates the absolute cutoff date and time after which the endpoint will return HTTP 410 (Gone).
   - `Link: <https://docs.malabak.app/api/migrations/v2>; rel="sunset"`: Hyperlink to documentation detailing migration instructions.

### Example Response on Deprecated Endpoint
```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Deprecation: @1793200000
Sunset: Mon, 27 Apr 2027 00:00:00 GMT
Link: <https://docs.malabak.app/api/migrations/v2>; rel="sunset"

{
  "status": "Healthy"
}
```

---

## 5. Contract Synchronization & CI Stale-Client Verification

To guarantee that TypeScript (`@malabak/api-client`) and Flutter/Dart (`malabak_mobile_client`) SDKs never drift out of sync with backend Minimal API changes:

1. **Source of Truth:** Swashbuckle generates `/swagger/v1/swagger.json` directly from ASP.NET Core endpoint definitions and data transfer objects.
2. **Snapshot Persistence:** The contract is committed to source control at `docs/openapi/v1.json`.
3. **CI Gate:** Every pull request executes `scripts/check-openapi-staleness.sh` in the CI pipeline (`.github/workflows/ci.yml`). If backend code modifies endpoints without updating `docs/openapi/v1.json` or regenerating the SDKs, the build fails immediately.
4. **Error Catalog Guarantee:** Every endpoint must document standard error codes:
   - `400 Bad Request`
   - `401 Unauthorized`
   - `403 Forbidden`
   - `404 Not Found`
   - `409 Conflict`
   - `422 Unprocessable Entity`
   - `429 Too Many Requests`
