import { expect, test, waitForCall } from "./fixtures";

/** The reservations list opens on every booking and can be narrowed by day, range, source, pitch and customer. */

test.beforeEach(async ({ api }) => {
  api.on("GET", /\/bookings$/, () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }));
});

test("opens on all bookings, with the status tabs after it", async ({ page, api }) => {
  await page.goto("/venue/reservations");

  const tabs = page.getByRole("tab");
  await expect(tabs.first()).toContainText("كل الحجوزات");
  await expect(tabs.first()).toHaveAttribute("aria-selected", "true");
  const first = await waitForCall(page, api.calls, (c) => c.method === "GET" && c.path.includes("/bookings?"));
  expect(first.path).not.toContain("statuses=");
});

test("a quick day filter sends that Cairo day as the range", async ({ page, api }) => {
  await page.goto("/venue/reservations");

  await page.getByRole("button", { name: "النهارده" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.path.includes("startDateUtc=") && c.path.includes("endDateUtc="));
  const query = new URLSearchParams(call.path.split("?")[1]);
  const start = new Date(query.get("startDateUtc")!).getTime();
  const end = new Date(query.get("endDateUtc")!).getTime();
  expect(end - start).toBeGreaterThan(22 * 3_600_000);
  expect(end - start).toBeLessThan(25 * 3_600_000);
});

test("a custom range sends both dates and the source filter sends manual", async ({ page, api }) => {
  await page.goto("/venue/reservations");

  await page.getByRole("button", { name: "من تاريخ لتاريخ" }).click();
  await page.getByLabel("من تاريخ").fill("2030-01-10");
  await page.getByLabel("إلى تاريخ").fill("2030-01-12");
  await page.getByLabel("المصدر").selectOption("manual");

  const call = await waitForCall(page, api.calls, (c) => c.path.includes("manual=true") && c.path.includes("2030-01-1"));
  const query = new URLSearchParams(call.path.split("?")[1]);
  expect(query.get("startDateUtc")).toBe("2030-01-09T22:00:00.000Z");
  expect(query.get("endDateUtc")).toBe("2030-01-12T21:59:59.000Z");
});
