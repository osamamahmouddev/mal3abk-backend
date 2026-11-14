"use client";

import React, { useEffect, useState } from "react";
import Link from "next/link";
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Toast, card, chip, muted, primaryBtn, secondaryBtn, toArabicDigits, useRevealOnSelect } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { BOOKING_STATUS, type BookingStatus, addDays, cairoDateTime, cairoDayStartUtc, cairoToday, money, timeLeft } from "@/lib/booking-format";
import { downloadCsv } from "@/lib/csv";
import { P } from "@/lib/permissions";
import { ReasonDialog } from "@/components/ReasonDialog";

type Item = Schemas["VenueBookingItemDto"];
type Tab = "pending" | "ok" | "closed" | "all";
type Sort = "start_desc" | "start_asc" | "created_desc" | "price_desc";

const PAGE_SIZE = 20;

const TAB_STATUSES: Record<Tab, BookingStatus[]> = {
  pending: [2, 3, 4],
  ok: [5, 9],
  closed: [6, 7, 8, 10],
  all: [],
};

const TABS: [Tab, string][] = [
  ["all", "كل الحجوزات"],
  ["pending", "طلبات معلقة"],
  ["ok", "مؤكدة"],
  ["closed", "ملغية ومنتهية"],
];

type Range = "all" | "today" | "tomorrow" | "week" | "month" | "custom";
type Source = "" | "app" | "manual";

const RANGES: [Range, string][] = [
  ["all", "كل الأيام"],
  ["today", "النهارده"],
  ["tomorrow", "بكرة"],
  ["week", "الأسبوع ده"],
  ["month", "الشهر ده"],
  ["custom", "من تاريخ لتاريخ"],
];

/** The Cairo calendar days a range covers, as [first day, last day] (inclusive), or null for "every day". */
function rangeDays(range: Range, from: string, to: string): [string, string] | null {
  const today = cairoToday();
  switch (range) {
    case "today":
      return [today, today];
    case "tomorrow":
      return [addDays(today, 1), addDays(today, 1)];
    case "week":
      return [today, addDays(today, 6)];
    case "month": {
      const first = `${today.slice(0, 8)}01`;
      const next = addDays(first, 31).slice(0, 8) + "01";
      return [first, addDays(next, -1)];
    }
    case "custom":
      return from || to ? [from || to, to || from] : null;
    default:
      return null;
  }
}

const SORTS: [Sort, string][] = [
  ["start_desc", "الأحدث ميعاداً"],
  ["start_asc", "الأقرب ميعاداً"],
  ["created_desc", "الأحدث طلباً"],
  ["price_desc", "الأعلى سعراً"],
];

const REASONS = ["صيانة طارئة", "حجز مكرر", "الملعب مقفول للظروف الجوية"];

const depositText = (b: Item) => {
  if (b.isManual) return "الدفع في الملعب";
  if (b.status === 2) return "لم يُرفع بعد";
  if (b.status === 3 || b.status === 4) return `${money(b.depositAmount, b.currency)} — إيصال مرفوع`;
  if (b.status === 5 || b.status === 9 || b.status === 10) return `${money(b.depositAmount, b.currency)} معتمد`;
  return "—";
};

