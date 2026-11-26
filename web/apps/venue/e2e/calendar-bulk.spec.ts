import { PITCH_ID, expect, test, waitForCall } from "./fixtures";

/** Many hours at once from the calendar: book them (confirmed or tentative, customer optional) or free the venue's own again. */

const MANUAL_ID = "77777777-7777-4777-8777-777777777777";
// 10 Jan 2030, winter: Cairo is UTC+2, so 10 PM local is 20:00 UTC.
const manual = {
  id: MANUAL_ID, pitchId: PITCH_ID, startUtc: "2030-01-10T20:00:00Z", endUtc: "2030-01-10T21:00:00Z", status: "PendingDeposit",
  customerName: null, bookingReference: "MLB-TEST01", isManual: true, reason: null, customerPhone: null, depositAmount: 0, isTentative: true,
};

test.beforeEach(async ({ api }) => {
  api.on("GET", /\/calendar$/, () => [manual]);
});

test("picked hours are booked together as tentative, with no customer needed", async ({ page, api }) => {
  api.on("POST", /\/bookings\/manual\/bulk$/, () => ({
    succeeded: 1, failed: 0,
    slots: [{ pitchId: PITCH_ID, startUtc: "2030-01-10T16:00:00Z", endUtc: "2030-01-10T18:00:00Z", succeeded: true, bookingId: "b1", errorCode: null, error: null }],
  }));

  await page.goto("/venue/calendar?view=day&date=2030-01-10");
  await page.getByRole("button", { name: "تحديد كذا ساعة" }).click();
  const grid = page.getByRole("region", { name: "جدول المواعيد" });
  const free = grid.getByRole("button", { name: /ميعاد فاضي/ });
  await free.nth(0).click();
  await free.nth(1).click();
  await expect(page.getByRole("region", { name: "الساعات المحددة" })).toContainText("٢ ساعة فاضية");
  await page.screenshot({ path: "test-results/calendar-bulk.png", fullPage: true });

  await page.getByRole("button", { name: "احجزهم" }).click();
  const dialog = page.getByRole("dialog", { name: /حجز ٢ ساعة/ });
  await dialog.getByRole("button", { name: "مبدئي (لسه مش متثبت)" }).click();
  await dialog.getByRole("button", { name: "احجزهم" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/bookings/manual/bulk"));
  expect(call.body).toMatchObject({ paymentStatus: 3, customerName: null, customerPhone: null, pricePerHour: null });
  expect((call.body as { slots: unknown[] }).slots).toHaveLength(2);
  await expect(page.getByRole("status").filter({ hasText: "اتحجز مبدئياً ١ ميعاد" })).toBeVisible();
});

test("a tentative manual booking is picked and freed so players can book it", async ({ page, api }) => {
  api.on("POST", /\/bookings\/manual\/bulk-release$/, () => ({ succeeded: 1, failed: 0, items: [{ bookingId: MANUAL_ID, succeeded: true, errorCode: null, error: null }] }));

  await page.goto("/venue/calendar?view=day&date=2030-01-10");
  await page.getByRole("button", { name: "تحديد كذا ساعة" }).click();
  await page.getByRole("button", { name: /محجوز مبدئي/ }).click();
  await page.getByRole("button", { name: "خليهم فاضيين" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/bulk-release"));
  expect(call.body).toEqual({ bookingIds: [MANUAL_ID] });
  await expect(page.getByRole("status").filter({ hasText: "اتفتح ١ ميعاد" })).toBeVisible();
});

test("a whole day is picked from its header, even before turning picking on, and a second click lets it go", async ({ page }) => {
  await page.goto("/venue/calendar?view=week&date=2030-01-10");
  const grid = page.getByRole("region", { name: "جدول المواعيد" });
  const bar = page.getByRole("region", { name: "الساعات المحددة" });

  // Thursday 10 Jan 2030: every hour is free except the manual booking at 10 PM.
  const day = grid.getByRole("button", { name: /^تحديد خميس ١٠ كله$/ });
  await day.click();
  await expect(bar).toBeVisible();
  await expect(page.getByRole("button", { name: "خلصت التحديد" })).toBeVisible();
  const picked = await grid.getByRole("button", { name: /^خميس ١٠ .*ميعاد فاضي$/, pressed: true }).count();
  expect(picked).toBeGreaterThan(1);
  await expect(bar).toContainText(`${picked.toLocaleString("ar-EG")} ساعة فاضية`);

  await grid.getByRole("button", { name: /^إلغاء تحديد خميس ١٠ كله$/ }).click();
  await expect(grid.getByRole("button", { name: /ميعاد فاضي$/, pressed: true })).toHaveCount(0);
});

test("today's column is marked in the week", async ({ page }) => {
  await page.goto("/venue/calendar?view=week");
  const today = page.getByRole("region", { name: "جدول المواعيد" }).locator('[aria-current="date"]');
  await expect(today).toHaveCount(1);
  await expect(today).toContainText("النهارده");
  await page.screenshot({ path: `test-results/calendar-today-${test.info().project.name}.png`, fullPage: true });
});
