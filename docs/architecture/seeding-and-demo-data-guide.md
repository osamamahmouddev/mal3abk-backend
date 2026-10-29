# Seed & Demo Data Tooling Guide

## 1. Overview and Architecture

Malabak provides automated, strictly idempotent data seeders designed for continuous delivery, local development, staging environments, and integration testing:
- **`ReferenceDataSeeder`:** Runs safely across all environments (including Production). Seeds foundational system accounts, Egyptian governorates/cities, standard facilities, and roles.
- **`DemoDataSeeder`:** Runs **strictly** in `Development` and `Testing` environments. Seeds realistic Egyptian sporting venues, pitches with varied turf specifications, test players, and pitch bookings across every lifecycle status.

---

## 2. Seeders Breakdown

### A. Reference Data Seeder (`ReferenceDataSeeder`)
- **System Admin:** Seeds initial root admin account `admin@malabak.app` with `Admin` role.
- **Venue Facilities:** Seeds standard amenities (`Floodlights`, `Parking`, `Locker Rooms`, `Cafeteria`, `Shower`, `Wi-Fi`, `Ball Rental`, `Bibs / Pinnies`, `Referee On-Demand`, `First Aid Station`).
- **Governorates Catalog:** Seeds supported Egyptian regions (Cairo, Giza, Alexandria, Dakahlia, Gharbia, Port Said, Suez, Ismailia, Aswan, Luxor, Red Sea, South Sinai).

### B. Demo Data Seeder (`DemoDataSeeder`)
- **Fictional Egyptian Venues:**
  - `ملاعب النادي الأهلي - مدينة نصر` (Nasr City, Cairo)
  - Pitches: `ملعب التتش الصغير` (5-a-side, Artificial 4G), `ملعب النجوم` (7-a-side, Artificial 5G).
- **Test Users:**
  - Player 1: `+201012345678` ("عمرو زكي")
  - Player 2: `+201098765432` ("محمد زيدان")
  - Venue Owner: `+201100000001` ("حسن شحاتة")
- **Bookings Lifecycle Coverage:**
  - `PendingDeposit`: Active 15-minute hold awaiting payment proof.
  - `Confirmed`: Deposit verified and slot reserved.
  - `Cancelled`: Terminated reservation with cancellation audit trail.

---

## 3. Production Safety Guard

`DemoDataSeeder` enforces an unconditional programmatic check:
```csharp
if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
{
    _logger.LogWarning("Demo data seeding is strictly blocked outside Development/Testing environments. Current: {Env}", _environment.EnvironmentName);
    return;
}
```
Demo data is never accidentally injected into staging or production databases.

---

## 4. Execution Methods

### CLI Execution
You can run the seeder via the command line using .NET CLI flags:
```bash
# Seed reference data only
dotnet run --project src/Malabak.Api/Malabak.Api.csproj -- --seed-reference

# Seed demo data in development
dotnet run --project src/Malabak.Api/Malabak.Api.csproj -- --seed-demo

# Seed both reference and demo data
dotnet run --project src/Malabak.Api/Malabak.Api.csproj -- --seed-reference --seed-demo
```

### Docker Compose Execution
`docker-compose.yml` includes automated SQL seed scripts under `docker/postgres/`:
- `01-init.sql`: Creates PostgreSQL extensions (`btree_gist`, `uuid-ossp`) and bounded schemas (`identity`, `venues`, `booking`, etc.).
- `02-seed-reference.sql`: Idempotent SQL upsert for reference data.
- `03-seed-demo.sql`: Idempotent SQL upsert for fictional Egyptian venues and demo players.

To run the dedicated .NET seeder container:
```bash
docker compose --profile seed up seeder
```