export default function ReservationsPage() {
  const queryClient = useQueryClient();
  const { venue, venueId, can } = useVenue();
  const [tab, setTab] = useState<Tab>("all");
  const [q, setQ] = useState("");
  const [search, setSearch] = useState("");
  const [pitchId, setPitchId] = useState("");
  const [sort, setSort] = useState<Sort>("start_asc");
  const [range, setRange] = useState<Range>("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [source, setSource] = useState<Source>("");
  const [page, setPage] = useState(1);
  const [selId, setSelId] = useState<string | null>(null);
  const detailsRef = useRevealOnSelect<HTMLElement>(selId);
  const [toast, setToast] = useState<string | null>(null);

  useEffect(() => {
    const t = setTimeout(() => {
      setSearch(q.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(t);
  }, [q]);

  const days = rangeDays(range, from, to);
  // A range covers whole Cairo days; the API keeps bookings that start between the two instants (end is inclusive).
  const startDateUtc = days ? cairoDayStartUtc(days[0]) : undefined;
  const endDateUtc = days ? new Date(new Date(cairoDayStartUtc(addDays(days[1], 1))).getTime() - 1000).toISOString() : undefined;
  const rangeKey = `${startDateUtc ?? ""}|${endDateUtc ?? ""}|${source}`;

  const filters = (statuses: BookingStatus[], p: number, size: number) => ({
    path: { venueId: venueId! },
    query: {
      statuses: statuses.length ? statuses : undefined,
      search: search || undefined,
      pitchId: pitchId || undefined,
      startDateUtc,
      endDateUtc,
      manual: source === "" ? undefined : source === "manual",
      sort,
      page: p,
      pageSize: size,
    },
  });

  const bookings = useQuery({
    queryKey: ["bookings", venueId, tab, search, pitchId, rangeKey, sort, page],
    enabled: !!venueId,
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/bookings", { params: filters(TAB_STATUSES[tab], page, PAGE_SIZE) })),
  });

  // Tab badges: one tiny request per tab for its total.
  const counts = useQueries({
    queries: TABS.map(([id]) => ({
      queryKey: ["bookings-count", venueId, id, search, pitchId, rangeKey],
      enabled: !!venueId,
      queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/bookings", { params: filters(TAB_STATUSES[id], 1, 1) })),
    })),
  });

  const list = bookings.data?.items ?? [];
  const total = bookings.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const pitchName = (id: string) => venue?.pitches.find((p) => p.id === id)?.name ?? "ملعب";
  const refresh = () => queryClient.invalidateQueries({ predicate: (query) => String(query.queryKey[0]).startsWith("booking") || query.queryKey[0] === "dashboard" });

  const exportList = () => {
    downloadCsv(
      "reservations.csv",
      ["رقم الحجز", "العميل", "الموبايل", "الملعب", "الميعاد", "السعر", "العربون", "الحالة"],
      list.map((b) => [
        b.reference,
        b.customerName ?? "",
        b.customerPhone ?? "",
        pitchName(b.pitchId),
        cairoDateTime(b.startUtc),
        String(b.totalPrice),
        String(b.depositAmount),
        BOOKING_STATUS[b.status].label,
      ]),
    );
    setToast(`اتنزّل ملف بـ ${toArabicDigits(list.length)} حجز. للفترات الطويلة استخدم تصدير التقارير.`);
  };

  return (
    <Page className="gap-[18px]">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">الحجوزات</h1>
        <button type="button" onClick={exportList} disabled={list.length === 0} className={`${secondaryBtn} rounded-xl disabled:opacity-50`}>
          تصدير الصفحة
        </button>
        {can(P.bookingsApprove) && (
          <Link href="/reservations/manual" className={primaryBtn}>
            + حجز يدوي
          </Link>
        )}
      </div>

      <div role="tablist" aria-label="حالة الحجز" className="flex gap-1.5 flex-wrap">
        {TABS.map(([id, label], i) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => {
              setTab(id);
              setPage(1);
            }}
            className={`${chip(tab === id, "min-h-[42px] px-4 rounded-xl text-sm")} flex items-center gap-2`}
          >
            {label}
            <span className={`min-w-[22px] h-[22px] px-1.5 rounded-[11px] text-xs inline-flex items-center justify-center ${tab === id ? "bg-white/[0.18]" : "bg-[#EEF0F5]"}`}>
              {counts[i].data ? toArabicDigits(counts[i].data!.totalCount) : "…"}
            </span>
          </button>
        ))}
      </div>

      <div className="flex flex-wrap gap-2.5 items-center">
        <div className="flex-[1_1_280px] min-w-0 flex items-center gap-2 min-h-[44px] bg-white border border-[#DCE0E8] rounded-xl px-3">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5B6475" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="M20 20l-4-4" />
          </svg>
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="ابحث باسم العميل أو رقم الحجز أو الموبايل"
            aria-label="بحث"
            maxLength={100}
            className="flex-1 min-w-0 border-none outline-none text-sm bg-transparent text-slate-900"
          />
        </div>
        <select
          aria-label="الملعب"
          value={pitchId}
          onChange={(e) => {
            setPitchId(e.target.value);
            setPage(1);
          }}
          className="min-h-[44px] px-3 rounded-xl border border-[#DCE0E8] bg-white text-sm"
        >
          <option value="">كل الملاعب</option>
          {venue?.pitches.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <select
          aria-label="المصدر"
          value={source}
          onChange={(e) => {
            setSource(e.target.value as Source);
            setPage(1);
          }}
          className="min-h-[44px] px-3 rounded-xl border border-[#DCE0E8] bg-white text-sm"
        >
          <option value="">كل المصادر</option>
          <option value="app">من التطبيق</option>
          <option value="manual">حجز يدوي</option>
        </select>
        <select aria-label="الترتيب" value={sort} onChange={(e) => setSort(e.target.value as Sort)} className="min-h-[44px] px-3 rounded-xl border border-[#DCE0E8] bg-white text-sm">
          {SORTS.map(([id, label]) => (
            <option key={id} value={id}>
              {label}
            </option>
          ))}
        </select>
      </div>

      <div role="group" aria-label="الفترة" className="flex flex-wrap items-center gap-2">
        {RANGES.map(([id, label]) => (
          <button
            key={id}
            type="button"
            aria-pressed={range === id}
            onClick={() => {
              setRange(id);
              setPage(1);
            }}
            className={chip(range === id, "min-h-[38px] px-3.5 rounded-[10px] text-[13px]")}
          >
            {label}
          </button>
        ))}
        {range === "custom" && (
          <span className="flex flex-wrap items-center gap-2 text-sm">
            <label className="flex items-center gap-1.5">
              من
              <input type="date" aria-label="من تاريخ" value={from} max={to || undefined} onChange={(e) => { setFrom(e.target.value); setPage(1); }} className="min-h-[38px] px-2.5 rounded-[10px] border border-[#DCE0E8] bg-white" />
            </label>
            <label className="flex items-center gap-1.5">
              إلى
              <input type="date" aria-label="إلى تاريخ" value={to} min={from || undefined} onChange={(e) => { setTo(e.target.value); setPage(1); }} className="min-h-[38px] px-2.5 rounded-[10px] border border-[#DCE0E8] bg-white" />
            </label>
          </span>
        )}
      </div>

      {bookings.isError && (
        <div role="alert" className="bg-[#FDECEA] text-[#A8261C] rounded-[14px] px-4 py-3 text-sm flex gap-3">
          <span className="flex-1">{(bookings.error as Error).message}</span>
          <button type="button" className="font-bold underline" onClick={() => bookings.refetch()}>
            جرّب تاني
          </button>
        </div>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_600px] min-w-0 ${card} rounded-[18px] overflow-hidden`} aria-busy={bookings.isFetching}>
          <ul className="md:hidden divide-y divide-[#EEF1F5]">
            {list.map((b) => (
              <li key={b.id} className={`p-4 flex flex-col gap-2 ${b.id === selId ? "bg-[#F5F7FB]" : ""}`}>
                <div className="flex items-start gap-2">
                  <div className="flex-1 min-w-0 flex flex-col">
                    <span className="font-semibold">{b.customerName || "—"}</span>
                    <span className={`text-xs ${muted}`}>{b.isManual ? "حجز يدوي" : "من التطبيق"}</span>
                  </div>
                  <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
                </div>
                <div className="text-sm">
                  {pitchName(b.pitchId)} · {cairoDateTime(b.startUtc)}
                </div>
                <div className={`flex flex-wrap gap-x-3 gap-y-1 text-xs ${muted}`}>
                  <span dir="ltr">{b.reference}</span>
                  <span>{depositText(b)}</span>
                </div>
                <RowActions b={b} canReview={can(P.proofsReview)} onDetails={() => setSelId(b.id)} compact={false} />
              </li>
            ))}
          </ul>

          <div className="hidden md:block overflow-x-auto" tabIndex={0} role="region" aria-label="جدول الحجوزات">
            <table className="w-full min-w-[760px] border-collapse text-sm">
              <thead>
                <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
                  <th scope="col" className="px-[18px] py-3 font-semibold">رقم الحجز</th>
                  <th scope="col" className="p-3 font-semibold">العميل</th>
                  <th scope="col" className="p-3 font-semibold">الملعب والميعاد</th>
                  <th scope="col" className="p-3 font-semibold">العربون</th>
                  <th scope="col" className="p-3 font-semibold">الحالة</th>
                  <th scope="col" className="px-[18px] py-3 font-semibold">
                    <span className="sr-only">إجراءات</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {list.map((b) => (
                  <tr
                    key={b.id}
                    className={`border-t border-[#EEF1F5] transition-all duration-150 ease-out hover:bg-[#F8FAFC] ${
                      b.id === selId ? "bg-[#F3F5FA] shadow-[inset_-3px_0_0_#0F1B3D]" : ""
                    }`}
                  >
                    <td dir="ltr" className="px-[18px] py-3 text-right text-[13px] text-[#3B4352]">
                      {b.reference}
                    </td>
                    <td className="p-3">
                      <div className="flex flex-col">
                        <span className="font-semibold">{b.customerName || "—"}</span>
                        <span className={`text-xs ${muted}`}>{b.isManual ? "حجز يدوي" : "من التطبيق"}</span>
                      </div>
                    </td>
                    <td className="p-3 whitespace-nowrap">
                      {pitchName(b.pitchId)} · {cairoDateTime(b.startUtc)}
                    </td>
                    <td className="p-3 whitespace-nowrap text-[13px] text-[#333B4A]">{depositText(b)}</td>
                    <td className="p-3">
                      <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
                    </td>
                    <td className="px-[18px] py-2 whitespace-nowrap">
                      <RowActions b={b} canReview={can(P.proofsReview)} onDetails={() => setSelId(b.id)} compact />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {bookings.isLoading && <div className={`p-10 text-center text-sm ${muted}`}>جاري التحميل…</div>}
          {!bookings.isLoading && list.length === 0 && (
            <div className={`p-10 text-center text-sm ${muted}`}>
              مفيش حجوزات بالبحث والفلاتر دي.{" "}
              <button
                type="button"
                onClick={() => {
                  setQ("");
                  setPitchId("");
                  setTab("all");
                }}
                className="bg-transparent text-navy-900 font-bold underline text-sm"
              >
                امسح الفلاتر
              </button>
            </div>
          )}
          <nav aria-label="صفحات الحجوزات" className={`px-[18px] py-3 border-t border-[#EEF1F5] flex items-center gap-2 text-[13px] ${muted}`}>
            <span className="flex-1">
              عرض {toArabicDigits(list.length)} من {toArabicDigits(total)} حجز
            </span>
            <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="min-h-[34px] px-3 rounded-[9px] border border-[#DCE0E8] bg-white disabled:opacity-40">
              السابق
            </button>
            <span>
              {toArabicDigits(page)} / {toArabicDigits(pages)}
            </span>
            <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="min-h-[34px] px-3 rounded-[9px] border border-[#DCE0E8] bg-white disabled:opacity-40">
              التالي
            </button>
          </nav>
        </section>

        {selId && venueId && (
          <BookingDrawer
            key={selId}
            venueId={venueId}
            bookingId={selId}
            panelRef={detailsRef}
            pitchName={pitchName}
            onClose={() => setSelId(null)}
            onChanged={async (message) => {
              await refresh();
              setToast(message);
            }}
            onError={setToast}
          />
        )}
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function RowActions({ b, canReview, onDetails, compact }: { b: Item; canReview: boolean; onDetails: () => void; compact: boolean }) {
  const size = compact ? "min-h-[34px] px-3 rounded-[9px] text-xs" : "min-h-[40px] px-3.5 rounded-[10px] text-[13px]";
  return (
    <div className="flex flex-wrap gap-1.5">
      {canReview && (b.status === 3 || b.status === 4) && (
        <Link href={`/reservations/proof-review?booking=${b.id}`} className={`${size} bg-navy-900 text-white font-bold inline-flex items-center`}>
          مراجعة الإيصال
        </Link>
      )}
      <button type="button" onClick={onDetails} className={`${size} bg-[#EEF1F7] text-navy-900 font-semibold`}>
        التفاصيل
      </button>
    </div>
  );
}

function BookingDrawer({
  venueId,
  bookingId,
  panelRef,
  pitchName,
  onClose,
  onChanged,
  onError,
}: {
  venueId: string;
  bookingId: string;
  panelRef: React.Ref<HTMLElement>;
  pitchName: (id: string) => string;
  onClose: () => void;
  onChanged: (message: string) => Promise<void>;
  onError: (message: string) => void;
}) {
  const { can } = useVenue();
  const [cancelOpen, setCancelOpen] = useState(false);
  const [noShowOpen, setNoShowOpen] = useState(false);
  const path = { venueId, id: bookingId };

  const detail = useQuery({
    queryKey: ["booking", venueId, bookingId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/bookings/{id}", { params: { path } })),
  });

  const cancel = useMutation({
    mutationFn: (reason: string) => unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/{id}/cancel", { params: { path }, body: { reason } })),
    onSuccess: async () => {
      setCancelOpen(false);
      await onChanged("اتلغى الحجز ووصل للاعب إشعار بالسبب. سجّل الاسترداد بعد التحويل.");
    },
    onError: (e: Error) => onError(e.message),
  });

  const noShow = useMutation({
    mutationFn: (reason: string) => unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/{id}/no-show", { params: { path }, body: { reason } })),
    onSuccess: async () => {
      setNoShowOpen(false);
      await onChanged("اتسجل عدم الحضور.");
    },
    onError: (e: Error) => onError(e.message),
  });

  const b = detail.data;
  const canCancel = b && [2, 3, 4, 5].includes(b.status) && can(P.bookingsApprove);
  const canNoShow = b && b.status === 5 && new Date(b.startUtc).getTime() <= Date.now() && can(P.bookingsApprove);

  return (
    <aside ref={panelRef} aria-label="تفاصيل الحجز" className={`scroll-mt-20 flex-[1_1_320px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-3.5 animate-scale-in shadow-xl`}>
      <div className="flex justify-between items-center">
        <h2 className="text-lg font-bold">{b?.customerName || "تفاصيل الحجز"}</h2>
        <button type="button" onClick={onClose} aria-label="إغلاق" className="min-w-[36px] min-h-[36px] rounded-[10px] bg-surface text-navy-900">
          ✕
        </button>
      </div>
      {detail.isLoading && <span className={`text-sm ${muted}`}>جاري التحميل…</span>}
      {detail.isError && <span className="text-sm text-[#A8261C]">{(detail.error as Error).message}</span>}
      {b && (
        <>
          <Badge tone={BOOKING_STATUS[b.status].tone} className="self-start">
            {BOOKING_STATUS[b.status].label}
          </Badge>
          <dl className="flex flex-col gap-2 text-sm">
            {(
              [
                ["رقم الحجز", <span key="r" dir="ltr">{b.reference}</span>],
                ["الملعب", pitchName(b.pitchId)],
                ["الميعاد", `${cairoDateTime(b.startUtc)} – ${cairoDateTime(b.endUtc)}`],
                ["السعر", money(b.totalPrice, b.currency)],
                ["العربون", money(b.depositAmount, b.currency)],
                ["الهاتف", b.customerPhone ? <span key="p" dir="ltr">{b.customerPhone}</span> : "—"],
                ...(b.status === 2 && b.holdExpiresAtUtc ? [["مهلة العربون", timeLeft(b.holdExpiresAtUtc)]] : []),
              ] as [string, React.ReactNode][]
            ).map(([k, v]) => (
              <div key={k} className="flex justify-between gap-3">
                <dt className={muted}>{k}</dt>
                <dd>{v}</dd>
              </div>
            ))}
          </dl>
          {b.notes && <p className="text-[13px] bg-surface rounded-xl px-3 py-2 whitespace-pre-wrap">{b.notes}</p>}
          <div className="flex flex-col gap-1.5 border-t border-[#EEF1F5] pt-3">
            <span className="text-[13px] font-bold">سجل الحالة</span>
            {b.statusHistory.map((h, i) => (
              <span key={`${h.changedAtUtc}-${i}`} className="text-[13px] text-[#333B4A]">
                • {BOOKING_STATUS[h.status as BookingStatus]?.label ?? String(h.status)} — {cairoDateTime(h.changedAtUtc)}
                {h.reason ? ` (${h.reason})` : ""}
              </span>
            ))}
          </div>
          <Link href="/customers" className="text-[13px] font-semibold text-navy-900 underline hover:text-brand-red">
            ملف العميل وسجل حجوزاته
          </Link>
          {(b.status === 3 || b.status === 4) && can(P.proofsReview) && (
            <Link href={`/reservations/proof-review?booking=${b.id}`} className="min-h-[44px] rounded-xl bg-navy-900 text-white text-sm font-bold flex items-center justify-center">
              مراجعة الإيصال
            </Link>
          )}
          {canNoShow && (
            <button type="button" onClick={() => setNoShowOpen(true)} className="min-h-[44px] rounded-xl border border-[#C9CFD9] bg-white text-navy-900 text-sm font-bold">
              تسجيل عدم حضور
            </button>
          )}
          {canCancel && (
            <button type="button" onClick={() => setCancelOpen(true)} className="min-h-[44px] rounded-xl border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-bold">
              إلغاء الحجز (يتطلب سبب)
            </button>
          )}
        </>
      )}

      {cancelOpen && b && (
        <ReasonDialog
          title={`إلغاء حجز ${b.customerName || b.reference}`}
          presets={REASONS}
          note="الإلغاء من الملعب = استرداد العربون كامل للاعب. هتسجّل الاسترداد يدوي بعد التحويل."
          confirmLabel="تأكيد الإلغاء"
          busy={cancel.isPending}
          onConfirm={(reason) => cancel.mutate(reason)}
          onClose={() => setCancelOpen(false)}
        />
      )}
      {noShowOpen && b && (
        <ReasonDialog
          title="تسجيل عدم حضور"
          presets={["محدش حضر", "الفريق اتأخر أكتر من فترة السماح"]}
          note="العربون بيتحجز حسب سياسة الإلغاء، واللاعب بيوصله إشعار."
          confirmLabel="تأكيد"
          busy={noShow.isPending}
          onConfirm={(reason) => noShow.mutate(reason)}
          onClose={() => setNoShowOpen(false)}
        />
      )}
    </aside>
  );
}
