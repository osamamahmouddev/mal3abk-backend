import { expect, test, waitForCall } from "./fixtures";

/** TASK-714..718: every admin screen loads real data and sends the right request for its main action. */

const VENUE_ID = "e0000000-0000-4000-8000-000000000005";
const TICKET_ID = "f0000000-0000-4000-8000-000000000006";
const PLAN_ID = "a1000000-0000-4000-8000-000000000007";
const OWNER_ID = "a2000000-0000-4000-8000-000000000008";

test.beforeEach(async ({ api }) => {
  await api.signInAs("Admin");
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, () => ({ stepUpToken: "tok", scope: "admin_destructive", expiresInSeconds: 300 }));
});

test("the dashboard shows KPI tiles with their definitions", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/admin\/dashboard$/, () => ({
    from: "2026-10-09", to: "2026-11-07", timeZone: "Africa/Cairo",
    figures: { "users.total": 1200, "bookings.created": 340, mrr: 15000, "disputes.open": 3 },
    series: { "bookings.created": [{ day: "2026-11-06", value: 10 }, { day: "2026-11-07", value: 14 }] },
    distributions: { "bookings.by_status": { Confirmed: 200, Cancelled: 40 } },
  }));
  await page.goto("/admin/overview");

  await expect(page.getByText("MRR")).toBeVisible();
  await expect(page.getByText("نزاعات مفتوحة")).toBeVisible();
  await expect(page.getByRole("img", { name: /الحجوزات يوميًا/ })).toBeVisible();
  await expect(page.getByText("الحجوزات حسب الحالة (في الفترة)")).toBeVisible();
});

