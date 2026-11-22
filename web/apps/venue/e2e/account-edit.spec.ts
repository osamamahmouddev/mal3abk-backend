import { expect, test, waitForCall } from "./fixtures";

/** The owner edits their own name, sign-in email and phone from Settings → account; email and phone go through verification. */

const me = { userId: "u1", fullName: "كابتن سعد", phoneNumber: "+201152630302", email: "owner@malabak.app", positions: [], language: "ar", isOnboardingComplete: true };

test.beforeEach(async ({ api }) => {
  api.on("GET", /^\/api\/v1\/me$/, () => me);
  api.on("GET", /^\/api\/v1\/me\/sessions$/, () => []);
});

async function openAccount(page: import("@playwright/test").Page) {
  await page.goto("/venue/settings");
  await page.getByRole("tab", { name: "الحساب والأمان" }).click();
}

test("the name is changed in place", async ({ page, api }) => {
  api.on("PATCH", /^\/api\/v1\/me$/, () => ({ ...me, fullName: "كابتن سعد صلاح" }));
  await openAccount(page);

  await page.getByRole("button", { name: "تعديل الاسم" }).click();
  const dialog = page.getByRole("dialog", { name: "تعديل الاسم" });
  await dialog.getByLabel("الاسم").fill("كابتن سعد صلاح");
  await dialog.getByRole("button", { name: "حفظ" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "PATCH" && c.path === "/api/v1/me");
  expect(call.body).toEqual({ fullName: "كابتن سعد صلاح" });
  await expect(dialog).toBeHidden();
});

test("a new email needs the password, then the code sent there, and then signing in again", async ({ page, api }) => {
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, () => ({ stepUpToken: "step-up-token", scope: "change_email", expiresInSeconds: 300 }));
  api.on("POST", /^\/api\/v1\/me\/email-change\/start$/, () => ({}));
  api.on("POST", /^\/api\/v1\/me\/email-change\/confirm$/, () => ({}));
  await openAccount(page);

  await page.getByRole("button", { name: "تعديل الإيميل" }).click();
  const dialog = page.getByRole("dialog", { name: "تغيير الإيميل" });
  await dialog.getByLabel("الإيميل الجديد").fill("new@malabak.app");
  await dialog.getByLabel("كلمة السر الحالية").fill("correct horse battery");
  await dialog.getByRole("button", { name: "ابعت الكود" }).click();

  const stepUp = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path === "/api/v1/auth/step-up");
  expect(stepUp.body).toEqual({ scope: "change_email", method: 0, secret: "correct horse battery" });
  const start = await waitForCall(page, api.calls, (c) => c.path === "/api/v1/me/email-change/start");
  expect(start.body).toEqual({ newEmail: "new@malabak.app", stepUpToken: "step-up-token" });

  await dialog.getByLabel("كود الإيميل").fill("123456");
  await dialog.getByRole("button", { name: "تأكيد" }).click();
  const confirm = await waitForCall(page, api.calls, (c) => c.path === "/api/v1/me/email-change/confirm");
  expect(confirm.body).toEqual({ code: "123456" });
  await expect(page.getByRole("dialog", { name: "الإيميل اتغيّر" })).toBeVisible();
  await page.screenshot({ path: "test-results/account-email-done.png" });
});

test("a new phone needs the password, a code on the current number, then one on the new number", async ({ page, api }) => {
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, () => ({ stepUpToken: "phone-token", scope: "change_phone", expiresInSeconds: 300 }));
  api.on("POST", /^\/api\/v1\/me\/phone-change\/start$/, () => ({ requestId: "r1", expiresAtUtc: "2030-01-01T00:00:00Z" }));
  api.on("POST", /^\/api\/v1\/me\/phone-change\/verify-old$/, () => ({}));
  api.on("POST", /^\/api\/v1\/me\/phone-change\/verify-new$/, () => ({ success: true }));
  await openAccount(page);

  await page.getByRole("button", { name: "تعديل الموبايل" }).click();
  const dialog = page.getByRole("dialog", { name: "تغيير رقم الموبايل" });
  await dialog.getByLabel("رقم الموبايل الجديد").fill("01012345678");
  await dialog.getByLabel("كلمة السر الحالية").fill("correct horse battery");
  await dialog.getByRole("button", { name: "ابعت الكود" }).click();

  const start = await waitForCall(page, api.calls, (c) => c.path === "/api/v1/me/phone-change/start");
  expect(start.body).toEqual({ newPhoneNumber: "01012345678", stepUpToken: "phone-token" });

  await dialog.getByLabel("كود الرقم الحالي").fill("111111");
  await dialog.getByRole("button", { name: "تأكيد" }).click();
  expect((await waitForCall(page, api.calls, (c) => c.path.endsWith("/verify-old"))).body).toEqual({ requestId: "r1", code: "111111" });

  await dialog.getByLabel("كود الرقم الجديد").fill("222222");
  await dialog.getByRole("button", { name: "تأكيد" }).click();
  expect((await waitForCall(page, api.calls, (c) => c.path.endsWith("/verify-new"))).body).toEqual({ requestId: "r1", code: "222222" });
  await expect(page.getByRole("dialog", { name: "الموبايل اتغيّر" })).toBeVisible();
});

test("a wrong password shows why and stays on the first step", async ({ page, api }) => {
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, async (route) => {
    await route.fulfill({ status: 400, contentType: "application/problem+json", body: JSON.stringify({ title: "Identity.StepUpFailed", status: 400 }) });
    return undefined;
  });
  await openAccount(page);

  await page.getByRole("button", { name: "تعديل الإيميل" }).click();
  const dialog = page.getByRole("dialog", { name: "تغيير الإيميل" });
  await dialog.getByLabel("الإيميل الجديد").fill("new@malabak.app");
  await dialog.getByLabel("كلمة السر الحالية").fill("wrong");
  await dialog.getByRole("button", { name: "ابعت الكود" }).click();

  await expect(dialog.getByRole("alert")).toBeVisible();
  await expect(dialog.getByLabel("الإيميل الجديد")).toBeVisible();
  expect(api.calls.some((c) => c.path === "/api/v1/me/email-change/start")).toBe(false);
});
