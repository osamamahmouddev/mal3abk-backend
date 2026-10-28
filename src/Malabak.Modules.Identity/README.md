# Malabak.Modules.Identity

Bounded context responsible for user authentication, authorization, token lifecycle, and session management.

## Key Features
- **Phone OTP Engine:** Passwordless authentication for players using CSPRNG codes, HMAC-SHA256 hashed in Redis with 5-minute TTL and brute-force attempt tracking.
- **ES256 Token Lifecycle:** Short-lived ECDSA access tokens (15 minutes) signed with NIST P-256 curve and key identifier (`kid`) rotation support.
- **Opaque Refresh Tokens:** SHA-256 hashed refresh tokens stored in database with automatic rotation and family ID reuse detection.
- **Multi-Step Phone Change State Machine:** Secure phone number migration (`PhoneChangeRequest`) requiring verification of old and new numbers.
- **PostgreSQL Schema:** `identity.*` managed by `IdentityDbContext`.
