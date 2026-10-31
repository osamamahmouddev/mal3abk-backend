import { BOOKING_ID, PITCH_ID, VENUE_ID, expect, test, waitForCall } from "./fixtures";

/** Flow B: a player uploaded a deposit receipt; venue staff review it and approve or reject it. */

const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", "base64");

const booking = {
  id: BOOKING_ID,
  reference: "MLB-E2E-0001",
  pitchId: PITCH_ID,
  startUtc: "2030-01-10T18:00:00Z",
  endUtc: "2030-01-10T19:00:00Z",
  totalPrice: 400,
  depositAmount: 120,
  currency: "EGP",
  status: 3,
  customerName: "كريم عادل",
  customerPhone: null,
  createdAtUtc: "2030-01-09T10:00:00Z",
  isManual: false,
};

const detail = {
  ...booking,
  venueId: VENUE_ID,
  playerId: "55555555-5555-4555-8555-555555555555",
  holdExpiresAtUtc: "2099-01-01T00:00:00Z",
  customerPhone: "+201012345678",
  notes: null,
  statusHistory: [],
  proofs: [
    {
      id: "66666666-6666-4666-8666-666666666666",
      mediaId: "77777777-7777-4777-8777-777777777777",
      originalFileName: "receipt.png",
      contentType: "image/png",
      fileSizeBytes: 68,
      sha256Hash: "abc",
      isApproved: null,
      rejectionReason: null,
      isScanClean: true,
      uploadedAtUtc: "2030-01-09T10:05:00Z",
      downloadUrl: "/api/v1/media/77777777-7777-4777-8777-777777777777/large?exp=1&sig=e2e",
    },
  ],
};

test.beforeEach(async ({ api }) => {
  let reviewed = false;
  api.on("GET", /\/bookings$/, () => ({ items: reviewed ? [] : [booking], totalCount: reviewed ? 0 : 1, page: 1, pageSize: 50 }));
  api.on("GET", new RegExp(`/bookings/${BOOKING_ID}$`), () => detail);
  api.on("GET", /^\/api\/v1\/media\//, (route) => route.fulfill({ status: 200, contentType: "image/png", body: PNG }));
  api.on("POST", /\/proof\/review$/, () => {
    reviewed = true;
    return { message: "ok", bookingId: BOOKING_ID };
  });
});

test("staff approve a receipt only after confirming the amount arrived", async ({ page, api }) => {
  await page.goto("/venue/reservations/proof-review");

  await expect(page.getByRole("heading", { name: "كريم عادل" })).toBeVisible();
  await expect(page.getByRole("img", { name: /إيصال التحويل المرفوع/ })).toBeVisible();

  await page.getByRole("button", { name: "اعتماد العربون" }).click();
  const dialog = page.getByRole("dialog");
  const confirm = dialog.getByRole("button", { name: "تأكيد الاعتماد" });
  await expect(confirm).toBeDisabled();
  await dialog.getByRole("checkbox").check();
  await confirm.click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/proof/review"));
  expect(call.path).toBe(`/api/v1/venue-manager/venues/${VENUE_ID}/bookings/${BOOKING_ID}/proof/review`);
  expect(call.body).toEqual({ approved: true, rejectionReason: null });
  await expect(page.getByRole("status").filter({ hasText: "اتعمد العربون" })).toBeVisible();
  await expect(page.getByText("مفيش إيصالات محتاجة مراجعة")).toBeVisible();
});

test("rejecting a receipt requires a reason that is sent to the player", async ({ page, api }) => {
  await page.goto("/venue/reservations/proof-review");

  await page.getByRole("button", { name: "رفض", exact: true }).click();
  const dialog = page.getByRole("dialog");
  const confirm = dialog.getByRole("button", { name: "تأكيد الرفض" });
  await expect(confirm).toBeDisabled();
  await dialog.getByRole("button", { name: /صورة الإيصال مش واضحة/ }).click();
  await confirm.click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/proof/review"));
  expect(call.body).toMatchObject({ approved: false, rejectionReason: expect.stringContaining("صورة الإيصال مش واضحة") });
});
