"use client";

import React, { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnDanger, card, money, muted, toArabicDigits } from "@/components/ui";
import { Drawer, Pager, QueryState, Section, Select, Table, fmtDateTime, useNotice, useListView } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";
import { Building2, CalendarDays, CheckCircle2, ChevronLeft, Clock, ExternalLink, FileText, LayoutGrid, NotebookPen, Phone, ReceiptText, RotateCw, ShieldAlert, Smartphone, Table2, TriangleAlert, User, XCircle, ZoomIn, ZoomOut } from "lucide-react";

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
  const [viewMode, setViewMode] = useListView();

  const venuesQuery = useQuery({
    queryKey: ["admin-venues-lookup"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues", { params: { query: { pageSize: 100 } } })),
    staleTime: 5 * 60 * 1000,
  });

  const venueMap = React.useMemo(() => {
    const map = new Map<string, string>();
    for (const v of venuesQuery.data?.items ?? []) {
      map.set(v.id, v.name);
    }
    return map;
  }, [venuesQuery.data]);

  const bookings = useQuery({
    queryKey: ["admin-bookings", reference, status, page],
    queryFn: () =>
      unwrap(api.GET("/api/v1/admin/bookings", { params: { query: { reference: reference || undefined, status: status || undefined, page, pageSize: PAGE_SIZE } } })),
  });

  return (
    <Page>
      <PageHeader title="الحجوزات" sub="كل الحجوزات في كل الملاعب. الإلغاء الإجباري بيرجّع العربون كامل للاعب وبيتسجل في التدقيق." />
      
      <div className="flex flex-wrap gap-2.5 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap items-center gap-2.5 flex-1 min-w-[280px]">
          <div className="w-full sm:w-80 shrink-0">
            <SearchBox value={reference} onChange={(v) => { setReference(v); setPage(1); }} placeholder="رقم الحجز (MLB-…)" />
          </div>
          <div className="w-40 sm:w-48 shrink-0">
            <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
          </div>
          {(reference || status) && (
            <button
              type="button"
              onClick={() => {
                setReference("");
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
                      {/* Top Header: Reference & Badges */}
                      <div className="flex items-center justify-between gap-3">
                        <div className="flex flex-col gap-0.5 min-w-0">
                          <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">رقم الحجز</span>
                          <span dir="ltr" className="font-mono text-sm font-extrabold text-slate-900 tracking-wider">
                            {r.reference}
                          </span>
                        </div>
                        <div className="flex items-center gap-1.5 shrink-0">
                          <span
                            className={`inline-flex items-center gap-1 text-[11px] px-2 py-0.5 rounded-md font-bold ${
                              r.isManual ? "bg-amber-50 text-amber-700 border border-amber-200/60" : "bg-sky-50 text-sky-700 border border-sky-200/60"
                            }`}
                          >
                            {r.isManual ? <NotebookPen className="w-3 h-3" /> : <Smartphone className="w-3 h-3" />}
                            <span>{r.isManual ? "يدوي" : "أونلاين"}</span>
                          </span>
                          <Pill tone={r.isManual && r.status === "PendingDeposit" ? "neutral" : (TONE[r.status] ?? "neutral")}>
                            {r.isManual && r.status === "PendingDeposit" ? "محجوز مبدئي" : (STATUS_MAP[r.status] ?? r.status)}
                          </Pill>
                        </div>
                      </div>

                      {/* Venue and Pitch Info */}
                      <div className="p-3 rounded-xl bg-slate-50/80 border border-slate-200/70 flex items-center justify-between gap-3">
                        <div className="flex items-center gap-2.5 min-w-0">
                          <div className="w-9 h-9 rounded-xl bg-white border border-slate-200/80 text-slate-700 flex items-center justify-center shadow-2xs shrink-0">
                            <Building2 className="w-4 h-4 text-slate-600" />
                          </div>
                          <div className="flex flex-col min-w-0">
                            <span className="text-[10px] text-slate-500 font-semibold">الملعب الرياضي</span>
                            <span className="text-xs font-bold text-slate-900 truncate" title={(r as any).venueName || venueMap.get(r.venueId) || "ملعب"}>
                              {(r as any).venueName || venueMap.get(r.venueId) || "ملعب"}
                            </span>
                          </div>
                        </div>
                        {Boolean((r as any).pitchName) && (
                          <span className="text-[11px] font-semibold text-slate-700 bg-white px-2 py-1 rounded-lg border border-slate-200 shadow-2xs shrink-0 max-w-[130px] truncate" title={(r as any).pitchName}>
                            {(r as any).pitchName}
                          </span>
                        )}
                      </div>

                      {/* Customer / Player Info */}
                      <div className="flex items-center justify-between gap-2 px-1">
                        <div className="flex items-center gap-2 min-w-0">
                          <div className="w-6 h-6 rounded-full bg-slate-100 border border-slate-200 flex items-center justify-center text-slate-600 shrink-0">
                            <User className="w-3.5 h-3.5 text-slate-600" />
                          </div>
                          <span className="text-xs font-bold text-slate-900 truncate">
                            {r.customerName || (r.isManual ? "حجز يدوي" : "لاعب مسجل")}
                          </span>
                        </div>
                        {(r as any).customerPhone ? (
                          <span className="text-[11px] font-mono font-semibold text-slate-600 bg-slate-50 px-2 py-0.5 rounded-md border border-slate-200/70 shrink-0" dir="ltr">
                            {(r as any).customerPhone}
                          </span>
                        ) : (
                          <span className="text-[11px] text-slate-400 font-medium">بدون هاتف</span>
                        )}
                      </div>

                      {/* Timing / Schedule */}
                      <div className="p-3 rounded-xl bg-slate-50/80 border border-slate-200/70 flex items-center gap-3">
                        <div className="w-9 h-9 rounded-xl bg-white border border-slate-200/80 text-slate-700 flex items-center justify-center shadow-2xs shrink-0">
                          <CalendarDays className="w-4 h-4 text-slate-600" />
                        </div>
                        <div className="flex flex-col">
                          <span className="text-[10px] text-slate-500 font-semibold">تاريخ وموعد المباراة</span>
                          <span className="text-xs font-extrabold text-slate-900">
                            {fmtDateTime(r.startUtc)}
                          </span>
                        </div>
                      </div>

                      {/* Financials Breakdown */}
                      <div className="pt-2 border-t border-slate-100 flex items-center justify-between gap-3 text-xs">
                        <div className="flex flex-col">
                          <span className="text-[10px] font-semibold text-slate-500">إجمالي الحجز</span>
                          <span className="text-sm font-extrabold text-slate-900">{money(r.totalPrice)}</span>
                        </div>
                        <div className="flex flex-col text-left items-end">
                          <span className="text-[10px] font-semibold text-slate-500">العربون المدفوع</span>
                          <div className="flex items-center gap-1.5">
                            <span className="text-sm font-extrabold text-emerald-700">{money(r.depositAmount)}</span>
                            {r.depositAmount >= r.totalPrice && r.totalPrice > 0 ? (
                              <span className="text-[10px] font-bold text-emerald-700 bg-emerald-50 px-1.5 py-0.2 rounded border border-emerald-200">
                                بالكامل
                              </span>
                            ) : null}
                          </div>
                        </div>
                      </div>

                      {/* Action Footer */}
                      <div className="w-full pt-1">
                        <div className="w-full py-2 px-3 rounded-xl bg-slate-50 group-hover:bg-slate-100/90 border border-slate-200/70 text-slate-700 group-hover:text-slate-900 font-bold text-xs flex items-center justify-between transition-colors">
                          <span className="flex items-center gap-1.5">
                            <FileText className="w-3.5 h-3.5 text-slate-500 group-hover:text-slate-800 transition-colors" />
                            <span>عرض تفاصيل الحجز</span>
                          </span>
                          <ChevronLeft className="w-4 h-4 text-slate-400 group-hover:text-slate-700 group-hover:-translate-x-0.5 transition-transform" />
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
                  {
                    title: "الملعب",
                    cell: (r) => (
                      <div className="flex flex-col">
                        <span className="font-bold text-slate-900">{(r as any).venueName || venueMap.get(r.venueId) || "—"}</span>
                        {Boolean((r as any).pitchName) && <span className="text-[11px] text-slate-500">{(r as any).pitchName}</span>}
                      </div>
                    ),
                  },
                  {
                    title: "اللاعب",
                    cell: (r) => (
                      <div className="flex flex-col">
                        <span className="font-medium text-slate-900">{r.customerName || (r.isManual ? "حجز يدوي" : "لاعب مسجل")}</span>
                        {(r as any).customerPhone && (
                          <span className="text-[11px] text-slate-500 font-mono" dir="ltr">
                            {(r as any).customerPhone}
                          </span>
                        )}
                      </div>
                    ),
                  },
                  { title: "الميعاد", cell: (r) => fmtDateTime(r.startUtc) },
                  { title: "السعر", cell: (r) => money(r.totalPrice) },
                  { title: "العربون", cell: (r) => money(r.depositAmount) },
                  { title: "النوع", cell: (r) => (r.isManual ? "يدوي" : "أونلاين") },
                  { title: "الحالة", cell: (r) => <Pill tone={r.isManual && r.status === "PendingDeposit" ? "neutral" : (TONE[r.status] ?? "neutral")}>{r.isManual && r.status === "PendingDeposit" ? "محجوز مبدئي" : (STATUS_MAP[r.status] ?? r.status)}</Pill> },
                ]}
              />
            )}
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <BookingDrawer bookingId={selected} onClose={() => setSelected(null)} venueMap={venueMap} />
    </Page>
  );
}

