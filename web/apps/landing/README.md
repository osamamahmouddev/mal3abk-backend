# Malabak Landing Page (`@malabak/landing`)

The public marketing page for **ملعبك**: one Arabic (RTL) page for players and venue owners, built from the `Landing` / `LandingMobile` artboards in the design file.

- **URL in development:** http://localhost:3002
- **Stack:** Next.js 15 (App Router), React 19, TypeScript, plain CSS (no Tailwind — keeps the page light)
- **Rendering:** fully static. The page is server-rendered at build time; only the interactive parts ship JavaScript.

## Run it

From the `web/` folder:

```bash
pnpm install                         # first time only
pnpm --filter @malabak/landing dev   # http://localhost:3002
```

Production build and preview:

```bash
pnpm --filter @malabak/landing build
pnpm --filter @malabak/landing start   # serves the build on http://localhost:3002
```

No pnpm? From the repo root: `npm --prefix web/apps/landing run dev`.

## What's on the page

| Section | Anchor | Notes |
|---|---|---|
| Hero + app mockup | — | App Store / Google Play buttons say "قريباً" and scroll to the sign-up form |
| How it works | `#how` | 3 steps |
| Before / after, features | — | |
| Teams & challenges | `#teams` | Ranking and challenge mockups |
| For venue owners + plans | `#venues` | Dashboard mockup, monthly / yearly toggle, plan cards |
| FAQ | `#faq` | Also emitted as `FAQPage` structured data for search engines |
| Join (waitlist) | `#join` | Player / venue switch + Egyptian mobile number validation |

## Files

```
src/
├─ app/
│  ├─ layout.tsx        # <html lang="ar" dir="rtl">, font (next/font), SEO metadata
│  ├─ page.tsx          # the whole page (server component) + phone mockup
│  └─ globals.css       # design styles, incl. the mobile breakpoint (≤ 900px)
└─ components/
   ├─ LandingState.tsx  # shared client state: choosing a plan switches the join form to "venue"
   ├─ Plans.tsx         # plan cards + monthly/yearly toggle (client)
   ├─ Faq.tsx           # accordion (client)
   ├─ faqItems.ts       # FAQ content — used by the accordion and the structured data
   └─ JoinForm.tsx      # waitlist form (client)
```

## Not done yet

- **The waitlist form doesn't send anything.** It validates the number and shows the success state, but there is no API endpoint to store it yet — see the `TODO` in `JoinForm.tsx`. When it's added, the API's CORS policy (`src/Malabak.Api/Program.cs`) must also allow `http://localhost:3002` (or the request should go through a Next.js route handler).
- **Prices** are `[السعر]` placeholders, as in the design.
- **Store links** point to the sign-up form until the apps are published.
