# ADR 0001: Backend Architecture & Technology Stack Choices

- **Status:** Accepted
- **Date:** 2026-10-24
- **Context:**
  Malabak requires a robust, scalable, and maintainable backend serving three frontends (Player Mobile App, Venue Web Dashboard, Admin Web Dashboard) in the Egyptian football booking and community domain.

- **Decision:**
  We adopt a **Modular Monolith** architecture with Clean Architecture within each bounded context:
  1. **Runtime & Framework:** .NET 10 (LTS) / ASP.NET Core 10, C# latest, strict nullable reference types enabled, warnings treated as errors.
  2. **Database & ORM:** PostgreSQL with Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL`), snake_case naming conventions, separate DbContexts and schemas per module, and PostgreSQL exclusion constraints (`btree_gist`) to guarantee zero double-booking at the database level.
  3. **Shared Kernel:** Ultra-lean shared kernel containing `Result<T>`, `Error`, `Money` value object, `Entity<TId>`, `IClock`, and Domain Event contracts.
  4. **CQRS-lite & Validation:** FluentValidation for command validation, compile-time mapping with Mapperly or explicit projections, and custom mediator/handler interfaces (avoiding commercial libraries).
  5. **Background Jobs & Outbox:** Hangfire for background tasks (hold expiration, rankings) and Outbox pattern for cross-module integration events.
  6. **Observability:** Structured logging with Serilog directed to Seq / OpenTelemetry traces / RFC 9457 ProblemDetails.

- **Alternatives Considered:**
  - *Microservices:* Rejected for MVP due to high operational overhead and network latency.
  - *Single Monolithic DbContext:* Rejected to maintain strict module boundaries and prevent cross-boundary direct joins.
  - *AutoMapper & MediatR:* Rejected due to commercial licensing and maintainability considerations.

- **Consequences:**
  - Clear boundaries make future microservice extraction straightforward if needed.
  - Fast compile-time verification and zero warning tolerance enforce high code quality.
  - Database-enforced exclusion constraints prevent race conditions and double-bookings.

- **Amendment (2026-10-29):** Pinned the runtime to **.NET 10 LTS** (`net10.0`) instead of ".NET 9/10" — .NET 9 is an STS release, and 10 is supported until November 2028. EF Core, Npgsql and the ASP.NET Core packages moved to their 10.x lines. The .NET 10 SDK audits transitive packages by default, so OpenTelemetry (1.10 → 1.20.1) and Testcontainers (4.0 → 4.16, which pulled a vulnerable SSH.NET) were bumped too.
