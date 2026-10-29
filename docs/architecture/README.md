# Malabak Architecture Overview

Malabak is implemented as a **Modular Monolith** in .NET 10, adhering to Clean Architecture principles within each bounded context.

---

## 1. High-Level System Architecture

```mermaid
graph TD
    ClientWeb["Web Dashboards (Next.js 15)\n- Venue Dashboard\n- Admin Dashboard\n- Landing"] -->|HTTPS / REST v1| Edge["Cloudflare Edge (Cairo PoP)\nWAF + Turnstile + Rate Limiting"]
    ClientMobile["Player Mobile App\n(Flutter iOS / Android)"] -->|HTTPS / REST v1| Edge
    
    Edge -->|mTLS Origin Pull| Host["Malabak.Api Host\nASP.NET Core Minimal APIs\n- PiiMasking Enricher\n- RFC 9457 ProblemDetails\n- Deny-by-default Auth"]

    subgraph ModularMonolith["Modular Monolith Boundaries"]
        Host --> Identity["Malabak.Modules.Identity\nPhone OTP, ES256 JWT, Users"]
        Host --> Venues["Malabak.Modules.Venues\nVenues, Pitches, Schedules, Policies"]
        Host --> Booking["Malabak.Modules.Booking\n10-State Machine, Hold Expiry"]
        Host --> Payments["Malabak.Modules.Payments\nManual Proofs (InstaPay/VF Cash)"]
        Host --> Matches["Malabak.Modules.Matches\nChallenges, Glicko-2 Ratings"]
        Host --> Teams["Malabak.Modules.Teams\nSquads, Lineups, Captaincy"]
        Host --> Subscriptions["Malabak.Modules.Subscriptions\nVenue SaaS Tiers, Entitlements"]
        Host --> Audit["Malabak.Modules.Audit\nImmutable Tamper-Evident Logs"]
        Host --> Notifications["Malabak.Modules.Notifications\nFCM, In-App, WhatsApp/SMS"]
        Host --> Support["Malabak.Modules.Support\nDisputes, Inquiries"]
        
        Identity & Venues & Booking & Payments & Matches & Teams & Subscriptions & Audit & Notifications & Support --> SharedKernel["Malabak.SharedKernel\nResult, Money, PhoneNumber, TimeSlotRange, IClock"]
    end

    subgraph DataStore["Persistence & Infrastructure"]
        ModularMonolith --> Postgres[("PostgreSQL 17\nSchema per module\nGiST exclusion constraints")]
        ModularMonolith --> Redis[("Redis 7.2\nOTP Hashes, Distributed Locks, Cache")]
        ModularMonolith --> Hangfire["Hangfire\nSlot Hold Reaper, Recurring Jobs"]
        Payments --> S3["Private S3 Object Storage\nAES-256 Server-Side Encryption"]
    end
```

---

## 2. Module Rules & Boundary Enforcement
1. **Zero Direct Module Coupling:** A module must never reference another module's assembly directly. This rule is enforced at compile-time and continuously verified by `Malabak.ArchitectureTests`.
2. **Integration via Contracts & Domain Events:** Cross-module communication occurs strictly via `SharedKernel` primitives, Outbox integration events, or mediator handlers.
3. **Dedicated PostgreSQL Schemas:** Each module owns its dedicated schema (`identity.*`, `venues.*`, `booking.*`, `payments.*`, `teams.*`, `matches.*`, `subscriptions.*`, `audit.*`, `notifications.*`, `support.*`). Cross-schema SQL joins are strictly forbidden.
4. **Time & Clock Abstraction:** All domain models and handlers depend on `IClock` rather than `DateTime.UtcNow` or `DateTime.Now`, ensuring full testability and accurate Egyptian Time Zone (`Africa/Cairo`) conversions.
