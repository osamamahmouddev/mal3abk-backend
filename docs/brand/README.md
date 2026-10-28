# Malabak (ملعبك) — Brand Guidelines & Design Tokens

This document specifies the official visual identity, color palette, typography, and styling standards for all Malabak applications (Next.js 15 Web Dashboards, Flutter Mobile App, and Marketing Landing).

---

## 1. Color Palette & Tokens

### Primary Brand Palette
| Token | Hex | RGB | Purpose |
| :--- | :---: | :---: | :--- |
| `color-brand-navy` | `#071832` | `rgb(7, 24, 50)` | Core platform branding, primary headings, dark surfaces |
| `color-brand-red` | `#EB4F42` | `rgb(235, 79, 66)` | Primary action buttons, badges, key accents |
| `color-brand-gold` | `#F6B326` | `rgb(246, 179, 38)` | Ratings, VIP subscriptions, match champions, highlights |
| `color-pitch-green` | `#10B981` | `rgb(16, 185, 129)` | Available pitch slots, confirmed bookings, success states |

### Neutral & Surface Palette
| Token | Hex | Purpose |
| :--- | :---: | :--- |
| `color-bg-light` | `#F8FAFC` | Light mode application background |
| `color-surface-light` | `#FFFFFF` | Light mode cards, modals, popovers |
| `color-border-light` | `#E2E8F0` | Subtle light mode borders |
| `color-text-primary` | `#0F172A` | Primary readable high-contrast text |
| `color-text-muted` | `#64748B` | Secondary metadata and hints |
| `color-bg-dark` | `#0B132B` | Dark mode application canvas |
| `color-surface-dark` | `#1C2541` | Dark mode container surfaces |

---

## 2. Typography

### Primary Font: Tajawal (عربي)
* **Family:** `'Tajawal', sans-serif`
* **Weights:** `300` (Light), `400` (Regular), `500` (Medium), `700` (Bold), `800` (ExtraBold)
* **Usage:** Applied to all Arabic headers, body prose, labels, and buttons.
* **Characteristics:** Modern geometric Arabic type with optimized horizontal proportions and distinct diacritic clarity on mobile screens.

### Secondary Font: Inter (Latin & Numerals)
* **Family:** `'Inter', sans-serif`
* **Weights:** `400`, `500`, `600`, `700`
* **Usage:** English translations, code, Latin names, and formatted currency numbers (`150.00 EGP`).

---

## 3. Directionality & RTL Principles
1. **RTL First:** All components must be designed for Right-to-Left flow by default (`dir="rtl"` in HTML/body, `Directionality.rtl` in Flutter).
2. **Logical Spacing:** Use logical properties (`padding-inline-start`, `margin-inline-end`, `border-start-start-radius`) rather than physical left/right.
3. **Icon Mirroring:**
   - Directional icons (arrows, chevrons, forward/back buttons, undo/redo) must mirror dynamically based on active reading direction.
   - Symmetric icons (search, checkmark, clock, user, calendar) do NOT mirror.
