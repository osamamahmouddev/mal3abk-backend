import { expect, test } from "./fixtures";

/** A browser cookie is not proof the API still accepts the session: the console must not show up empty first. */

test("a session the API rejects goes to sign-in without ever showing the console", async ({ page, api }) => {
  await api.signInAs("Admin");
  api.on("GET", /^\/api\/v1\/admin\/navigation$/, (route) =>
    route.fulfill({ status: 401, contentType: "application/problem+json", body: JSON.stringify({ title: "Bff.SessionExpired", status: 401 }) }),
  );
  await page.goto("/admin/overview");

  await expect(page).toHaveURL(/\/admin\/login/);
  await expect(page.getByRole("navigation", { name: "أقسام الإدارة" })).toHaveCount(0);
});

test("an unreachable API shows a retry screen, not an empty console, and recovers on retry", async ({ page, api }) => {
  await api.signInAs("Admin");
  api.on("GET", /^\/api\/v1\/admin\/navigation$/, (route) =>
    route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "Bff.ApiUnavailable", status: 503 }) }),
  );
  await page.goto("/admin/overview");

  const alert = page.getByRole("alert").filter({ hasText: "مقدرناش نوصل للخدمة" });
  await expect(alert).toBeVisible();
  await expect(page.getByRole("navigation", { name: "أقسام الإدارة" })).toHaveCount(0);

  api.on("GET", /^\/api\/v1\/admin\/navigation$/, () => [{ key: "dashboard" }]);
  await alert.getByRole("button", { name: "إعادة المحاولة" }).click();
  await expect(alert).toHaveCount(0);
});
