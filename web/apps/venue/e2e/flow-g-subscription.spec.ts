import { expect, test, waitForCall } from "./fixtures";

/** Flow G (TASK-507): the owner sees plan, usage and invoices, compares plans and requests an upgrade (no online payment). */

const starter = {
  id: "55555555-5555-4555-8555-555555555555",
  code: "starter",
  name: "Starter",
  description: "ملعب واحد",
  monthlyPrice: 300,
  annualPrice: 3000,
  currency: "EGP",
  maxVenues: 1,
  maxPitches: 2,
  maxStaffPerVenue: 2,
  features: [],
  trialDays: 14,
  graceDays: 7,
  isPublic: true,
  isDefault: true,
  sortOrder: 0,
  status: "Active",
  subscribers: 4,
  updatedAtUtc: "2026-11-01T10:00:00Z",
};
const pro = { ...starter, id: "66666666-6666-4666-8666-666666666666", code: "pro", name: "Pro", monthlyPrice: 900, annualPrice: 9000, maxVenues: 5, maxPitches: 20, maxStaffPerVenue: 10, features: ["reports.advanced"], isDefault: false };
const tiny = { ...starter, id: "77777777-7777-4777-8777-777777777777", code: "tiny", name: "Tiny", maxPitches: 1, isDefault: false };

test.beforeEach(async ({ api }) => {
  api.on("GET", /^\/api\/v1\/plans$/, () => [tiny, starter, pro]);
  api.on("GET", /^\/api\/v1\/venue-manager\/billing$/, () => ({
    subscription: {
      id: "88888888-8888-4888-8888-888888888888",
      ownerId: "44444444-4444-4444-8444-444444444444",
      plan: starter,
      cycle: "Monthly",
      status: "Active",
      startedAtUtc: "2026-10-01T00:00:00Z",
      currentPeriodEndUtc: "2026-12-01T00:00:00Z",
      graceEndsAtUtc: null,
      statusReason: null,
      allowsNewResources: true,
    },
    effectivePlan: starter,
    usage: { venues: 1, pitches: 2, maxStaffInOneVenue: 1 },
    invoices: [
      {
        id: "99999999-9999-4999-8999-999999999999",
        number: "INV-2026-000007",
        ownerId: "44444444-4444-4444-8444-444444444444",
        planCode: "starter",
        planName: "Starter",
        cycle: "Monthly",
        amount: 300,
        currency: "EGP",
        issuedAtUtc: "2026-11-01T10:00:00Z",
        periodStartUtc: "2026-11-01T00:00:00Z",
        periodEndUtc: "2026-12-01T00:00:00Z",
        status: "Paid",
        voidReason: null,
      },
    ],
  }));
});

test("the owner sees plan, usage against limits and invoices", async ({ page }) => {
  await page.goto("/venue/subscription");

  await expect(page.getByRole("heading", { name: "باقة Starter" })).toBeVisible();
  await expect(page.getByText("نشطة")).toBeVisible();
  await expect(page.getByRole("progressbar", { name: "الملاعب الفرعية" })).toHaveAttribute("aria-valuemax", "2");
  await expect(page.getByText("INV-2026-000007")).toBeVisible();
  await expect(page.getByRole("button", { name: "تحميل PDF" })).toBeVisible();
});

test("requesting an upgrade opens a subscription ticket instead of taking a payment", async ({ page, api }) => {
  api.on("POST", /^\/api\/v1\/venue-manager\/support\/tickets$/, (route) =>
    route.fulfill({
      status: 201,
      contentType: "application/json",
      body: JSON.stringify({ ticket: { id: "a", number: "SUP-7KQ2MX" }, messages: [] }),
    }),
  );
  await page.goto("/venue/subscription");

  await page.getByRole("button", { name: "سنوي" }).click();
  await page.getByRole("article").filter({ hasText: "Pro" }).getByRole("button", { name: "اطلب الباقة دي" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/support/tickets"));
  expect(call.body).toMatchObject({ category: "Subscription" });
  expect(String((call.body as { message: string }).message)).toContain("سنوي");
  await expect(page.getByRole("status").filter({ hasText: "SUP-7KQ2MX" })).toBeVisible();
});

test("a plan smaller than current usage cannot be requested", async ({ page }) => {
  await page.goto("/venue/subscription");

  await expect(page.getByRole("article").filter({ hasText: "Tiny" }).getByRole("button", { name: "استخدامك أكبر من حدودها" })).toBeDisabled();
});
