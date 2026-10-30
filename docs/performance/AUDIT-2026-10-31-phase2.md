# Performance audit: Phase 2 (availability, pricing and search)

- **Date:** 2026-10-31
- **Scope:** branch `backend_phase_2`. This covers the pricing calculator, the availability query and Redis cache, explore, map and home search, and venue details.
- **Method:** static review of the hot paths, then micro-measurements in Release builds (stopwatch plus allocated bytes) before and after each fix.
- **Not measured:** no load test was run, because Docker and Postgres are not available on the audit machine. Run k6 against staging before launch (see Recommendations).

## Hot path

Public availability, explore with a date, and the home feed all price every slot. For one venue-day that is about 3 pitches × 40 slots × 4 fifteen-minute slices = 480 pricing slices. On a cold cache, explore with a date repeats this for up to 60 venues, so roughly 30,000 slices per request.

## Findings

| ID | Severity | Title | Status |
|---|---|---|---|
| PERF-01 | High | Holiday calendar rebuilt for every priced slice | Fixed |
| PERF-02 | Medium | Explore candidate assembly was O(venues × pitches) | Fixed |
| PERF-03 | Medium | Unbounded availability work per explore request | Fixed (shared with SEC-02) |
| PERF-04 | Low | Two sequential Redis round trips per availability read | Recommendation |
| PERF-05 | Low | Case-insensitive city/area filters cannot use the (City, Area) index | Recommendation |

### PERF-01: Holiday calendar rebuilt per slice (High)

- **Issue:** `EgyptHolidayCalendar.IsHoliday` called `ForYear`, which builds about 20 holidays, does Umm al-Qura calendar conversions and sorts the result, on every call. The pricing calculator calls it once per 15-minute slice.
- **Measured (Release, 30,000 calls, about one cold explore-with-date request):**

  | | Time | Allocated |
  |---|---|---|
  | Before | 192 ms | 113 MB |
  | After | 6 ms | 0 MB |

- **Fix:** holiday dates are computed once per year into a `FrozenSet<DateOnly>` held in a `ConcurrentDictionary`.
- **Guards:**
  - `ReferenceDataTests.IsHoliday_DoesNotRebuildTheCalendarPerCall` (allocation budget).
  - `IsHoliday_AgreesWithTheFullList_ForEveryDayOfTheYear` (correctness).

### PERF-02: Quadratic candidate assembly in explore (Medium)

- **Issue:** for up to 500 candidate venues, pitches, base rules and favorite counts were matched with `Where` and `FirstOrDefault` scans inside the loop. That is about 500 × (pitches + rules + favorites) comparisons, or around a million at 500 venues with 3 pitches each.
- **Fix:** `ToLookup` and a dictionary before the loop, so assembly is linear. Behaviour is covered by the existing explore tests.

### PERF-03: Availability work per request (Medium)

- **Issue:** the empty-result suggestions repeated the dated search, so one request could do 300 availability computations. Details are in SEC-02.
- **Fix:** a per-request budget of 120 lookups, guarded by a test that counts store reads.

### PERF-04: Sequential Redis round trips (Low)

`CachedVenueAvailability` reads the venue version and then the entry, which is 2 round trips per venue. Explore checks up to 60 venues one after another.

**Recommendation:** batch the version reads (MGET) for all candidate venues before the loop. That would remove about 60 round trips per dated explore.

### PERF-05: City/area index unused (Low)

Filters use `lower("City")` and `lower("Area")`, which the plain `(City, Area)` btree cannot serve. This is fine at the current venue counts (hundreds).

**Recommendation:** past about 10k venues, add an expression index on `(lower("City"), lower("Area"))` with `EXPLAIN ANALYZE` before and after.

## Verified as fine

- **Location index:** the bounding-box SQL filter uses the new partial index `ix_venues_searchable_location`. The Haversine check runs only on the box's candidates (at most 500).
- **Popularity counts:** they use the new `favorite_venues(VenueId)` index.
- **Availability cache:**
  - The TTL is at most 30 s and is capped at the next state change.
  - Single-flight per key prevents stampedes.
  - Invalidation is a per-venue version bump, with no key scans.
- **Venue details:** 6 indexed queries per request, plus a public 60 s cache with ETag/304.
- **Home feed:** nearby and popular share one search. The recently-viewed section is limited to 10 ids.

## Recommendations before launch

1. Run a k6 test on staging. Mix: 70% availability, 20% explore, 10% home. Targets: p95 under 300 ms at 200 RPS, and an availability cache hit ratio of at least 80% (log it).
2. Add a query-count assertion test once the integration suite runs in CI. Targets: explore without a date at 4 queries or fewer, details at 6 or fewer.
3. Track the availability cache hit/miss ratio as a metric (`Malabak.Availability` meter).
