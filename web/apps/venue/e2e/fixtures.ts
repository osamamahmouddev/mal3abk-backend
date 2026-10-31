import { test as base, expect, type Page, type Route } from "@playwright/test";
import { sealData } from "iron-session";

export const SESSION_SECRET = "e2e-only-session-secret-0123456789abcdef";
export const VENUE_ID = "11111111-1111-4111-8111-111111111111";
export const PITCH_ID = "22222222-2222-4222-8222-222222222222";
export const BOOKING_ID = "33333333-3333-4333-8333-333333333333";

const ALL_PERMISSIONS = [
  "venue.view", "venue.edit", "venue.pitches.manage", "venue.schedules.manage", "venue.blocked_slots.manage", "venue.policies.manage",
  "venue.bookings.view", "venue.bookings.approve", "venue.payment_proofs.review", "venue.customers.view", "venue.reports.view", "venue.staff.manage",
];

export const venue = {
  id: VENUE_ID,
  name: "ملعب الاختبار",
  slug: "e2e-venue",
  ownerId: "44444444-4444-4444-8444-444444444444",
  address: "شارع ٩",
  city: "القاهرة",
  area: "المعادي",
  phoneNumber: "+201000000000",
  approvalStatus: "Approved",
  isApproved: true,
  isPublished: true,
  facilities: ["parking"],
  photos: [],
  pitches: [{ id: PITCH_ID, venueId: VENUE_ID, name: "ملعب ١", turfType: "ArtificialTurf", surfaceArabicDisplayName: "نجيل صناعي", capacity: 5, isActive: true, photos: [] }],
};

/** A request the page sent to the API through the BFF. */
export interface Captured {
  method: string;
  path: string;
  body: unknown;
}

type Handler = (route: Route, path: string, captured: Captured) => unknown | Promise<unknown>;

/**
 * Signs the browser in with a real sealed session cookie (same format the BFF writes) and answers BFF calls from the
 * handlers; anything not handled gets an empty 200 so a forgotten fixture shows up as an assertion, not a hang.
 */
export const test = base.extend<{ api: { on: (method: string, pattern: RegExp, handler: Handler) => void; calls: Captured[] } }>({
  api: async ({ page, context }, use) => {
    const sealed = await sealData(
      {
        accessToken: "e2e",
        refreshToken: "e2e",
        accessExpiresAt: Date.now() + 3_600_000,
        user: { id: venue.ownerId, name: "صاحب الملعب", roles: ["VenueOwner"] },
        csrf: "e2e-csrf",
      },
      { password: SESSION_SECRET, ttl: 3600 },
    );
    await context.addCookies([
      { name: "malabak-venue", value: sealed, url: "http://localhost:3100" },
      { name: "malabak-csrf", value: "e2e-csrf", url: "http://localhost:3100" },
    ]);

    const handlers: { method: string; pattern: RegExp; handler: Handler }[] = [];
    const calls: Captured[] = [];
    const on = (method: string, pattern: RegExp, handler: Handler) => handlers.unshift({ method, pattern, handler });

    on("GET", /^\/api\/v1\/venue-manager\/venues$/, () => [venue]);
    on("GET", /^\/api\/v1\/me\/permissions$/, () => ({ roles: ["VenueOwner"], permissions: [], venues: [{ venueId: VENUE_ID, accessLevel: "Owner", permissions: ALL_PERMISSIONS }] }));
    on("GET", /^\/api\/v1\/me\/notifications\/unread-count$/, () => ({ unreadCount: 0 }));
    on("GET", /^\/api\/v1\/reference\//, () => []);

    await page.route("**/venue/bff/**", async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname.replace(/^\/venue\/bff/, "");
      const method = route.request().method();
      const raw = route.request().postData();
      const captured: Captured = { method, path: path + url.search, body: raw ? safeJson(raw) : null };
      calls.push(captured);
      const match = handlers.find((h) => h.method === method && h.pattern.test(path));
      if (!match) return route.fulfill({ status: 200, contentType: "application/json", body: "{}" });
      const result = await match.handler(route, path, captured);
      if (result === undefined) return; // the handler fulfilled the route itself
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(result) });
    });

    await use({ on, calls });
  },
});

function safeJson(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

/** Waits until the page has sent a request matching the predicate and returns it. */
export async function waitForCall(page: Page, calls: Captured[], predicate: (c: Captured) => boolean): Promise<Captured> {
  await expect.poll(() => calls.find(predicate) !== undefined, { timeout: 10_000 }).toBe(true);
  return calls.find(predicate)!;
}

export { expect };
