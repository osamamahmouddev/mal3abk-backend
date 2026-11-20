import { expect, test, waitForCall } from "./fixtures";

/** The admin CRM: a board of venue contacts by stage, follow-ups, notes and stage moves. */

const CONTACT_ID = "c1000000-0000-4000-8000-000000000001";
const contact = (stage: string, extra: Record<string, unknown> = {}) => ({
  id: CONTACT_ID, name: "كابتن أحمد", venueName: "ملعب النصر", area: "مدينة نصر", phone: "+201012345678", stage, source: "Manual",
  nextFollowUpAtUtc: "2020-01-01T10:00:00Z", assignedToUserId: null, assignedToName: null, stageChangedAtUtc: "2026-11-20T10:00:00Z",
  updatedAtUtc: "2026-11-20T10:00:00Z", createdAtUtc: "2026-11-19T10:00:00Z", lostReason: null, ...extra,
});

test.beforeEach(async ({ api }) => {
  await api.signInAs("Admin");
  api.on("GET", /^\/api\/v1\/admin\/crm\/summary$/, () => ({
    byStage: { New: 1, Contacted: 0, Interested: 0, Onboarding: 0, Active: 0, Lost: 0 }, followUpsDue: 1, newThisWeek: 1, waitlistNotImported: 3,
  }));
  api.on("GET", /^\/api\/v1\/admin\/crm\/contacts$/, () => ({ items: [contact("New")], page: 1, pageSize: 100, total: 1 }));
  api.on("GET", new RegExp(`^/api/v1/admin/crm/contacts/${CONTACT_ID}$`), () => ({ contact: contact("New"), notes: [] }));
});

test("contacts show on the board by stage, with an overdue follow-up flagged", async ({ page }) => {
  await page.goto("/admin/crm");

  const column = page.getByRole("region", { name: "جديد" });
  await expect(column.getByRole("button", { name: /كابتن أحمد/ })).toBeVisible();
  await expect(column.getByText(/متأخر من/)).toBeVisible();
  await expect(page.getByRole("button", { name: /استيراد ٣ من قائمة الانتظار/ })).toBeVisible();
  await page.screenshot({ path: "test-results/crm-board.png", fullPage: true });
});

test("moving a contact to «not interested» asks why, and a call is logged on its timeline", async ({ page, api }) => {
  api.on("POST", new RegExp(`/contacts/${CONTACT_ID}/stage$`), () => ({ contact: contact("Lost", { lostReason: "عنده نظام تاني" }), notes: [] }));
  api.on("POST", new RegExp(`/contacts/${CONTACT_ID}/notes$`), () => ({
    contact: contact("New"),
    notes: [{ id: "n1", kind: "Call", text: "هيبعت الصور بكره", authorUserId: "a", authorName: "Admin", createdAtUtc: "2026-11-21T10:00:00Z" }],
  }));

  await page.goto("/admin/crm");
  await page.getByRole("button", { name: /كابتن أحمد/ }).click();
  const drawer = page.getByRole("dialog", { name: "كابتن أحمد" });
  await expect(drawer.getByRole("link", { name: "واتساب" })).toHaveAttribute("href", "https://wa.me/201012345678");

  await drawer.getByPlaceholder("اتقال إيه؟").fill("هيبعت الصور بكره");
  await drawer.getByRole("button", { name: "سجّل مكالمة" }).click();
  const note = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/notes"));
  expect(note.body).toEqual({ text: "هيبعت الصور بكره", kind: 1 });

  await drawer.getByRole("button", { name: "مش مهتم" }).click();
  await page.getByRole("textbox").last().fill("عنده نظام تاني");
  await page.getByRole("button", { name: "حفظ", exact: true }).last().click();
  const move = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/stage"));
  expect(move.body).toEqual({ stage: 5, reason: "عنده نظام تاني" });
  await page.screenshot({ path: "test-results/crm-drawer.png" });
});

test("a new contact is added with its phone and follow-up day", async ({ page, api }) => {
  api.on("POST", /^\/api\/v1\/admin\/crm\/contacts$/, () => ({ contact: contact("New"), notes: [] }));

  await page.goto("/admin/crm");
  await page.getByRole("button", { name: "عميل جديد" }).first().click();
  const drawer = page.getByRole("dialog", { name: "عميل جديد" });
  await drawer.getByLabel("الاسم").fill("كابتن أحمد");
  await drawer.getByLabel("رقم الموبايل").fill("01012345678");
  await drawer.getByLabel("نكلمه تاني يوم (اختياري)").fill("2026-11-25");
  await drawer.getByRole("button", { name: "إضافة" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path === "/api/v1/admin/crm/contacts");
  expect(call.body).toMatchObject({ name: "كابتن أحمد", phone: "01012345678", nextFollowUpAtUtc: "2026-11-25T10:00:00.000Z" });
});
