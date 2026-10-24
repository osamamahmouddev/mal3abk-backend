# PRD — Malabak (ملعبك) Football Community & Pitch Booking Platform

## 1. Executive Summary & Vision
Malabak (ملعبك) is a comprehensive football ecosystem tailored for the Egyptian market. It unites three major products under one unified backend architecture:
1. **Player App (Mobile-First):** Discover venues, browse real-time pitch availability, reserve slots, upload manual payment proofs, create teams, schedule challenges, and track match rankings.
2. **Venue Dashboard (Web, Desktop-First):** Manage pitches, configure operating hours (including overnight slots crossing midnight), set dynamic pricing rules, review payment proofs, confirm reservations, and analyze operational KPIs.
3. **Admin Dashboard (Web):** System governance, venue verification and approval queues, subscription tier management (SaaS), audit log inspections, and dispute moderation.

---

## 2. Core Market Specifications
- **Primary Market:** Egypt.
- **Languages:** Arabic (RTL, primary default) and English (LTR).
- **Currency:** Egyptian Pound (`EGP` / `ج.م`).
- **Time Zone:** `Africa/Cairo` (accounting for Egypt Daylight Saving Time transitions; never hardcode offset).

---

## 3. Product Architecture & Bounded Contexts
- **Identity:** Dual authentication pathways (Phone OTP for players, Email/Password + 2FA for Venue Staff & Platform Admins). Role-based & Resource-based access control.
- **Venues:** Venue listings, pitch configurations, sports facilities, operating hours, cancellation policies.
- **Booking:** Booking lifecycle aggregate (`Draft` -> `PendingDeposit` -> `ProofSubmitted` -> `UnderReview` -> `Confirmed` / `Rejected` / `Expired` / `Cancelled` / `Completed` / `NoShow`).
- **Payments:** Manual payment method aggregation (InstaPay, Vodafone Cash, Bank Transfer), proof upload, review and verification workflows.
- **Teams & Matches:** Football squads, captaincy permissions, invitations, match challenges, bilateral result reporting, dispute handling, and ranking strategy calculation.
- **Subscriptions:** Venue SaaS tier limits, quota verification (`IEntitlementService`), billing records.
- **Notifications & Audit:** Outbox-driven transactional notifications (In-app + FCM), immutable audit logs.
