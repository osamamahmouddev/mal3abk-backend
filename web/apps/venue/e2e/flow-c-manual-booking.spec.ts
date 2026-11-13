import { PITCH_ID, VENUE_ID, expect, test, waitForCall } from "./fixtures";

/** Flow C: a customer calls; staff pick a free slot on the calendar and record a confirmed manual booking. */

test.beforeEach(async ({ api }) => {
  api.on("GET", /\/calendar$/, () => []);
  api.on("POST", /\/price-rules\/preview$/, () => ({
    current: {
      venueId: VENUE_ID, pitchId: PITCH_ID, startUtc: "", endUtc: "", durationMinutes: 60, currency: "EGP",
      subtotal: 400, discount: 0, total: 400, deposit: 0, depositType: "None", lines: [], quotedAtUtc: "",
    },
  }));
  api.on("POST", /\/bookings\/manual$/, (route) =>
    route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify({ bookingId: "88888888-8888-4888-8888-888888888888" }) }),
  );
});

test("staff book a free slot from the day view with the suggested price", async ({ page, api }) => {
  // 10 Jan 2030, winter: Cairo is UTC+2, so 8 PM local is 18:00 UTC.
  await page.goto("/venue/calendar?view=day&date=2030-01-10");

  await page.getByRole("button", { name: "ملعب ١ ٨ م: ميعاد فاضي" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByLabel("السعر (ج.م)")).toHaveValue("400");

  await dialog.getByLabel("اسم العميل").fill("م. فتحي");
  await dialog.getByLabel("موبايل العميل").fill("01012345678");
  await dialog.getByRole("button", { name: "تأكيد الحجز" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/bookings/manual"));
  expect(call.body).toMatchObject({
    pitchId: PITCH_ID,
    startUtc: "2030-01-10T18:00:00.000Z",
    endUtc: "2030-01-10T19:00:00.000Z",
    price: 400,
    currency: "EGP",
    customerName: "م. فتحي",
    customerPhone: "01012345678",
  });
  await expect(page.getByRole("status").filter({ hasText: "اتسجل حجز م. فتحي" })).toBeVisible();
});

test("a customer who paid a deposit is booked and confirmed with the amount received", async ({ page, api }) => {
  await page.goto("/venue/calendar?view=day&date=2030-01-10");
  await page.getByRole("button", { name: "ملعب ١ ٨ م: ميعاد فاضي" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("اسم العميل").fill("م. فتحي");
  await dialog.getByLabel("موبايل العميل").fill("01012345678");
  await dialog.getByRole("button", { name: "دفع عربون" }).click();
  await dialog.getByLabel("العربون المدفوع (ج.م)").fill("100");
  await expect(dialog.getByText("الباقي في الملعب")).toBeVisible();
  await dialog.getByRole("button", { name: "تأكيد الحجز" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/bookings/manual"));
  expect(call.body).toMatchObject({ price: 400, paymentStatus: 1, depositAmount: 100 });
  await expect(page.getByRole("status").filter({ hasText: "ومؤكد بعربون" })).toBeVisible();
});

test("a customer who has not paid yet gets the slot held while the deposit is awaited", async ({ page, api }) => {
  await page.goto("/venue/calendar?view=day&date=2030-01-10");
  await page.getByRole("button", { name: "ملعب ١ ٨ م: ميعاد فاضي" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("اسم العميل").fill("م. فتحي");
  await dialog.getByLabel("موبايل العميل").fill("01012345678");
  await dialog.getByRole("button", { name: "في انتظار العربون" }).click();
  await dialog.getByLabel("العربون المطلوب (ج.م)").fill("150");
  await dialog.getByRole("button", { name: "احجز الميعاد" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/bookings/manual"));
  expect(call.body).toMatchObject({ paymentStatus: 2, depositAmount: 150 });
  await expect(page.getByRole("status").filter({ hasText: "بيستنى العربون" })).toBeVisible();
});

test("a partial payment needs an amount below the price, and fully paid needs none", async ({ page, api }) => {
  await page.goto("/venue/calendar?view=day&date=2030-01-10");
  await page.getByRole("button", { name: "ملعب ١ ٨ م: ميعاد فاضي" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("اسم العميل").fill("م. فتحي");
  await dialog.getByLabel("موبايل العميل").fill("01012345678");
  await expect(dialog.getByLabel("العربون المدفوع (ج.م)")).toHaveCount(0);

  await dialog.getByRole("button", { name: "دفع عربون" }).click();
  await dialog.getByRole("button", { name: "تأكيد الحجز" }).click();
  await expect(dialog.getByRole("alert").filter({ hasText: "اكتب العربون اللي اتدفع" })).toBeVisible();

  await dialog.getByLabel("العربون المدفوع (ج.م)").fill("400");
  await dialog.getByRole("button", { name: "تأكيد الحجز" }).click();
  await expect(dialog.getByRole("alert").filter({ hasText: "أقل من السعر" })).toBeVisible();
  expect(api.calls.some((c) => c.path.endsWith("/bookings/manual"))).toBe(false);
});

test("the form refuses an invalid phone number before calling the API", async ({ page, api }) => {
  await page.goto("/venue/calendar?view=day&date=2030-01-10");

  await page.getByRole("button", { name: "ملعب ١ ٨ م: ميعاد فاضي" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("اسم العميل").fill("م. فتحي");
  await dialog.getByLabel("موبايل العميل").fill("123");
  await dialog.getByRole("button", { name: "تأكيد الحجز" }).click();

  await expect(dialog.getByRole("alert").filter({ hasText: "رقم موبايل مصري" })).toBeVisible();
  expect(api.calls.some((c) => c.path.endsWith("/bookings/manual"))).toBe(false);
});

test.describe("browser outside Egypt", () => {
  // Regression guard: slot times once depended on the browser's own time zone (off by two hours for users in Cairo).
  test.use({ timezoneId: "UTC" });

  test("the slot is still 8 PM Cairo time", async ({ page, api }) => {
    await page.goto("/venue/calendar?view=day&date=2030-01-10");
    await page.getByRole("button", { name: "ملعب ١ ٨ م: ميعاد فاضي" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("اسم العميل").fill("م. فتحي");
    await dialog.getByLabel("موبايل العميل").fill("01012345678");
    await dialog.getByRole("button", { name: "تأكيد الحجز" }).click();

    const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/bookings/manual"));
    expect(call.body).toMatchObject({ startUtc: "2030-01-10T18:00:00.000Z" });
  });
});
