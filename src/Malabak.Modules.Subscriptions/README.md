# Malabak.Modules.Subscriptions

Bounded context handling venue B2B SaaS tiers, subscription billing cycles, feature quotas, and entitlement enforcement.

## Key Features
- **SaaS Tiers:** Free Starter, Pro, and Enterprise tiers.
- **Entitlement Checks:** Pitch limits, custom pricing rules, staff accounts, and priority listing eligibility via `IEntitlementService`.
- **Billing Records:** Monthly/annual subscription invoices and renewal reminders.
- **PostgreSQL Schema:** `subscriptions.*`.
