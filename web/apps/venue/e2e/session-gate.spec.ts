import { expect, test } from "./fixtures";

/** A browser cookie is not proof the API still accepts the session: the dashboard must not show up empty first. */

test("a session the API rejects goes to sign-in without ever showing the dashboard", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/me\/permissions$/, (route) =>
    route.fulfill({ status: 401, contentType: "application/problem+json", body: JSON.stringify({ title: "Bff.SessionExpired", status: 401 }) }),
  );
  await page.goto("/venue/overview");

  await expect(page).toHaveURL(/\/venue\/login/);
  await expect(page.getByRole("link", { name: "الموظفين والصلاحيات" })).toHaveCount(0);
});

test("an unreachable API shows a retry screen, not empty data, and recovers on retry", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/me\/permissions$/, (route) =>
    route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "Bff.ApiUnavailable", status: 503 }) }),
  );
  await page.goto("/venue/overview");

  const alert = page.getByRole("alert").filter({ hasText: "مقدرناش نوصل للخدمة" });
  await expect(alert).toBeVisible();
  await expect(page.getByRole("link", { name: "الموظفين والصلاحيات" })).toHaveCount(0);
  await expect(page).not.toHaveURL(/\/venue\/setup/);

  // The API comes back: the same screen recovers without a new sign-in.
  api.on("GET", /^\/api\/v1\/me\/permissions$/, () => ({ roles: ["VenueOwner"], permissions: [], venues: [] }));
  await alert.getByRole("button", { name: "إعادة المحاولة" }).click();
  await expect(alert).toHaveCount(0);
});

test("while the session is being checked only a loading state is shown", async ({ page, api }) => {
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  api.on("GET", /^\/api\/v1\/me\/permissions$/, async (route) => {
    await gate;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ roles: ["VenueOwner"], permissions: [], venues: [] }) });
  });
  await page.goto("/venue/overview");

  await expect(page.getByRole("status").filter({ hasText: "جاري التحقق من الجلسة" })).toBeVisible();
  await expect(page.getByRole("link", { name: "الموظفين والصلاحيات" })).toHaveCount(0);
  release();
  await expect(page.getByRole("status").filter({ hasText: "جاري التحقق من الجلسة" })).toHaveCount(0);
});
