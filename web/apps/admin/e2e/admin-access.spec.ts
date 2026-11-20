import { expect, problem, test, waitForCall, openItem } from "./fixtures";

/** TASK-719: role-based navigation, permission-denied states and step-up confirmation in the admin console. */

const USER_ID = "b0000000-0000-4000-8000-000000000002";

test("each staff role sees only the sections it may open", async ({ page, api }) => {
  await api.signInAs("Finance");
  api.on("GET", /^\/api\/v1\/admin\/payouts$/, () => []);
  await page.goto("/admin/payouts");

  // Phones keep the sections in a drawer behind the menu button.
  const menu = page.getByRole("button", { name: "فتح القائمة" });
  if (await menu.isVisible()) await menu.click();
  const nav = page.getByRole("navigation", { name: "أقسام الإدارة" }).last();
  await expect(nav.getByRole("link", { name: "مستحقات الملاعب" })).toBeVisible();
  await expect(nav.getByRole("link", { name: "مراجعة العرابين" })).toBeVisible();
  await expect(nav.getByRole("link", { name: "الإيرادات والتقارير" })).toBeVisible();
  await expect(nav.getByRole("link", { name: "المستخدمين" })).toHaveCount(0);
  await expect(nav.getByRole("link", { name: "حسابات الفريق" })).toHaveCount(0);
});

test("a screen the role cannot use shows a permission-denied state, not a crash", async ({ page, api }) => {
  await api.signInAs("SupportAgent");
  api.on("GET", /^\/api\/v1\/admin\/accounts$/, (route) => problem(route, 403, "Forbidden"));
  api.on("GET", /^\/api\/v1\/admin\/roles$/, (route) => problem(route, 403, "Forbidden"));
  await page.goto("/admin/accounts");

  await expect(page.getByRole("alert").filter({ hasText: "مش عندك صلاحية" }).first()).toBeVisible();
});

test("suspending a user needs a reason and a fresh authenticator code, sent as a step-up token", async ({ page, api }) => {
  await api.signInAs("Admin");
  api.on("GET", /^\/api\/v1\/admin\/users$/, () => ({
    items: [{ id: USER_ID, fullName: "أحمد علي", maskedPhone: "+2010•••••678", email: null, roles: ["Player"], status: "Active", createdAtUtc: "2026-10-01T10:00:00Z" }],
    page: 1,
    pageSize: 25,
    total: 1,
  }));
  const detail = {
    id: USER_ID, fullName: "أحمد علي", phoneNumber: "+201012345678", email: null, roles: ["Player"], status: "Active", suspensionReason: null,
    suspendedAtUtc: null, deletionRequestedAtUtc: null, city: "القاهرة", area: "المعادي", language: "ar", twoFactorEnabled: false,
    isPhoneNumberConfirmed: true, isEmailConfirmed: false, activeSessions: 2, lastActivityAtUtc: "2026-11-06T10:00:00Z", createdAtUtc: "2026-10-01T10:00:00Z",
  };
  api.on("GET", new RegExp(`^/api/v1/admin/users/${USER_ID}$`), () => detail);
  api.on("GET", new RegExp(`^/api/v1/admin/users/${USER_ID}/history$`), () => []);
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, () => ({ stepUpToken: "step-up-token-e2e", scope: "admin_destructive", expiresInSeconds: 300 }));
  api.on("POST", new RegExp(`^/api/v1/admin/users/${USER_ID}/suspend$`), () => ({ ...detail, status: "Suspended", activeSessions: 0 }));

  await page.goto("/admin/users");
  await openItem(page, "أحمد علي");
  await page.getByRole("button", { name: "إيقاف الحساب" }).click();

  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("تكرار عدم الحضور");
  await page.getByRole("button", { name: "إيقاف", exact: true }).click();

  const stepUpDialog = page.getByRole("dialog", { name: "تأكيد الهوية" });
  await expect(stepUpDialog).toBeVisible();
  await stepUpDialog.getByLabel("كود التحقق").fill("123456");
  await stepUpDialog.getByRole("button", { name: "تأكيد" }).click();

  const stepUp = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path === "/api/v1/auth/step-up");
  expect(stepUp.body).toMatchObject({ scope: "admin_destructive", method: 1, secret: "123456" });
  const suspend = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/suspend"));
  expect(suspend.headers["x-step-up-token"]).toBe("step-up-token-e2e");
  expect(suspend.body).toEqual({ reason: "تكرار عدم الحضور" });
  await expect(page.getByRole("status").filter({ hasText: "اتوقف الحساب" })).toBeVisible();
});

test("a dispute decision needs an outcome and a reason before it can be sent", async ({ page, api }) => {
  await api.signInAs("Admin");
  const dispute = {
    id: "d0000000-0000-4000-8000-000000000003", bookingId: "c0000000-0000-4000-8000-000000000004", bookingReference: "MAL-AB12CD", bookingStatus: "ProofSubmitted",
    venueId: "e0000000-0000-4000-8000-000000000005", playerId: USER_ID, depositAmount: 150, openedBy: "Player", reason: "حوّلت العربون والملعب مش راضي يأكد",
    status: "Open", reviewerId: null, outcome: null, resolutionNote: null, createdAtUtc: "2026-11-06T10:00:00Z", resolvedAtUtc: null, evidence: [],
  };
  api.on("GET", /^\/api\/v1\/admin\/disputes$/, () => ({ items: [dispute], page: 1, pageSize: 25, total: 1 }));
  api.on("GET", /^\/api\/v1\/admin\/disputes\/d0000000/, () => dispute);
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, () => ({ stepUpToken: "tok", scope: "admin_destructive", expiresInSeconds: 300 }));
  api.on("POST", /\/resolve$/, () => ({ ...dispute, status: "Resolved", outcome: "ConfirmBooking", bookingStatus: "Confirmed" }));

  await page.goto("/admin/disputes");
  await openItem(page, "MAL-AB12CD");
  const submit = page.getByRole("button", { name: "تسجيل القرار" });
  await expect(submit).toBeDisabled();

  await page.getByRole("radio", { name: /تأكيد الحجز/ }).check();
  await page.getByLabel("سبب القرار").fill("كشف الحساب مطابق للإيصال");
  await submit.click();
  await page.getByRole("dialog", { name: "تأكيد الهوية" }).getByLabel("كود التحقق").fill("654321");
  await page.getByRole("dialog", { name: "تأكيد الهوية" }).getByRole("button", { name: "تأكيد" }).click();

  const resolve = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/resolve"));
  expect(resolve.body).toEqual({ outcome: "ConfirmBooking", note: "كشف الحساب مطابق للإيصال" });
  expect(resolve.headers["x-step-up-token"]).toBe("tok");
});
