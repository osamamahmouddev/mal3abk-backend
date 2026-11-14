"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnDanger, card, money, muted } from "@/components/ui";
import { Drawer, Pager, QueryState, Section, Select, Table, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";
import { CalendarDays, ChevronLeft, LayoutGrid, NotebookPen, Smartphone, Table2 } from "lucide-react";

type Row = Schemas["AdminBookingListItem"];

const STATUSES: [string, string][] = [
  ["", "كل الحالات"],
  ["PendingDeposit", "مستني العربون"],
  ["ProofSubmitted", "إيصال مرفوع"],
  ["UnderReview", "تحت المراجعة"],
  ["Confirmed", "مؤكد"],
  ["Completed", "مكتمل"],
  ["Cancelled", "ملغي"],
  ["Rejected", "مرفوض"],
  ["Expired", "منتهي"],
  ["NoShow", "غياب"],
];
const STATUS_MAP = Object.fromEntries(STATUSES);
const TONE: Record<string, "ok" | "warn" | "bad" | "neutral"> = {
  Confirmed: "ok",
  Completed: "ok",
  PendingDeposit: "warn",
  ProofSubmitted: "warn",
  UnderReview: "warn",
  Cancelled: "bad",
  Rejected: "bad",
  NoShow: "bad",
  Expired: "neutral",
};
const ACTIVE = new Set(["PendingDeposit", "ProofSubmitted", "UnderReview", "Confirmed"]);
const PAGE_SIZE = 25;

export default function BookingsPage() {
  const [reference, setReference] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");

  const bookings = useQuery({
    queryKey: ["admin-bookings", reference, status, page],
    queryFn: () =>
      unwrap(api.GET("/api/v1/admin/bookings", { params: { query: { reference: reference || undefined, status: status || undefined, page, pageSize: PAGE_SIZE } } })),
  });

  return (
    <Page>
      <PageHeader title="الحجوزات" sub="كل الحجوزات في كل الملاعب. الإلغاء الإجباري بيرجّع العربون كامل للاعب وبيتسجل في التدقيق." />
      
      <div className="flex flex-wrap gap-2.5 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap gap-2 flex-1 min-w-[280px]">
          <div className="flex-[1_1_240px]">
            <SearchBox value={reference} onChange={(v) => { setReference(v); setPage(1); }} placeholder="رقم الحجز (MAL-…)" />
          </div>
          <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
        </div>

        <div className="inline-flex rounded-xl bg-slate-100 p-1 border border-slate-200">
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
        query={bookings}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="لم يتم العثور على أي حجوزات"
        emptyDescription={reference || status ? "لا توجد حجوزات تطابق رقم المرجع أو الحالة المحددة. يمكنك تعديل البحث لعرض باقي الحجوزات." : "لا توجد حجوزات مسجلة على المنصة حتى الآن."}
        emptyIcon={reference || status ? "search" : "calendar"}
        emptyAction={
          reference || status
            ? {
                label: "إعادة ضبط البحث والفلاتر",
                onClick: () => {
                  setReference("");
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
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                {data.items.map((r) => {
                  const isSelected = selected === r.id;
                  return (
                    <div
                      key={r.id}
                      onClick={() => setSelected(r.id)}
                      className={`${card} rounded-[20px] p-5 flex flex-col justify-between gap-4 border transition-all cursor-pointer hover:border-slate-400 hover:shadow-md active:scale-[0.99] ${
                        isSelected ? "border-brand-red ring-2 ring-brand-red/20 bg-[#FDFDFE]" : "border-[#E2E6EF]"
                      }`}
                    >
                      {/* Top Header: Reference & Badges */}
                      <div className="flex items-start justify-between gap-3">
                        <div className="flex flex-col gap-1 min-w-0">
                          <span className="text-[11px] font-semibold text-[#5B6475]">رقم الحجز</span>
                          <strong dir="ltr" className="font-inter text-base font-bold text-slate-900 tracking-wide text-right truncate">
                            {r.reference}
                          </strong>
                        </div>
                        <div className="flex flex-col items-end gap-1.5 shrink-0">
                          <Pill tone={TONE[r.status] ?? "neutral"}>
                            {STATUS_MAP[r.status] ?? r.status}
                          </Pill>
                          <span className="text-[11px] px-2 py-0.5 rounded-md font-semibold bg-slate-100 text-slate-600 inline-flex items-center gap-1">
                            {r.isManual ? <NotebookPen className="size-3" /> : <Smartphone className="size-3" />}
                            {r.isManual ? "يدوي" : "أونلاين"}
                          </span>
                        </div>
                      </div>

                      {/* Center: Timing badge */}
                      <div className="flex items-center gap-3 p-3 rounded-xl bg-[#F6F8FB] border border-[#E9EDF5]">
                        <span className="grid place-items-center size-9 rounded-lg bg-white border border-[#E9EDF5] text-slate-600 shrink-0"><CalendarDays className="size-[18px]" /></span>
                        <div className="flex flex-col">
                          <span className="text-xs text-[#5B6475]">تاريخ وميعاد الحجز</span>
                          <span className="text-sm font-bold text-slate-800">{fmtDateTime(r.startUtc)}</span>
                        </div>
                      </div>

                      {/* Bottom: Financials & Action */}
                      <div className="flex flex-col gap-2 pt-2 border-t border-[#F0F2F6]">
                        <div className="grid grid-cols-2 gap-2 text-xs">
                          <div className="flex flex-col">
                            <span className={muted}>السعر الإجمالي</span>
                            <strong className="text-sm font-bold text-emerald-700">{money(r.totalPrice)}</strong>
                          </div>
                          <div className="flex flex-col text-left">
                            <span className={muted}>العربون المدفوع</span>
                            <strong className="text-sm font-bold text-slate-900">{money(r.depositAmount)}</strong>
                          </div>
                        </div>

                        <div className="flex items-center justify-between text-xs text-brand-red font-bold pt-2 border-t border-dashed border-[#EEF0F5]">
                          <span>عرض تفاصيل الحجز والإلغاء</span>
                          <ChevronLeft className="size-4" />
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <Table<Row>
                caption="الحجوزات"
                rows={data.items}
                rowKey={(r) => r.id}
                selected={selected}
                onSelect={(r) => setSelected(r.id)}
                columns={[
                  { title: "الرقم", cell: (r) => <strong dir="ltr" className="font-inter">{r.reference}</strong> },
                  { title: "الميعاد", cell: (r) => fmtDateTime(r.startUtc) },
                  { title: "السعر", cell: (r) => money(r.totalPrice) },
                  { title: "العربون", cell: (r) => money(r.depositAmount) },
                  { title: "النوع", cell: (r) => (r.isManual ? "يدوي" : "أونلاين") },
                  { title: "الحالة", cell: (r) => <Pill tone={TONE[r.status] ?? "neutral"}>{STATUS_MAP[r.status] ?? r.status}</Pill> },
                ]}
              />
            )}
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <BookingDrawer bookingId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function BookingDrawer({ bookingId, onClose }: { bookingId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);

  const detail = useQuery({
    queryKey: ["admin-booking", bookingId],
    enabled: !!bookingId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/bookings/{bookingId}", { params: { path: { bookingId: bookingId! } } })),
  });
  const cancel = useMutation({
    mutationFn: (text: string) =>
      stepUp((headers) => unwrap(api.POST("/api/v1/admin/bookings/{bookingId}/force-cancel", { params: { path: { bookingId: bookingId! } }, headers, body: { reason: text } }))),
    onSuccess: async () => {
      notice.ok("اتلغى الحجز والعربون اتسجل مستحق للاعب.");
      await queryClient.invalidateQueries({ queryKey: ["admin-booking"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-bookings"] });
    },
    onError: notice.fail,
  });

  return (
    <Drawer title="تفاصيل الحجز" open={!!bookingId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(d) => (
          <>
            <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
              <strong dir="ltr" className="font-inter text-[17px] text-right">{d.booking.reference}</strong>
              <KV k="الحالة"><Pill tone={TONE[d.booking.status] ?? "neutral"}>{STATUS_MAP[d.booking.status] ?? d.booking.status}</Pill></KV>
              <KV k="الميعاد">{fmtDateTime(d.booking.startUtc)}</KV>
              <KV k="السعر / العربون">{money(d.booking.totalPrice)} / {money(d.booking.depositAmount)}</KV>
              <KV k="الملعب">{d.booking.venueId}</KV>
              <KV k="اللاعب">{d.customerName ?? d.booking.playerId}</KV>
              {d.cancellationReason && <KV k="سبب الإلغاء">{d.cancellationReason}</KV>}
              {d.rejectionReason && <KV k="سبب الرفض">{d.rejectionReason}</KV>}
              {d.policy && <KV k="سياسة الإلغاء">قبلها بـ {d.policy.cancellationDeadlineHours} ساعة · استرداد {d.policy.refundPercentage}٪</KV>}
            </div>
            {ACTIVE.has(d.booking.status) && (
              <button
                type="button"
                className={btnDanger}
                onClick={() =>
                  setReason({
                    title: "إلغاء إجباري",
                    body: "الحجز هيتلغى حتى لو قرب ميعاده، والعربون كامل هيتسجل مستحق للاعب. اللاعب هيشوف السبب.",
                    confirmLabel: "إلغاء الحجز",
                    onConfirm: (text) => cancel.mutate(text),
                  })
                }
              >
                إلغاء إجباري
              </button>
            )}
            <Section title="تاريخ الحالة">
              {d.history.map((h) => (
                <div key={`${h.toStatus}-${h.changedAtUtc}`} className="text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <strong>{h.fromStatus} ← {h.toStatus}</strong> <span className={muted}>· {fmtDateTime(h.changedAtUtc)}</span>
                  {h.reason && <p className={muted}>{h.reason}</p>}
                </div>
              ))}
            </Section>
            <Section title="الإيصالات">
              {d.proofs.length === 0 && <span className={`text-sm ${muted}`}>مفيش إيصالات.</span>}
              {d.proofs.map((p) => (
                <div key={p.id} className="text-[13px] border-b border-[#EEF0F5] py-1.5 flex justify-between">
                  <span>{fmtDateTime(p.uploadedAtUtc)} · {p.isScanClean ? "فحص سليم" : "مستني الفحص"}</span>
                  <span className={muted}>{p.isApproved === null ? "لسه" : p.isApproved ? "مقبول" : `مرفوض: ${p.rejectionReason ?? ""}`}</span>
                </div>
              ))}
            </Section>
            <Section title="الاسترداد">
              {d.refunds.length === 0 && <span className={`text-sm ${muted}`}>مفيش.</span>}
              {d.refunds.map((r) => (
                <div key={r.id} className="text-[13px] flex justify-between">
                  <span>{r.status} · متوقع {money(r.expectedAmount)}</span>
                  <span className={muted}>{r.actualAmount !== null && r.actualAmount !== undefined ? `اتدفع ${money(r.actualAmount)}` : ""}</span>
                </div>
              ))}
            </Section>
          </>
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}