test("an approved venue can be suspended with a reason and step-up", async ({ page, api }) => {
  const detail = {
    id: VENUE_ID, name: "ملعب النجوم", slug: "stars", address: "شارع ٩", city: "القاهرة", area: "المعادي", phoneNumber: "+201000000000",
    ownerId: OWNER_ID, ownerName: "كريم", approvalStatus: "Approved", isPublished: true, adminNote: null, staffCount: 2,
    pitches: [{ id: "p1", name: "ملعب ١", turfType: "ArtificialTurf", capacity: 10, isActive: true }],
    last30Days: { total: 20, confirmed: 15, completed: 2, cancelled: 2, noShow: 1, pending: 0, confirmedValue: 6800, currency: "EGP", lastBookingStartUtc: "2026-11-06T18:00:00Z" },
    history: [], createdAtUtc: "2026-09-01T10:00:00Z",
  };
  api.on("GET", /^\/api\/v1\/admin\/venues$/, () => ({ items: [{ id: VENUE_ID, name: "ملعب النجوم", city: "القاهرة", area: "المعادي", ownerId: OWNER_ID, approvalStatus: "Approved", isPublished: true, pitches: 1, createdAtUtc: "2026-09-01T10:00:00Z" }], page: 1, pageSize: 25, total: 1 }));
  api.on("GET", new RegExp(`^/api/v1/admin/venues/${VENUE_ID}$`), () => detail);
  api.on("GET", new RegExp(`^/api/v1/admin/venues/${VENUE_ID}/complaints$`), () => []);
  api.on("POST", /\/suspend$/, () => ({ ...detail, approvalStatus: "Suspended", isPublished: false }));

  await page.goto("/admin/venues");
  await page.getByRole("row").filter({ hasText: "ملعب النجوم" }).click();
  await expect(page.getByText("ملعب ١ · ArtificialTurf")).toBeVisible();
  await page.getByRole("button", { name: "إيقاف", exact: true }).click();
  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("شكاوى متكررة");
  await page.getByRole("dialog").getByRole("button", { name: "إيقاف", exact: true }).last().click();
  await page.getByRole("dialog", { name: "تأكيد الهوية" }).getByLabel("كود التحقق").fill("111111");
  await page.getByRole("dialog", { name: "تأكيد الهوية" }).getByRole("button", { name: "تأكيد" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/suspend"));
  expect(call.body).toEqual({ reason: "شكاوى متكررة" });
  expect(call.headers["x-step-up-token"]).toBe("tok");
});

test("support agents reply, add internal notes and resolve tickets", async ({ page, api }) => {
  const ticket = {
    ticket: { id: TICKET_ID, number: "SUP-7KQ2MX", category: "Refund", priority: "High", status: "Open", subject: "الاسترداد متأخر", requesterKind: "Player", requesterId: OWNER_ID, assigneeId: null, relatedBookingId: null, relatedVenueId: null, createdAtUtc: "2026-11-06T10:00:00Z", lastActivityAtUtc: "2026-11-06T10:00:00Z" },
    resolutionReason: null, resolvedAtUtc: null,
    messages: [{ id: "m1", authorKind: "Requester", authorId: OWNER_ID, body: "لغيت من ٣ أيام", isInternal: false, createdAtUtc: "2026-11-06T10:00:00Z" }],
  };
  api.on("GET", /^\/api\/v1\/admin\/support\/tickets$/, () => ({ items: [ticket.ticket], page: 1, pageSize: 25, total: 1 }));
  api.on("GET", new RegExp(`^/api/v1/admin/support/tickets/${TICKET_ID}$`), () => ticket);
  api.on("POST", /\/messages$/, () => ticket);
  api.on("POST", /\/resolve$/, () => ({ ...ticket, ticket: { ...ticket.ticket, status: "Resolved" }, resolutionReason: "اترد" }));

  await page.goto("/admin/support");
  await page.getByRole("row").filter({ hasText: "SUP-7KQ2MX" }).click();
  await page.getByRole("checkbox", { name: /ملاحظة داخلية/ }).check();
  await page.getByLabel("الرد").fill("كلمت الملعب، هيحوّل النهارده");
  await page.getByRole("button", { name: "إضافة ملاحظة" }).click();
  const note = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/messages"));
  expect(note.body).toEqual({ message: "كلمت الملعب، هيحوّل النهارده", internal: true });

  await page.getByRole("button", { name: "حل", exact: true }).click();
  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("الملعب حوّل المبلغ");
  await page.getByRole("button", { name: "حل", exact: true }).last().click();
  const resolve = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/resolve"));
  expect(resolve.body).toEqual({ reason: "الملعب حوّل المبلغ" });
});

test("finance records a manual payment and gets the invoice number back", async ({ page, api }) => {
  const plan = { id: PLAN_ID, code: "pro", name: "Pro", monthlyPrice: 900, annualPrice: 9000, currency: "EGP", features: [], trialDays: 14, graceDays: 7, isPublic: true, isDefault: false, sortOrder: 0, status: "Active", subscribers: 1, updatedAtUtc: "2026-11-01T00:00:00Z" };
  api.on("GET", /^\/api\/v1\/admin\/plans$/, () => [plan]);
  api.on("GET", /^\/api\/v1\/admin\/subscriptions$/, () => [{ id: "s1", ownerId: OWNER_ID, planCode: "pro", planName: "Pro", cycle: "Monthly", status: "Active", currentPeriodEndUtc: "2026-12-01T00:00:00Z", graceEndsAtUtc: null }]);
  api.on("GET", new RegExp(`^/api/v1/admin/subscriptions/owners/${OWNER_ID}$`), () => ({
    subscription: { id: "s1", ownerId: OWNER_ID, plan, cycle: "Monthly", status: "Active", startedAtUtc: "2026-11-01T00:00:00Z", currentPeriodEndUtc: "2026-12-01T00:00:00Z", graceEndsAtUtc: null, statusReason: null, allowsNewResources: true },
    history: [], payments: [],
  }));
  api.on("POST", /\/payments$/, () => ({ id: "i1", number: "INV-2026-000042", ownerId: OWNER_ID, planCode: "pro", planName: "Pro", cycle: "Monthly", amount: 900, currency: "EGP", issuedAtUtc: "2026-11-07T10:00:00Z", periodStartUtc: "2026-11-01T00:00:00Z", periodEndUtc: "2026-12-01T00:00:00Z", status: "Paid", voidReason: null }));

  await page.goto("/admin/billing");
  await page.getByRole("row").filter({ hasText: "Pro" }).click();
  await page.getByLabel("المبلغ").fill("900");
  await page.getByLabel("الطريقة").last().selectOption("1");
  await page.getByLabel("رقم العملية (اختياري)").fill("TRX-77");
  await page.getByRole("button", { name: "تسجيل الدفعة" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path.endsWith("/payments"));
  expect(call.body).toMatchObject({ amount: 900, method: 1, reference: "TRX-77" });
  await expect(page.getByRole("status").filter({ hasText: "INV-2026-000042" })).toBeVisible();
});

test("a new plan is created with its limits and features", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/admin\/plans$/, () => []);
  api.on("POST", /^\/api\/v1\/admin\/plans$/, (route) => route.fulfill({ status: 201, contentType: "application/json", body: "{}" }));

  await page.goto("/admin/plans");
  await page.getByRole("button", { name: "باقة جديدة" }).click();
  await page.getByLabel("الكود (إنجليزي، مش بيتغير بعد الاشتراك)").fill("Starter");
  await page.getByLabel("الاسم", { exact: true }).fill("ستارتر");
  await page.getByLabel("السعر الشهري").fill("300");
  await page.getByLabel("أقصى ملاعب (فاضي = بلا حدود)").fill("");
  await page.getByRole("checkbox", { name: "تصدير CSV" }).check();
  await page.getByRole("button", { name: "حفظ" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path === "/api/v1/admin/plans");
  expect(call.body).toMatchObject({ code: "starter", name: "ستارتر", monthlyPrice: 300, maxVenues: null, features: ["reports.csv_export"] });
});

test("platform settings are saved with a reason, and flags toggle with one", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/admin\/settings$/, () => [
    { key: "booking.default_hold_minutes", type: "Integer", value: "30", default: "30", isDefault: true, description: "مهلة الحجز", min: 5, max: 120, maxLength: null, updatedAtUtc: null, updatedBy: null },
  ]);
  api.on("GET", /^\/api\/v1\/admin\/feature-flags$/, () => [{ key: "venues.featured", enabled: false, description: "ملاعب مميزة", public: true, updatedAtUtc: null, updatedBy: null }]);
  api.on("PUT", /\/settings\/booking\.default_hold_minutes$/, () => ({}));
  api.on("PUT", /\/feature-flags\/venues\.featured$/, () => ({}));

  await page.goto("/admin/settings");
  await page.getByLabel("مهلة الحجز").fill("45");
  await page.getByRole("button", { name: "حفظ" }).click();
  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("زحمة الويك إند");
  await page.getByRole("dialog").getByRole("button", { name: "حفظ" }).click();
  const setting = await waitForCall(page, api.calls, (c) => c.method === "PUT" && c.path.includes("/settings/"));
  expect(setting.body).toEqual({ value: "45", reason: "زحمة الويك إند" });

  await page.getByRole("tab", { name: "المميزات (Flags)" }).click();
  await page.getByRole("switch", { name: "ملاعب مميزة" }).click();
  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("بداية الحملة");
  await page.getByRole("button", { name: "تشغيل" }).click();
  const flag = await waitForCall(page, api.calls, (c) => c.method === "PUT" && c.path.includes("/feature-flags/"));
  expect(flag.body).toEqual({ enabled: true, reason: "بداية الحملة" });
});

