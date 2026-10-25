# Venue Web Dashboard Status & Modern Web Refactoring Log

## Project: `web/apps/venue` (ملعبك — Venue Dashboard)
- **Framework:** Next.js (App Router), React, TypeScript, Tailwind CSS.
- **RTL Support:** Primary Arabic (`dir="rtl"`), font: `IBM Plex Sans Arabic`.

---

## Refactoring Initiatives & Architecture Roadmap

### 1. Architectural Integrity & Server-First Modernization
- [x] **Update `rule.md` (§11.2):** Formalized modern web standards, code review protocols, security reviews, performance benchmarks, and coding conventions.
- [ ] **Decouple Client vs Server Boundaries:**
  - Move static layout components, presentational cards, formatting utilities, and badges out of monolithic `"use client"` scopes into React Server Components (RSC).
  - Isolate stateful interactive controls (`Switch`, `Toast`, interactive drawers/modals) as lightweight client components.
- [ ] **Suspense & Fallback UI:**
  - Introduce `<Suspense>` boundaries with skeleton placeholders for all dashboard views and asynchronous data boundaries.

### 2. Strict Coding Standards Compliance
- [ ] **Function Declarations & Types:**
  - Standardize all component definitions and callbacks as typed `const` arrow functions: `const Component: React.FC<Props> = (...) => ...`.
  - Prefix all event handlers with `handle` (e.g., `handleToggle`, `handleSubmit`, `handleSelectPitch`).
  - Standardize boolean state identifiers with auxiliary verbs (e.g., `isLoading`, `isSubmitting`, `hasReachedLimit`, `isPublished`).
  - Enforce early returns for guard clauses.

### 3. Security Hardening (`<SECURITY_REVIEW>`)
- [ ] **Form & Input Validation:**
  - Introduce Zod schemas for manual reservations (`@app/reservations/manual/page.tsx`), venue setup (`@app/setup/page.tsx`), and staff management (`@app/staff/page.tsx`).
  - Sanitize and normalize Egyptian phone numbers (`+20 1[0-2,5]...`).
- [ ] **Safe API Execution:**
  - Wrap all mutation and data submission flows in `try-catch` blocks with explicit error states and user notifications.

---

## Feedback & Open Items
- Note: If no definitive solution is available for specific business workflows (e.g. multi-branch accounting vs single venue hold policies), state explicitly: *"No definitive solution is available."*
- Missing context requests will be referenced via `@app/...` paths.
