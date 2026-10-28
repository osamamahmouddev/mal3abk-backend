# Database Persistence & EF Core Conventions Guide — Malabak (ملعبك)

This guide documents the mandatory database design rules, EF Core conventions, schema isolation, and migration guidelines for Malabak, adhering to **[TASK-107]** and system engineering directives.

---

## 1. Schema-per-Module Isolation (Modular Monolith)
Each bounded context owns its dedicated PostgreSQL schema:
- `identity.*`: Users, Refresh Tokens, Sessions, Phone Change Requests.
- `venues.*`: Venues, Pitches, Operating Schedules, Dynamic Price Rules, Policies.
- `booking.*`: Booking Aggregates, Temporal Slot Holds, Expirations.
- `payments.*`: Manual Proofs, Financial Transactions, Ledger Entries.
- `teams.*`: Football Squads, Lineups, Invitations.
- `matches.*`: Match Challenges, Results, Glicko-2 Ratings.
- `subscriptions.*`: Venue SaaS Tiers, Entitlements, Invoices.
- `audit.*`: Immutable Audit Logs.
- `notifications.*`: In-App Alerts, Outbox Dispatches.
- `support.*`: Disputes, Inquiries.

### Cardinal Rule: Zero Cross-Module SQL Joins
A DbContext must **never** reference tables or navigation properties in another module's schema. Cross-module data integration is achieved solely through asynchronous integration events (Outbox) or mediated CQRS queries.

---

## 2. BaseDbContext Conventions & Automatic Interceptors

All module DbContexts inherit from [`BaseDbContext`](file:///home/osama/Desktop/mal3abk-backend/src/Malabak.SharedKernel/Persistence/BaseDbContext.cs):

### A. Automatic Auditing (`AuditableEntityInterceptor`)
* Automatically populates `CreatedAtUtc` on `EntityState.Added` via `IClock.UtcNow`.
* Automatically updates `UpdatedAtUtc` on `EntityState.Modified` via `IClock.UtcNow`.

### B. Transparent Soft Delete (`SoftDeleteInterceptor` & Global Filter)
* Any entity implementing `ISoftDeletable` (`IsDeleted`, `DeletedAtUtc`) intercepted upon deletion:
  * `EntityState.Deleted` is automatically rewritten to `EntityState.Modified`.
  * `IsDeleted = true` and `DeletedAtUtc = clock.UtcNow`.
* `OnModelCreating` applies a global query filter: `modelBuilder.Entity<T>().HasQueryFilter(e => !e.IsDeleted)`.
* To query including deleted entities (e.g. for audit or account recovery): `.IgnoreQueryFilters()`.

### C. Optimistic Concurrency via PostgreSQL `xmin`
* All `IVersionedEntity` models map the PostgreSQL system column `xmin` as a concurrency token:
  ```csharp
  modelBuilder.Entity(clrType)
      .Property(nameof(IVersionedEntity.RowVersion))
      .HasColumnName("xmin")
      .HasColumnType("xid")
      .ValueGeneratedOnAddOrUpdate()
      .IsConcurrencyToken();
  ```
* When concurrent updates collide, PostgreSQL detects the row modification and EF Core throws `DbUpdateConcurrencyException`, preventing silent data overwrites.

### D. Time-Ordered UUIDs: Guid v7 (`IdGenerator.NewGuid()`)
* All entities utilize Guid v7 (RFC 9562) generated via `IdGenerator.NewGuid()`.
* **Why Guid v7 over Guid v4:** Guid v7 embeds a millisecond-precision Unix timestamp in the leading 48 bits. This ensures sequential B-Tree index insertions in PostgreSQL, completely eliminating the 40–60% index page fragmentation and write degradation suffered by random Guid v4.

### E. PostgreSQL UTC Timestamptz Enforcement
* Every `DateTime` and `DateTime?` property automatically uses a UTC value converter ensuring `DateTimeKind.Utc` is preserved across all database reads and writes.

---

## 3. Migration Naming & Review Guidelines

### Command Syntax
Each module specifies its migration history table inside its own schema:
```bash
dotnet ef migrations add <Timestamp>_<DescriptiveAction> \
  --project src/Malabak.Modules.Identity \
  --startup-project src/Malabak.Api \
  --context IdentityDbContext \
  --output-dir Infrastructure/Migrations
```

### Migration History Table Isolation
To prevent cross-module migration collisions, each DbContext configures its own history table:
```csharp
npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)
```

### Migration Review Safety Checklist
Before committing any EF Core migration:
- [ ] **Reversible:** The `Down()` method must cleanly undo the changes made in `Up()`.
- [ ] **Indexes on Foreign Keys:** Verify all foreign keys and filter columns (e.g. `status`, `expires_at_utc`, `is_deleted`) have explicit indexes.
- [ ] **No Destructive Table Locks:** Avoid adding non-nullable columns with static defaults on massive tables in single transactions without `pg_dump` backup.
- [ ] **Temporal Exclusion Constraint:** Pitch booking ranges must enforce `EXCLUDE USING gist (pitch_id WITH =, slot_range WITH &&)` to guarantee zero double-booking at the PostgreSQL engine level.
