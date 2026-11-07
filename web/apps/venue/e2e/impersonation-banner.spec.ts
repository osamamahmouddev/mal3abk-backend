import { sealData } from "iron-session";
import { SESSION_SECRET, expect, test, venue } from "./fixtures";

// The banner lives in the app shell; any signed-in page works. Billing is mocked so the page renders without the API.
test.beforeEach(async ({ api }) => {
  api.on("GET", /^\/api\/v1\/venue-manager\/billing$/, () => ({ usage: { venues: 1, pitches: 1, maxStaffInOneVenue: 0 }, invoices: [] }));
  api.on("GET", /^\/api\/v1\/plans$/, () => []);
});

/** TASK-063: while support is signed in as the customer, a banner is always visible and the session is time-boxed. */

test("an impersonation session shows the support banner with the time left", async ({ page, context, api }) => {
  void api; // installs the BFF fixtures
  const sealed = await sealData(
    {
      accessToken: "e2e",
      refreshToken: "impersonation-has-no-refresh",
      accessExpiresAt: Date.now() + 15 * 60_000,
      user: { id: venue.ownerId, name: "صاحب الملعب", roles: ["VenueOwner"], impersonatedBy: "12121212-1212-4121-8121-121212121212", impersonationEndsAt: Date.now() + 14 * 60_000 },
      csrf: "e2e-csrf",
    },
    { password: SESSION_SECRET, ttl: 3600 },
  );
  await context.addCookies([{ name: "malabak-venue", value: sealed, url: "http://localhost:3100" }]);

  await page.goto("/venue/subscription");

  const banner = page.getByRole("alert").filter({ hasText: "وضع الدعم" });
  await expect(banner).toBeVisible();
  await expect(banner).toContainText("14 دقيقة");
  await expect(banner.getByRole("button", { name: "إنهاء الجلسة" })).toBeVisible();
});

test("an ordinary session has no support banner", async ({ page, api }) => {
  void api;
  await page.goto("/venue/subscription");
  await expect(page.getByRole("heading").first()).toBeVisible();
  await expect(page.getByText("وضع الدعم")).toHaveCount(0);
});

test("the support entry page needs a full code before trying", async ({ page }) => {
  await page.goto("/venue/impersonate");
  const button = page.getByRole("button", { name: "دخول" });
  await expect(button).toBeDisabled();
  await page.getByLabel("الكود").fill("x".repeat(43));
  await expect(button).toBeEnabled();
});
