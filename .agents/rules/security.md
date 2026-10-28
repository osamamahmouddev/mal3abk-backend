# Malabak Project Rules & Security Directives

## 1. Primary Sources of Truth
- **Security Standards & Protocols:** [SECURITY.md](file:///home/osama/Desktop/mal3abk-backend/SECURITY.md) — The comprehensive security manual for Malabak. Always refer to this document for any authentication, authorization, token lifecycle, encryption, rate limiting, SignalR, or data protection decisions.
- **Engineering & Architecture Rules:** [rule.md](file:///home/osama/Desktop/mal3abk-backend/rule.md) — The engineering principles, code layout, C# standards, and testing rules.
- **Product Requirements:** [docs/PRD.md](file:///home/osama/Desktop/mal3abk-backend/docs/PRD.md) — The functional and business specifications.

## 2. Mandatory Directives for AI Assistants
1. **Always Consult `SECURITY.md`:** Before implementing, modifying, or reviewing any feature, verify its security implications against [SECURITY.md](file:///home/osama/Desktop/mal3abk-backend/SECURITY.md).
2. **Strictness Precedence:** If any instruction or code pattern conflicts with `SECURITY.md`, the stricter rule in `SECURITY.md` must be enforced.
3. **Zero Security Compromise:**
   - Never store secrets or sensitive credentials in code, config, or git.
   - Never log tokens, OTPs, passwords, or PII.
   - Never trust client inputs: enforce all authorization, pricing, and availability server-side.
   - Enforce deny-by-default on all endpoints.
   - Use ES256 for JWT signing, SHA-256 hashed opaque tokens for refresh tokens with rotation and reuse detection.
   - Use AES-256-GCM for field-level encryption with AAD, and HMAC for blind indexes.
   - Protect file uploads by verifying magic bytes, stripping EXIF, and storing in private buckets with short-lived pre-signed URLs.
4. **Definition of Done:** Every change must build with zero warnings (`dotnet build -warnaserror`), pass all unit and architecture tests, pass formatting verification (`dotnet format --verify-no-changes`), and adhere to the security checklist in `SECURITY.md`.
