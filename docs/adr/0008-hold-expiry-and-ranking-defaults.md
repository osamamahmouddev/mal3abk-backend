# ADR 0008: Booking Hold Expiry Defaults, Cancellation Rules & Team Ranking Engine

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  Malabak's booking and community platform relies on balanced, transparent rules governing:
  1. Pitch slot reservations and payment deposit confirmation windows.
  2. Prevention of slot hoarding and malicious denial-of-inventory attacks.
  3. Fair cancellation policies aligning with standard Egyptian football venue commercial customs.
  4. Competitive integrity and anti-cheat mechanisms for community team rankings and bilateral match score reporting.

- **Decision:**
  We adopt the following operational defaults and algorithmic specifications:

  ### 1. Booking Hold Expiry Engine (The 10-Minute Rule):
  - **Hold Window:** When a player initiates booking (`POST /api/v1/bookings`), the slot transitions to `PendingDeposit` with an exact 10-minute validity (`ExpiresAtUtc = now + 10 minutes`).
  - **Payment Proof Submission:** The player must transfer the deposit via InstaPay or Vodafone Cash and upload the receipt screenshot within this 10-minute window. Once submitted, status advances to `ProofSubmitted`, pausing the expiration timer.
  - **Hangfire Hold Reaper Job:**
    - Executes every **60 seconds**.
    - Queries `bookings.bookings` for slots where `Status = 'PendingDeposit'` and `ExpiresAtUtc < DateTime.UtcNow`.
    - Batch transitions expired records to `Expired`, releases the temporal exclusion hold, emits `BookingExpiredDomainEvent`, and broadcasts real-time slot release via SignalR `BookingHub`.
  - **Slot Hoarding & Abuse Controls:**
    - **Concurrency Ceiling:** Max **3 concurrent pending holds** per player across the entire platform.
    - **Penalty Cooldown:** If a player triggers 3 expired holds within 24 hours without submitting payment proof, a 6-hour temporary booking lock is placed on the player account.

  ### 2. Standard Cancellation & Deposit Policy Defaults:
  Venues can customize policies, but system defaults reflect Egyptian market norms:
  - **Window A (> 12 Hours before Kickoff):** 100% deposit refunded or credited to player's venue credit balance. Slot immediately relisted as available.
  - **Window B (6 to 12 Hours before Kickoff):** 50% deposit forfeiture retained by venue to cover short-notice marketing; 50% credited to player.
  - **Window C (< 6 Hours before Kickoff):** 100% deposit forfeiture to venue due to high probability of unfilled vacancy.
  - **Venue-Initiated Cancellations (e.g., bad weather or pitch maintenance):** 100% full refund to player regardless of timing + automated SMS/Push apology notification.

  ### 3. Team & Match Competitive Ranking Engine (Glicko-2):
  - **Algorithm Choice:** We adopt the **Glicko-2 rating system** (superior to basic Elo by tracking rating confidence and volatility):
    - Initial Rating: $\mu = 1500$
    - Rating Deviation (Uncertainty): $\phi = 350$
    - Volatility Measure: $\sigma = 0.06$
  - **Bilateral Result Verification:**
    - Following a completed match challenge, both team captains must submit the final match score within 6 hours.
    - If both captains agree: Match is marked `Confirmed`, and Glicko-2 ratings are calculated and updated in a single atomic transaction.
    - If scores conflict or one captain disputes: Status transitions to `Disputed`. Ratings are frozen for this match, and the dispute is routed to the Admin Dashboard moderation queue with score receipts and referee/venue confirmation.
  - **Inactivity Rating Decay:** Teams that remain inactive for more than 30 days experience an increase in Rating Deviation ($\phi$), reflecting higher uncertainty and allowing active teams to rank competitively on the seasonal leaderboards.

- **Alternatives Considered:**
  - *Standard Elo System:* Rejected because Elo treats all rating changes uniformly without factoring in match frequency or rating certainty, leading to rating inflation and stale leaderboards.
  - *Unilateral Score Reporting (Winner submits only):* Rejected to prevent fraud, fake match reporting, and unverified leaderboard climbing.

- **Consequences:**
  - Positive: Guarantees high pitch inventory turnover and minimizes idle slots for venue owners.
  - Positive: Robust deterrent against slot hoarding and fraudulent ranking manipulation.
  - Negative: Requires responsive background job execution (Hangfire) to release expired slots promptly within 60 seconds of expiration.
