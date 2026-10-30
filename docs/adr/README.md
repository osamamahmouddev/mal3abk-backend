# Architecture Decision Records (ADRs) — Malabak (ملعبك)

This directory maintains the permanent Architecture Decision Records for the Malabak platform in accordance with **[TASK-101]** and system engineering rules.

| ADR | Title | Status | Date | Area |
| :---: | :--- | :---: | :---: | :--- |
| **[ADR-0001](0001-backend-stack.md)** | Backend Architecture & Technology Stack Choices (.NET 10, PostgreSQL 17, Redis 7.2) | Accepted | 2026-10-24 | Backend / Core |
| **[ADR-0002](0002-nextjs-web-dashboards.md)** | Next.js (App Router) for Venue and Admin Dashboards | Accepted | 2026-10-24 | Web / Frontend |
| **[ADR-0003](0003-mobile-technology.md)** | Mobile Technology Choice (Flutter vs .NET MAUI vs React Native) | Accepted | 2026-10-28 | Mobile App |
| **[ADR-0004](0004-hosting-and-data-residency.md)** | Hosting Infrastructure, Cloudflare Edge & Egyptian Data Residency (Law 151/2020) | Accepted | 2026-10-28 | Cloud / Security |
| **[ADR-0005](0005-sms-whatsapp-provider.md)** | SMS & WhatsApp OTP Delivery Strategy and Toll Fraud Mitigation | Accepted | 2026-10-28 | Auth / Security |
| **[ADR-0006](0006-maps-and-distance-provider.md)** | Geospatial Engine, Distance Calculation & Map Provider (PostGIS & MapLibre) | Accepted | 2026-10-28 | Geospatial / Privacy |
| **[ADR-0007](0007-retention-and-deletion-policy.md)** | Data Retention, Archival & Account Deletion Policy (Law 151/2020 Compliance) | Accepted | 2026-10-28 | Compliance / DB |
| **[ADR-0008](0008-hold-expiry-and-ranking-defaults.md)** | Booking Hold Expiry Defaults, Cancellation Rules & Team Ranking Engine (Glicko-2) | Accepted | 2026-10-28 | Business Rules / Anti-Cheat |
