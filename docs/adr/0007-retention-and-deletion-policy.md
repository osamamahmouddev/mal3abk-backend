# ADR 0007: Data Retention, Archival & Account Deletion Policy (Law 151/2020 Compliance)

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  Malabak processes personal user data, sporting activity logs, venue operational records, and manual financial proof documents (InstaPay/Vodafone Cash transfer screenshots). We must comply with:
  1. The Egyptian Personal Data Protection Law (Law No. 151 of 2020) regarding data minimization, purpose limitation, and the user's right to account erasure.
  2. The Egyptian Commercial Code (Law No. 17 of 1999) and Egyptian Tax Regulations, which mandate maintaining financial books, transaction histories, and audit trails for a minimum of 5 to 7 years.
  3. Storage cost optimization for high-resolution image uploads (payment receipts) and background job clutter prevention.

- **Decision:**
  We adopt a **Lifecycle-Based Data Retention & Anonymization Matrix**:

| Data Category | Retention Period | Storage Tier | Expiry & Purge Mechanism |
| :--- | :--- | :--- | :--- |
| **Financial Ledgers & Booking History** | **7 Years** | PostgreSQL (Hot) -> Encrypted Cold Archive | Immutable append-only audit records. Retained for legal & tax audits. |
| **Audit Logs (`audit.*`)** | **7 Years** | PostgreSQL -> Compressed S3 Parquet / Cold Archive | Automated monthly archival partition. Cryptographically verifiable event chain. |
| **Payment Proof Images (Confirmed)** | **180 Days Hot, 2 Years Cold** | Private S3 Standard -> S3 Glacier Deep Archive | S3 Lifecycle transition rule; permanently expunged after 2 years. |
| **Payment Proof Images (Rejected/Expired)** | **90 Days** | Private S3 Standard | Hard delete from S3 via automated Hangfire cleanup job after dispute window. |
| **Active Booking Holds (`PendingDeposit`)** | **10 Minutes** | Redis & PostgreSQL (Hold State) | Hangfire recurring job runs every 60 seconds; releases expired slots back to availability pool. |
| **Ephemeral OTP Codes** | **5 Minutes** | Redis In-Memory | Redis native volatile TTL eviction; keys deleted immediately upon successful verification. |
| **Revoked & Expired Refresh Tokens** | **30 Days** | PostgreSQL `identity.refresh_tokens` | Hangfire batch job deletes revoked tokens older than 30 days. |
| **Application Structured Logs** | **30 Days Hot, 1 Year Cold** | Seq / ElasticSearch -> Compressed Storage | Retention policy drops verbose debug events after 7 days; info/error after 30 days. |

  ### Account Deletion Procedure (Right to Erasure / Law 151):
  1. **Phase 1 — Immediate Soft Deactivation:**
     - When a user requests deletion (`POST /api/v1/users/me/delete`), the account is marked `IsDeleted = true`, `DeletedAtUtc = now`.
     - All active JWT refresh tokens and user sessions are immediately revoked (`RevokeAllSessions`).
     - User is immediately logged out on all devices.
  2. **Phase 2 — 30-Day Recovery Grace Period:**
     - User can cancel the deletion request within 30 days by authenticating via original phone OTP and confirming account restoration.
  3. **Phase 3 — Irreversible Anonymization (Day 31):**
     - User's PII is permanently wiped:
       - `Name` is overwritten with `"لاعب محذوف / Deleted Player"`.
       - `PhoneNumber` and `NormalizedPhoneNumber` are overwritten with random cryptographically unique tombstones (`deleted_<guid>`).
       - Blind index HMAC hash is destroyed, making future searches for the original phone number yield zero results.
       - Profile avatar image is shredded from S3 object storage.
     - Historical booking records, match results, and financial receipts are preserved in an anonymized state, safeguarding historical financial reconciliations and team match rankings without retaining individual user identity.

- **Alternatives Considered:**
  - *Immediate Hard Deletion of Database Rows:* Rejected because cascading deletes would corrupt historical financial records, break match ranking integrity, and violate statutory 7-year Egyptian accounting mandates.
  - *Indefinite Storage of Payment Proof Images:* Rejected due to escalating S3 storage costs and regulatory risks of retaining third-party personal financial screenshots longer than operationally necessary.

- **Consequences:**
  - Positive: Full legal compliance with both Egyptian commercial accounting laws and data privacy rights.
  - Positive: Predictable storage utilization with automated tier transitions and cleanup jobs.
  - Negative: Requires maintaining automated scheduled Hangfire jobs and S3 lifecycle configuration policies.
