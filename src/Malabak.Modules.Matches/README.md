# Malabak.Modules.Matches

Bounded context governing match challenges between football teams, pitch reservations linking, bilateral score verification, and Glicko-2 competitive ratings.

## Key Features
- **Match Challenges:** Public and direct challenge requests between team captains.
- **Bilateral Score Submission:** Both captains verify the final match score before rating calculations are finalized.
- **Glicko-2 Competitive Ranking:** Rating calculations factoring in rating deviation ($\phi$) and volatility ($\sigma$) with inactivity rating decay.
- **PostgreSQL Schema:** `matches.*`.
