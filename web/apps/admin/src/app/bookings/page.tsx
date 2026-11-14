"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnDanger, card, money, muted } from "@/components/ui";
import { Drawer, Pager, QueryState, Section, Select, Table, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

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

  const bookings = useQuery({
    queryKey: ["admin-bookings", reference, status, page],
    queryFn: () =>
      unwrap(api.GET("/api/v1/admin/bookings", { params: { query: { reference: reference || undefined, status: status || undefined, page, pageSize: PAGE_SIZE } } })),
  });

  return (
    <Page>
      <PageHeader title="الحجوزات" sub="كل الحجوزات في كل الملاعب. الإلغاء الإجباري بيرجّع العربون كامل للاعب وبيتسجل في التدقيق." />
      <div className="flex flex-wrap gap-2">
        <div className="flex-[1_1_260px]">
          <SearchBox value={reference} onChange={(v) => { setReference(v); setPage(1); }} placeholder="رقم الحجز (MAL-…)" />
        </div>
        <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
      </div>
      <QueryState query={bookings} isEmpty={(d) => d.items.length === 0} empty="مفيش حجوزات بالبحث ده.">
        {(data) => (
          <>
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
