# Malabak Backend & Web (ملعبك)

[![CI Build & Test](https://github.com/osamamahmouddev/mal3abk-backend/actions/workflows/ci.yml/badge.svg)](https://github.com/osamamahmouddev/mal3abk-backend/actions/workflows/ci.yml)

A high-performance, modular monolith backend and Next.js web ecosystem powering the **Malabak (ملعبك)** football marketplace, team community, and SaaS pitch venue management platform in Egypt.

---

## 🏛 Architecture Overview

Built using **Modular Monolith** and **Clean Architecture** with Vertical Slice feature organization on the backend, paired with a modern **Next.js App Router** frontend:

- **Strict Boundaries:** Each bounded context is isolated with its own schemas, contracts, and DbContexts.
- **Layers (inward dependency flow):**
  `Api` ➔ `Application` ➔ `Domain` ⮜ `Infrastructure`
- **Frontend Isolation:** The web dashboards talk to the backend **only through the REST API** (no shared C# code).
- **Core Engineering Rules:** Stored in [`rule.md`](./rule.md).
- **Security Architecture & Protocols:** Stored in [`SECURITY.md`](./SECURITY.md).

---

## 📦 Solution & Repository Layout

```
Malabak.sln
├─ src/
│  ├─ Malabak.SharedKernel/            # Primitives: Result, Money, Clock, Entity
│  ├─ Malabak.Modules.Identity/        # Players & Staff Auth (Phone OTP, Email)
│  ├─ Malabak.Modules.Venues/          # Venues, Pitches, Operating Hours, Schedules
│  ├─ Malabak.Modules.Booking/         # Booking Aggregate, State Machine, Holds
│  ├─ Malabak.Modules.Payments/        # Manual Payment Methods, Proofs, Reviews
│  ├─ Malabak.Modules.Teams/           # Squads, Memberships, Invitations
│  ├─ Malabak.Modules.Matches/         # Challenges, Bilateral Match Results, Moderation
│  ├─ Malabak.Modules.Notifications/   # Outbox, Push (FCM), In-App
│  ├─ Malabak.Modules.Subscriptions/   # SaaS Tiers, Entitlements, Limits
│  ├─ Malabak.Modules.Support/         # Helpdesk & Tickets
│  ├─ Malabak.Modules.Audit/           # Append-Only Audit Trail
│  └─ Malabak.Api/                     # Versioned REST API (/api/v1/...)
├─ web/                                # pnpm + Turborepo monorepo
│  ├─ apps/landing/                    # Next.js App Router — Public landing page (SEO, static)
│  ├─ apps/venue/                      # Next.js App Router — Venue Dashboard
│  ├─ apps/admin/                      # Next.js App Router — Admin Dashboard (scaffold only)
│  ├─ packages/ui/                     # Shared design tokens, RTL helpers, components
│  ├─ packages/api-client/             # TypeScript client GENERATED from OpenAPI
│  └─ packages/i18n/                   # Arabic (RTL, primary) & English messages
├─ tests/
│  ├─ Malabak.UnitTests/               # Domain Aggregates & Value Objects
│  ├─ Malabak.IntegrationTests/        # API Endpoints & Database Tests
│  └─ Malabak.ArchitectureTests/       # NetArchTest Enforcing Boundaries
├─ docs/                               # PRD, Architecture Decision Records (ADRs)
└─ docker-compose.yml                  # PostgreSQL, Seq, Mailpit
```

---

## 🚀 Getting Started

### Ports at a glance

| What | URL | Started by |
|---|---|---|
| Landing page (public) | http://localhost:3002 | `pnpm --filter @malabak/landing dev` |
| Venue Dashboard | http://localhost:3000 | `pnpm --filter @malabak/venue-dashboard dev` |
| Admin Dashboard | http://localhost:3001 | scaffold only — no pages yet |
| Backend API + Swagger | http://localhost:5000/swagger | `dotnet run --project src/Malabak.Api` |
| PostgreSQL | `localhost:5432` | `docker compose up -d` |
| Redis (TLS only) | `localhost:6379` | `docker compose up -d` |
| pgAdmin | http://localhost:5050 | `docker compose up -d` |
| Seq (structured logs) | http://localhost:5341 | `docker compose up -d` |
| Mailpit (fake SMTP UI) | http://localhost:8025 (SMTP on `1025`) | `docker compose up -d` |

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS)
- [Node.js 20+](https://nodejs.org/) and **pnpm 9** (the version pinned in `web/package.json`)
- [Docker & Docker Compose](https://www.docker.com/)

### 1. Start local infrastructure
Redis only accepts TLS, also locally. Create the local dev CA and Redis certificate once per machine (they go to the git-ignored `docker/redis/tls/`), then start the containers:
```bash
./scripts/generate-dev-certs.sh
```
```bash
docker compose up -d
```
Database credentials (dev only): database `malabak_dev`, user `malabak`, password `malabak_dev_password`. pgAdmin login: `admin@malabak.com` / `admin`.

### 2. Backend — build & test
```bash
dotnet restore
dotnet build --warnaserror
dotnet test
```
Integration tests start their own throwaway PostgreSQL 17 and Redis containers (Testcontainers), so Docker must be running; they don't use the compose services or any committed credentials. To reuse already-running services instead, set `MALABAK_TEST_POSTGRES` and `MALABAK_TEST_REDIS` to their connection strings.

### 3. Backend — run the API
Connection strings are not stored in the repo (see `SECURITY.md`). Store the local ones in .NET User Secrets once per machine — the script uses the same values as `docker-compose.yml`:
```bash
./scripts/setup-dev-secrets.sh
```
Then run:
```bash
dotnet run --project src/Malabak.Api
```

#### OTP codes in Slack (until an SMS provider is integrated)
No SMS provider is connected yet, so login and phone-change codes can be posted to a Slack channel instead. By default no channel is configured and `/auth/otp/send` returns **503** (fail closed); codes are never written to the logs.

1. In Slack, create an app with an **Incoming Webhook** for a private channel (e.g. `#malabak-otp`) that only the team can see.
2. Store the webhook in User Secrets — it is a credential, never put it in `appsettings*.json`:
   ```bash
   dotnet user-secrets set "Identity:OtpDelivery:Provider" "Slack" --project src/Malabak.Api
   ```
   ```bash
   dotnet user-secrets set "Identity:OtpDelivery:Slack:WebhookUrl" "https://hooks.slack.com/services/…" --project src/Malabak.Api
   ```
   On staging, set `Identity__OtpDelivery__Provider=Slack` and `Identity__OtpDelivery__Slack__WebhookUrl` from the secret store.

Each message shows the masked number (`+2010•••••678`), the purpose, the code and its validity. **The API refuses to start with the Slack provider in Production**: everyone in the channel could read every code and sign in as anyone.

The same setting delivers security alerts (phone changed, phone restored, new-device sign-in, staff invitations without an email) to the same channel, with masked numbers and the recovery or invitation link. With no channel configured, alerts are dropped and only their kind is logged — never the number, the message or a token. Step-up codes (`POST /auth/step-up/otp`) use the same channel.

#### OTP codes over SMS
`Identity:OtpDelivery:Provider` = `Sms` sends codes and alerts through the SMS gateway (`Sms` section): Egypt-only allow-list, daily/monthly spend caps, and failover across the Twilio accounts listed in `Sms:Providers`. Twilio credentials go in the secret store, never in `appsettings*.json`. In Development with no `Sms:Providers` configured, messages are delivered to the local Mailpit inbox (http://localhost:8025) as `sms+NUMBER@malabak.local`. If every provider fails or a cap is reached, OTP requests return **503**.
Swagger / OpenAPI: http://localhost:5000/swagger

**Field-encryption keys (optional locally, required outside Development).** Without them the API generates throwaway keys on every start, so anything encrypted can't be read after a restart. To keep encrypted data across restarts, put random 32-byte keys in User Secrets (never in `appsettings`):
```bash
dotnet user-secrets --project src/Malabak.Api set "Security:Encryption:CurrentKeyEncryptionKeyId" "local-kek-1"
dotnet user-secrets --project src/Malabak.Api set "Security:Encryption:KeyEncryptionKeys:local-kek-1" "$(openssl rand -base64 32)"
dotnet user-secrets --project src/Malabak.Api set "Security:Encryption:BlindIndexKey" "$(openssl rand -base64 32)"
```
See [ADR-0003](docs/adr/0003-field-level-encryption-and-key-management.md) for the design and how rotation works.

### 4. Web apps

Install once, from the `web/` folder:
```bash
cd web
pnpm install
```

Run **one** app (recommended while working on it):
```bash
pnpm --filter @malabak/landing dev           # http://localhost:3002
pnpm --filter @malabak/venue-dashboard dev   # http://localhost:3000
```

Or run every app at once with Turborepo: `pnpm dev` (this also starts the admin scaffold on 3001, which has no pages yet).

| App | Folder | Notes |
|---|---|---|
| Landing | `web/apps/landing` | Public marketing page for players and venue owners. Fully static (good for SEO). See [its README](./web/apps/landing/README.md). |
| Venue Dashboard | `web/apps/venue` | Overview, calendar (day / week / month), reservations & deposit-proof review, pitches, schedule, pricing, policies, customers, reports, subscription, notifications, staff, settings, login, setup wizard. Uses sample data until the API is wired. |
| Admin Dashboard | `web/apps/admin` | Package scaffold only. |

Other useful commands (from `web/`):
```bash
pnpm build   # production build of every app (also type-checks them)
pnpm lint
```

### Troubleshooting

- **`ERR_PNPM_BAD_PM_VERSION`** — your global pnpm is newer than the pinned 9.0.0. Run pnpm 9 through npx instead:
  ```bash
  npx -y pnpm@9.0.0 install
  ```
  Without pnpm you can still start a single app with npm: `npm --prefix web/apps/landing run dev`.
- **Pages show "Internal Server Error" or load without styles** — a `next build` was run while `next dev` was running for the same app; both write to that app's `.next/` folder. Stop the dev server, delete the app's `.next/` folder, and start it again:
  ```bash
  rm -rf web/apps/venue/.next
  ```
- **Redis connection fails after pulling the TLS change** — your Redis container still runs without TLS. Run `./scripts/generate-dev-certs.sh`, then `docker compose up -d redis` and `./scripts/setup-dev-secrets.sh`. Check it with:
  ```bash
  docker exec malabak-redis redis-cli --tls --cacert /tls/ca.crt -a malabak_dev_redis_password --no-auth-warning ping
  ```
- **Port already in use** — another dev server is still running on that port. Stop it, or check what holds it with `ss -ltnp | grep :3000`.

---

## 📜 Engineering Standards & AI Guidelines
All contributions must adhere strictly to [`rule.md`](./rule.md).
- **Time Zone:** Always `Africa/Cairo` with proper DST handling. Never hardcode `+02:00`.
- **Currency:** `EGP` (`ج.م`).
- **No Overlapping Bookings:** Guarded by Postgres exclusion constraints (`btree_gist`).
- **Zero Warnings:** All builds must compile cleanly with `TreatWarningsAsErrors`.
- **Frontend Decoupling:** Next.js uses BFF with `httpOnly` secure cookies; no business logic in React components.
