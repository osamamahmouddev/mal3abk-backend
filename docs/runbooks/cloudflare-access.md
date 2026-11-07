# Cloudflare Access (Zero Trust) for admin, staging and internal tools

TASK-720. Terraform lives in `infra/cloudflare/access.tf`. The API also checks the signed assertion.

## What is protected

| Hostname / path | Who can get in | Extra requirement |
|---|---|---|
| `admin.malabak.app` | Malabak staff group (allowed emails and the company email domain) | MFA. IP allowlist if set |
| `staging.malabak.app` | Malabak staff group | IP allowlist if set |
| `api.malabak.app/hangfire` | Engineering group | IP allowlist if set |
| `staging-api.malabak.app/swagger` | Engineering group | none |

The public API (`api.malabak.app/api/...`) is **not** behind Access. Mobile and web clients call it directly. Admin endpoints are still protected by staff JWT + TOTP, permissions, step-up and `AdminIpAllowlistFilter`.

Swagger is never enabled in Production (`Program.cs`). It exists only on staging.

## Sign-in methods

- **One-time PIN** to an allowed email. Always on, and needs no setup.
- **Google Workspace SSO.** Created only when `google_sso_client_id` and `google_sso_client_secret` are set. Keep the secret in the Terraform secret store, never in `tfvars` committed to git.

## Optional IP allowlist

Set `access_ip_allowlist` (office/VPN CIDRs) to require a matching IP *in addition* to identity on admin, staging and Hangfire. Leave it empty to rely on identity alone.

## Defence in depth inside the API

`CloudflareAccessGuardMiddleware` refuses `/hangfire` and `/swagger` with 403 unless the request carries a valid `Cf-Access-Jwt-Assertion`. The check verifies:

- the signature, against `https://<team>/cdn-cgi/access/certs`, cached for 1 h;
- the issuer;
- the audience;
- the expiry.

This matters because the plain `Cf-Access-Authenticated-User-Email` header can be forged by anyone who reaches the origin some other way.

Configure the API (environment variables, never committed):

```
CloudflareAccess__TeamDomain=<team>.cloudflareaccess.com
CloudflareAccess__Audiences__0=<terraform output access_aud_tags.hangfire>
CloudflareAccess__Audiences__1=<terraform output access_aud_tags.swagger>
```

An empty `TeamDomain` turns the check off. Use that only for local development.

## Rollout

1. `terraform -chdir=infra/cloudflare plan`, then review and `apply`.
2. Copy the AUD tags from `terraform output access_aud_tags` into the API secrets above, then redeploy.
3. Check that `https://api.malabak.app/hangfire` shows the Access login, then the dashboard, and returns 403 when called with `curl` without Access.
4. Check that `https://admin.malabak.app` asks for Access, then the app's own TOTP login.

## Offboarding

Remove the person from `admin_allowed_emails` or `engineering_allowed_emails` (or from Google Workspace) and apply. Then revoke their open Access sessions in Zero Trust → My Team → Users → Revoke. Disable their Malabak staff account too: `POST /api/v1/admin/accounts/{id}/disable` signs them out everywhere.
