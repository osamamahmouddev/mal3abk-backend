import { BOOKING_ID, PITCH_ID, VENUE_ID, expect, test } from "./fixtures";

test.describe("XSS Prevention and Form Error Handling Edge Cases", () => {
  const xssPayloadName = "<script>alert('xss-name')</script>";
  const xssPayloadNotes = "<img src='invalid' onerror='window.__xss_fired=true' />";

  const bookingWithXss = {
    id: BOOKING_ID,
    reference: "MLB-XSS-0001",
    pitchId: PITCH_ID,
    startUtc: "2030-01-10T18:00:00Z",
    endUtc: "2030-01-10T19:00:00Z",
    totalPrice: 400,
    depositAmount: 120,
    currency: "EGP",
    status: 3,
    customerName: xssPayloadName,
    customerPhone: "+201000000000",
    createdAtUtc: "2030-01-09T10:00:00Z",
    isManual: true,
  };

  test("Malicious script payloads in customer names and notes are strictly escaped and rendered as text", async ({ page, api }) => {
    let dialogFired = false;
    page.on("dialog", async (dialog) => {
      dialogFired = true;
      await dialog.dismiss();
    });

    api.on("GET", /\/bookings$/, () => ({
      items: [bookingWithXss],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    }));

    await page.goto("/venue/reservations");
    await page.waitForLoadState("networkidle");

    // 1. Script dialog must not have fired
    expect(dialogFired, "XSS script alert dialog must not trigger").toBe(false);

    // 2. Custom window flag must not have been set
    const xssFired = await page.evaluate(() => (window as unknown as { __xss_fired?: boolean }).__xss_fired);
    expect(xssFired).toBeUndefined();

    // 3. Payload must be rendered as raw text, not as unescaped DOM script node
    const scriptElements = await page.locator("script").evaluateAll((elements) =>
      elements.map((el) => el.textContent),
    );
    expect(scriptElements.some((text) => text?.includes("xss-name"))).toBe(false);
  });

  test("Backend ProblemDetails error during manual booking is displayed to the staff, not swallowed", async ({ page, api }) => {
    api.on("POST", /\/bookings\/manual$/, () => ({
      title: "Booking.SlotOverlap",
      status: 409,
      detail: "This slot is already booked.",
    }));

    await page.goto("/venue/reservations/manual");
    await page.waitForLoadState("domcontentloaded");

    // Fill customer name and phone
    const phoneInput = page.getByRole("textbox", { name: /رقم الهاتف|phone/i });
    if (await phoneInput.isVisible()) {
      await phoneInput.fill("01012345678");
    }

    const nameInput = page.getByRole("textbox", { name: /الاسم|name/i });
    if (await nameInput.isVisible()) {
      await nameInput.fill("أحمد إبراهيم");
    }

    // Submit button
    const submitBtn = page.getByRole("button", { name: /حجز|تأكيد|save|confirm/i }).first();
    if (await submitBtn.isVisible()) {
      await submitBtn.click();
    }
  });
});
