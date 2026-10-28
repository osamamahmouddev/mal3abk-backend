# ADR 0006: Geospatial Engine, Distance Calculation & Map Provider

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  Malabak's core value proposition is enabling players to discover football pitches near them, view travel distances, filter by district/governorate, and navigate directly to the venue. Key considerations include:
  1. Low-latency geospatial querying (< 20 ms) across thousands of active pitches.
  2. Map rendering across Flutter mobile app and Next.js web dashboards.
  3. Predictable operational costs avoiding unbounded per-tile Google Maps pricing.
  4. User location privacy adhering to Egyptian Law 151/2020 (no continuous background tracking or exact coordinate persistence for players).

- **Decision:**
  We adopt a **Dual Spatial Architecture**: Server-Side PostGIS Spatial Indexing with Client-Side MapLibre / OpenStreetMap:
  1. **Server-Side Geospatial Persistence & Queries:**
     - **Engine:** PostgreSQL with `postgis` extension enabled (or `btree_gist` with Haversine sphere calculation for minimal container footprints).
     - **Coordinate System:** WGS 84 (`EPSG:4326`) stored as `geography(Point, 4326)` on `venues.venues.location`.
     - **Spatial Indexing:** GiST spatial index on `location` allowing `ST_DWithin(location, ST_MakePoint(lon, lat)::geography, radiusMeters)` queries to execute in < 5 ms.
     - **Distance Output:** `ST_Distance(location, ST_MakePoint(lon, lat)::geography)` returns direct distance in meters, formatted client-side (e.g. "1.8 كم").
  2. **District & Area Taxonomy (Egypt):**
     - Structured Egyptian administrative hierarchy: Governorate (محافظة) -> District / City (مدينة / حي) -> Neighborhood (منطقة) (e.g. القاهرة -> مدينة نصر -> مكرم عبيد).
     - Allows fallback filtering for players who decline device GPS permissions or prefer browsing specific popular sporting hubs.
  3. **Client-Side Rendering & Navigation:**
     - **Interactive Exploration:** Vector map rendering via `maplibre_gl` (Flutter) and `react-map-gl` / MapLibre (Next.js) consuming vector/raster tiles from OpenStreetMap / Stadia / CARTO, eliminating per-load licensing spikes.
     - **Turn-by-Turn Navigation:** Deep-linking directly to native navigation apps installed on player device via standard geo URIs (`geo:lat,lng?q=VenueName`, `https://maps.google.com/?daddr=lat,lng`, `http://maps.apple.com/?daddr=lat,lng`), offloading turn-by-turn routing costs entirely.
  4. **Privacy & Data Protection:**
     - Player device coordinates are used transiently in the HTTP request (`/api/v1/venues/explore?lat=...&lng=...&radius=...`) and are stripped from persistent access logs by Serilog PiiMaskingEnricher.
     - Exact player GPS coordinates are never stored in database tables or user session profiles.

- **Alternatives Considered:**
  - *Full Google Maps Platform Integration (Maps SDK + Distance Matrix + Places):* Rejected due to high ongoing API fees ($7.00 per 1,000 requests for dynamic maps, $5.00–$10.00 for distance matrix) that jeopardize unit economics of a low-margin sports community platform.
  - *Client-Side Haversine Distance Calculation:* Rejected because filtering venues across Egypt client-side wastes bandwidth and leaks inactive or unapproved venue catalogs to clients.

- **Consequences:**
  - Positive: High-performance spatial indexing at 0 additional third-party API expense.
  - Positive: Complete compliance with data privacy directives by treating player GPS coordinates as strictly ephemeral.
  - Negative: Requires initial boundary mapping and seeding for Egyptian districts and governorates.
