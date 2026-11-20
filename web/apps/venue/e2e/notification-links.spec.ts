import { expect, test } from "./fixtures";

/** A notification opens the dashboard page that handles what it is about (the API's links are for the phone app). */

const BOOKING_ID = "99999999-9999-4999-8999-999999999999";
const VENUE = "11111111-1111-4111-8111-111111111111";

const notification = (type: string, category: string, title: string, deepLink: string) => ({
  id: crypto.randomUUID(), type, category, title, body: "نص الإشعار", deepLink, isRead: true, createdAtUtc: "2030-01-10T10:00:00Z",
});

test.beforeEach(async ({ api }) => {
  api.on("GET", /^\/api\/v1\/me\/notifications$/, () => ({
    items: [
      notification("payment.proof_submitted", "Payments", "إيصال دفع جديد", `malabak://booking/${BOOKING_ID}`),
      notification("venue.info_requested", "Venues", "مطلوب معلومات إضافية", `malabak://venue-manager/venue/${VENUE}`),
      notification("subscription.expiring", "Venues", "اشتراكك قرب يخلص", "malabak://venue-manager/billing"),
    ],
    page: 1, pageSize: 50, totalCount: 3, unreadCount: 0,
  }));
  api.on("GET", /\/proofs$/, () => []);
});

test("a payment proof notification opens that booking's proof review", async ({ page }) => {
  await page.goto("/venue/notifications");
  await page.getByRole("button", { name: /إيصال دفع جديد/ }).click();

  await expect(page).toHaveURL(new RegExp(`/venue/reservations/proof-review\\?booking=${BOOKING_ID}`), { timeout: 15_000 });
});

test("a request for more venue info opens the setup page and a billing notice opens the subscription page", async ({ page }) => {
  await page.goto("/venue/notifications");
  await page.getByRole("button", { name: /مطلوب معلومات إضافية/ }).click();
  // The dev server compiles a page on its first visit, which can take a while on a busy CI runner.
  await expect(page).toHaveURL(/\/venue\/setup/, { timeout: 15_000 });

  await page.goto("/venue/notifications");
  await page.getByRole("button", { name: /اشتراكك قرب يخلص/ }).click();
  await expect(page).toHaveURL(/\/venue\/subscription/, { timeout: 15_000 });
});
