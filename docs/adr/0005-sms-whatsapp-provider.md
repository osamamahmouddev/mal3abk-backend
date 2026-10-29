# ADR 0005: SMS & WhatsApp OTP Delivery Strategy and Toll Fraud Mitigation

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  Malabak's primary authentication mechanism for players is passwordless Phone OTP. In the Egyptian market:
  1. Delivery rates of global SMS aggregators (e.g. Twilio) to Egyptian Mobile Network Operators (WE, Vodafone, Orange, e&) suffer from variable latency (30–120s) and sporadic filtering during peak hours (6 PM – 11 PM, matching football pitch prime times).
  2. SMS pumping (toll fraud) poses an existential financial risk if bots trigger high-volume SMS dispatches.
  3. WhatsApp penetration in Egypt is over 85%, offering higher delivery reliability, immediate push delivery, and lower operational unit cost compared to international A2P SMS.

- **Decision:**
  We adopt a **Tiered Multi-Channel Messaging Gateway** combining local Egyptian SMS aggregators with Meta WhatsApp Business Cloud API:
  1. **Provider Architecture:**
     - **Channel 1 (Preferred / Lowest Cost & Highest Delivery):** Meta WhatsApp Business Cloud API. Delivered directly to player's verified WhatsApp account with branded authentication template.
     - **Channel 2 (Primary Local SMS):** VictoryLink or Unifonic with local Egyptian Sender ID (`MALABAK`) registered with NTRA (National Telecommunications Regulatory Authority), ensuring direct route delivery to Egyptian carriers.
     - **Channel 3 (International Fallback):** Twilio or Infobip reserved strictly for foreign phone numbers (e.g., tourists or Gulf-based users +966, +971).
  2. **Toll Fraud & SMS Pumping Defenses:**
     - **Bot Defense:** Mandatory Cloudflare Turnstile verification prior to accepting `POST /api/v1/auth/send-otp`. Requests failing Turnstile are discarded before invoking external messaging APIs.
     - **Multi-Tiered Rate Limiting (Redis Sliding Window):**
       - Per Phone Cooldown: 1 request per 60 seconds.
       - Per Phone Window: Maximum 3 OTP requests per 10 minutes.
       - Per IP Window: Maximum 5 OTP requests per 10 minutes.
       - Per Device Identifier / Fingerprint: Maximum 5 OTP requests per hour.
     - **Daily Financial Circuit Breaker:** Daily budget caps on external SMS provider spend. If the cap is reached, SMS falls back to WhatsApp-only and notifies on-call engineering via Seq/Slack.
  3. **OTP Cryptographic Security:**
     - 6-digit cryptographically secure random number (`RandomNumberGenerator.GetInt32(100_000, 1_000_000)`).
     - Never stored in plaintext; stored in Redis as `HMAC-SHA256(secretKey, phone + ":" + otp)` with a strict 5-minute TTL.
     - Max 5 failed attempts per OTP lifecycle before the key is deleted and invalidated.
     - Verification evaluated using constant-time comparison `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.

- **Alternatives Considered:**
  - *Twilio-only Global SMS:* Rejected due to high failure rate on Egyptian local networks and 4x higher pricing compared to local Egyptian NTRA-licensed gateways.
  - *Email OTP as Primary:* Rejected because the player target demographic expects instant frictionless mobile authentication; email adoption for local sports activities in Egypt is negligible.

- **Consequences:**
  - Positive: High OTP delivery success (>98%) within 5 seconds via WhatsApp + local Egyptian SMS.
  - Positive: Complete protection against automated SMS fraud and financial drain.
  - Negative: Requires maintaining dual integration adapters (`IPhoneVerificationProvider` with WhatsApp and SMS implementations).
