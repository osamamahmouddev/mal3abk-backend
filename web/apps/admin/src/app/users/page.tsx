"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnDanger, btnOutline, btnSmall, card, muted } from "@/components/ui";
import { Drawer, Pager, QueryState, Section, Select, Table, fmtDate, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Row = Schemas["AdminUserListItem"];

const ROLES: [string, string][] = [
  ["", "كل الأدوار"],
  ["Player", "لاعب"],
  ["VenueOwner", "مالك ملعب"],
  ["VenueStaff", "موظف ملعب"],
  ["Admin", "أدمن"],
  ["SupportAgent", "دعم"],
  ["Finance", "مالية"],
];
const STATUSES: [string, string][] = [
  ["", "كل الحالات"],
  ["Active", "نشط"],
  ["Suspended", "موقوف"],
  ["Deleted", "محذوف"],
];
const ROLE_MAP = Object.fromEntries(ROLES);
const STATUS_MAP = Object.fromEntries(STATUSES);
const STATUS_TONE = { Active: "ok", Suspended: "bad", Deleted: "neutral" } as const;
const PAGE_SIZE = 25;

export default function UsersPage() {
  const [q, setQ] = useState("");
  const [role, setRole] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);

  const users = useQuery({
    queryKey: ["admin-users", q, role, status, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/users", {
          params: { query: { q: q || undefined, role: role || undefined, status: status || undefined, page, pageSize: PAGE_SIZE } },
        }),
      ),
  });

  return (
    <Page>
      <PageHeader title="المستخدمين" sub="الأرقام مخفية في القائمة. فتح تفاصيل أي مستخدم بيتسجل في سجل التدقيق." />
      <div className="flex flex-wrap gap-2">
        <div className="flex-[1_1_260px]">
          <SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="اسم، رقم، إيميل أو ID" />
        </div>
        <Select label="الدور" value={role} onChange={(v) => { setRole(v); setPage(1); }} options={ROLES} />
        <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
      </div>
      <QueryState query={users} isEmpty={(d) => d.items.length === 0} empty="مفيش مستخدمين بالبحث ده.">
        {(data) => (
          <>
            <Table<Row>
              caption="المستخدمين"
              rows={data.items}
              rowKey={(r) => r.id}
              selected={selected}
              onSelect={(r) => setSelected(r.id)}
              columns={[
                { title: "الاسم", cell: (r) => <strong>{r.fullName}</strong> },
                { title: "الرقم", cell: (r) => <span dir="ltr" className="font-inter">{r.maskedPhone}</span> },
                { title: "الإيميل", cell: (r) => r.email ?? "—" },
                { title: "الأدوار", cell: (r) => r.roles.map((x) => ROLE_MAP[x] ?? x).join("، ") },
                { title: "الحالة", cell: (r) => <Pill tone={STATUS_TONE[r.status as keyof typeof STATUS_TONE] ?? "neutral"}>{STATUS_MAP[r.status] ?? r.status}</Pill> },
                { title: "التسجيل", cell: (r) => fmtDate(r.createdAtUtc) },
              ]}
            />
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <UserDrawer userId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function UserDrawer({ userId, onClose }: { userId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [code, setCode] = useState<Schemas["ImpersonationCodeDto"] | null>(null);

  const detail = useQuery({
    queryKey: ["admin-user", userId],
    enabled: !!userId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/users/{userId}", { params: { path: { userId: userId! } } })),
  });
  const history = useQuery({
    queryKey: ["admin-user-history", userId],
    enabled: !!userId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/users/{userId}/history", { params: { path: { userId: userId! } } })),
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin-user"] }).then(() => queryClient.invalidateQueries({ queryKey: ["admin-users"] }));
  const action = useMutation({
    mutationFn: async ({ kind, text }: { kind: "suspend" | "reactivate" | "revoke" | "impersonate"; text: string }) => {
      const path = { userId: userId! };
      if (kind === "suspend") return stepUp((headers) => unwrap(api.POST("/api/v1/admin/users/{userId}/suspend", { params: { path }, headers, body: { reason: text } })));
      if (kind === "reactivate") return unwrap(api.POST("/api/v1/admin/users/{userId}/reactivate", { params: { path }, body: { reason: text } }));
      if (kind === "revoke") return unwrap(api.POST("/api/v1/admin/users/{userId}/sessions/revoke", { params: { path }, body: { reason: text } }));
      const issued = await stepUp((headers) => unwrap(api.POST("/api/v1/admin/impersonate", { headers, body: { userId: userId!, reason: text } })));
      setCode(issued);
      return issued;
    },
    onSuccess: async (_, { kind }) => {
      notice.ok({ suspend: "اتوقف الحساب واتقفلت كل جلساته.", reactivate: "اترفع الإيقاف.", revoke: "اتقفلت كل جلسات المستخدم.", impersonate: "الكود جاهز (صالح ٦٠ ثانية)." }[kind]);
      await refresh();
    },
    onError: notice.fail,
  });

  const ask = (kind: "suspend" | "reactivate" | "revoke" | "impersonate", title: string, body: string, confirmLabel: string) =>
    setReason({ title, body, confirmLabel, onConfirm: (text) => action.mutate({ kind, text }) });

  return (
    <Drawer title="تفاصيل المستخدم" open={!!userId} onClose={() => { setCode(null); notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(u) => (
          <>
            <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
              <strong className="text-[17px]">{u.fullName}</strong>
              <KV k="الرقم"><span dir="ltr" className="font-inter">{u.phoneNumber}</span></KV>
              <KV k="الإيميل">{u.email ?? "—"}</KV>
              <KV k="الأدوار">{u.roles.map((x) => ROLE_MAP[x] ?? x).join("، ")}</KV>
              <KV k="الحالة"><Pill tone={STATUS_TONE[u.status as keyof typeof STATUS_TONE] ?? "neutral"}>{STATUS_MAP[u.status] ?? u.status}</Pill>{u.suspensionReason ? ` — ${u.suspensionReason}` : ""}</KV>
              <KV k="المنطقة">{[u.city, u.area].filter(Boolean).join("، ") || "—"}</KV>
              <KV k="التحقق بخطوتين">{u.twoFactorEnabled ? "مفعّل" : "مش مفعّل"}</KV>
              <KV k="جلسات نشطة">{num(u.activeSessions)} (آخر نشاط {fmtDateTime(u.lastActivityAtUtc)})</KV>
              <KV k="تاريخ التسجيل">{fmtDate(u.createdAtUtc)}</KV>
            </div>
            <div className="flex flex-wrap gap-2">
              {u.status === "Active" && (
                <button type="button" className={btnDanger} onClick={() => ask("suspend", "إيقاف الحساب", "المستخدم هيتقفل من كل الأجهزة ومش هيقدر يدخل لحد ما ترفع الإيقاف.", "إيقاف")}>
                  إيقاف الحساب
                </button>
              )}
              {u.status === "Suspended" && (
                <button type="button" className={btnOutline} onClick={() => ask("reactivate", "رفع الإيقاف", "الحساب هيرجع يشتغل عادي.", "رفع الإيقاف")}>
                  رفع الإيقاف
                </button>
              )}
              {u.status !== "Deleted" && (
                <button type="button" className={btnSmall} onClick={() => ask("revoke", "قفل كل الجلسات", "مفيد لو الحساب اتسرق. المستخدم هيسجّل دخول تاني.", "قفل الجلسات")}>
                  قفل كل الجلسات
                </button>
              )}
              {u.status === "Active" && (
                <button
                  type="button"
                  className={btnSmall}
                  onClick={() => ask("impersonate", "الدخول بصفة المستخدم", "هتاخد كود صالح ٦٠ ثانية لمرة واحدة. العميل هيوصله إشعار، ومفيش تغيير باسورد أو رقم أو بيانات دفع.", "إصدار كود")}
                >
                  دخول للمساعدة
                </button>
              )}
            </div>
            {code && (
              <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2 border-2 border-brand-red`}>
                <strong>كود الدخول (مرة واحدة، {num(code.expiresInSeconds)} ثانية)</strong>
                <code dir="ltr" className="font-inter text-[12px] break-all bg-[#F6F7FA] rounded-lg p-2 select-all">{code.code}</code>
                <span className={`text-xs ${muted}`}>افتح تطبيق الملاعب على /impersonate والصق الكود. الجلسة ١٥ دقيقة وفيها بانر واضح.</span>
              </div>
            )}
          </>
        )}
      </QueryState>
      <Section title="السجل">
        <QueryState query={history}>
          {(parts) => (
            <div className="flex flex-col gap-3">
              {parts.map((part) => (
                <div key={part.source} className={`${card} rounded-[14px] p-3 flex flex-col gap-1.5`}>
                  <strong className="text-sm">{part.source === "bookings" ? "الحجوزات" : "المجتمع"} ({num(part.total)})</strong>
                  <span className={`text-xs ${muted}`}>{Object.entries(part.counts).map(([k, v]) => `${k}: ${num(v)}`).join(" · ") || "—"}</span>
                  {part.recent.map((item) => (
                    <div key={`${item.kind}-${item.detail}-${item.occurredAtUtc}`} className="flex justify-between text-[13px] border-t border-[#EEF0F5] pt-1.5">
                      <span>{item.reference}</span>
                      <span className={muted}>{item.status} · {fmtDate(item.occurredAtUtc)}</span>
                    </div>
                  ))}
                </div>
              ))}
            </div>
          )}
        </QueryState>
      </Section>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}
