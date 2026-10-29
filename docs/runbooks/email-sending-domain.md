# Runbook: Email sending domain (SPF, DKIM, DMARC, bounces)

The API sends account email (verification, password reset, staff invitations, new-device alerts) over SMTP.
Locally that's Mailpit (`docker compose up`, UI on http://localhost:8025). In staging/production it is an SMTP
relay from a transactional provider (e.g. Amazon SES, Postmark, SendGrid). Without the DNS records below, mail lands
in spam or is rejected — and password-reset email is a security feature, so this is not optional.

## 1. Application configuration (secret store, never appsettings)

| Key | Example | Notes |
|---|---|---|
| `Email:Smtp:Host` | `email-smtp.eu-west-1.amazonaws.com` | provider SMTP endpoint |
| `Email:Smtp:Port` | `587` | STARTTLS |
| `Email:Smtp:EnableSsl` | `true` | must be true outside local dev |
| `Email:Smtp:Username` / `Password` | provider SMTP credentials | rotate on staff changes |
| `Email:From` / `Email:FromName` | `no-reply@mail.malabak.app` / `Malabak \| ملعبك` | use a dedicated sending subdomain |
| `Email:WebhookSecret` | 32+ random bytes | HMAC key for the bounce webhook; webhook is disabled when empty |
| `App:VenueDashboardUrl` | `https://venue.malabak.app` | base for links in emails |

## 2. DNS records for the sending subdomain (`mail.malabak.app`)

Use a subdomain so marketing or other mail can never hurt transactional deliverability.

| Type | Name | Value | Purpose |
|---|---|---|---|
| TXT | `mail.malabak.app` | `v=spf1 include:<provider-spf-domain> -all` | SPF: only the provider may send for this domain (hard fail otherwise) |
| CNAME/TXT | `<selector>._domainkey.mail.malabak.app` | value given by the provider | DKIM: messages are signed; rotate keys yearly |
| TXT | `_dmarc.mail.malabak.app` | `v=DMARC1; p=quarantine; rua=mailto:dmarc@malabak.app; adkim=s; aspf=s; pct=100` | DMARC: start with `p=none` for 2 weeks, read reports, then `quarantine`, later `reject` |
| MX (optional) | `mail.malabak.app` | provider's feedback MX | needed by some providers for bounce/complaint processing |

Verification: send a test email to a Gmail account → "Show original" must show `SPF: PASS`, `DKIM: PASS`, `DMARC: PASS`.

## 3. Bounces and complaints

Configure the provider to POST bounce/complaint events (directly or via a tiny relay that normalizes its format) to:

```
POST https://api.malabak.app/api/v1/webhooks/email/bounce
X-Malabak-Signature: hex(HMAC-SHA256(Email:WebhookSecret, raw body))
{"email":"user@example.com","type":"hard" | "complaint" | "soft"}
```

- `hard` / `complaint` → the address (stored only as a SHA-256 hash) is added to `identity.email_suppressions`; the sender
  skips it from then on. `soft` bounces are ignored (temporary).
- Unsigned or wrongly signed requests get 403.
- To un-suppress an address (user fixed their mailbox), delete its row by hash after confirming with the user.

## 4. Monitoring

- Alert if the bounce rate exceeds 5% or complaints exceed 0.1% in 24h (providers suspend accounts above these).
- Watch the `Failed to send account email` log entries — SMTP outages break sign-up and password reset.
