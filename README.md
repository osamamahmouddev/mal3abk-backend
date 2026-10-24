# Malabak Backend (ملعبك)

[![CI Build & Test](https://github.com/osamamahmouddev/mal3abk-backend/actions/workflows/ci.yml/badge.svg)](https://github.com/osamamahmouddev/mal3abk-backend/actions/workflows/ci.yml)

A high-performance, modular monolith backend powering the **Malabak (ملعبك)** football marketplace, team community, and SaaS pitch venue management platform in Egypt.

---

## 🏛 Architecture Overview

Built using **Modular Monolith** and **Clean Architecture** with Vertical Slice feature organization:

- **Strict Boundaries:** Each bounded context is isolated with its own schemas, contracts, and DbContexts.
- **Layers (inward dependency flow):**
  `Api / Web` ➔ `Application` ➔ `Domain` ⮜ `Infrastructure`
- **Shared Kernel:** Ultra-lightweight common primitives (`Result<T>`, `Error`, `Money`, `Entity<TId>`, `IClock`, Domain Events).
- **Core Engineering Rules:** Stored in [`rule.md`](./rule.md).

---

## 📦 Solution Structure

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
│  ├─ Malabak.Api/                     # Versioned REST API (/api/v1/...)
│  ├─ Malabak.Web.Venue/               # Blazor Web App (Venue Dashboard)
│  └─ Malabak.Web.Admin/               # Blazor Web App (Admin Dashboard)
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
- [.NET 9 SDK](https://dotnet.microsoft.com/)
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

### 2. Build & Test
```bash
dotnet restore
dotnet build --warnaserror
dotnet test
```

### 3. Run the API Host
```bash
dotnet run --project src/Malabak.Api
```
Browse OpenAPI / Swagger documentation at `http://localhost:5000/swagger`.

---

## 📜 Engineering Standards & AI Guidelines
All contributions must adhere strictly to [`rule.md`](./rule.md).
- **Time Zone:** Always `Africa/Cairo` with proper DST handling. Never hardcode `+02:00`.
- **Currency:** `EGP` (`ج.م`).
- **No Overlapping Bookings:** Guarded by Postgres exclusion constraints (`btree_gist`).
- **Zero Warnings:** All builds must compile cleanly with `TreatWarningsAsErrors`.
