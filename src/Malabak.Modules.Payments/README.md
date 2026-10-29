# Malabak.Modules.Payments

Bounded context managing manual payment methods, receipt uploads, proof verification workflows, and financial transaction records.

## Key Features
- **Egyptian Manual Payment Gateways:** InstaPay, Vodafone Cash, and direct bank transfers.
- **Secure Proof Handling:** Uploads directly to private S3 buckets via short-lived pre-signed URLs with magic byte validation and EXIF metadata stripping.
- **Venue Review Workflow:** Approval or rejection of deposit receipts with audit trails and refund/credit tracking.
- **PostgreSQL Schema:** `payments.*`.
