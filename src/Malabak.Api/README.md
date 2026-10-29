# Malabak.Api Host

The central entrypoint and composition root for the Malabak modular monolith.

## Responsibilities
- ASP.NET Core Minimal API endpoint route mapping (`/api/v{version}/...`).
- Cross-cutting HTTP middlewares:
  - Serilog request logging enriched with `PiiMaskingEnricher`.
  - OpenTelemetry distributed tracing and runtime metrics.
  - RFC 9457 `ProblemDetails` via `GlobalExceptionHandler`.
  - Rate limiting with fixed and sliding partitioners.
  - CORS policy allowing Next.js web dashboards.
  - Request localization supporting Arabic (`ar-EG`, default RTL) and English (`en-US`).
  - Security fallback authorization policy (`RequireAuthenticatedUser`).
  - Liveness and readiness health check endpoints (`/health/live`, `/health/ready`).
