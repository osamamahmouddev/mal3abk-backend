import AxeBuilder from "@axe-core/playwright";
import { BOOKING_ID, PITCH_ID, expect, test } from "./fixtures";

/** TASK-511: no serious or critical WCAG 2.1 AA issues on the main screens, in Arabic (RTL), on desktop and phone. */

const today = new Date().toISOString().slice(0, 10);
const point = (date: string) => ({ date, openMinutes: 600, bookedMinutes: 240, percent: 40 });
const row = {
  bookingId: BOOKING_ID, reference: "MLB-E2E-0001", pitchId: PITCH_ID, startUtc: `${today}T18:00:00Z`, endUtc: `${today}T19:00:00Z`,
  customerName: "كريم عادل", isManual: false, status: 4, totalPrice: 400, depositAmount: 120, currency: "EGP", depositState: "UnderReview",
  deadlineUtc: "2099-01-01T00:00:00Z",
};

test.beforeEach(async ({ api }) => {
  api.on("GET", /\/dashboard$/, () => ({
    businessDate: today,
    today: { todayBookings: 3, confirmed: 2, pendingDeposit: 0, pendingProofReview: 1, cancelled: 0, noShows: 0 },
    todayRevenue: { currency: "EGP", confirmedValue: 800, depositsCollected: 240, dueAtVenue: 560, depositsPendingReview: 120, expectedValue: 1200 },
    monthRevenue: { currency: "EGP", confirmedValue: 9000, depositsCollected: 2700, dueAtVenue: 6300, depositsPendingReview: 120, expectedValue: 9400 },
    todayOccupancy: point(today),
    occupancyThisWeek: Array.from({ length: 7 }, () => point(today)),
    occupancyLastWeek: Array.from({ length: 7 }, () => point(today)),
    revenueTrend: Array.from({ length: 30 }, () => ({ date: today, confirmedValue: 300, depositsCollected: 90 })),
    timeline: [row],
    upcoming: [row],
    proofsAwaitingReview: [row],
    recentActivity: [{ bookingId: BOOKING_ID, reference: "MLB-E2E-0001", fromStatus: 2, toStatus: 3, changedAtUtc: `${today}T10:00:00Z`, reason: null }],
    generatedAtUtc: `${today}T10:00:00Z`,
  }));
  api.on("GET", /\/bookings$/, () => ({
    items: [{ id: BOOKING_ID, reference: "MLB-E2E-0001", pitchId: PITCH_ID, startUtc: row.startUtc, endUtc: row.endUtc, totalPrice: 400, depositAmount: 120, currency: "EGP", status: 3, customerName: "كريم عادل", customerPhone: null, createdAtUtc: row.startUtc, isManual: false }],
    totalCount: 1, page: 1, pageSize: 20,
  }));
  api.on("GET", /\/calendar$/, () => [
    { id: BOOKING_ID, pitchId: PITCH_ID, startUtc: row.startUtc, endUtc: row.endUtc, status: "Confirmed", customerName: "كريم عادل", bookingReference: "MLB-E2E-0001", isManual: false },
  ]);
});

for (const path of ["/venue/overview", "/venue/reservations", "/venue/calendar?view=day", "/venue/login"]) {
  test(`no serious accessibility issues on ${path}`, async ({ page }) => {
    await page.goto(path);
    await page.waitForLoadState("networkidle");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");

    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
    const blocking = results.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
    expect(blocking.map((v) => `${v.id}: ${v.help} (${v.nodes.length})`)).toEqual([]);
  });
}
