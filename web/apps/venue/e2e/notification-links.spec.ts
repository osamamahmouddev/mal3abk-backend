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
      notification("payout.recorded", "Payments", "حوّلنالك فلوسك", `malabak://venue-manager/venue/${VENUE}`),
    ],
    page: 1, pageSize: 50, totalCount: 3, unreadCount: 0,
  }));
  api.on("GET", /\/proofs$/, () => []);
});

test("a payment proof notification opens the reservations: the platform team reviews the deposit", async ({ page }) => {
  await page.goto("/venue/notifications");
  await page.getByRole("button", { name: /إيصال دفع جديد/ }).click();

  await expect(page).toHaveURL(/\/venue\/reservations/);
});

test("an info request opens the setup and a payout notice opens the payouts", async ({ page }) => {
  await page.goto("/venue/notifications");
  await page.getByRole("button", { name: /مطلوب معلومات إضافية/ }).click();
  await expect(page).toHaveURL(/\/venue\/setup/);

  await page.goto("/venue/notifications");
  await page.getByRole("button", { name: /حوّلنالك فلوسك/ }).click();
  await expect(page).toHaveURL(/\/venue\/payouts/);
});