/** Media URLs returned by the API are relative to the API host; load them through this app's authenticated BFF. */
function mediaSrc(path: string) {
  if (!path) return "";
  if (/^https?:\/\//i.test(path)) return path;
  if (path.startsWith("/admin/bff")) return path;
  return `/admin/bff${path.startsWith("/") ? "" : "/"}${path}`;
}

/** Receipt preview: images zoom (buttons, wheel) and pan (drag), rotate; PDFs open in the browser's own viewer. */
function ReceiptViewer({ proof }: { proof: any }) {
  const [scale, setScale] = useState(1);
  const [rotation, setRotation] = useState(0);
  const [offset, setOffset] = useState({ x: 0, y: 0 });
  const drag = useRef<{ x: number; y: number } | null>(null);

  const rawUrl = proof.downloadUrl || proof.url;
  const src = rawUrl ? mediaSrc(rawUrl) : null;
  const isPdf = proof.contentType === "application/pdf";

  useEffect(() => {
    setScale(1);
    setRotation(0);
    setOffset({ x: 0, y: 0 });
  }, [proof.id]);

  if (!src) {
    return (
      <div className="aspect-[3/4] max-h-[360px] rounded-2xl bg-slate-100 border border-slate-200 flex flex-col items-center justify-center gap-2 text-slate-500 text-xs">
        <FileText className="size-8 text-slate-400" />
        <span>رابط معاينة الإيصال مش متاح حالياً.</span>
      </div>
    );
  }

  const zoom = (delta: number) => setScale((s) => Math.min(5, Math.max(1, Math.round((s + delta) * 10) / 10)));
  const rotate = () => setRotation((r) => (r + 90) % 360);

  return (
    <div className="flex flex-col gap-2">
      <div
        className="aspect-[3/4] max-h-[380px] rounded-2xl border border-slate-200/90 bg-slate-900/5 overflow-hidden relative touch-none select-none flex items-center justify-center shadow-inner"
        onWheel={(e) => !isPdf && zoom(e.deltaY < 0 ? 0.25 : -0.25)}
        onPointerDown={(e) => {
          if (isPdf || scale === 1) return;
          drag.current = { x: e.clientX - offset.x, y: e.clientY - offset.y };
          (e.target as HTMLElement).setPointerCapture(e.pointerId);
        }}
        onPointerMove={(e) => drag.current && setOffset({ x: e.clientX - drag.current.x, y: e.clientY - drag.current.y })}
        onPointerUp={() => (drag.current = null)}
      >
        {isPdf ? (
          <a
            href={src}
            target="_blank"
            rel="noopener noreferrer"
            className="w-full h-full flex flex-col items-center justify-center gap-2.5 text-slate-800 hover:text-emerald-700 transition-colors p-4 text-center"
          >
            <div className="size-14 rounded-2xl bg-white shadow-xs border border-slate-200 flex items-center justify-center text-red-600">
              <FileText className="size-8" strokeWidth={1.5} />
            </div>
            <span className="text-sm font-bold underline">فتح إيصال PDF في نافذة جديدة</span>
            {proof.originalFileName && <span className="text-xs text-slate-500">{proof.originalFileName}</span>}
          </a>
        ) : (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={src}
            alt="صورة إيصال التحويل"
            draggable={false}
            className="w-full h-full object-contain transition-transform duration-75"
            style={{
              transform: `translate(${offset.x}px, ${offset.y}px) rotate(${rotation}deg) scale(${scale})`,
              cursor: scale > 1 ? "grab" : "default",
            }}
          />
        )}
      </div>

      <div className="flex items-center gap-1.5 flex-wrap">
        {!isPdf && (
          <>
            <button
              type="button"
              onClick={() => zoom(0.5)}
              title="تكبير"
              className="flex-1 min-h-[36px] px-2 rounded-xl border border-slate-200 bg-white hover:bg-slate-50 text-slate-700 text-xs font-semibold flex items-center justify-center gap-1 transition-colors cursor-pointer"
            >
              <ZoomIn className="size-3.5" />
              <span>تكبير +</span>
            </button>
            <button
              type="button"
              onClick={() => {
                setScale(1);
                setOffset({ x: 0, y: 0 });
              }}
              title="الحجم الأصلي"
              className="min-h-[36px] px-3 rounded-xl border border-slate-200 bg-white hover:bg-slate-50 text-slate-700 text-xs font-semibold transition-colors cursor-pointer"
            >
              {toArabicDigits(Math.round(scale * 100))}٪
            </button>
            <button
              type="button"
              onClick={() => zoom(-0.5)}
              title="تصغير"
              className="flex-1 min-h-[36px] px-2 rounded-xl border border-slate-200 bg-white hover:bg-slate-50 text-slate-700 text-xs font-semibold flex items-center justify-center gap-1 transition-colors cursor-pointer"
            >
              <ZoomOut className="size-3.5" />
              <span>تصغير −</span>
            </button>
            <button
              type="button"
              onClick={rotate}
              title="تدوير 90 درجة"
              className="min-h-[36px] px-3 rounded-xl border border-slate-200 bg-white hover:bg-slate-50 text-slate-700 text-xs font-semibold flex items-center justify-center gap-1 transition-colors cursor-pointer"
            >
              <RotateCw className="size-3.5" />
              <span>تدوير</span>
            </button>
          </>
        )}
        <a
          href={src}
          target="_blank"
          rel="noopener noreferrer"
          className="flex-1 min-h-[36px] px-2.5 rounded-xl border border-slate-200 bg-white hover:bg-slate-50 text-slate-800 text-xs font-semibold flex items-center justify-center gap-1 transition-colors"
        >
          <ExternalLink className="size-3.5" />
          <span>فتح بحجم كامل</span>
        </a>
      </div>
    </div>
  );
}

function BookingDrawer({ bookingId, onClose, venueMap }: { bookingId: string | null; onClose: () => void; venueMap?: Map<string, string> }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [selectedProofIdx, setSelectedProofIdx] = useState(0);

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

  const confirmDeposit = useMutation({
    mutationFn: () =>
      unwrap(
        (api.POST as any)("/api/v1/admin/bookings/{bookingId}/review-deposit", {
          params: { path: { bookingId: bookingId! } },
          body: { approved: true },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تم تأكيد استلام العربون وتثبيت الحجز بنجاح!");
      await queryClient.invalidateQueries({ queryKey: ["admin-booking"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-bookings"] });
    },
    onError: (e: any) => notice.fail(e?.message ?? "فشل تأكيد العربون"),
  });

  const rejectDeposit = useMutation({
    mutationFn: (text: string) =>
      unwrap(
        (api.POST as any)("/api/v1/admin/bookings/{bookingId}/review-deposit", {
          params: { path: { bookingId: bookingId! } },
          body: { approved: false, rejectionReason: text },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تم رفض العربون وإشعار اللاعب بنجاح.");
      await queryClient.invalidateQueries({ queryKey: ["admin-booking"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-bookings"] });
    },
    onError: (e: any) => notice.fail(e?.message ?? "فشل رفض العربون"),
  });

  const processRefund = useMutation({
    mutationFn: (refundId: string) =>
      unwrap(
        (api.POST as any)("/api/v1/admin/bookings/refunds/{refundId}/process", {
          params: { path: { refundId } },
          body: { notes: "تم تحويل العربون للاعب من الإدارة" },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تم تسجيل إتمام رد العربون للاعب بنجاح.");
      await queryClient.invalidateQueries({ queryKey: ["admin-booking"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-bookings"] });
    },
    onError: (e: any) => notice.fail(e?.message ?? "فشل تسجيل الاسترداد"),
  });

  return (
    <Drawer title="تفاصيل الحجز" open={!!bookingId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(d) => {
          const selectedProof = d.proofs[selectedProofIdx] || d.proofs[0];
          const hasProofs = d.proofs.length > 0;
          const isDepositPending =
            !d.booking.isManual &&
            (d.booking.depositAmount ?? 0) > 0 &&
            (d.booking.status === "PendingDeposit" || d.booking.status === "ProofSubmitted" || d.booking.status === "UnderReview");

          return (
            <>
              <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
                <div className="flex items-center justify-between">
                  <strong dir="ltr" className="font-inter text-[17px] text-right font-extrabold text-slate-900">{d.booking.reference}</strong>
                  <Pill tone={d.booking.isManual && d.booking.status === "PendingDeposit" ? "neutral" : (TONE[d.booking.status] ?? "neutral")}>
                    {d.booking.isManual && d.booking.status === "PendingDeposit" ? "محجوز مبدئي" : (STATUS_MAP[d.booking.status] ?? d.booking.status)}
                  </Pill>
                </div>
                <KV k="الميعاد">{fmtDateTime(d.booking.startUtc)}</KV>
                <KV k="السعر / العربون">{money(d.booking.totalPrice)} / {money(d.booking.depositAmount)}</KV>
                <KV k="الملعب">
                  <span className="font-bold text-slate-900">
                    {(d.booking as any).venueName || venueMap?.get(d.booking.venueId) || d.booking.venueId}
                  </span>
                </KV>
                {Boolean((d.booking as any).pitchName) && (
                  <KV k="الملعب الفرعي">
                    <span className="font-medium text-slate-800">{(d.booking as any).pitchName}</span>
                  </KV>
                )}
                <KV k="اللاعب">
                  <div className="flex flex-col">
                    <span className="font-bold text-slate-900">
                      {d.customerName || (d.booking as any).customerName || (d.booking.isManual ? "حجز يدوي" : "لاعب مسجل")}
                    </span>
                    {(d as any).customerPhone || (d.booking as any).customerPhone ? (
                      <span className="text-xs text-slate-600 font-mono font-medium" dir="ltr">
                        {(d as any).customerPhone || (d.booking as any).customerPhone}
                      </span>
                    ) : null}
                  </div>
                </KV>
                {d.cancellationReason && <KV k="سبب الإلغاء">{d.cancellationReason}</KV>}
                {d.rejectionReason && <KV k="سبب الرفض">{d.rejectionReason}</KV>}
                {d.policy && <KV k="سياسة الإلغاء">قبلها بـ {d.policy.cancellationDeadlineHours} ساعة · استرداد {d.policy.refundPercentage}٪</KV>}
              </div>

              {/* Visual Payment Proof Viewer */}
              {hasProofs && (
                <div className="bg-slate-50 border border-slate-200/90 rounded-2xl p-4 flex flex-col gap-3 shadow-xs">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <ReceiptText className="size-4 text-slate-500" />
                      <strong className="text-sm font-bold text-slate-900">إيصال التحويل المرفوع</strong>
                    </div>
                    <span className="text-[11px] text-slate-500 font-medium">
                      {fmtDateTime(selectedProof.uploadedAtUtc)}
                    </span>
                  </div>

                  {d.proofs.length > 1 && (
                    <div className="flex items-center gap-1.5 bg-white p-1 rounded-xl border border-slate-200">
                      {d.proofs.map((p, idx) => (
                        <button
                          key={p.id}
                          type="button"
                          onClick={() => setSelectedProofIdx(idx)}
                          className={`flex-1 py-1 px-2 text-xs font-semibold rounded-lg transition-colors cursor-pointer ${
                            selectedProofIdx === idx ? "bg-slate-900 text-white" : "text-slate-600 hover:bg-slate-100"
                          }`}
                        >
                          إيصال {toArabicDigits(idx + 1)} {idx === 0 ? "(الأحدث)" : ""}
                        </button>
                      ))}
                    </div>
                  )}

                  <ReceiptViewer proof={selectedProof} />

                  {selectedProof.rejectionReason && (
                    <div className="p-2.5 rounded-xl bg-red-50 border border-red-200 text-xs text-red-700">
                      <strong>سبب الرفض السابق:</strong> {selectedProof.rejectionReason}
                    </div>
                  )}
                </div>
              )}

              {/* Manual Booking Notice */}
              {d.booking.isManual && (
                <div className="bg-amber-50/70 border border-amber-200/80 rounded-2xl p-4 flex items-start gap-3 text-xs text-amber-900 shadow-2xs">
                  <div className="size-8 rounded-xl bg-amber-100/80 text-amber-800 flex items-center justify-center shrink-0">
                    <NotebookPen className="size-4" />
                  </div>
                  <div className="flex flex-col gap-1">
                    <strong className="text-sm font-bold text-amber-950">حجز يدوي مسجل من طاقم الملعب</strong>
                    <p className="text-amber-900/90 leading-relaxed text-[12px]">
                      هذا الحجز مسجل يدوياً في الملعب ومحاسبته نقداً مباشرة مع إدارة الملعب، ولا يتطلب مراجعة أو تأكيد عربون من إدارة المنصة المركزية.
                    </p>
                  </div>
                </div>
              )}

              {/* Admin Deposit Confirmation / Review Box */}
              {isDepositPending && (
                <div className={`${card} rounded-2xl p-4 flex flex-col gap-3`}>
                  <div className="flex items-center gap-2.5">
                    <div className="size-8 rounded-xl bg-[#E6F4EA] text-[#1F6B3A] flex items-center justify-center shrink-0">
                      <CheckCircle2 className="size-5" />
                    </div>
                    <div className="flex flex-col">
                      <strong className="text-sm font-bold text-slate-900">مراجعة وتأكيد العربون (صلاحية الإدارة)</strong>
                      <span className="text-[11px] text-slate-500">العمولة والعربون يدخلان لحساب المنصة وتأكيد الحجز يتم مركزياً</span>
                    </div>
                  </div>

                  {(() => {
                    const depositAmt = d.booking.depositAmount;
                    const commissionAmt = (d.booking as any).platformCommission ?? 0;
                    // Signed: a commission larger than the deposit leaves the venue owing the difference.
                    const netDepositToVenue = depositAmt - commissionAmt;
                    const remainingCashAtVenue = Math.max(0, d.booking.totalPrice - depositAmt);

                    return (
                      <div className="grid grid-cols-2 gap-2 text-xs bg-slate-50 p-3 rounded-xl">
                        <div>
                          <span className="text-slate-500 block text-[11px]">مبلغ العربون المحصل:</span>
                          <strong className="text-sm font-bold text-emerald-700">{money(depositAmt)}</strong>
                        </div>
                        <div>
                          <span className="text-slate-500 block text-[11px]">عمولة المنصة:</span>
                          <strong className="text-sm font-bold text-[#C7362A]">{money(commissionAmt)}</strong>
                        </div>
                        <div className="border-t border-slate-100 pt-2">
                          <span className="text-slate-500 block text-[11px]">صافي العربون للملعب:</span>
                          <strong className={`text-xs font-semibold ${netDepositToVenue < 0 ? "text-[#A8261C]" : "text-slate-800"}`}>
                            {netDepositToVenue < 0 ? `عليه ${money(-netDepositToVenue)}` : money(netDepositToVenue)}
                          </strong>
                        </div>
                        <div className="border-t border-slate-100 pt-2">
                          <span className="text-slate-500 block text-[11px]">المتبقي كاش يدفع بالملعب:</span>
                          <strong className="text-xs font-semibold text-slate-800">{money(remainingCashAtVenue)}</strong>
                        </div>
                      </div>
                    );
                  })()}

                  <div className="flex items-center gap-2 pt-1">
                    <button
                      type="button"
                      disabled={confirmDeposit.isPending}
                      onClick={() => confirmDeposit.mutate()}
                      className="flex-1 min-h-[40px] px-3.5 rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white font-bold text-xs shadow-xs flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
                    >
                      <CheckCircle2 className="size-4" />
                      {confirmDeposit.isPending ? "جاري الاعتماد…" : "تأكيد العربون وتثبيت الحجز"}
                    </button>

                    <button
                      type="button"
                      disabled={rejectDeposit.isPending}
                      onClick={() =>
                        setReason({
                          title: "رفض العربون",
                          body: "يرجى كتابة سبب عدم اعتماد العربون؛ سيتم إخطار اللاعب بالسبب وإلغاء الحجز.",
                          confirmLabel: "تأكيد الرفض",
                          onConfirm: (text) => rejectDeposit.mutate(text),
                        })
                      }
                      className="min-h-[40px] px-3.5 rounded-xl border border-red-200 bg-white hover:bg-red-50 text-red-700 font-semibold text-xs transition-colors cursor-pointer"
                    >
                      رفض
                    </button>
                  </div>
                </div>
              )}

            {/* Cancelled by Venue - Pending Refund Alert */}
            {d.booking.status === "Cancelled" && d.refunds.some((r) => r.status === "Pending") && (
              <div className="bg-amber-50 border border-amber-300 rounded-2xl p-4 flex flex-col gap-2.5 shadow-xs">
                <div className="flex items-center gap-2">
                  <TriangleAlert className="size-[18px] text-amber-600 shrink-0" />
                  <strong className="text-sm font-bold text-amber-950">إحالة استرداد عربون (إلغاء الملعب)</strong>
                </div>
                <p className="text-xs text-amber-900 leading-relaxed">
                  قام الملعب بإلغاء هذا الحجز {d.cancellationReason ? `(السبب: ${d.cancellationReason})` : ""}. يتوجب على الإدارة إرجاع مبلغ العربون ({money(d.booking.depositAmount)}) للاعب.
                </p>
                {d.refunds.filter((r) => r.status === "Pending").map((r) => (
                  <button
                    key={r.id}
                    type="button"
                    disabled={processRefund.isPending}
                    onClick={() => processRefund.mutate(r.id)}
                    className="min-h-[38px] px-4 rounded-xl bg-amber-600 hover:bg-amber-700 text-white font-bold text-xs shadow-xs flex items-center justify-center gap-1.5 transition-colors cursor-pointer w-fit"
                  >
                    <CheckCircle2 className="size-4" />
                    {processRefund.isPending ? "جاري التسجيل…" : `تسجيل إتمام رد العربون (${money(r.expectedAmount)})`}
                  </button>
                ))}
              </div>
            )}

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
          );
        }}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}
