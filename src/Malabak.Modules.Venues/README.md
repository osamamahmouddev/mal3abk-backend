# Malabak.Modules.Venues

Bounded context governing venues, pitches, amenities, weekly operating schedules, dynamic pricing rules, and venue policies.

## Key Features
- **Venue & Pitch Aggregates:** Pitch surface types (Grass, Turf 5v5, 7v7, 11v11), lighting quality, amenities, and GPS coordinates (`EPSG:4326`).
- **Operating Schedules:** Weekly day-by-day operating hours with support for overnight schedules crossing midnight (e.g. 18:00 to 02:00 next day).
- **Dynamic Pricing Rules:** Peak vs off-peak hourly pricing, weekend surcharges, and holiday promotional rates.
- **Venue Policies:** Cancellation policies, deposit percentages, payment method configurations (InstaPay, Vodafone Cash, Bank Transfer).
- **PostgreSQL Schema:** `venues.*` managed by `VenuesDbContext`.
