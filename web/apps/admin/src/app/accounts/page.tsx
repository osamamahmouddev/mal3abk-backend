"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, btnDanger, btnOutline, btnPrimary, btnSmall, card, muted } from "@/components/ui";
import { Drawer, Field, QueryState, Section, Table, TextInput, fmtDate, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";
import { Check } from "lucide-react";

type Account = Schemas["AdminAccountDto"];

const STAFF_ROLES: [string, string][] = [
  ["SuperAdmin", "مدير النظام الأعلى (SuperAdmin)"],
  ["Admin", "مسؤول عام (كل الصلاحيات)"],
  ["SupportAgent", "دعم فني"],
  ["Finance", "مالية واشتراكات"],
];

const ROLE_LABELS: Record<string, string> = {
  SuperAdmin: "مدير النظام الأعلى",
  Admin: "مسؤول عام",
  SupportAgent: "دعم فني",
  Finance: "مسؤول مالي",
};

const STATUS_LABELS: Record<string, { label: string; tone: "ok" | "bad" | "warn" | "neutral" }> = {
  Active: { label: "نشط", tone: "ok" },
  Suspended: { label: "معطّل", tone: "bad" },
};

const PERMISSION_LABELS: Record<string, { label: string; desc: string; category: string }> = {
  // Platform administration
  "admin.accounts.manage": { label: "إدارة حسابات الفريق", desc: "إنشاء وتعديل وتجميد حسابات المشرفين وتعيين أدوارهم", category: "إدارة النظام" },
  "admin.audit_log.view": { label: "عرض سجل التدقيق والرقابة", desc: "استعراض كافة العمليات الحساسة وتتبع نشاط المشرفين", category: "إدارة النظام" },
  "admin.bookings.view": { label: "عرض ومتابعة الحجوزات", desc: "استعراض كافة الحجوزات وحالات الدفع والمطابقة", category: "الحجوزات" },
  "admin.disputes.resolve": { label: "البت في النزاعات والشكاوى", desc: "تسوية النزاعات بين اللاعبين والملاعب وإرجاع المدفوعات", category: "النزاعات" },
  "admin.impersonate": { label: "الدخول كحساب مستخدم للدعم", desc: "تسجيل الدخول المؤقت بحساب العميل لحل المشكلات التقنية", category: "الدعم الفني" },
  "admin.settings.manage": { label: "إدارة إعدادات المنصة", desc: "التحكم في إعدادات النظام وتفعيل أو إيقاف الميزات", category: "إدارة النظام" },
  "admin.users.manage": { label: "إدارة وتعديل المستخدمين", desc: "حظر وتعديل وإعادة تعيين بيانات اللاعبين وأصحاب الملاعب", category: "المستخدمين" },
  "admin.users.view": { label: "عرض بيانات وملفات المستخدمين", desc: "استعراض قوائم المستخدمين وسجلاتهم", category: "المستخدمين" },
  "admin.venues.approve": { label: "مراجعة واعتماد الملاعب", desc: "الموافقة على طلبات انضمام الملاعب وطلب معلومات إضافية", category: "الملاعب" },

  // Finance
  "finance.invoices.view": { label: "عرض الفواتير والمعاملات المالية", desc: "استعراض الفواتير وسجلات الدفع والعمولات", category: "المالية" },
  "finance.subscriptions.manage": { label: "إدارة باقات واشتراكات الملاعب", desc: "ترقية وتعديل باقات الملاعب والاشتراكات", category: "المالية" },

  // Support & Common
  "support.tickets.manage": { label: "إدارة تذاكر الدعم الفني", desc: "استلام والرد على استفسارات وشكاوى المستخدمين والملاعب", category: "الدعم الفني" },
  "profile.manage": { label: "إدارة الملف الشخصي", desc: "تعديل البيانات الأساسية وكلمة المرور وجلسات الحساب", category: "عام" },
  "venues.manage": { label: "إدارة بيانات الملاعب", desc: "التحكم في بيانات ومرافق الملاعب", category: "الملاعب" },
  "teams.participate": { label: "المشاركة وإدارة الفرق", desc: "الانضمام للفرق الرياضية وإدارتها", category: "عام" },
  "bookings.create": { label: "إنشاء وحجز الملاعب", desc: "حجز المواعيد وإتمام الدفع", category: "الحجوزات" },
};

export default function AccountsPage() {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<Account | null>(null);
  const [reason, setReason] = useState<ReasonRequest | null>(null);

  const accounts = useQuery({ queryKey: ["admin-accounts"], queryFn: () => unwrap(api.GET("/api/v1/admin/accounts")) });
  const roles = useQuery({ queryKey: ["admin-roles"], queryFn: () => unwrap(api.GET("/api/v1/admin/roles")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin-accounts"] });

  const toggle = useMutation({
    mutationFn: ({ account, text }: { account: Account; text: string }) =>
      account.status === "Suspended"
        ? unwrap(api.POST("/api/v1/admin/accounts/{accountId}/enable", { params: { path: { accountId: account.id } }, body: { reason: text } }))
        : stepUp((headers) => unwrap(api.POST("/api/v1/admin/accounts/{accountId}/disable", { params: { path: { accountId: account.id } }, headers, body: { reason: text } }))),
    onSuccess: async (a) => {
      notice.ok(a.status === "Suspended" ? "تم تعطيل الحساب وإنهاء جميع جلساته فورياً." : "تم تفعيل الحساب بنجاح.");
      await refresh();
    },
    onError: notice.fail,
  });

  return (
    <Page>
      <PageHeader
        title="حسابات الفريق والمشرفين"
        sub="تتلقى الحسابات الجديدة رابطاً لتعيين كلمة المرور، ويشترط تفعيل التحقق بخطوتين (2FA) عند أول تسجيل دخول. لا يمكن حذف أو تعطيل آخر مسؤول نشط."
      >
        <button type="button" className={btnPrimary} onClick={() => setCreating(true)}>
          حساب جديد +
        </button>
      </PageHeader>
      {notice.node}
      <QueryState
        query={accounts}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد حسابات مسجلة للفريق حالياً"
        emptyDescription="يمكنك إضافة وتعيين حسابات جديدة للمشرفين والمسؤولين وتحديد أدوارهم وصلاحياتهم في إدارة المنصة."
        emptyIcon="users"
        emptyAction={{
          label: "إضافة حساب مشرف جديد",
          onClick: () => setCreating(true),
          variant: "primary",
        }}
      >
        {(list) => (
          <Table<Account>
            caption="حسابات الفريق"
            rows={list}
            rowKey={(a) => a.id}
            columns={[
              {
                title: "الاسم",
                cell: (a) => (
                  <div className="flex flex-col">
                    <strong className="text-slate-900">{a.fullName}</strong>
                  </div>
                ),
              },
              {
                title: "البريد الإلكتروني",
                cell: (a) => (
                  <span dir="ltr" className="font-inter text-xs text-slate-600 font-mono">
                    {a.email}
                  </span>
                ),
              },
              {
                title: "الأدوار",
                cell: (a) => {
                  const activeRoles = a.roles.filter((r) => STAFF_ROLES.some(([k]) => k === r));
                  return (
                    <div className="flex flex-wrap gap-1">
                      {activeRoles.map((r) => (
                        <span key={r} className="inline-flex items-center px-2 py-0.5 rounded-md text-xs font-semibold bg-[#EEF2F6] text-[#1F2A44]">
                          {ROLE_LABELS[r] ?? r}
                        </span>
                      ))}
                    </div>
                  );
                },
              },
              {
                title: "التحقق بخطوتين",
                cell: (a) => (
                  <Pill tone={a.twoFactorEnabled ? "ok" : "warn"}>
                    {a.twoFactorEnabled ? "مفعّل" : "غير مفعّل"}
                  </Pill>
                ),
              },
              {
                title: "الحالة",
                cell: (a) => {
                  const s = STATUS_LABELS[a.status] ?? { label: a.status, tone: "neutral" as const };
                  return <Pill tone={s.tone}>{s.label}</Pill>;
                },
              },
              {
                title: "تاريخ الإضافة",
                cell: (a) => (
                  <span className="text-xs text-[#5B6475] whitespace-nowrap">
                    {fmtDate(a.createdAtUtc)}
                  </span>
                ),
              },
              {
                title: "",
                cell: (a) => (
                  <div className="flex gap-1.5 justify-end">
                    <button type="button" className={btnSmall} onClick={() => setEditing(a)}>
                      الأدوار
                    </button>
                    <button
                      type="button"
                      className={btnSmall}
                      onClick={() =>
                        setReason({
                          title: a.status === "Suspended" ? "تفعيل الحساب" : "تعطيل الحساب",
                          body: a.status === "Suspended" ? "سيتمكن المستخدم من تسجيل الدخول فوراً." : "سيتم إنهاء كافة الجلسات النشطة للمستخدم فوراً.",
                          confirmLabel: a.status === "Suspended" ? "تفعيل الحساب" : "تأكيد التعطيل",
                          onConfirm: (text) => toggle.mutate({ account: a, text }),
                        })
                      }
                    >
                      {a.status === "Suspended" ? "تفعيل" : "تعطيل"}
                    </button>
                  </div>
                ),
              },
            ]}
          />
        )}
      </QueryState>

      <Section title="الأدوار والصلاحيات المعتمدة">
        <QueryState query={roles}>
          {(list) => (
            <div className="grid md:grid-cols-3 gap-4">
              {list
                .filter((r) => r.isStaffRole)
                .map((r) => {
                  const roleName = STAFF_ROLES.find(([k]) => k === r.role)?.[1] ?? r.role;
                  return (
                    <div key={r.role} className={`${card} rounded-[18px] p-4 flex flex-col gap-3 border border-[#E5E9F0] shadow-xs`}>
                      <div className="flex items-center justify-between pb-2 border-b border-[#EEF2F6]">
                        <strong className="text-[15px] text-slate-900">{roleName}</strong>
                        <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-slate-100 text-slate-600">
                          {r.permissions.length} صلاحيات
                        </span>
                      </div>
                      <div className="flex flex-col gap-2">
                        {r.permissions.map((p) => {
                          const info = PERMISSION_LABELS[p];
                          return (
                            <div key={p} className="flex items-start gap-2 py-1 border-b border-[#F5F7FA] last:border-0">
                              <Check className="size-3.5 text-emerald-600 mt-0.5 shrink-0" strokeWidth={2.5} />
                              <div className="flex flex-col gap-0.5">
                                <span className="text-[12.5px] font-semibold text-slate-800">
                                  {info?.label ?? p}
                                </span>
                                {info?.desc && (
                                  <span className="text-[11px] text-slate-500 leading-tight">
                                    {info.desc}
                                  </span>
                                )}
                                <span dir="ltr" className="text-[10px] font-mono text-slate-400 text-right">
                                  {p}
                                </span>
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  );
                })}
            </div>
          )}
        </QueryState>
      </Section>

      {creating && (
        <CreateAccount
          onClose={() => setCreating(false)}
          onDone={async () => {
            setCreating(false);
            notice.ok("تم إنشاء الحساب بنجاح وإرسال رابط تعيين كلمة المرور إلى البريد الإلكتروني.");
            await refresh();
          }}
        />
      )}
      {editing && (
        <EditRoles
          account={editing}
          onClose={() => setEditing(null)}
          onDone={async () => {
            setEditing(null);
            notice.ok("تم تحديث أدوار الحساب وإنهاء جلساته السابقة لضمان الأمان.");
            await refresh();
          }}
        />
      )}
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Page>
  );
}

function RolePicker({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="text-[13px] font-semibold text-slate-800 mb-1">الأدوار المخصصة للحساب</legend>
      <div className="flex flex-col gap-2">
        {STAFF_ROLES.map(([role, title]) => {
          const checked = value.includes(role);
          return (
            <label
              key={role}
              className={`flex items-start gap-3 p-3 rounded-xl border text-sm cursor-pointer transition-all ${
                checked ? "border-[#0A1330] bg-[#F7F9FC]" : "border-[#E5E9F0] hover:bg-slate-50"
              }`}
            >
              <input
                type="checkbox"
                className="mt-1 h-4 w-4 rounded border-slate-300 text-[#0A1330] focus:ring-[#0A1330]"
                checked={checked}
                onChange={(e) => onChange(e.target.checked ? [...value, role] : value.filter((r) => r !== role))}
              />
              <div className="flex flex-col gap-0.5">
                <span className="font-bold text-slate-900">{title}</span>
                <span className="text-xs text-slate-500 font-mono" dir="ltr">
                  Role: {role}
                </span>
              </div>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}

function CreateAccount({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const stepUp = useStepUp();
  const notice = useNotice();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [roles, setRoles] = useState<string[]>(["SupportAgent"]);
  const create = useMutation({
    mutationFn: () => stepUp((headers) => unwrap(api.POST("/api/v1/admin/accounts", { headers, body: { fullName: name, email, phoneNumber: null, roles } }))),
    onSuccess: onDone,
    onError: notice.fail,
  });
  return (
    <Drawer title="إنشاء حساب مشرف جديد" open onClose={onClose}>
      {notice.node}
      <div className="flex flex-col gap-4">
        <Field label="الاسم الكامل">
          <TextInput placeholder="مثال: أحمد محمود" value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="البريد الإلكتروني">
          <TextInput dir="ltr" type="email" placeholder="agent@malabak.app" value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <RolePicker value={roles} onChange={setRoles} />
        <div className="flex gap-2 justify-end pt-2 border-t border-[#EEF2F6]">
          <button type="button" className={btnOutline} onClick={onClose}>
            إلغاء
          </button>
          <button
            type="button"
            className={btnPrimary}
            disabled={!name.trim() || !email.includes("@") || roles.length === 0 || create.isPending}
            onClick={() => create.mutate()}
          >
            {create.isPending ? "جاري الإنشاء..." : "إنشاء الحساب"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function EditRoles({ account, onClose, onDone }: { account: Account; onClose: () => void; onDone: () => void }) {
  const stepUp = useStepUp();
  const notice = useNotice();
  const [roles, setRoles] = useState<string[]>(account.roles.filter((r) => STAFF_ROLES.some(([k]) => k === r)));
  const [why, setWhy] = useState("");
  const save = useMutation({
    mutationFn: () =>
      stepUp((headers) => unwrap(api.PUT("/api/v1/admin/accounts/{accountId}/roles", { params: { path: { accountId: account.id } }, headers, body: { roles, reason: why } }))),
    onSuccess: onDone,
    onError: notice.fail,
  });
  return (
    <Drawer title={`تعديل أدوار ${account.fullName}`} open onClose={onClose}>
      {notice.node}
      <div className="flex flex-col gap-4">
        <RolePicker value={roles} onChange={setRoles} />
        <Field label="سبب التعديل (يُسجل في سجل التدقيق والرقابة)">
          <TextInput placeholder="اكتب سبب تغيير الأدوار..." value={why} onChange={(e) => setWhy(e.target.value)} />
        </Field>
        <div className="flex gap-2 justify-end pt-2 border-t border-[#EEF2F6]">
          <button type="button" className={btnOutline} onClick={onClose}>
            إلغاء
          </button>
          <button
            type="button"
            className={roles.includes("Admin") ? btnPrimary : btnDanger}
            disabled={roles.length === 0 || why.trim().length < 5 || save.isPending}
            onClick={() => save.mutate()}
          >
            {save.isPending ? "جاري الحفظ..." : "حفظ التغييرات"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

