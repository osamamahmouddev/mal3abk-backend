# rule.md — Malabak (ملعبك) Engineering Rules for AI Assistants

> This file is the source of truth for how AI assistants (Claude Code, Copilot, Cursor, etc.) must work on this repo.
> Read it fully before writing any code. If a rule here conflicts with a chat request, say so and ask.
> Product requirements live in `docs/PRD.md` (the Football Community & Pitch Booking PRD). This file is about **how we build**, the PRD is about **what we build**.

---

## 0. Project snapshot

- **Product:** Football marketplace + team community + SaaS venue management. Market: Egypt. Languages: Arabic (RTL, primary) + English.
- **Three products, one backend:**
  1. Player App (mobile-first)
  2. Venue Dashboard (web, desktop-first)
  3. Admin Dashboard (web)
- **Backend stack (defaults — change only with an ADR, see §14):**
  - .NET 10 (LTS) / ASP.NET Core, C# latest, nullable enabled
  - PostgreSQL + EF Core (Npgsql)
  - ASP.NET Core Identity + JWT (access + refresh tokens), phone OTP login
  - Background jobs: Hangfire (or Quartz.NET)
  - Real-time: SignalR (dashboards); push: FCM
  - File storage: private blob storage (local disk in dev, S3/Azure Blob in prod)
  - Web (Venue + Admin dashboards, and later a public venue/SEO site): **Next.js (App Router) + TypeScript + Tailwind + shadcn/ui**, living in `web/`. The web apps talk to the backend **only through the REST API** (no shared C# code).
  - Mobile client: consumes the REST API only (Flutter or .NET MAUI — decision pending, API must not care)
  - Logging: Serilog → structured logs; Health checks; OpenTelemetry
  - Tests: xUnit, FluentAssertions, Testcontainers (real PostgreSQL), Respawn, Playwright for UI smoke
- **Currency:** EGP. **Time zone:** `Africa/Cairo` (has DST — never hardcode +02:00).

---

## 1. How the AI must work (process rules)

1. **Read before writing.** Open the relevant PRD section and existing code first. Never invent requirements, entities, or endpoints that aren't in the PRD or this file.
2. **Small steps.** One feature/slice per change. Don't refactor unrelated code inside a feature change.
3. **Plan first for anything non-trivial** (more than ~3 files or any DB change): write a short plan (what, files, migration, tests), then implement.
4. **Ask when ambiguous and expensive to redo** (data model, money, booking state, auth). Otherwise pick the sensible default, implement, and state the assumption in the PR description.
5. **Always add/adjust tests** with the code. No feature is "done" without them (see §10).
6. **Run before claiming done:** `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`. Report real results; never say "should work".
7. **Never fake it.** Do not stub payment verification, availability, or auth and present it as working. Mark unfinished parts with `// TODO(malabak-123): ...` and list them in the PR.
8. **Never commit secrets** (connection strings, JWT keys, API keys). Use User Secrets locally, env vars/secret manager in prod. If you see a secret in code, stop and flag it.
9. **Never edit applied migrations.** Add a new one.
10. **Don't add NuGet packages casually.** Each new dependency needs a one-line justification, a check of its licence (some popular libs went commercial: e.g. MediatR, AutoMapper, MassTransit v9+), and maintenance status.
11. **Match existing style** before introducing a new pattern. Consistency beats cleverness.
12. **Be explicit about trade-offs** in PR descriptions: what you chose, what you rejected, why.

### Definition of Done (every change)
- [ ] Builds with zero warnings
- [ ] Unit + integration tests added and green
- [ ] Authorization checked server-side
- [ ] Input validated; errors return ProblemDetails
- [ ] Arabic + English strings added to resource files (no hardcoded UI text)
- [ ] Audit log written for sensitive actions (§9)
- [ ] Migration included if schema changed, and it is reversible/safe
- [ ] Docs/ADR updated if a decision changed

---

## 2. Architecture

### 2.1 Style: Modular Monolith + Clean Architecture (+ vertical slices)

One deployable app, split into **modules** (bounded contexts) with strict boundaries. Easy to ship now, easy to split later.

**Modules:** `Identity`, `Venues` (venues, pitches, schedules, pricing), `Booking` (bookings, holds, policies), `Payments` (manual methods, proofs, reviews), `Teams` (teams, memberships, invitations), `Matches` (challenges, matches, results, rankings), `Notifications`, `Subscriptions` (plans, entitlements, invoices), `Support`, `Audit`.

Rules:
- A module talks to another module **only through its public contract** (interface / integration event), never by touching its DbContext or entities.
- No circular module references. Enforce with architecture tests (NetArchTest / ArchUnitNET).
- Shared kernel stays tiny: `Result<T>`, `Money`, `Error`, base `Entity`, `IClock`, domain-event interfaces.

### 2.2 Layers (dependencies point inward only)

```
Api  →  Application  →  Domain
                 ↑
          Infrastructure   (implements Application interfaces)
```

- **Domain** — entities, value objects, domain events, business rules. No EF, no ASP.NET, no I/O.
- **Application** — use cases (commands/queries), validators, DTOs, interfaces (`IBookingRepository`, `IClock`, `IFileStorage`). No HTTP, no EF-specific types leaking out.
- **Infrastructure** — EF Core, Identity, storage, FCM, Hangfire, email/SMS. Implements Application interfaces.
- **Api** — controllers/minimal-API endpoints, DI wiring. **Thin**: parse → call use case → map to response.

### 2.3 Solution layout

```
Malabak.sln
├─ src/
│  ├─ Malabak.SharedKernel/
│  ├─ Malabak.Modules.Identity/        (Domain, Application, Infrastructure folders inside each module)
│  ├─ Malabak.Modules.Venues/
│  ├─ Malabak.Modules.Booking/
│  ├─ Malabak.Modules.Payments/
│  ├─ Malabak.Modules.Teams/
│  ├─ Malabak.Modules.Matches/
│  ├─ Malabak.Modules.Notifications/
│  ├─ Malabak.Modules.Subscriptions/
│  ├─ Malabak.Modules.Support/
│  ├─ Malabak.Modules.Audit/
│  └─ Malabak.Api/                     (REST API for mobile + web dashboards)
├─ web/                                (pnpm + Turborepo monorepo)
│  ├─ apps/venue/                      (Next.js — venue dashboard)
│  ├─ apps/admin/                      (Next.js — admin dashboard)
│  ├─ packages/ui/                     (shared components, design tokens, RTL helpers)
│  ├─ packages/api-client/             (TypeScript client GENERATED from OpenAPI)
│  └─ packages/i18n/                   (ar.json / en.json messages)
├─ tests/
│  ├─ Malabak.UnitTests/
│  ├─ Malabak.IntegrationTests/
│  └─ Malabak.ArchitectureTests/
├─ docs/  (PRD.md, adr/, api/, runbooks/)
├─ docker-compose.yml   (postgres, pgadmin, seq, mailpit)
├─ Directory.Build.props   (nullable, warnaserror, analyzers, versions)
├─ Directory.Packages.props (central package management)
├─ .editorconfig
└─ rule.md
```

Inside a module, organise by **feature (vertical slice)**, not by technical type:

```
Booking/Application/Bookings/CreateBooking/
   CreateBookingCommand.cs
   CreateBookingHandler.cs
   CreateBookingValidator.cs
   CreateBookingResponse.cs
```

### 2.4 Handlers / mapping / validation
- Use plain handler interfaces (`ICommandHandler<TCmd,TResult>`) or the free source-generator `Mediator` library. Don't add MediatR (commercial licence).
- Validation: **FluentValidation** (run in a pipeline/endpoint filter).
- Mapping: **Mapperly** (compile-time) or hand-written. No AutoMapper (commercial licence, hides bugs).
- Errors: **Result pattern** for expected failures (`Result<T>` with `Error` codes). Exceptions only for truly exceptional situations. Map Result → HTTP in one place.

---

## 3. Software-engineering principles to apply

Use these as a checklist while designing and reviewing.

| Principle | What it means here |
|---|---|
| **SOLID** | Small classes with one reason to change; depend on interfaces; extend via new classes (e.g., new payment method = new strategy, not an `if` chain). |
| **DRY, but not at any cost** | Dedupe business rules (price calc, slot conflict, status transitions). Don't dedupe code that merely looks similar. |
| **KISS / YAGNI** | Build MVP only (PRD §4.1). Don't build tournaments/ads/payment gateway in MVP. |
| **Separation of concerns** | Controllers don't contain business rules. Entities don't know about HTTP. |
| **Domain-Driven Design (lite)** | Rich domain model: `Booking.Confirm()`, `Booking.Reject(reason)` enforce invariants. Aggregates: `Booking`, `Venue` (with `Pitch` children), `Team`, `Match`, `Subscription`. Reference other aggregates by Id only. |
| **Make illegal states unrepresentable** | State machines (§6), value objects (`Money`, `PhoneNumber`, `TimeSlot`), private setters. |
| **Fail fast** | Validate at boundaries; guard clauses in domain constructors/methods. |
| **Idempotency** | Retry-safe commands (client-supplied `Idempotency-Key` for booking creation and uploads). |
| **Eventual consistency** | Cross-module side effects (notifications, audit, stats) via domain/integration events + outbox, not inline calls. |
| **Defense in depth** | UI hides, API authorizes, domain enforces, DB constrains (unique/exclusion constraints). |
| **Principle of least privilege** | Roles/policies grant the minimum; venue staff see only their venue's data. |
| **12-Factor** | Config via env, stateless app, logs to stdout, dev/prod parity (Docker). |
| **Observability** | Structured logs with correlation id, metrics, traces, health checks. |
| **Testing pyramid** | Many unit tests on domain, fewer integration tests on DB/API, few E2E. |
| **Documentation as code** | ADRs for decisions, OpenAPI for the API, README per module. |

Patterns we use (and when):
- **Repository + Unit of Work** — only for aggregates; queries can use EF directly in read-side handlers (CQRS-lite: commands go through the domain, queries project straight to DTOs with `AsNoTracking` + `Select`).
- **Outbox pattern** — events written in the same transaction, published by a background job (reliable notifications).
- **State pattern / state machine** — booking, challenge, match result, subscription, support ticket.
- **Strategy** — price rules, ranking algorithm (configurable, PRD §7.6), payment methods.
- **Specification / query objects** — complex search filters (Explore screen).
- **Options pattern** — all config typed (`IOptions<T>`), validated on startup.
- **Anti-corruption layer** — wrap external services (FCM, SMS, storage) behind our interfaces.

---

## 4. Coding standards (C#)

- `Nullable` enabled, `TreatWarningsAsErrors` true. Use `.editorconfig` + Roslyn analyzers; `dotnet format` must be clean.
- Naming: `PascalCase` types/methods/properties, `camelCase` locals/params, `_camelCase` private fields, `IFoo` interfaces, `Async` suffix on async methods.
- Files: one public type per file; file name = type name; file-scoped namespaces.
- Prefer `record` for DTOs/commands/value objects, `sealed` classes by default.
- **Async all the way**; pass `CancellationToken` through every I/O method; never `.Result` / `.Wait()`.
- No static mutable state. No service locator. Constructor injection only.
- No magic strings/numbers: constants, enums, or options.
- Time: always inject `IClock` / `TimeProvider`. **Never** `DateTime.Now`/`UtcNow` in domain or handlers.
- Money: `decimal` + currency in a `Money` value object; `numeric(18,2)` in DB. Never `double`/`float`.
- IDs: `Guid` v7 (sortable) generated in code. Public booking reference: short human code (e.g., `MLB-4F7K2Q`) stored with a unique index — never expose sequential ints.
- Comments explain **why**, not what. Public APIs get XML docs.
- Logging: structured (`_logger.LogInformation("Booking {BookingId} confirmed", id)`); never log PII, tokens, or payment details.
- Keep methods short (~30 lines) and classes cohesive; cyclomatic complexity low.

---

## 5. Database & EF Core rules

- One `DbContext` **per module** (own schema: `booking.*`, `venues.*`, `identity.*`, …). No cross-module joins in EF — use contracts/read models.
- Fluent configuration in `IEntityTypeConfiguration<T>` classes; no data annotations on domain entities.
- snake_case naming (`EFCore.NamingConventions`).
- All timestamps `timestamptz` stored in **UTC**. Convert to venue local time only at the edge.
- Every table: `Id`, `CreatedAtUtc`, `UpdatedAtUtc`, `RowVersion` (optimistic concurrency, use Postgres `xmin`). Soft delete (`IsDeleted`) only where the PRD needs history; otherwise hard delete is forbidden for bookings/payments/audit.
- Index every FK and every column used in filters/sorts (venue search: area, price, rating; booking: pitch+time).
- Migrations: one per change, descriptive name, reviewed, never edited after merge. Seed data in a separate seeder, not in migrations (except reference data like roles/plans).
- **Booking double-booking protection is enforced by the database**, not only code:
  ```sql
  CREATE EXTENSION IF NOT EXISTS btree_gist;
  ALTER TABLE booking.bookings
    ADD CONSTRAINT no_overlapping_active_bookings
    EXCLUDE USING gist (pitch_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
    WHERE (status IN ('Pending','ProofSubmitted','UnderReview','Confirmed'));
  ```
  Handle the resulting violation as a `Conflict` result (HTTP 409), not a 500.
- Avoid N+1: use `Select` projections / `Include` deliberately; test with query logging on.
- Pagination everywhere on lists (keyset for large/infinite lists, offset for admin tables). Never return unbounded collections.
- Store price + policy **snapshot** on the booking at creation (PRD §6.5). Later price/policy edits must not change existing bookings.

---

## 6. Core domain rules (from the PRD — implement exactly)

### 6.1 Booking state machine

States: `Draft, PendingDeposit, ProofSubmitted, UnderReview, Confirmed, Rejected, Expired, Cancelled, Completed, NoShow`.

| From | Event | To | Who |
|---|---|---|---|
| (new) | create request | PendingDeposit | Player / Venue (manual booking may go straight to Confirmed per policy) |
| PendingDeposit | upload proof | ProofSubmitted | Player |
| PendingDeposit | expiry timer | Expired | System |
| ProofSubmitted | venue opens | UnderReview | Venue |
| ProofSubmitted/UnderReview | approve | Confirmed | Venue staff |
| ProofSubmitted/UnderReview | reject (reason) | Rejected* | Venue staff |
| Confirmed | cancel (reason) | Cancelled | Player/Venue (policy checked) |
| Confirmed | match ended | Completed | System/Venue |
| Confirmed | no-show | NoShow | Venue |

\* Rejected may allow re-upload if enabled in the venue policy → back to `PendingDeposit` while the hold is still valid.

- All transitions live **inside the `Booking` aggregate** as methods that throw/return `Error` on illegal moves.
- Every transition writes a `BookingStatusHistory` row (actor, from, to, reason, timestamp) and an audit record.
- Rejection and cancellation **require a reason**.
- A pending booking holds the slot until `HoldExpiresAtUtc` (from venue policy). A Hangfire job expires holds, releases the slot, notifies the player, keeps history.
- Uploading a proof ≠ verified payment. Three separate events: **ProofSubmitted** (player), **PaymentReviewed** (venue), **BookingConfirmed**. Never name or label anything "verified" unless a venue user approved it.
- Never show an unverified/unavailable/past slot as bookable. Availability = operating hours − blocked slots − active bookings − holds.
- Refund status is tracked **separately** from booking status (manual refunds in MVP).

### 6.2 Slots, time, overnight hours
- Operating hours can cross midnight (e.g., 10:00 → 02:00). Model a slot with `BusinessDate` + `StartUtc/EndUtc`; a 01:00 slot belongs to the previous business date.
- Convert with `TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")` (Linux) — handle DST transitions in tests.
- Slot increments, min notice, max advance window come from the pitch/venue config.

### 6.3 Pricing
- Price is computed **only on the server** by one `PricingService` (default hourly → pitch override → peak/weekend/holiday/special-date rules → discounts). Rule precedence documented and unit-tested.
- Client shows the quote; the server recomputes at submit and rejects if it differs materially (return the new quote).

### 6.4 Teams, challenges, results
- Challenge ≠ booking. An accepted challenge does **not** create or confirm a pitch booking.
- Challenge states: `Pending, Accepted, Declined, CounterProposed, Scheduled, Completed, Cancelled, Disputed`.
- Result flow: submit (one team) → opponent confirms/disputes → confirmed = official → disputed goes to moderation. **Disputed or unconfirmed results never affect rankings.** Timeout policy configurable.
- Ranking algorithm is a pluggable strategy (`IRankingStrategy`), configurable, recalculable when an official result is corrected.
- Only authorised captains/members can act; check team membership server-side.

### 6.5 Subscriptions (SaaS)
- Plans and limits are data, editable by admin. Enforce limits **in the backend** (`IEntitlementService.EnsureCanCreate(Pitch|Venue|Staff)`), not only in the UI.
- Expired/suspended subscription: block creating **new** resources; existing bookings/data stay accessible; never auto-cancel customers' bookings.
- MVP subscription payments are handled manually by the admin. Don't build or fake a self-service billing flow.

### 6.6 Approvals
- Venue **approval** (admin decision) is separate from **published/visible** status (venue's choice). Both must be true to show in search.

---

## 7. Authentication, authorization & security

> **SECURITY MANDATE:** Detailed security specifications, threat models, cryptographic rules (ES256, Argon2id, AES-256-GCM), phone change state machines, WS tickets, and rate limiting policies are fully specified in [SECURITY.md](SECURITY.md). All AI assistants must adhere strictly to `SECURITY.md`. If a rule here conflicts with `SECURITY.md`, the stricter rule in `SECURITY.md` takes precedence.

- **Auth:** ASP.NET Core Identity; phone number + OTP for players; email/password (+ optional 2FA) for venue & admin. JWT access tokens (short-lived, ~15 min) + rotating refresh tokens (stored hashed). Rate-limit OTP and login.
- **Roles:** `Player`, `TeamCaptain` (derived from team membership, not a global role), `VenueOwner`, `VenueStaff`, `Admin`, `SupportAgent`, `Finance`. A user can hold multiple roles.
- **Authorization is policy-based and resource-based**, enforced on the server:
  - `[Authorize(Policy = "...")]` on every endpoint — **deny by default** (fallback policy requires auth).
  - Resource checks in handlers: "is this user staff of the venue that owns this booking?" "is this user a member/captain of this team?" — never trust ids from the client alone (prevent IDOR).
  - Admin navigation/permissions depend on role claims; hide in UI **and** enforce on API.
- **Uploads (payment proofs):** validate extension + magic bytes + size, re-encode images, random file names, store in a **private** bucket, serve through an authorized endpoint with short-lived signed URLs, never from `wwwroot`. Define retention/deletion.
- **Input safety:** parameterized queries only (EF), no raw SQL string concat, never use `dangerouslySetInnerHTML` with user content, CSRF protection (SameSite cookies + anti-forgery token) on cookie-auth endpoints, CORS allow-list, HTTPS + HSTS, security headers, request size limits.
- **Rate limiting:** built-in `RateLimiter` middleware on auth, OTP, booking creation, uploads.
- **Privacy:** collect minimum data; never expose a player's precise location to others (round/blur; store only city/area unless needed); customer records visible only to that venue's staff; payment details only to authorised users; no PII in logs.
- **Secrets:** User Secrets / env / secret manager. Data Protection keys persisted for multi-instance.
- Run `dotnet list package --vulnerable` in CI; fail on high severity.

---

## 8. API design

- REST, JSON, `/api/v1/...`, versioned (Asp.Versioning). Resource-oriented nouns, plural: `/venues/{id}/pitches`, `/bookings/{id}/proofs`.
- Verbs: `GET` read, `POST` create/commands, `PUT/PATCH` update, `DELETE` remove. State transitions as sub-resources: `POST /bookings/{id}/cancel`, `/approve`, `/reject`.
- DTOs only — never expose entities. Separate request/response models per endpoint.
- Errors: RFC 9457 **ProblemDetails** with stable machine `code` + localized `title/detail` + field errors. Correct status codes (400 validation, 401, 403, 404, 409 conflict, 422 business rule, 429).
- Pagination: `?page=&pageSize=` (max 50) or cursor; include total/next link.
- Filtering/sorting via explicit whitelisted query params (Explore: area, date, time range, budget, pitch type, surface, rating, facilities; sort: relevance, distance, price, rating).
- `Idempotency-Key` header on booking creation and proof upload.
- Localization: `Accept-Language: ar|en`; dates in ISO 8601 UTC in the API, localized only in UI; amounts as number + `currency`.
- OpenAPI/Swagger generated and kept accurate; contract tests for the mobile app.
- Enable response compression, caching headers for public read endpoints (venue details), ETags where useful.

---

## 9. Auditing, notifications, background work

- **Audit log** (actor, action, entity, timestamp, before/after, IP/correlation id) for: booking status changes, payment-proof decisions, venue approvals, subscription changes, suspensions, admin edits, dispute resolutions. Append-only; no update/delete.
- **Notifications:** raised from domain events → outbox → dispatcher → in-app (DB) + push (FCM). Each has a deep link (`malabak://booking/{id}`), localized text, retry with backoff, dedupe key.
- **Background jobs (Hangfire):** expire holds, send reminders, recompute rankings, close unconfirmed results, subscription expiry warnings, clean up proofs per retention policy. Jobs must be idempotent and safe to retry.

---

## 10. Testing rules

- **Unit tests** (fast, no I/O): domain aggregates, state machines, `PricingService`, slot/overnight logic, ranking, validators. Aim: every business rule in §6 has at least one test, including failure paths.
- **Integration tests** (Testcontainers + real PostgreSQL, `WebApplicationFactory`): endpoints, auth policies, EF mappings, migrations apply cleanly, the **exclusion constraint** really rejects overlaps, concurrency test: N parallel requests for the same slot → exactly 1 succeeds.
- **Architecture tests:** layer + module dependency rules, no `DateTime.UtcNow` in domain/application, no EF in Domain.
- **Authorization tests:** for each endpoint — anonymous (401), wrong role (403), other venue's data (403/404), correct role (200).
- **E2E (few):** Playwright for Flow A–G smoke (PRD §14).
- Test naming: `Method_Scenario_ExpectedResult`. Arrange-Act-Assert. Deterministic (fake clock, no sleeping, no random without seed).
- Bug fix = first a failing test, then the fix.
- CI blocks merge on failing tests or coverage drop in Domain/Application (target ≥ 80% there).

---

## 11. Web rules (Next.js dashboards) & localization

### 11.0 Web stack & architecture (Next.js)
- **Stack:** Next.js (App Router, latest stable), React, TypeScript `strict`, Tailwind CSS (logical properties + RTL), shadcn/ui or Radix primitives, TanStack Query (server state), TanStack Table, React Hook Form + Zod, `next-intl` (ar/en, RTL/LTR), Recharts for charts, a calendar grid built on our own components (the booking calendar is core — don't fight a generic library).
- **Rendering:** dashboards are authenticated → mostly client components + Route Handlers; use Server Components for layout/data that is safe to fetch on the server. A future public venue/SEO site should use SSR/ISR.
- **Auth (BFF pattern):** Next.js server holds the session in an **httpOnly, Secure, SameSite cookie**; it calls the .NET API with the access token server-side. Never put JWTs in `localStorage`. Middleware guards routes by role; the API still enforces everything.
- **API contract:** the .NET API publishes OpenAPI → generate the TypeScript client (`openapi-typescript`/`orval`) into `packages/api-client`. CI fails if the generated client is out of date. Never hand-write API types.
- **No business rules in the frontend:** price, availability, status transitions, and permissions come from the API. The UI renders what the API says is allowed (e.g., `allowedActions` on a booking).
- **Real-time:** SignalR client (or SSE) for new requests/proofs in the venue dashboard; fall back to polling.
- **Quality:** ESLint + Prettier, `tsc --noEmit`, Vitest + Testing Library for components, Playwright for E2E (Flows A–G), axe for accessibility checks. All run in CI.
- **Performance:** route-level code splitting, `next/image`, virtualized long tables, no giant client bundles, Core Web Vitals budget.
- **Security:** CSP and security headers in `next.config`, env vars only via validated `env.ts` (no secrets in `NEXT_PUBLIC_*`), dependency audit (`pnpm audit`) in CI.

### 11.1 Design & localization rules
- Follow the design system (PRD §12): colors from tokens (primary **navy `#0F1B3D`**, accent **red `#D63B2F`**, achievement **gold `#F5B62B`** (source of truth: the approved design file); add neutral/semantic tokens: success, warning, error, info). No hardcoded colors in components — use CSS variables.
- Brand: logo kit is in `docs/brand/` (SVG + PNG). Don't redraw or recolor the logo.
- Font: IBM Plex Sans Arabic (Arabic and Latin), as in the design file.
- **RTL first:** `dir="rtl"` for Arabic, `dir="ltr"` for English, use CSS logical properties (`margin-inline-start`, not `margin-left`), mirror directional icons, keep numbers/phones/prices LTR-isolated (`<bdi>`/`unicode-bidi`).
- Strings only from `.resx`/localizer — no hardcoded text. Arabic and English must ship together.
- Dates/times: venue local time, Arabic locale formatting; currency `EGP` / `ج.م` consistent.
- Every screen needs: loading (skeleton), empty, error, success, disabled and permission-denied states.
- Status badges consistent everywhere and **not color-only** (icon + text). One shared `BookingStatusBadge` component.
- Accessibility: contrast ≥ WCAG AA, 44px touch targets, visible focus, aria labels, keyboard navigation.
- Reusable components first, in `packages/ui` (venue card, pitch card, booking card, KPI card, table, calendar slot, drawer, confirm dialog). No one-off styling.
- Confirm dialogs for destructive/financial actions; rejection requires a reason field.

### 11.2 Modern Web Development Standards, Code Review & Operational Guidelines

- **Expertise & Tool Selection:** Expert modern web development practices across JavaScript, TypeScript, CSS, React, Tailwind CSS, Node.js, and Next.js (App Router & Pages Router). Prioritize optimal libraries and tools, avoid redundancy/complexity, and ensure full compatibility with Next.js server-first architecture. Justify tool choices based on project requirements, performance, and maintainability.
- **Code Review Protocol:** Before making suggestions or alterations, perform a thorough review of the existing codebase, referencing specific files (e.g., `@app/page.tsx`, `@components/ui.tsx`). Provide accurate, concise suggestions in incremental steps:
  - Explanation of the change and its architectural purpose.
  - Minimal, high-impact code snippet.
  - Targeted test to validate the change.
  - Expected outcomes and edge cases.
  - Request clarification for missing context via `@` references or `status.md`.
- **Security & Mandatory `<SECURITY_REVIEW>`:** Prioritize security to prevent vulnerabilities (e.g., XSS, CSRF, Injection). For high-risk areas (user input, authentication, financial/booking actions), conduct a mandatory `<SECURITY_REVIEW>` containing:
  - Vulnerability identification.
  - Mitigation strategies (e.g., Zod schema validation, input sanitization).
  - OWASP or Next.js official security references.
  - Test verifying the mitigation.
  - Enforce secure defaults (e.g., security headers, Zod schemas) and strictly avoid unsafe practices (e.g., `eval`, raw `dangerouslySetInnerHTML`).
- **Performance & Robustness:** Optimize for performance, reliability, and scalability:
  - Minimize re-renders, client bundle size, and server load (e.g., `React.memo`, selective client hydration, ISR/SSG where applicable).
  - Implement comprehensive `try-catch` blocks for all API/data operations, with user-friendly error messages and structured error logging.
  - Explicitly address edge cases (empty states, loading skeletons, network disconnects/failures).
  - Measure performance with Lighthouse or `@next/bundle-analyzer`.
  - Document technical trade-offs in comments or `status.md`.
- **Operational Concerns:** Ensure production-ready web deployments:
  - Hosting: Multi-provider compatibility (Vercel, AWS ECS/Amplify, Netlify, Docker).
  - Environment Management: Strict use of `.env.local` for local secrets and validated environment variables via `env.ts`.
  - Monitoring: Integrated error and performance tracking (e.g., Sentry, OpenTelemetry).
  - Maintenance: Write self-documenting code, maintain `status.md`, and execute regular `npm audit` / `pnpm audit` security scans.
- **Coding Standards:**
  - **Early Returns:** Enforce early returns for guard clauses and readability.
  - **Tailwind CSS:** Mobile-first responsive utilities with logical properties (`ms-`, `me-`, `ps-`, `pe-`). Avoid inline CSS unless strictly justified.
  - **Functional TypeScript:** Pure functional, declarative TypeScript; avoid classes. Strongly define types and interfaces for all models and props.
  - **Naming Conventions:** Descriptive names with auxiliary verbs for booleans (e.g., `isLoading`, `hasError`, `isSubmitting`). Prefix all event handlers with `handle` (e.g., `handleClick`, `handleToggle`, `handleSubmit`).
  - **Explicit Function Types:** Declare `const` arrow functions with explicit return and parameter types (e.g., `const handleToggle: () => void = () => { ... }`).
  - **Server-First Architecture:** Minimize `'use client'`, `useEffect`, and `useState`. Maximize React Server Components (RSC) for layout and data rendering.
  - **Suspense Boundaries:** Wrap dynamic/client components in `<Suspense>` with lightweight skeleton fallbacks.
  - **Dynamic Imports:** Use `next/dynamic` for heavy or non-critical client-only components (`ssr: false`).
  - **Image Optimization:** Enforce `next/image` with WebP format, explicit width/height dimensions, and `loading="lazy"`.
  - **Error Handling:** Include `try-catch` and dedicated fallback UI (`error.tsx`, `<ErrorBoundary>`).
  - **Component Memoization:** Use `React.memo` where re-render prevention provides measurable savings.
- **Feedback & Uncertainty Handling:**
  - Track user feedback, architectural decisions, and pending work in `status.md`.
  - If no clear definitive answer exists, state: *"No definitive solution is available."*
  - If context is missing or unknown, state: *"I lack sufficient information. Please provide details (e.g., @app/page.tsx)."* and suggest actionable next steps (e.g., consult Next.js docs).

---

## 12. DevOps, environments, config

- **Environments:** Local (docker-compose) → Staging → Production. Same container image, config via env vars.
- **Docker:** multi-stage Dockerfile, non-root user, health checks. `docker compose up` starts Postgres, Mailpit (fake SMTP), Seq (logs), and the apps.
- **CI (GitHub Actions):** backend: restore → build (warnaserror) → format check → unit tests → integration tests (Testcontainers) → vulnerable-package scan → publish image. Web: install → lint → typecheck → unit tests → regenerate API client (must be clean) → build → Playwright → publish image. CD to staging on `main`, to prod on tag with manual approval.
- **Branching:** trunk-based with short-lived branches `feat/…`, `fix/…`, `chore/…`. PRs required, 1 review, CI green.
- **Commits:** Conventional Commits (`feat(booking): add proof upload`), small and atomic.
- **Migrations in prod:** applied by a migration job/bundle (`dotnet ef migrations bundle`), not on app startup. Backwards-compatible (expand → migrate → contract).
- **Config:** typed options, validated on start (`ValidateOnStart`). Feature flags for Phase-2/3 features (tournaments, ads, payment gateway) so they stay hidden in MVP.
- **Backups & DR:** daily Postgres backups + PITR, tested restore. Document runbooks.
- **Monitoring:** health endpoints (`/health/live`, `/health/ready`), Serilog + Seq/Grafana Loki, OpenTelemetry traces, alerts on 5xx rate, job failures, and booking-conflict spikes.

---

## 13. How to build the project — step by step

> Build the walking skeleton first, then vertical slices in the PRD's order. Don't start a phase until the previous one passes its tests.

### Phase 0 — Foundation (do first)
1. `git init`, `.gitignore`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`.
2. Create solution + projects per §2.3. Add architecture tests immediately.
3. `docker-compose.yml` (Postgres, Mailpit, Seq).
4. SharedKernel: `Result`, `Error`, `Money`, `Entity`, `IClock`, domain events, outbox.
5. API host: Serilog, ProblemDetails, versioning, Swagger, health checks, rate limiting, CORS, localization (ar/en), fallback authorization policy.
6. CI pipeline with build + tests.
7. Scaffold `web/` (pnpm + Turborepo, `apps/venue`, `apps/admin`, `packages/ui`, `packages/api-client`, `packages/i18n`), RTL + `next-intl` set up, design tokens, BFF login against the API.
8. ADR-0001 (backend stack) and ADR-0002 (Next.js for web): record the choices from §0 and §11.

### Phase 1 — Identity & Venues
- Registration/login (phone OTP for players, email/password for venue/admin), refresh tokens, roles/policies.
- Venue + Pitch CRUD, photos, facilities, operating hours (incl. overnight), policies, payment methods.
- Admin: venue approval queue; publish/visibility separate from approval.
- Tests: authorization matrix, overnight schedule, approval vs visibility.

### Phase 2 — Availability, Pricing, Search
- Availability engine (hours − blocks − bookings − holds), price rules + `PricingService`.
- Explore/search endpoint with filters, sort, pagination (+ distance via PostGIS or Haversine).
- Venue details & slots endpoint.
- Tests: pricing precedence, DST, overnight slots, no past/unavailable slot returned.

### Phase 3 — Booking & Payment Proof (core flow, PRD Flow A–C)
- Booking aggregate + state machine + history + hold expiry job.
- DB exclusion constraint + concurrency test.
- Manual payment methods, proof upload (secure), venue review (approve/reject with reason), notifications, audit.
- Manual booking by venue (same conflict checks).
- Booking calendar read models (day/week/month, per pitch / all pitches).
- Tests: every transition, expiry, double-booking race, proof ≠ confirmed.

### Phase 4 — Venue Dashboard
- Overview KPIs, calendar UI, reservations table, payment review screen, pricing/availability editors, customers, basic reports (+CSV export), staff & permissions.

### Phase 5 — Teams & Community
- Teams, memberships, invite link/code, discovery, challenges, matches, result submit/confirm/dispute, ranking strategy, team stats.
- Tests: only captains act, disputed results excluded, ranking recalculation.

### Phase 6 — Admin Dashboard & Subscriptions
- Users, venues, bookings monitoring, disputes, support tickets, plans CRUD, subscription assignment (manual), entitlement enforcement, audit-log viewer, featured venues (flagged), platform settings.

### Phase 7 — Hardening & launch
- Security review (OWASP ASVS checklist), load test on search + booking, accessibility pass, RTL/LTR pass, backup/restore drill, runbooks, staging UAT of Flows A–G, production rollout.

### Phase 2/3 of the PRD (post-MVP, behind feature flags)
Tournaments, reviews, referrals, payment gateway + reconciliation, ads/sponsorship, advanced analytics. **Do not build in MVP.**

---

## 14. Decision log (ADR)

Any decision that changes §0, §2, §5, or §7 needs an ADR in `docs/adr/NNNN-title.md`:
`Context → Decision → Alternatives considered → Consequences`. The AI should propose an ADR instead of silently deviating.

Open decisions to resolve early (write ADRs):
1. Mobile client tech (Flutter vs .NET MAUI).
2. ~~Dashboards: Blazor vs SPA~~ → decided: Next.js (see §11 and ADR-0002).
3. Hosting (Azure / AWS / local Egyptian provider) and data-residency needs.
4. SMS/OTP provider available in Egypt.
5. Map & distance provider (Google Maps / Mapbox / OSM + PostGIS).
6. Retention period for payment proofs and personal data.
7. Hold-expiry defaults and ranking formula (configurable defaults).

---

## 15. Quick "NEVER" list

- Never trust the client for price, availability, role, or ids.
- Never mark a payment "verified" without a venue approval record.
- Never allow overlapping active bookings (DB constraint + tests).
- Never use `DateTime.Now`/`UtcNow` directly; never store local times.
- Never return entities from the API; never expose sequential ids.
- Never put business logic in controllers or React components (the frontend displays; the API decides).
- Never skip authorization or audit on sensitive actions.
- Never log secrets/PII; never commit secrets.
- Never edit an applied migration.
- Never hardcode UI strings or colors; never use physical left/right CSS.
- Never present Phase 2/3 features as working in the MVP.
- Never claim "done" without running build + tests.
