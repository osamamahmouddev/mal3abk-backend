# ADR 0003: Mobile Technology Choice (Flutter vs .NET MAUI vs React Native)

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  Malabak requires a player-facing mobile application for iOS and Android targeting the Egyptian sports community. The mobile app must deliver:
  1. High performance (60–120 FPS) on mid-tier and budget Android hardware common in Egypt (e.g. Xiaomi, Realme, Samsung A-series).
  2. First-class Arabic RTL layout rendering, custom Arabic typography (IBM Plex Sans Arabic, as in the design file and the app theme), and bidirectional UI components.
  3. Interactive features including dynamic pitch slot pickers, team tactical lineup boards, live challenge notifications, and smooth map interactions.
  4. Integration with device hardware (camera/gallery for payment receipts, biometric unlock, Firebase Cloud Messaging, local secure storage).

- **Decision:**
  We adopt **Flutter (Dart)** for the Malabak Player Mobile App (`mobile/`):
  1. **Consistent Skia/Impeller Rendering Engine:** Flutter controls every pixel on screen independently of OEM Android platform differences, eliminating layout glitches and Arabic font shaping/kerning inconsistencies across heterogeneous Android OS skins.
  2. **Superior RTL Support:** Native directional widgets (`Directionality`, `EdgeInsetsDirectional`, `AlignmentDirectional`) provide comprehensive RTL switching without style degradation.
  3. **High-Performance Canvas & Custom Painters:** Complex visual widgets like football pitch tactical formations, real-time availability heatmaps, and interactive timeline slots render at native 60–120 FPS without bridge serialization penalties.
  4. **Robust Native Security Integrations:**
     - `flutter_secure_storage` leverages Android EncryptedSharedPreferences (AES-256 with Android Keystore) and iOS Keychain for token storage.
     - Biometric authentication (`local_auth`) with cryptographic challenge binding.
     - TLS certificate pinning and SafetyNet/Play Integrity API attestation.
  5. **Shared Domain Contracts:** The mobile app consumes the exact same versioned OpenAPI contract (`/api/v1`) using generated Dart DTOs via `openapi-generator-cli`.

- **Alternatives Considered:**
  - *.NET MAUI:* Evaluated to share C# codebase with the backend. Rejected due to slower cold startup times, platform-specific rendering quirks for complex Arabic bidirectional UI, and smaller ecosystem for custom sports UI components.
  - *React Native / Expo:* Evaluated. Rejected due to performance overhead of JavaScript bridge during rapid touch interactions (e.g. interactive slot zooming and lineup dragging), and historical rendering quirks with Arabic diacritics and HarfBuzz font shaping on low-end Android OEM builds.
  - *Native Swift / Kotlin:* Rejected for MVP due to doubling mobile engineering effort and maintenance costs across two separate codebases.

- **Consequences:**
  - Positive: Single codebase for iOS and Android with identical visual fidelity and 120Hz smooth scrolling.
  - Positive: Rapid iteration speed with Hot Reload for UI refinements.
  - Negative: Requires Dart language familiarity within the mobile team (separate from backend C#).
  - Negative: App bundle size baseline is slightly larger (~15–20 MB) compared to pure native Kotlin/Swift apps.