test("the audit log filters by action prefix and shows masked snapshots", async ({ page, api }) => {
  api.on("GET", /^\/api\/v1\/admin\/audit-logs$/, () => ({
    items: [{ id: "a1", occurredAtUtc: "2026-11-07T09:00:00Z", actorId: OWNER_ID, actorType: "Admin", impersonatorId: null, action: "admin.user.suspended", entityType: "User", entityId: "u-1234567890", beforeJson: "{\"status\":\"Active\"}", afterJson: "{\"phone\":\"***5678\"}", reason: "تكرار الغياب", ipAddress: "41.33.0.1", correlationId: "c" }],
    totalCount: 1, page: 1, pageSize: 50,
  }));

  await page.goto("/admin/audit");
  await page.getByLabel("الإجراء (بيبدأ بـ)").fill("admin.user");
  await waitForCall(page, api.calls, (c) => c.path.includes("action=admin.user"));
  await page.getByRole("row").filter({ hasText: "admin.user.suspended" }).click();
  await expect(page.getByText("***5678")).toBeVisible();
  await expect(page.getByText("تكرار الغياب").last()).toBeVisible();
});

test("impersonation issues a short code after step-up", async ({ page, api }) => {
  const userId = "b0000000-0000-4000-8000-000000000002";
  const detail = { id: userId, fullName: "أحمد", phoneNumber: "+201012345678", email: null, roles: ["Player"], status: "Active", suspensionReason: null, suspendedAtUtc: null, deletionRequestedAtUtc: null, city: null, area: null, language: "ar", twoFactorEnabled: false, isPhoneNumberConfirmed: true, isEmailConfirmed: false, activeSessions: 1, lastActivityAtUtc: null, createdAtUtc: "2026-10-01T00:00:00Z" };
  api.on("GET", /^\/api\/v1\/admin\/users$/, () => ({ items: [{ id: userId, fullName: "أحمد", maskedPhone: "+2010•••••678", email: null, roles: ["Player"], status: "Active", createdAtUtc: "2026-10-01T00:00:00Z" }], page: 1, pageSize: 25, total: 1 }));
  api.on("GET", new RegExp(`^/api/v1/admin/users/${userId}$`), () => detail);
  api.on("GET", new RegExp(`^/api/v1/admin/users/${userId}/history$`), () => []);
  api.on("POST", /^\/api\/v1\/admin\/impersonate$/, () => ({ code: "c0de-very-long-random-value-0123456789abcdefgh", expiresInSeconds: 60 }));

  await page.goto("/admin/users");
  await page.getByRole("row").filter({ hasText: "أحمد" }).click();
  await page.getByRole("button", { name: "دخول للمساعدة" }).click();
  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("تذكرة SUP-1: الحجز مش ظاهر");
  await page.getByRole("button", { name: "إصدار كود" }).click();
  await page.getByRole("dialog", { name: "تأكيد الهوية" }).getByLabel("كود التحقق").fill("222222");
  await page.getByRole("dialog", { name: "تأكيد الهوية" }).getByRole("button", { name: "تأكيد" }).click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "POST" && c.path === "/api/v1/admin/impersonate");
  expect(call.body).toEqual({ userId, reason: "تذكرة SUP-1: الحجز مش ظاهر" });
  expect(call.headers["x-step-up-token"]).toBe("tok");
  await expect(page.getByText("c0de-very-long-random-value-0123456789abcdefgh")).toBeVisible();
});

