"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnDanger, btnOutline, btnPrimary, btnSmall, card, inputClass, money, muted } from "@/components/ui";
import { Drawer, Field, Pager, QueryState, Section, Select, Table, TextInput, fmtDate, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Row = Schemas["AdminVenueListItem"];

const STATUSES: [string, string][] = [
  ["", "كل الحالات"],
  ["Approved", "معتمد"],
  ["Submitted", "بانتظار الموافقة"],
  ["NeedsInfo", "بحاجة لمعلومات"],
  ["Rejected", "مرفوض"],
  ["Suspended", "موقوف"],
  ["Draft", "مسودة"],
];
const STATUS_MAP = Object.fromEntries(STATUSES);
const TONE: Record<string, "ok" | "warn" | "bad" | "neutral"> = { Approved: "ok", Submitted: "warn", NeedsInfo: "warn", Rejected: "bad", Suspended: "bad", Draft: "neutral" };
const PAGE_SIZE = 25;

export default function VenuesPage() {
  const [q, setQ] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);

  const venues = useQuery({
    queryKey: ["admin-venues", q, status, page],
    queryFn: () =>
      unwrap(api.GET("/api/v1/admin/venues", { params: { query: { q: q || undefined, approvalStatus: status || undefined, page, pageSize: PAGE_SIZE } } })),
  });

  return (
    <Page>
      <PageHeader title="الملاعب" sub="حالة الاعتماد منفصلة عن نشر المالك. الإيقاف بيخفي الملعب ويوقف الحجز الجديد، والحجوزات القديمة بتفضل." />
      <div className="flex flex-wrap gap-2">
        <div className="flex-[1_1_260px]">
          <SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="اسم الملعب أو المدينة أو المنطقة" />
        </div>
        <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
      </div>
      <QueryState query={venues} isEmpty={(d) => d.items.length === 0} empty="مفيش ملاعب بالبحث ده.">
        {(data) => (
          <>
            <Table<Row>
              caption="الملاعب"
              rows={data.items}
              rowKey={(r) => r.id}
              selected={selected}
              onSelect={(r) => setSelected(r.id)}
              columns={[
                { title: "الملعب", cell: (r) => <strong>{r.name}</strong> },
                { title: "المكان", cell: (r) => `${r.city}، ${r.area}` },
                { title: "الملاعب الفرعية", cell: (r) => num(r.pitches) },
                { title: "الاعتماد", cell: (r) => <Pill tone={TONE[r.approvalStatus] ?? "neutral"}>{STATUS_MAP[r.approvalStatus] ?? r.approvalStatus}</Pill> },
                { title: "منشور", cell: (r) => (r.isPublished ? "منشور" : "مسودة") },
                { title: "الإضافة", cell: (r) => fmtDate(r.createdAtUtc) },
              ]}
            />
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <VenueDrawer venueId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function VenueDrawer({ venueId, onClose }: { venueId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const [newOwner, setNewOwner] = useState("");

  const usersQuery = useQuery({
    queryKey: ["admin-users-candidates-select"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/users", { params: { query: { pageSize: 100 } } })),
  });
  const userOptions: [string, string][] = [
    ["", "— اختر الحساب الجديد —"],
    ...((usersQuery.data?.items ?? []).map((u) => [u.id, `${u.fullName} (${u.maskedPhone || u.email || ""})`] as [string, string])),
  ];

  const detail = useQuery({
    queryKey: ["admin-venue", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/{venueId}", { params: { path: { venueId: venueId! } } })),
  });
  const complaints = useQuery({
    queryKey: ["admin-venue-complaints", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/{venueId}/complaints", { params: { path: { venueId: venueId! } } })),
  });

  type Kind = "suspend" | "reactivate" | "ask" | "note" | "owner";
  const action = useMutation({
    mutationFn: ({ kind, text }: { kind: Kind; text: string }) => {
      const path = { venueId: venueId! };
      switch (kind) {
        case "suspend":
          return stepUp((headers) => unwrap(api.POST("/api/v1/admin/venues/{venueId}/suspend", { params: { path }, headers, body: { reason: text } })));
        case "reactivate":
          return unwrap(api.POST("/api/v1/admin/venues/{venueId}/reactivate", { params: { path }, body: { reason: text } }));
        case "ask":
          return unwrap(api.POST("/api/v1/admin/venues/{venueId}/ask-owner", { params: { path }, body: { reason: text } }));
        case "note":
          return unwrap(api.PUT("/api/v1/admin/venues/{venueId}/admin-note", { params: { path }, body: { note: text } }));
        default:
          return stepUp((headers) =>
            unwrap(api.POST("/api/v1/admin/venues/{venueId}/ownership", { params: { path }, headers, body: { newOwnerId: newOwner.trim(), reason: text } })),
          );
      }
    },
    onSuccess: async (_, { kind }) => {
      notice.ok({ suspend: "اتوقف الملعب.", reactivate: "الملعب رجع نشط.", ask: "اتبعت الطلب للمالك.", note: "اتحفظت الملاحظة.", owner: "اتنقلت الملكية." }[kind]);
      setNote(null);
      setNewOwner("");
      await queryClient.invalidateQueries({ queryKey: ["admin-venue"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-venues"] });
    },
    onError: notice.fail,
  });
  const ask = (kind: Kind, title: string, body: string, confirmLabel: string) =>
    setReason({ title, body, confirmLabel, onConfirm: (text) => action.mutate({ kind, text }) });

  return (
    <Drawer title="تفاصيل الملعب" open={!!venueId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(v) => (
          <>
            <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
              <strong className="text-[17px]">{v.name}</strong>
              <KV k="العنوان">{v.address}، {v.area}، {v.city}</KV>
              <KV k="المالك">{v.ownerName ?? v.ownerId}</KV>
              <KV k="الاعتماد"><Pill tone={TONE[v.approvalStatus] ?? "neutral"}>{STATUS_MAP[v.approvalStatus] ?? v.approvalStatus}</Pill></KV>
              <KV k="منشور">{v.isPublished ? "منشور" : "مسودة"}</KV>
              <KV k="الموظفين">{num(v.staffCount)}</KV>
            </div>
            <Section title="آخر ٣٠ يوم">
              <div className="grid grid-cols-3 gap-2 text-center text-sm">
                <Stat label="حجوزات" value={num(v.last30Days.total)} />
                <Stat label="مؤكدة/مكتملة" value={num(v.last30Days.confirmed + v.last30Days.completed)} />
                <Stat label="ملغاة/غياب" value={num(v.last30Days.cancelled + v.last30Days.noShow)} />
                <Stat label="قيمة المؤكد" value={money(v.last30Days.confirmedValue)} />
                <Stat label="مستنية دفع" value={num(v.last30Days.pending)} />
                <Stat label="آخر حجز" value={fmtDate(v.last30Days.lastBookingStartUtc)} />
              </div>
            </Section>
            <Section title={`الملاعب الفرعية (${num(v.pitches.length)})`}>
              {v.pitches.map((p) => (
                <div key={p.id} className="flex justify-between text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <span>{p.name} · {p.turfType} · {num(p.capacity)} لاعب</span>
                  <span className={muted}>{p.isActive ? "شغال" : "متوقف"}</span>
                </div>
              ))}
            </Section>
            <div className="flex flex-wrap gap-2">
              {v.approvalStatus === "Approved" && (
                <button type="button" className={btnDanger} onClick={() => ask("suspend", "إيقاف الملعب", "الملعب هيختفي من اللاعبين ومفيش حجز جديد. المالك هيوصله السبب.", "إيقاف")}>
                  إيقاف
                </button>
              )}
              {v.approvalStatus === "Suspended" && (
                <button type="button" className={btnOutline} onClick={() => ask("reactivate", "إعادة التفعيل", "الملعب هيرجع لحالة النشر اللي كان عليها.", "تفعيل")}>
                  إعادة التفعيل
                </button>
              )}
              <button type="button" className={btnSmall} onClick={() => ask("ask", "طلب معلومات من المالك", "اكتب المطلوب بالظبط. الحالة مش هتتغير.", "إرسال")}>
                طلب معلومات
              </button>
            </div>
            <Section title="ملاحظة داخلية (مش بتظهر للمالك)">
              <textarea
                aria-label="ملاحظة داخلية"
                value={note ?? v.adminNote ?? ""}
                onChange={(e) => setNote(e.target.value)}
                className={`${inputClass} min-h-[80px]`}
                maxLength={2000}
              />
              <button type="button" className={btnPrimary} disabled={note === null} onClick={() => action.mutate({ kind: "note", text: note ?? "" })}>
                حفظ الملاحظة
              </button>
            </Section>
            <Section title="نقل الملكية">
              <Field label="الحساب الجديد">
                <Select label="الحساب الجديد" value={newOwner} onChange={setNewOwner} options={userOptions} />
              </Field>
              <button
                type="button"
                className={btnOutline}
                disabled={!/^[0-9a-f-]{36}$/i.test(newOwner.trim())}
                onClick={() => ask("owner", "نقل الملكية", "الحساب الجديد هياخد دور مالك ملعب وهيوصله إشعار.", "نقل")}
              >
                نقل الملكية
              </button>
            </Section>
            <Section title="السجل">
              {v.history.map((e) => (
                <div key={`${e.action}-${e.occurredAtUtc}`} className="text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <strong>{e.action}</strong> <span className={muted}>{e.fromStatus} ← {e.toStatus} · {fmtDateTime(e.occurredAtUtc)}</span>
                  {e.message && <p className={muted}>{e.message}</p>}
                </div>
              ))}
            </Section>
          </>
        )}
      </QueryState>
      <Section title="الشكاوى">
        <QueryState query={complaints} isEmpty={(d) => d.length === 0} empty="مفيش شكاوى.">
          {(list) => (
            <>
              {list.map((c) => (
                <div key={c.ticketId} className="flex justify-between text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <span>{c.number} · {c.subject}</span>
                  <span className={muted}>{c.status} · {fmtDate(c.createdAtUtc)}</span>
                </div>
              ))}
            </>
          )}
        </QueryState>
      </Section>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-[#F6F7FA] rounded-xl p-2 flex flex-col gap-0.5">
      <span className={`text-[11px] ${muted}`}>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
