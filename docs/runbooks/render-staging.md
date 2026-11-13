# Runbook: Free staging on Render + Neon + Upstash

A no-credit-card alternative to [Fly staging](fly-staging.md): Render runs the API from the repo-root
`Dockerfile`, Neon hosts Postgres and Upstash hosts Redis, each on its free tier. The same rules apply:
**staging only**, test data only (ADR-0004). Never load real user data here.

Free-tier limits to expect: the Render service sleeps after a period without traffic, so the first
request afterwards is slow, and each provider caps usage. Check their pricing pages for the current
limits.

## 1. Postgres on Neon

1. Create a project in the region closest to Egypt that Neon offers (e.g. Frankfurt).
2. From **Connection details**, take the **direct** (not pooled) endpoint. Migrations and Hangfire need a
   session connection.
3. Write it in Npgsql form:
   `Host=<endpoint>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require`

## 2. Redis on Upstash

1. Create a Redis database in the same region, with TLS on and eviction off (OTP codes and rate-limit
   counters must not be evicted).
2. Write it in StackExchange.Redis form:
   `<endpoint>:6379,password=<password>,ssl=true,abortConnect=false`

## 3. API on Render

Create a **Web Service** from the GitHub repo with the **Docker** runtime, the branch to deploy, and the
free instance type. Set the health check path to `/health/live`. Add these environment variables:

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` |
| `PORT` | `8080` (the port the container listens on) |
| `Database__MigrateOnStartup` | `true` (Render's free tier has no release step) |
| `Hangfire__BypassDashboardAuthInDevelopment` | `false` |
| `ConnectionStrings__Postgres` and `Database__ConnectionString` | the Neon string from step 1 |
| `ConnectionStrings__Redis` and `Redis__ConnectionString` | the Upstash string from step 2 |
| `Security__Jwt__PrivateKeyPem` | ES256 key (multi-line PEM): `openssl ecparam -name prime256v1 -genkey -noout \| openssl pkcs8 -topk8 -nocrypt` |
| `Security__Otp__HmacSecret` | `openssl rand -hex 32` |
| `Security__StepUp__SecretKey` | `openssl rand -hex 32` |
| `Security__Turnstile__SecretKey` / `Security__Turnstile__SiteKey` | the staging Turnstile widget's keys |
| `Security__Cors__AllowedOrigins__0` | the staging web origin |
| `Identity__OtpDelivery__Provider` | `Slack` |
| `Identity__OtpDelivery__Slack__WebhookUrl` | Incoming Webhook for the private OTP channel |

Generate the keys fresh for staging; never reuse local or production values. Then deploy and check
`https://<service>.onrender.com/health/ready` and `/swagger`.

Seed reference data once, from your machine against the Neon database (the free tier may not offer a
shell on the service):

```bash
ASPNETCORE_ENVIRONMENT=Staging ConnectionStrings__Postgres="<neon string>" Database__ConnectionString="<neon string>" ConnectionStrings__Redis="<upstash string>" Redis__ConnectionString="<upstash string>" dotnet run --project src/Malabak.Api -- --seed-reference
```