test("a wrong authenticator code keeps the action from being sent", async ({ page, api }) => {
  const userId = "b0000000-0000-4000-8000-000000000009";
  const detail = { id: userId, fullName: "منى", phoneNumber: "+201112345678", email: null, roles: ["Player"], status: "Active", suspensionReason: null, suspendedAtUtc: null, deletionRequestedAtUtc: null, city: null, area: null, language: "ar", twoFactorEnabled: false, isPhoneNumberConfirmed: true, isEmailConfirmed: false, activeSessions: 0, lastActivityAtUtc: null, createdAtUtc: "2026-10-01T00:00:00Z" };
  api.on("GET", /^\/api\/v1\/admin\/users$/, () => ({ items: [{ id: userId, fullName: "منى", maskedPhone: "+2011•••••678", email: null, roles: ["Player"], status: "Active", createdAtUtc: "2026-10-01T00:00:00Z" }], page: 1, pageSize: 25, total: 1 }));
  api.on("GET", new RegExp(`^/api/v1/admin/users/${userId}$`), () => detail);
  api.on("GET", new RegExp(`^/api/v1/admin/users/${userId}/history$`), () => []);
  api.on("POST", /^\/api\/v1\/auth\/step-up$/, (route) =>
    route.fulfill({ status: 400, contentType: "application/problem+json", body: JSON.stringify({ title: "Identity.StepUpFailed", detail: "الكود غلط" }) }),
  );

  await page.goto("/admin/users");
  await page.getByRole("row").filter({ hasText: "منى" }).click();
  await page.getByRole("button", { name: "إيقاف الحساب" }).click();
  await page.getByLabel("السبب (بيتسجل في سجل التدقيق)").fill("سلوك مسيء");
  await page.getByRole("button", { name: "إيقاف", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "تأكيد الهوية" });
  await dialog.getByLabel("كود التحقق").fill("999999");
  await dialog.getByRole("button", { name: "تأكيد" }).click();

  await expect(dialog.getByRole("alert")).toContainText("الكود غلط");
  expect(api.calls.some((c) => c.path.endsWith("/suspend"))).toBe(false);
});
