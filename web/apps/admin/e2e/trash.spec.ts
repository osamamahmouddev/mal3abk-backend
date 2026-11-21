import { expect, test, waitForCall } from "./fixtures";

/** The admin trash: items deleted from the console, restorable for 30 days; purging needs the name typed and step-up. */

const ENTRY_ID = "e9000000-0000-4000-8000-000000000001";
const VENUE_ID = "e0000000-0000-4000-8000-000000000005";
const entry = {
  id: ENTRY_ID, kind: "venue", entityId: VENUE_ID, title: "ملعب النجوم", subtitle: "المعادي · القاهرة", reason: "ملعب مقفول نهائي",
  deletedByUserId: "a0000000-0000-4000-8000-000000000001", deletedByName: "مدير الاختبار", deletedAtUtc: "2026-11-20T10:00:00Z",
  purgeAtUtc: new Date(Date.now() + 10 * 86_400_000).toISOString(),
};

test.beforeEach(async ({ api }) => {
  await api.signInAs("Admin");
  api.on("GET", /^\/api\/v1\/admin\/trash$/, () => [entry]);
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, () => ({ stepUpToken: "tok", scope: "admin_destructive", expiresInSeconds: 300 }));
});

test("a deleted item shows who deleted it, why, and when it is purged, and can be restored", async ({ page, api }) => {
  api.on("POST", new RegExp(`/admin/trash/${ENTRY_ID}/restore$`), (route) => route.fulfill({ status: 204 }));

  await page.goto("/admin/trash");
  const row = page.getByRole("listitem").filter({ hasText: "ملعب النجوم" });
  await expect(row).toContainText("ملعب مقفول نهائي");
  await expect(row).toContainText("مدير الاختبار");
  await expect(row).toContainText(/هيتمسح بعد/);
  await page.screenshot({ path: "test-results/trash-page.png", fullPage: true });

  await row.getByRole("button", { name: "رجّعه" }).click();
  await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith(`/${ENTRY_ID}/restore`));
  await expect(page.getByRole("status").filter({ hasText: "رجع مكانه" })).toBeVisible();
});

test("purging for good needs the exact name typed and a fresh authenticator code", async ({ page, api }) => {
  api.on("POST", new RegExp(`/admin/trash/${ENTRY_ID}/purge$`), (route) => route.fulfill({ status: 204 }));

  await page.goto("/admin/trash");
  await page.getByRole("listitem").filter({ hasText: "ملعب النجوم" }).getByRole("button", { name: "مسح نهائي" }).click();
  const dialog = page.getByRole("dialog", { name: /مسح «ملعب النجوم» نهائي/ });
  const confirm = dialog.getByRole("button", { name: "مسح نهائي" });
  await dialog.getByRole("textbox").fill("ملعب");
  await expect(confirm).toBeDisabled();
  await dialog.getByRole("textbox").fill("ملعب النجوم");
  await confirm.click();

  const stepUpDialog = page.getByRole("dialog", { name: "تأكيد الهوية" });
  await stepUpDialog.getByLabel("كود التحقق").fill("123456");
  await stepUpDialog.getByRole("button", { name: "تأكيد" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith(`/${ENTRY_ID}/purge`));
  expect(call.headers["x-step-up-token"]).toBe("tok");
  expect(call.body).toEqual({ confirmName: "ملعب النجوم" });
});
