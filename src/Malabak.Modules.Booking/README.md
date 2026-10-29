# Malabak.Modules.Booking

Bounded context managing pitch reservations, temporal slot locks, and the 10-state booking lifecycle.

## Key Features
- **10-State Booking Machine:** `Draft` -> `PendingDeposit` -> `ProofSubmitted` -> `UnderReview` -> `Confirmed` / `Rejected` / `Expired` / `Cancelled` / `Completed` / `NoShow`.
- **Double-Booking Prevention:** Enforces PostgreSQL `btree_gist` temporal exclusion constraints (`EXCLUDE USING gist (pitch_id WITH =, slot_range WITH &&)`) at database engine level.
- **Hold Expiry Engine:** Hangfire background reaper releasing unconfirmed slots after 10 minutes.
- **PostgreSQL Schema:** `booking.*`.
