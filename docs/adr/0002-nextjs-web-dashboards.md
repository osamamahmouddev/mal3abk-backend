# ADR 0002: Next.js (App Router) for Venue and Admin Dashboards

- **Status:** Accepted
- **Date:** 2026-10-24
- **Context:**
  Malabak requires modern, responsive, RTL-first web dashboards for Venue Managers and Platform Administrators, with future public/SEO pitch booking capabilities. We previously considered Blazor and SPA alternatives.

- **Decision:**
  We adopt **Next.js (App Router) + TypeScript + Tailwind CSS + shadcn/ui** inside a `web/` monorepo managed with pnpm and Turborepo:
  1. **Decoupled Architecture:** The web applications interact with the backend exclusively through the versioned REST API (`/api/v{version}/...`), maintaining clear separation between frontend presentation and backend business rules.
  2. **BFF Pattern & Security:** Next.js Route Handlers act as a Backend-For-Frontend storing session tokens in secure, `httpOnly`, `SameSite=Strict` cookies. Tokens are never stored in `localStorage`.
  3. **Strict Code-Gen API Contracts:** The TypeScript API client is automatically generated from the backend's OpenAPI/Swagger specification into `@malabak/api-client`.
  4. **Monorepo Structure:**
     - `apps/venue`: Next.js Venue Dashboard
     - `apps/admin`: Next.js Platform Admin Dashboard
     - `packages/ui`: Shared design tokens (navy `#071832`, accent red `#EB4F42`, gold `#F6B326`), accessible components, RTL support.
     - `packages/api-client`: Generated typed API client.
     - `packages/i18n`: Centralized bilingual messages (Arabic RTL primary, English LTR).

- **Alternatives Considered:**
  - *Blazor Web App:* Considered, but Next.js offers richer ecosystem support for Egyptian RTL typography, broader UI component ecosystems (shadcn/ui), and superior SSR/SEO capabilities for public venue marketing pages.
  - *Vite SPA:* Rejected in favor of Next.js due to lack of built-in BFF capabilities for secure cookie-based auth token handling.

- **Consequences:**
  - Frontend development can proceed independently using modern web tooling.
  - API schema changes are strictly enforced at build time via generated client synchronization.
