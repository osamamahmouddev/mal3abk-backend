import { test as base, expect, type Page, type Route } from "@playwright/test";
import { sealData } from "iron-session";

export const SESSION_SECRET = "e2e-only-session-secret-0123456789abcdef";
export const ADMIN_ID = "a0000000-0000-4000-8000-000000000001";

/** Navigation keys the API returns per role (mirrors RolePermissions + AdminNavigation on the server). */
export const NAVIGATION = {
  Admin: ["dashboard", "users", "venues", "approvals", "bookings", "disputes", "moderation", "support", "plans", "subscriptions", "invoices", "featured", "settings", "accounts", "audit"],
  SupportAgent: ["dashboard", "users", "bookings", "support"],
  Finance: ["plans", "subscriptions", "invoices"],
} as const;

export type StaffRole = keyof typeof NAVIGATION;

export interface Captured {
  method: string;
  path: string;
  body: unknown;
  headers: Record<string, string>;
}

type Handler = (route: Route, path: string, captured: Captured) => unknown | Promise<unknown>;

export interface Api {
  on: (method: string, pattern: RegExp, handler: Handler) => void;
  calls: Captured[];
  signInAs: (role: StaffRole) => Promise<void>;
}

/**
 * Signs the browser in with a real sealed admin session cookie and answers BFF calls from handlers. Unhandled calls get an
 * empty 200 so a missing fixture shows up as a failed assertion, not a hang.
 */
export const test = base.extend<{ api: Api }>({
  api: async ({ page, context }, use) => {
    const handlers: { method: string; pattern: RegExp; handler: Handler }[] = [];
    const calls: Captured[] = [];
    const on = (method: string, pattern: RegExp, handler: Handler) => handlers.unshift({ method, pattern, handler });

    const signInAs = async (role: StaffRole) => {
      const sealed = await sealData(
        { accessToken: "e2e", refreshToken: "e2e", accessExpiresAt: Date.now() + 3_600_000, user: { id: ADMIN_ID, name: "مدير الاختبار", roles: [role] }, csrf: "e2e-csrf" },
        { password: SESSION_SECRET, ttl: 3600 },
      );
      await context.addCookies([
        { name: "malabak-admin", value: sealed, url: "http://localhost:3110" },
        { name: "malabak-csrf", value: "e2e-csrf", url: "http://localhost:3110" },
      ]);
      on("GET", /^\/api\/v1\/admin\/navigation$/, () => NAVIGATION[role].map((key) => ({ key, path: `/${key}`, permission: "x" })));
    };

    on("GET", /^\/api\/v1\/admin\/venues\/pending-approval$/, () => []);
    on("GET", /^\/api\/v1\/admin\/dashboard$/, () => ({ from: "2026-10-09", to: "2026-11-07", timeZone: "Africa/Cairo", figures: {}, series: {}, distributions: {} }));
    on("GET", /^\/api\/v1\/me$/, () => ({ userId: ADMIN_ID, fullName: "مدير الاختبار" }));

    await page.route("**/admin/bff/**", async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname.replace(/^\/admin\/bff/, "");
      const method = route.request().method();
      const raw = route.request().postData();
      const captured: Captured = { method, path: path + url.search, body: raw ? safeJson(raw) : null, headers: route.request().headers() };
      calls.push(captured);
      const match = handlers.find((h) => h.method === method && h.pattern.test(path));
      if (!match) return route.fulfill({ status: 200, contentType: "application/json", body: "{}" });
      const result = await match.handler(route, path, captured);
      if (result === undefined) return;
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(result) });
    });

    await use({ on, calls, signInAs });
  },
});

function safeJson(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

export async function waitForCall(page: Page, calls: Captured[], predicate: (c: Captured) => boolean): Promise<Captured> {
  await expect.poll(() => calls.find(predicate) !== undefined, { timeout: 10_000 }).toBe(true);
  return calls.find(predicate)!;
}

export function problem(route: Route, status: number, title: string) {
  return route.fulfill({ status, contentType: "application/problem+json", body: JSON.stringify({ title, status, detail: title }) });
}

export { expect };
