# Test Infrastructure & Authorization Verification Guide

## 1. Overview

Malabak's testing strategy guarantees 100% test coverage across Unit, Architecture, and Integration test suites without flaky executions, database state pollution, or hardcoded mock bypasses.

---

## 2. Core Components

### A. Role-Based Authenticated Client Helpers
Located in `tests/Malabak.IntegrationTests/Infrastructure/Auth/`:
- `TestJwtTokenGenerator`: Issues RFC 7519 compliant JSON Web Tokens signed with the application's runtime ES256 NIST P-256 ECDSA key from `IJwtKeyProvider`.
- `AuthenticatedTestClientExtensions`: Fluent helpers on `HttpClient`:
  - `client.AsAnonymous()`
  - `client.AsPlayer(services, userId, phoneNumber)`
  - `client.AsVenueOwner(services, venueId, userId)`
  - `client.AsVenueStaff(services, venueId, userId)`
  - `client.AsAdmin(services, userId)`
  - `client.AsSupportAgent(services, userId)`
  - `client.WithRole(services, role, userId, phoneNumber, customClaims)`

### B. Authorization Matrix Testing Helper
Located in `AuthorizationMatrixHelper.cs`:
Allows executing an endpoint across the entire matrix of roles (`Anonymous`, `Player`, `VenueOwner`, `VenueStaff`, `Admin`, `SupportAgent`, `Finance`) and asserting expected status codes in a single declarative block:

```csharp
await AuthorizationMatrixHelper
    .ForEndpoint(HttpMethod.Post, "/api/v1/venues/123/pitches", contentFactory)
    .WithAnonymousExpecting(HttpStatusCode.Unauthorized)
    .WithPlayerExpecting(HttpStatusCode.Forbidden)
    .WithVenueOwnerExpecting(HttpStatusCode.Created)
    .WithAdminExpecting(HttpStatusCode.Created)
    .ExecuteAsync(factory);
```

### C. Testcontainers Infrastructure
- `IntegrationTestEnvironment` (module initializer) starts one PostgreSQL 17 container (with `btree_gist`) and one Redis 7.2 container per test run, each with a random password, and hands their connection strings to every `WebApplicationFactory` through environment variables. No credentials live in the repo, and the suite runs the same locally and in CI (Docker required).
- To reuse services that are already running (e.g. `docker compose up`), set `MALABAK_TEST_POSTGRES` and `MALABAK_TEST_REDIS`.
- Test classes run sequentially (`AssemblyInfo.cs`): they share one database, and parallel hosts race on shared state such as Hangfire's schema creation on a fresh database.
- `PostgreSqlContainerFixture` / `RedisContainerFixture` (in `Infrastructure/Containers/`) remain available for tests that need a fully isolated instance.

### D. WebApplicationFactory & Database Reset Strategy
Located in `MalabakWebApplicationFactory.cs` and `DatabaseResetHelper.cs`:
- Fast schema-level truncation: Truncates all tables across all bounded contexts (`identity`, `venues`, `booking`, `audit`, etc.) using `RESTART IDENTITY CASCADE`, while preserving `__EFMigrationsHistory`.
- `MalabakWebApplicationFactory`: Overrides `IClock` with `FakeClock`, sets environment to `Testing`, and wires integration test services.

### E. Fake Clock & Domain Test Data Builders
- `FakeClock`: Controllable implementation of `IClock` allowing `AdvanceMinutes()`, `AdvanceHours()`, `AdvanceDays()`, and `SetUtcNow()`.
- Test Data Builders:
  - `UserBuilder`: Generates player and staff users with preconfigured security stamps.
  - `VenueBuilder`: Generates test venues with slug, facilities, and coordinates.
  - `PitchBuilder`: Generates turf pitches with custom dimensions and capacities.
  - `BookingBuilder`: Generates bookings with monetary amounts and hold expirations.
