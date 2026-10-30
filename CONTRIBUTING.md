# Contributing to Malabak (ملعبك)

Thank you for contributing to Malabak! This document outlines our repository governance, developer workflow, quality gates, and security protocols.

---

## 1. Primary Sources of Truth
Before making any changes, review:
- **Security Protocols:** [`SECURITY.md`](SECURITY.md) — Mandatory directives for auth, tokens, encryption, rate limiting, and uploads.
- **Engineering Rules:** [`rule.md`](rule.md) — Coding conventions, module boundaries, C# standards, and testing rules.
- **Architecture Decisions:** [`docs/adr/`](docs/adr/) — Accepted technical decisions.
- **Product Requirements:** [`docs/PRD.md`](docs/PRD.md) — Functional specifications.

---

## 2. Developer Workflow & Branching Strategy

### Branching Model
- **`main`:** Production branch. Protected; direct pushes forbidden. Releases deploy automatically via CI/CD.
- **`dev`:** Active integration branch. All feature branches merge into `dev` via Pull Request after review.
- **Feature Branches:** Create branches named:
  - Phase branches: `back_end_phases_0`, `back_end_phase_01`, etc.
  - Feature branches: `feat/<module>-<short-description>` (e.g. `feat/booking-gist-lock`)
  - Bugfix branches: `fix/<module>-<short-description>`

### Local Environment Setup
1. **Clone repository:**
   ```bash
   git clone https://github.com/osamamahmouddev/mal3abk-backend.git
   cd mal3abk-backend
   ```
2. **Install Git Hooks:**
   ```bash
   ./scripts/install-hooks.sh
   ```
   *This activates pre-commit secret scanning and Conventional Commit validation.*
3. **Start local infrastructure services:**
   ```bash
   docker compose up -d
   ```
   *Starts PostgreSQL 17 (port 5432), Redis 7.2 (port 6379), Seq (port 5341/8081), Mailpit (port 8025).*
4. **Build .NET solution:**
   ```bash
   dotnet build Malabak.sln
   ```
5. **Run test suite:**
   ```bash
   dotnet test Malabak.sln
   ```
6. **Build Web Monorepo:**
   ```bash
   cd web && npm install && npm run build
   ```

---

## 3. Commit Message Standards (Conventional Commits)
All commit messages must adhere to the Conventional Commits specification:
```text
<type>(<scope>): <subject>
```
* **Allowed Types:** `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.
* **Examples:**
  - `feat(identity): implement phone OTP verification and rate limiting`
  - `fix(booking): prevent concurrent hold expiry race condition`
  - `docs(adr): add ADR-0005 for SMS and WhatsApp OTP gateway strategy`
  - `test(venues): add architecture test forbidding circular module dependencies`

---

## 4. Pull Request & Definition of Done (DoD)
Every Pull Request must satisfy the Definition of Done before merge:
- [ ] Builds with **0 warnings** (`dotnet build -c Release -warnaserror`).
- [ ] Automated tests pass **100% green** (`dotnet test -c Release`).
- [ ] Code formatting verified (`dotnet format --verify-no-changes`).
- [ ] Web monorepo builds and typechecks pass cleanly (`turbo build`).
- [ ] Endpoints enforce **deny-by-default** authorization (`RequireAuthenticatedUser`).
- [ ] Zero secrets in code, config, or git.
- [ ] PII masked in all structured logs (`PiiMaskingEnricher`).
- [ ] ProblemDetails returned for all client errors (RFC 9457).
- [ ] Bilingual support included: Arabic (RTL primary) and English strings in resource files.
- [ ] Database migration included if EF Core schema changed, verified safe and reversible.
