---
name: malabak-security
description: >-
  Comprehensive security guidelines, threat models, architectural protocols, and implementation rules for Malabak (ملعبك).
  Always activate and consult this skill when designing, reviewing, modifying, or implementing any features across backend,
  web, mobile, or cloud infrastructure, especially authentication, authorization, OTP, JWT/refresh tokens, encryption,
  rate limiting, WebSockets/SignalR, file uploads, booking fraud prevention, CSRF, CORS, audit logging, and Cloudflare configurations.
---

# Malabak Security Skill (ملعبك — دليل وسياسات الأمان)

This skill encapsulates the full security architecture, threat model, and engineering rules defined for the Malabak platform.

> **Primary Source of Truth for Security:**
> Always refer to [SECURITY.md](references/SECURITY.md) (and project root [SECURITY.md](../../../SECURITY.md)) for the complete, in-depth guidelines, code examples, threat models, and architectural mandates. It complements [rule.md](../../../rule.md) (Section 7). In case of conflict, the stricter rule always applies.

---

## 1. Core Principles (المبادئ الأساسية)

- **Never trust the client:** Validate and compute everything server-side (prices, availability, slots, roles, permissions).
- **Deny by default:** Endpoints require authentication unless explicitly marked `[AllowAnonymous]`.
- **Least privilege:** Users and services only hold the minimum permissions required for their tasks.
- **Defense in depth:** UI conceals, API authorizes, domain invariants enforce, database constraints reject.
- **Zero secrets in code:** Secrets live in Secret Manager / Key Vault; run secret scanners in CI.
- **Fail closed:** If dependencies (e.g., Redis) fail during sensitive operations (e.g., OTP), reject the request rather than allowing unmetered access.

---

## 2. Key Security Domains & Workflows

### 2.1 Authentication & Tokens (التوكن والباسورد)
- **Access Tokens:** Short-lived (10–15 min) JWT signed using **ES256** (ECDSA P-256) with a `kid` header for zero-downtime key rotation.
- **Refresh Tokens:** Opaque 256-bit random strings (`RandomNumberGenerator.GetBytes(32)`), stored exclusively as SHA-256 hashes in the database.
- **Token Rotation & Reuse Detection:** Every refresh issues a new token and revokes the old one. If an invalidated token is reused, revoke the entire session family immediately and alert the user.
- **Player Authentication:** Phone OTP only (6-digit CSPRNG, stored as HMAC-SHA256, 5-minute expiry, max 5 attempts, constant-time compare, Turnstile bot protection, rate limited per phone + IP + device).
- **Admin Authentication:** Separate audience (`aud=malabak-admin`), short expiration (5–10 min), mandatory 2FA (TOTP/Passkey), protected by Cloudflare Access.

### 2.2 Sensitive Flows: Phone Number Change (تغيير رقم التليفون)
- State machine: `PendingOldVerify` -> `PendingNewVerify` -> `Completed`.
- Requires recent authentication (Step-up token valid for 5 min).
- Verifies ownership of the old phone first via OTP, then the new phone via OTP.
- Applied in a single atomic database transaction, revoking all other active sessions and alerting the old phone.

### 2.3 Web Security & BFF (الويب)
- Next.js acts as a Backend-For-Frontend (BFF), holding tokens in `HttpOnly; Secure; SameSite=Lax` cookies with `__Host-` prefixes.
- Zero client-accessible JWTs (prevents XSS theft).
- Strict CORS allowlists, anti-forgery tokens (`X-CSRF-Token`), and restrictive CSP headers.

### 2.4 Rate Limiting & Abuse Prevention
- Multi-tier rate limiting: Cloudflare WAF at the edge + ASP.NET RateLimiter backed by distributed Redis.
- Defense against SMS Pumping: Allowlist target country codes (+20 for Egypt), Turnstile challenges, per-phone / per-IP / per-device limits.
- Slot hoarding protection: Maximum 2 concurrent `PendingDeposit` bookings per player; short hold windows (15–30 min) backed by Hangfire hold-expiry jobs.

### 2.5 Real-Time Communication (WebSockets / SignalR)
- WebSockets are for **server-to-client push only**. All state-mutating actions (bookings, cancellations, approvals) MUST execute through REST APIs.
- Short-lived (60s), single-use **WS Tickets** obtained via authenticated REST (`/api/v1/realtime/ticket`) replace long-lived tokens in query strings.
- Channel / group subscriptions strictly validated and assigned server-side.

### 2.6 Encryption & Data Protection
- **In transit:** TLS 1.2/1.3 enforced, Cloudflare Full (strict) mode, Authenticated Origin Pulls (mTLS).
- **At rest:** Volume & database encryption enabled; private blob storage buckets with SSE-KMS.
- **Field-level encryption:** AES-256-GCM with Associated Authenticated Data (AAD) for sensitive banking/payout details; HMAC-based Blind Index for searching encrypted phone numbers.

### 2.7 Secure File Uploads (إيصالات الدفع)
- Strict validation of magic bytes (JPEG/PNG/WebP/PDF), max file size limits (5MB).
- Re-encoding images to strip malicious payloads and metadata (EXIF/GPS).
- Storage in private non-public buckets; retrieval strictly via short-lived (5 min) pre-signed URLs.

---

## 3. Pre-Merge Security Checklist (قائمة التحقق قبل الدمج)

Always verify this checklist before considering any task or PR complete:
- [ ] Server-side authorization enforced on every endpoint (no IDOR / BOLA)?
- [ ] Input validated with FluentValidation against strict allowlists?
- [ ] Zero secrets or keys committed to Git?
- [ ] Sensitive data (tokens, passwords, OTP, PII, payment info) masked in logs?
- [ ] Append-only audit log records created for sensitive mutations?
- [ ] Rate limits applied to new endpoints?
- [ ] ProblemDetails returned without leaking internal stack traces or database errors?

For the full architectural manual, review [SECURITY.md](references/SECURITY.md).
