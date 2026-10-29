# ADR 0004: Hosting Infrastructure, Cloudflare Edge & Egyptian Data Residency (Law 151/2020)

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  Malabak operates primarily within the Arab Republic of Egypt, collecting sensitive player, venue owner, and financial proof data (InstaPay numbers, Vodafone Cash wallets, transaction receipts). Under the Egyptian Personal Data Protection Law (Law No. 151 of 2020), digital platforms must ensure:
  1. Lawful personal data processing, minimal collection, and strict data residency compliance regarding cross-border transfer of sensitive personal data.
  2. Robust security controls preventing unauthorized access, leakage, or tampering.
  3. Low network latency (< 50 ms) for users connecting from Egyptian ISPs (WE, Vodafone Egypt, Orange, Etisalat Misr).
  4. Resilience against automated volumetric DDoS attacks, scrapers, and botnet abuse targeting booking slots.

- **Decision:**
  We adopt a **Hybrid Edge-Regional Architecture** governed by strict data protection boundaries:
  1. **Edge Perimeter via Cloudflare:**
     - All public HTTP/HTTPS traffic must ingress through Cloudflare's Egyptian Edge PoP (`CAI` - Cairo Internet Exchange).
     - **Cloudflare WAF & Managed Rules:** OWASP Core Ruleset, Rate Limiting (100 req/min baseline per IP), and Bot Management enabled to mitigate scrapers and credential stuffing.
     - **Cloudflare Turnstile:** Invisible CAPTCHA validation embedded on sensitive endpoints (`POST /api/v1/auth/send-otp`, `POST /api/v1/bookings`).
     - **Authenticated Origin Pulls (mTLS):** The origin application host enforces mutual TLS using Cloudflare's origin client certificates; direct connections bypassing Cloudflare are dropped at the Linux firewall (`iptables` / `ufw`).
  2. **Compute & Persistence Location:**
     - Database clusters (PostgreSQL 17 with `btree_gist`) and Redis memory caches reside in regional Tier III/IV data center facilities with dedicated low-latency peering routes to Egyptian national backbones (e.g., AWS Middle East `me-central-1` / UAE or regional compliant VPS).
     - Storage for sensitive files (payment receipts, venue trade registers) uses private S3-compatible object storage with server-side encryption (AES-256-GCM) with short-lived pre-signed download URLs (15-minute TTL).
  3. **Data Residency & Law 151/2020 Compliance:**
     - **Data Minimization:** No National ID numbers (الرقم القومي) or precise background GPS locations are collected or stored.
     - **Field-Level Encryption:** Sensitive financial identifiers (InstaPay handles, wallet phone numbers) are encrypted in the database using AES-256-GCM with Application-Authenticated Data (AAD) and HMAC blind indexes for searchable queries.
     - **Cross-Border Transfer Controls:** Master cryptographic keys never leave the secure Key Vault. Backups are encrypted with separate KMS keys before transfer.

- **Alternatives Considered:**
  - *Direct On-Premise Hosting in Cairo:* Evaluated for strict local residency. Rejected for MVP due to prohibitive CAPEX, lack of managed failover, and inadequate DDoS mitigation compared to tier-one cloud providers.
  - *Unrestricted Global Public Cloud (US/EU):* Rejected due to regulatory exposure under Law 151/2020 concerning international transfer of Egyptian citizen personal data without explicit permit.

- **Consequences:**
  - Positive: Ultra-low latency for Egyptian end-users with Cairo PoP caching static assets and terminating TLS at the edge.
  - Positive: Full alignment with Egyptian data protection principles and auditability requirements.
  - Negative: Requires maintaining Cloudflare mTLS origin certificates and automated renewal procedures.
