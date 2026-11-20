import { expect, test } from "./fixtures";

/** Flow G (TASK-507): the billing page explains every subscription state honestly. */

const plan = {
  id: "55555555-5555-4555-8555-555555555555", code: "starter", name: "Starter", description: null, monthlyPrice: 300, annualPrice: 3000, currency: "EGP",
  maxVenues: null, maxPitches: null, maxStaffPerVenue: null, features: [], trialDays: 0, graceDays: 7, isPublic: true, isDefault: true, sortOrder: 0,
  status: "Active", subscribers: 1, updatedAtUtc: "2026-11-01T10:00:00Z",
};
const subscription = (status: string, extra: Record<string, unknown> = {}) => ({
  id: "88888888-8888-4888-8888-888888888888", ownerId: "44444444-4444-4444-8444-444444444444", plan, cycle: "Monthly", status,
  startedAtUtc: "2026-10-01T00:00:00Z", currentPeriodEndUtc: "2026-11-01T00:00:00Z", graceEndsAtUtc: null, statusReason: null, allowsNewResources: true, ...extra,
});

test.beforeEach(async ({ api }) => {
  api.on("GET", /^\/api\/v1\/plans$/, () => [plan]);
});

test("grace period: a clear deadline, adding still allowed", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/venue-manager\/billing$/, () => ({
    subscription: subscription("Grace", { graceEndsAtUtc: "2026-11-08T00:00:00Z" }), effectivePlan: plan, usage: { venues: 1, pitches: 1, maxStaffInOneVenue: 0 }, invoices: [],
  }));
  await page.goto("/venue/subscription");

  await expect(page.getByText("فترة سماح").first()).toBeVisible();
  await expect(page.getByText(/فترة السماح بتنتهي/)).toBeVisible();
  await expect(page.getByText("مش هتقدر تضيف ملاعب")).toHaveCount(0);
});

test("suspended: adding is blocked and existing bookings are reassured", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/venue-manager\/billing$/, () => ({
    subscription: subscription("Suspended", { allowsNewResources: false, statusReason: "Chargeback" }), effectivePlan: plan, usage: { venues: 1, pitches: 1, maxStaffInOneVenue: 0 }, invoices: [],
  }));
  await page.goto("/venue/subscription");

  await expect(page.getByText("موقوفة")).toBeVisible();
  await expect(page.getByText(/مش هتقدر تضيف ملاعب أو موظفين جداد/)).toBeVisible();
  await expect(page.getByText(/الحجوزات الحالية مش بتتلغي/).first()).toBeVisible();
});

test("no subscription yet: the default plan is explained and unlimited usage has no bar", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/venue-manager\/billing$/, () => ({ effectivePlan: plan, usage: { venues: 2, pitches: 6, maxStaffInOneVenue: 3 }, invoices: [] }));
  await page.goto("/venue/subscription");

  await expect(page.getByText("دي الباقة الافتراضية لحد ما تختار باقة.")).toBeVisible();
  await expect(page.getByText("٢ (بلا حدود)")).toBeVisible();
  await expect(page.getByRole("progressbar")).toHaveCount(0);
  await expect(page.getByText(/لا توجد فواتير اشتراكات بعد/)).toBeVisible();
});

test("a voided invoice cannot be downloaded", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/venue-manager\/billing$/, () => ({
    subscription: subscription("Active"), effectivePlan: plan, usage: { venues: 1, pitches: 1, maxStaffInOneVenue: 0 },
    invoices: [{ id: "99999999-9999-4999-8999-999999999999", number: "INV-2026-000003", ownerId: "x", planCode: "starter", planName: "Starter", cycle: "Monthly", amount: 300, currency: "EGP", issuedAtUtc: "2026-10-01T00:00:00Z", periodStartUtc: "2026-10-01T00:00:00Z", periodEndUtc: "2026-11-01T00:00:00Z", status: "Void", voidReason: "duplicate" }],
  }));
  await page.goto("/venue/subscription");

  await expect(page.getByText("INV-2026-000003")).toBeVisible();
  await expect(page.getByText("ملغاة")).toBeVisible();
  await expect(page.getByRole("button", { name: "تحميل PDF" })).toHaveCount(0);
});

test("a refused support code shows an error, re-enables the button and stays on the entry page", async ({ page }) => {
  // The exchange runs server side; in E2E the API is unreachable, which must still end in a clear message.
  await page.goto("/venue/impersonate");
  await page.getByLabel("الكود").fill("this-code-was-already-used-0123456789abcdef");
  await page.getByRole("button", { name: "دخول" }).click();

  await expect(page.getByRole("alert").filter({ hasText: /الكود غلط|تعذّر الاتصال/ })).toBeVisible();
  await expect(page.getByRole("button", { name: "دخول" })).toBeEnabled();
  await expect(page).toHaveURL(/\/venue\/impersonate$/);
});
