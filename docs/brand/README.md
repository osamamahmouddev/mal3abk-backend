# Malabak (ملعبك) — Brand Guidelines & Design Tokens

> **Source of truth:** the approved design file (artifact "ملعبك — MVP"). Values below are taken from its artboards; `web/packages/ui/src/tokens.ts` and `docs/brand/tokens.json` mirror them.

This document specifies the official visual identity, color palette, typography, and styling standards for all Malabak applications (Next.js 15 Web Dashboards, Flutter Mobile App, and Marketing Landing).

---

## 1. Color Palette & Tokens

### Primary Brand Palette
| Token | Hex | RGB | Purpose |
| :--- | :---: | :---: | :--- |
| `color-brand-navy` | `#0F1B3D` | `rgb(15, 27, 61)` | Core platform branding, primary headings, dark surfaces |
| `color-brand-red` | `#D63B2F` | `rgb(214, 59, 47)` | Primary action buttons, badges, key accents |
| `color-brand-gold` | `#F5B62B` | `rgb(245, 182, 43)` | Ratings, VIP subscriptions, match champions, highlights |
| `color-brand-ink` | `#0A1330` | `rgb(10, 19, 48)` | Admin panel sidebar and primary actions |
| `color-pitch` | `#2E8B55` / `#287D4C` | — | Pitch illustrations (alternating stripes) |

### Neutral & Surface Palette
| Token | Hex | Purpose |
| :--- | :---: | :--- |
| `color-bg` | `#F3F5F9` | Page background |
| `color-surface` | `#FFFFFF` | Cards, dialogs, panels |
| `color-border` | `#EDF0F5` | Card borders and dividers |
| `color-border-strong` | `#C9CFD9` | Inputs and secondary buttons |
| `color-text-primary` | `#0F172A` | Primary text |
| `color-text-muted` | `#5B6475` | Secondary text and hints |

### Status Colors (tinted background + dark text — never color alone)
| Status | Background | Text |
| :--- | :---: | :---: |
| Success / confirmed | `#E5F5EB` | `#1B6E3E` |
| Warning / under review | `#FEF3D6` | `#7A4D00` |
| Error / rejected | `#FDECEA` | `#A8261C` |
| Neutral / waiting | `#EEF0F5` | `#3B4352` |

---

## 2. Typography

### Font: IBM Plex Sans Arabic
* **Family:** `'IBM Plex Sans Arabic', system-ui, sans-serif` (covers Arabic and Latin)
* **Weights:** `400`, `500`, `600`, `700`
* **Usage:** All text in the web apps and landing page, as in the design file. Load it with `next/font` and expose it as `--font-arabic`.

---

## 3. Directionality & RTL Principles
1. **RTL First:** All components must be designed for Right-to-Left flow by default (`dir="rtl"` in HTML/body, `Directionality.rtl` in Flutter).
2. **Logical Spacing:** Use logical properties (`padding-inline-start`, `margin-inline-end`, `border-start-start-radius`) rather than physical left/right.
3. **Icon Mirroring:**
   - Directional icons (arrows, chevrons, forward/back buttons, undo/redo) must mirror dynamically based on active reading direction.
   - Symmetric icons (search, checkmark, clock, user, calendar) do NOT mirror.
