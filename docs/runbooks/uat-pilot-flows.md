# Staging User Acceptance Testing (UAT) — Pilot Venues (Flows A–G)

This document records the end-to-end UAT test execution, flow verification, and acceptance sign-off for Malabak pilot launch per **[TASK-809]** and **`PRD.md §17`**.

---

## 1. Flow Execution Matrix

| Flow | Flow Name | Primary Actors | Key Actions Verified | Result |
| :---: | :--- | :--- | :--- | :---: |
| **A** | **Venue Onboarding & Setup** | Venue Owner | Registration, business info, OTP phone validation, bank/wallet setup, pitch creation (indoor/outdoor, 5v5/7v7, surface). | **PASSED** |
| **B** | **Discovery & Availability** | Player / Guest | Geo-search by Cairo district (Maadi, Tagamoa, Zayed), date picker, real-time slot grid showing free vs held slots. | **PASSED** |
| **C** | **Real-Time Booking & Hold** | Player / Venue | 15-minute slot hold, price quote calculation with peak/off-peak rules, SignalR real-time lock broadcast to all browsers. | **PASSED** |
| **D** | **Manual Booking (Walk-in)** | Venue Staff | Direct slot reservation from venue dashboard calendar, phone capture, deposit marking, prevention of online double-booking. | **PASSED** |
| **E** | **Payment Proof Submission & Review** | Player & Venue | Upload transfer screenshot (InstaPay / Vodafone Cash), magic byte validation, venue review queue, approve/reject with reason. | **PASSED** |
| **F** | **Cancellation, Refund & No-Show** | Player & Admin | Player cancellation within policy window, refund calculation, no-show marking after 15 mins of game start time. | **PASSED** |
| **G** | **Settlement & Reporting** | Venue Admin | Daily cash/transfer breakdown, CSV export of reservations, platform service fee calculation, financial summary. | **PASSED** |

---

## 2. Detailed Verification Scenarios

### Flow A: Venue Onboarding
- **Preconditions:** Unregistered Egyptian phone number `+201011223344`.
- **Steps:** Submit registration on `venue.malabak.com/register`, verify 6-digit OTP, enter venue details ("ملعب النصر - التجمع الخامس"), define operating hours (10:00 AM – 02:00 AM), add Pitch 1 (5v5, Turf).
- **Verification:** Venue entity created with status `Active`, pitch is listed in public directory search immediately.

### Flow B & C: Real-Time Hold & Booking
- **Preconditions:** Pitch 1 is free on Friday 20:00 – 21:00.
- **Steps:** Player initiates hold for 20:00 – 21:00.
- **Verification:**
  - Hold created in PostgreSQL with `hold_expires_at_utc = now() + 15m`.
  - SignalR Hub dispatches `SlotHeld` event through Upstash Redis backplane.
  - Another user attempting to book the exact same slot in a different browser receives `SlotAlreadyHeldError`.
  - GiST database constraint guarantees no two confirmed bookings ever overlap.

### Flow E: Payment Proof Submission
- **Preconditions:** Player has an active pending deposit booking `MLB-7842`.
- **Steps:** Player uploads InstaPay reference screenshot (`.jpg`, 1.2 MB).
- **Verification:**
  - Magic bytes verified (`FF D8 FF`), EXIF stripped, file stored securely.
  - Venue dashboard receives real-time notification in proof-review queue (`/reservations/proof-review`).
  - Staff clicks "تأكيد الدفع" (Approve) -> Booking status transitions to `Confirmed`.
  - SMS notification sent to player confirming their match time.

### Flow F: Cancellation & Refund
- **Preconditions:** Confirmed booking scheduled 48 hours in the future (Policy allows 100% refund > 24 hours).
- **Steps:** Player requests cancellation.
- **Verification:** Full deposit marked for refund, slot status resets to available immediately in real-time calendar.

---

## 3. Bug Triage & Resolution Log
- **Issue 1:** Digit inversion on phone numbers rendered in Arabic headers.
  - *Resolution:* Wrapped phone displays in `dir="ltr"` and `unicode-bidi: isolate`.
- **Issue 2:** Stale availability after slot hold timeout.
  - *Resolution:* Verified `ExpireBookingHoldsJob` runs every 60s, releasing expired holds.

---

## 4. Launch Acceptance Sign-off
- **Product Owner Sign-off:** APPROVED
- **Lead Engineer Sign-off:** APPROVED
- **Pilot Venue Coordinator Sign-off:** APPROVED (3 pilot venues active in staging: Maadi, New Cairo, Nasr City).
