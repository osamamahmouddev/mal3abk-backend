# Runbook: Staging on Fly.io

Fly.io hosts **staging only**: test data, OTP codes and security alerts posted to Slack. It is not a
production target. ADR-0004 requires real users' data to stay in a Law 151/2020-compliant region, and
`ams` (Amsterdam) is not one. Never load real user data here.

The image is built from the repo-root `Dockerfile`; Fly reads `fly.toml`. Each deploy runs
`dotnet Malabak.Api.dll --migrate` as the release step, which applies pending EF Core migrations for
every module before new machines start. Swagger is served in Staging at `/swagger`.

## 1. One-time setup

Install `flyctl` and sign in, then from the repo root:

```bash
fly apps create mal3abk-backend
```

### Postgres

Create a Postgres cluster in the same region and note the host, user, password and database it prints:

```bash
fly postgres create --name mal3abk-staging-db --region ams
```

`fly postgres attach` sets a `DATABASE_URL` in URI form, which the API does not read. Set the
connection strings below in Npgsql key/value form instead.

### Redis

Create a Redis instance (Upstash on Fly) in the same region and note its host and password:

```bash
fly redis create
```

### Secrets

Set every value with `fly secrets set` (never in `fly.toml` or the repo). Generate the keys fresh for
staging; do not reuse local or production values.

| Secret | Value |
|---|---|
| `ConnectionStrings__Postgres` | `Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>` |
| `Database__ConnectionString` | same as above |
| `ConnectionStrings__Redis` | `<host>:6379,password=<password>,ssl=<true if the provider gives rediss://>,abortConnect=false` |
| `Redis__ConnectionString` | same as above |
| `Security__Jwt__PrivateKeyPem` | ES256 key: `openssl ecparam -name prime256v1 -genkey -noout \| openssl pkcs8 -topk8 -nocrypt`. Without it, every restart signs with a new key and logs everyone out. |
| `Security__Otp__HmacSecret` | `openssl rand -hex 32` |
| `Security__StepUp__SecretKey` | `openssl rand -hex 32` |
| `Security__Turnstile__SecretKey` / `SiteKey` | the staging Turnstile widget's keys |
| `Security__Cors__AllowedOrigins__0` | the staging web origin, e.g. `https://venue-staging.example.com` |
| `Identity__OtpDelivery__Provider` | `Slack` |
| `Identity__OtpDelivery__Slack__WebhookUrl` | an Incoming Webhook for a private channel (see README, "OTP codes in Slack") |

A multi-line PEM is easiest to set from a file:

```bash
fly secrets set Security__Jwt__PrivateKeyPem="$(cat staging-jwt.pem)"
```

## 2. Deploy

```bash
fly deploy
```

The release step applies migrations; if it fails, the deploy stops and the running machines are left
as they were. Then check:

```bash
curl -s https://mal3abk-backend.fly.dev/health/ready
```

Seed reference data once, after the first deploy:

```bash
fly ssh console -C "dotnet Malabak.Api.dll --seed-reference"
```

## 3. Notes

- Machines stop when idle (`auto_stop_machines`), so the first request after a quiet period is slow.
- The app refuses to start if Slack delivery is configured with `ASPNETCORE_ENVIRONMENT=Production`;
  that guard is intentional.
