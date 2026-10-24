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
│  ├─ apps/venue/                      # Next.js App Router — Venue Dashboard
│  ├─ apps/admin/                      # Next.js App Router — Admin Dashboard
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

### Prerequisites
- [.NET 9/10 SDK](https://dotnet.microsoft.com/)
- [Node.js & pnpm](https://pnpm.io/)
- [Docker & Docker Compose](https://www.docker.com/)

### 1. Start Local Infrastructure
```bash
docker compose up -d
```
This launches:
- **PostgreSQL** at `localhost:5432`
- **Seq (Structured Logs)** at `http://localhost:5341`
- **Mailpit (Fake SMTP Web UI)** at `http://localhost:8025`
- **pgAdmin** at `http://localhost:5050`

### 2. Backend (Build & Test)
```bash
dotnet restore
dotnet build --warnaserror
dotnet test
```

### 3. Run the Backend API Host
```bash
dotnet run --project src/Malabak.Api
```
Browse OpenAPI / Swagger documentation at `http://localhost:5000/swagger`.

### 4. Run the Web Dashboards
```bash
cd web
pnpm install
pnpm dev
```
- Venue Dashboard: `http://localhost:3000`
- Admin Dashboard: `http://localhost:3001`

---

## 📜 Engineering Standards & AI Guidelines
All contributions must adhere strictly to [`rule.md`](./rule.md).
- **Time Zone:** Always `Africa/Cairo` with proper DST handling. Never hardcode `+02:00`.
- **Currency:** `EGP` (`ج.م`).
- **No Overlapping Bookings:** Guarded by Postgres exclusion constraints (`btree_gist`).
- **Zero Warnings:** All builds must compile cleanly with `TreatWarningsAsErrors`.
- **Frontend Decoupling:** Next.js uses BFF with `httpOnly` secure cookies; no business logic in React components.
