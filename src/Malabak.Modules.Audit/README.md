# Malabak.Modules.Audit

Bounded context responsible for tamper-evident, immutable audit trail logging across all platform actions.

## Key Features
- **Immutable Log Store:** Append-only records documenting actor ID, IP address, user agent, target entity, previous state, new state, and timestamp.
- **Sensitive Operations Audited:** Proof approval/rejection, admin role grants, phone change requests, manual booking overrides, and financial refunds.
- **7-Year Retention:** Automated monthly partition management and cold storage export complying with Egyptian tax and commercial record retention mandates.
- **PostgreSQL Schema:** `audit.*`.
