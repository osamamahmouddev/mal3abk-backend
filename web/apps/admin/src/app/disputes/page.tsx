"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, btnOutline, btnPrimary, btnSmall, card, inputClass, money, muted } from "@/components/ui";
import { Drawer, Pager, QueryState, Section, Select, Table, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";
import { ChevronLeft, LayoutGrid, Table2 } from "lucide-react";

type Row = Schemas["DisputeDto"];

const STATUSES: [string, string][] = [
  ["", "المفتوحة"],
  ["Open", "جديدة"],
  ["UnderReview", "تحت المراجعة"],
  ["Resolved", "محلولة"],
];
const DISPUTE_STATUS_MAP: Record<string, string> = {
  Open: "جديد",
  UnderReview: "تحت المراجعة",
  Resolved: "تمت التسوية",
};
const BOOKING_STATUS_MAP: Record<string, string> = {
  PendingDeposit: "بانتظار العربون",
  ProofSubmitted: "إيصال مرفوع",
  UnderReview: "تحت المراجعة",
  Confirmed: "مؤكد",
  Completed: "مكتمل",
  Cancelled: "ملغي",
  Rejected: "مرفوض",
  Expired: "منتهي",
  NoShow: "غياب",
};
const OUTCOMES: { value: string; title: string; effect: string }[] = [
  { value: "ConfirmBooking", title: "تأكيد الحجز", effect: "الدفع سليم: الحجز اللي مستني مراجعة الإيصال بيتأكد." },
  { value: "RefundPlayer", title: "رد العربون للاعب", effect: "الحجز النشط بيتلغى والعربون كامل بيتسجل مستحق للاعب." },
  { value: "FavorVenue", title: "لصالح الملعب", effect: "الإيصال المعلّق بيترفض، وغير كده الحجز مايتغيرش." },
  { value: "Dismiss", title: "رفض النزاع", effect: "مفيش أي تغيير على الحجز (مكرر أو اتسحب)." },
];
const SIDE: Record<string, string> = { Player: "اللاعب", Venue: "الملعب", System: "المراجع" };
const PAGE_SIZE = 25;

export default function DisputesPage() {
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");

  const disputes = useQuery({
    queryKey: ["admin-disputes", status, page],
    queryFn: () => unwrap(api.GET("/api/v1/admin/disputes", { params: { query: { status: status || undefined, page, pageSize: PAGE_SIZE } } })),
  });

  return (
    <Page>
      <PageHeader title="نزاعات الإيصالات" sub="الأقدم الأول. كل قرار محتاج سبب وتأكيد هوية، وبيوصل للطرفين." />
      
      <div className="flex flex-wrap gap-2.5 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex items-center gap-2.5 flex-1 min-w-[240px]">
          <div className="w-full sm:w-60 shrink-0">
            <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
          </div>
          {status && (
            <button
              type="button"
              onClick={() => {
                setStatus("");
                setPage(1);
              }}
              className="text-xs font-bold text-slate-500 hover:text-slate-800 px-3 py-1.5 rounded-lg hover:bg-slate-100 transition-colors cursor-pointer"
            >
              إعادة ضبط
            </button>
          )}
        </div>

        <div className="inline-flex rounded-xl bg-slate-100 p-1 border border-slate-200 shrink-0">
          <button
            type="button"
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
              viewMode === "cards" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("cards")}
          >
            <LayoutGrid className="size-3.5" />
            بطاقات
          </button>
          <button
            type="button"
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
              viewMode === "table" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("table")}
          >
            <Table2 className="size-3.5" />
            جدول
          </button>
        </div>
      </div>

      <QueryState
        query={disputes}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="لا توجد أي نزاعات معلقة"
        emptyDescription={status ? "لا توجد نزاعات بالحالة المحددة حالياً. يمكنك تغيير الفلتر لعرض البقية." : "ممتاز! لا توجد أي نزاعات إيصالات أو مدفوعات معلقة بين الملاعب واللاعبين حالياً."}
        emptyIcon="shield"
        emptyBadge="السجل سليم"
        emptyAction={
          status
            ? {
                label: "عرض كل النزاعات",
                onClick: () => {
                  setStatus("");
                  setPage(1);
                },
                variant: "outline",
              }
            : undefined
        }
      >
        {(data) => (
          <>
            {viewMode === "cards" ? (
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5 sm:gap-6">
                {data.items.map((r) => {
                  const isSelected = selected === r.id;
                  return (
                    <div
                      key={r.id}
                      onClick={() => setSelected(r.id)}
                      className={`rounded-2xl p-5 flex flex-col justify-between gap-4 border bg-white transition-all duration-200 cursor-pointer group hover:shadow-lg hover:border-slate-300 relative ${
                        isSelected
                          ? "border-ink ring-2 ring-ink/15 shadow-md bg-slate-50/40"
                          : "border-slate-200/90 shadow-[0_1px_3px_rgba(15,23,42,0.04)]"
                      }`}
                    >
                      {/* Top: Booking Ref & Dispute Status */}
                      <div className="flex items-center justify-between gap-2">
                        <div className="flex flex-col gap-0.5 min-w-0">
                          <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">رقم الحجز</span>
                          <strong dir="ltr" className="font-mono text-base font-extrabold text-slate-900 tracking-wider text-right truncate">
                            {r.bookingReference}
                          </strong>
                        </div>
                        <Pill tone={r.status === "Resolved" ? "ok" : "warn"}>
                          {DISPUTE_STATUS_MAP[r.status] ?? r.status}
                        </Pill>
                      </div>

                      {/* Dispute Details Box */}
                      <div className="flex flex-col gap-2 p-3 rounded-xl bg-slate-50/80 border border-slate-200/70 text-xs">
                        <div className="flex items-center justify-between">
                          <span className="text-slate-500 font-medium">الطرف الذي فتح النزاع</span>
                          <span className="font-bold text-slate-900">{SIDE[r.openedBy] ?? r.openedBy}</span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span className="text-slate-500 font-medium">مبلغ العربون</span>
                          <strong className="font-extrabold text-emerald-700">{money(r.depositAmount)}</strong>
                        </div>
                        <div className="flex items-center justify-between">
                          <span className="text-slate-500 font-medium">حالة الحجز الحالية</span>
                          <span className="font-semibold text-slate-900">{BOOKING_STATUS_MAP[r.bookingStatus] ?? r.bookingStatus}</span>
                        </div>
                      </div>

                      {/* Footer */}
                      <div className="w-full pt-1 border-t border-slate-100 flex items-center justify-between gap-2 text-xs">
                        <span className="text-[11px] text-slate-500 font-semibold">تاريخ الفتح: {fmtDateTime(r.createdAtUtc)}</span>
                        <div className="py-1.5 px-3 rounded-lg bg-slate-50 group-hover:bg-slate-100 border border-slate-200/70 text-slate-700 group-hover:text-slate-900 font-bold flex items-center gap-1 transition-colors">
                          <span>الفصل في النزاع</span>
                          <ChevronLeft className="size-3.5 group-hover:-translate-x-0.5 transition-transform" />
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <Table<Row>
                caption="النزاعات"
                rows={data.items}
                rowKey={(r) => r.id}
                selected={selected}
                onSelect={(r) => setSelected(r.id)}
                columns={[
                  { title: "الحجز", cell: (r) => <strong dir="ltr" className="font-inter">{r.bookingReference}</strong> },
                  { title: "فتحه", cell: (r) => SIDE[r.openedBy] ?? r.openedBy },
                  { title: "العربون", cell: (r) => money(r.depositAmount) },
                  { title: "حالة الحجز", cell: (r) => BOOKING_STATUS_MAP[r.bookingStatus] ?? r.bookingStatus },
                  { title: "النزاع", cell: (r) => <Pill tone={r.status === "Resolved" ? "ok" : "warn"}>{DISPUTE_STATUS_MAP[r.status] ?? r.status}</Pill> },
                  { title: "اتفتح", cell: (r) => fmtDateTime(r.createdAtUtc) },
                ]}
              />
            )}
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <DisputeDrawer disputeId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function DisputeDrawer({ disputeId, onClose }: { disputeId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [outcome, setOutcome] = useState("");
  const [decisionNote, setDecisionNote] = useState("");
  const [evidence, setEvidence] = useState("");

  const detail = useQuery({
    queryKey: ["admin-dispute", disputeId],
    enabled: !!disputeId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/disputes/{disputeId}", { params: { path: { disputeId: disputeId! } } })),
  });
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ["admin-dispute"] });
    await queryClient.invalidateQueries({ queryKey: ["admin-disputes"] });
  };
  const path = { disputeId: disputeId ?? "" };
  const take = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/admin/disputes/{disputeId}/review", { params: { path } })),
    onSuccess: async () => { notice.ok("النزاع بقى تحت مراجعتك."); await refresh(); },
    onError: notice.fail,
  });
  const addNote = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/admin/disputes/{disputeId}/notes", { params: { path }, body: { note: evidence, proofId: null } })),
    onSuccess: async () => { setEvidence(""); notice.ok("اتضافت الملاحظة."); await refresh(); },
    onError: notice.fail,
  });
  const resolve = useMutation({
    mutationFn: () =>
      stepUp((headers) => unwrap(api.POST("/api/v1/admin/disputes/{disputeId}/resolve", { params: { path }, headers, body: { outcome, note: decisionNote } }))),
    onSuccess: async () => { notice.ok("اتسجل القرار ووصل للطرفين."); setOutcome(""); setDecisionNote(""); await refresh(); },
    onError: notice.fail,
  });

  return (
    <Drawer title="تفاصيل النزاع" open={!!disputeId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(d) => (
          <>
            <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
              <KV k="الحجز"><span dir="ltr" className="font-inter">{d.bookingReference}</span> ({BOOKING_STATUS_MAP[d.bookingStatus] ?? d.bookingStatus})</KV>
              <KV k="العربون">{money(d.depositAmount)}</KV>
              <KV k="فتحه">{SIDE[d.openedBy] ?? d.openedBy} · {fmtDateTime(d.createdAtUtc)}</KV>
              <KV k="الحالة"><Pill tone={d.status === "Resolved" ? "ok" : "warn"}>{DISPUTE_STATUS_MAP[d.status] ?? d.status}</Pill>{d.outcome ? ` — ${OUTCOMES.find(o => o.value === d.outcome)?.title ?? d.outcome}` : ""}</KV>
              <p className="text-sm leading-[1.7] bg-[#F6F7FA] rounded-xl p-3">{d.reason}</p>
              {d.resolutionNote && <KV k="سبب القرار">{d.resolutionNote}</KV>}
            </div>
            <Section title="الأدلة">
              {d.evidence.length === 0 && <span className={`text-sm ${muted}`}>مفيش أدلة لسه.</span>}
              {d.evidence.map((e) => (
                <div key={e.id} className="text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <strong>{SIDE[e.side] ?? e.side}</strong> <span className={muted}>· {fmtDateTime(e.createdAtUtc)}</span>
                  {e.note && <p>{e.note}</p>}
                  {e.proofId && <p className={muted}>إيصال مرفق ({e.proofId.slice(0, 8)})</p>}
                </div>
              ))}
            </Section>
            {d.status !== "Resolved" && (
              <>
                {d.status === "Open" && (
                  <button type="button" className={btnSmall} onClick={() => take.mutate()}>
                    خدها للمراجعة
                  </button>
                )}
                <Section title="ملاحظة مراجع">
                  <textarea aria-label="ملاحظة مراجع" value={evidence} onChange={(e) => setEvidence(e.target.value)} className={`${inputClass} min-h-[70px]`} maxLength={2000} />
                  <button type="button" className={btnOutline} disabled={!evidence.trim()} onClick={() => addNote.mutate()}>
                    إضافة ملاحظة
                  </button>
                </Section>
                <Section title="القرار">
                  <div className="flex flex-col gap-2" role="radiogroup" aria-label="القرار">
                    {OUTCOMES.map((o) => (
                      <label key={o.value} className={`rounded-xl border p-3 flex gap-2 cursor-pointer ${outcome === o.value ? "border-brand-red bg-[#FDF3F2]" : "border-[#E2E5EC]"}`}>
                        <input type="radio" name="outcome" checked={outcome === o.value} onChange={() => setOutcome(o.value)} />
                        <span className="flex flex-col">
                          <strong className="text-sm">{o.title}</strong>
                          <span className={`text-xs ${muted}`}>{o.effect}</span>
                        </span>
                      </label>
                    ))}
                  </div>
                  <textarea aria-label="سبب القرار" placeholder="سبب القرار (بيوصل للطرفين)" value={decisionNote} onChange={(e) => setDecisionNote(e.target.value)} className={`${inputClass} min-h-[70px]`} maxLength={2000} />
                  <button type="button" className={btnPrimary} disabled={!outcome || decisionNote.trim().length < 5 || resolve.isPending} onClick={() => resolve.mutate()}>
                    تسجيل القرار
                  </button>
                </Section>
              </>
            )}
          </>
        )}
      </QueryState>
    </Drawer>
  );
}
