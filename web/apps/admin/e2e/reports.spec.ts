import { expect, test } from "./fixtures";

/** The revenue report: headline totals, where the money sits, and each venue's settlement with the platform. */

const SUMMARY = [
  {
    venueId: "e0000000-0000-4000-8000-000000000011", venueName: "مجمع ملاعب الأبطال", totalBookings: 4, confirmedBookings: 3, cancelledBookings: 0,
    pendingConfirmationBookings: 1, totalGrossAmount: 1050, totalDepositAmount: 200, totalPlatformCommission: 80, totalVenueNetAmount: 970,
    totalDueAtVenueCash: 850, pendingRefundsCount: 0, pendingRefundsAmount: 0, venueDepositShare: 60, currency: "EGP", manualBookings: 1,
    platformCollectedOnline: 100, commissionCollected: 40, commissionOutstanding: 40, platformOwesVenue: 60,
  },
  {
    venueId: "e0000000-0000-4000-8000-000000000012", venueName: "ملاعب النجوم", totalBookings: 2, confirmedBookings: 2, cancelledBookings: 1,
    pendingConfirmationBookings: 0, totalGrossAmount: 600, totalDepositAmount: 200, totalPlatformCommission: 60, totalVenueNetAmount: 540,
    totalDueAtVenueCash: 400, pendingRefundsCount: 1, pendingRefundsAmount: 100, venueDepositShare: 0, currency: "EGP", manualBookings: 0,
    platformCollectedOnline: 0, commissionCollected: 0, commissionOutstanding: 60, platformOwesVenue: 0,
  },
];

test("the report adds the venues up and says who owes whom", async ({ page, api }, info) => {
  await api.signInAs("Admin");
  api.on("GET", /^\/api\/v1\/admin\/bookings\/financial-summary$/, () => SUMMARY);
  api.on("GET", /^\/api\/v1\/admin\/dashboard$/, () => ({ from: "2026-10-21", to: "2026-11-19", timeZone: "Africa/Cairo", figures: { mrr: 0 }, series: {}, distributions: {} }));
  api.on("GET", /^\/api\/v1\/admin\/invoices\/reconciliation$/, () => ({ invoices: 0, collected: 0, voided: 0, byMethod: [] }));

  await page.goto("/admin/reports");

  await expect(page.getByText("قيمة الحجوزات").first()).toBeVisible();
  await expect(page.getByText("١٬٦٥٠ ج.م").first()).toBeVisible(); // 1050 + 600
  await expect(page.getByText("١٤٠ ج.م").first()).toBeVisible(); // commission 80 + 60
  await expect(page.getByText("له ٢٠ ج.م")).toBeVisible(); // venue 1: platform owes 60, venue owes 40
  await expect(page.getByText("عليه ٦٠ ج.م")).toBeVisible(); // venue 2 owes its whole commission
  await expect(page.getByText("الملاعب عليها ٤٠ ج.م للمنصة (عمولة لسه ما اتحصلتش).")).toBeVisible(); // +20 and -60

  await page.screenshot({ path: "/tmp/claude-1000/-home-osama-Desktop-Mal3abk-bs-mal3abk-backend/e4b92b5e-1c75-4dc5-8a10-f7e30612cfca/scratchpad/reports-" + info.project.name + ".png", fullPage: true });
});
