"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, card, muted, redShadow, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { APPROVAL_LABELS } from "@/lib/venue-format";
import { BOOKING_STATUS, DEPOSIT_STATE, cairoDateTime, cairoTime, money, percent, timeLeft } from "@/lib/booking-format";

type Dashboard = Schemas["VenueDashboardDto"];
type Row = Schemas["DashboardBooking"];

const WEEKDAY_SHORT = ["أحد", "إثنين", "ثلاثاء", "أربعاء", "خميس", "جمعة", "سبت"];

export default function OverviewPage() {
  const { venue, venueId, isLoading: venueLoading } = useVenue();
  const [range, setRange] = useState<"this" | "last">("this");

  const dashboard = useQuery({
    queryKey: ["dashboard", venueId],
    enabled: !!venueId,
    refetchInterval: 60_000,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/dashboard", { params: { path: { venueId: venueId! } } })),
  });

  const pitchName = (id: string) => venue?.pitches.find((p) => p.id === id)?.name ?? "ملعب";
  const activePitches = venue?.pitches.filter((p) => p.isActive).length ?? 0;
  const today = new Date().toLocaleDateString("ar-EG", { timeZone: "Africa/Cairo", weekday: "long", day: "numeric", month: "long" });

  return (
    <Page className="gap-5">
      {venue && venue.approvalStatus !== "Approved" && (
        <div role="status" className="bg-[#FEF3D6] text-[#7A4D00] rounded-[14px] px-4 py-3 flex flex-wrap items-center gap-3">
          <span className="flex-[1_1_260px] text-sm leading-[1.6]">
            <b>{APPROVAL_LABELS[venue.approvalStatus]?.label ?? venue.approvalStatus}:</b>{" "}
            {venue.approvalStatus === "Submitted"
              ? "فريق ملعبك بيراجع الملعب (عادةً خلال يومين عمل). الملعب مش هيظهر للاعبين غير بعد الموافقة."
              : "الملعب لسه مش ظاهر للاعبين. كمّل البيانات وابعته للمراجعة."}
          </span>
          {venue.approvalStatus !== "Submitted" && (
            <Link href="/setup" className="text-sm font-bold underline">
              كمّل الإعداد
            </Link>
          )}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_280px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">نظرة عامة</h1>
          <span className={`text-sm ${muted}`}>
            {today} · {toArabicDigits(activePitches)} ملاعب نشطة
          </span>
        </div>
        <QuickActions />
      </div>

      {!venueLoading && !venueId && <EmptyVenue />}
      {dashboard.isLoading && <SkeletonGrid />}
      {dashboard.isError && (
        <div role="alert" className="bg-[#FDECEA] text-[#A8261C] rounded-[14px] px-4 py-3 flex flex-wrap items-center gap-3 text-sm">
          <span className="flex-1">مقدرناش نجيب بيانات النهارده: {(dashboard.error as Error).message}</span>
          <button type="button" onClick={() => dashboard.refetch()} className="font-bold underline">
            جرّب تاني
          </button>
        </div>
      )}

      {dashboard.data && (
        <DashboardBody data={dashboard.data} range={range} setRange={setRange} pitchName={pitchName} />
      )}
    </Page>
  );
}

function DashboardBody({
  data,
  range,
  setRange,
  pitchName,
}: {
  data: Dashboard;
  range: "this" | "last";
  setRange: (r: "this" | "last") => void;
  pitchName: (id: string) => string;
}) {
  const nextHold = data.timeline
    .filter((b) => b.status === 2 && b.deadlineUtc)
    .map((b) => b.deadlineUtc!)
    .sort()[0];
  const occupancy = range === "this" ? data.occupancyThisWeek : data.occupancyLastWeek;

  return (
    <>
      {/* KPIs */}
      <div className="grid gap-3 grid-cols-2 md:grid-cols-[repeat(auto-fit,minmax(min(190px,100%),1fr))]">
        <Kpi label="حجوزات اليوم" value={toArabicDigits(data.today.todayBookings)} hint={`${toArabicDigits(data.today.confirmed)} مؤكدة`} />
        <Kpi
          label="طلبات بانتظار العربون"
          value={toArabicDigits(data.today.pendingDeposit)}
          hint={nextHold ? `أقرب انتهاء خلال ${timeLeft(nextHold)}` : "مفيش طلبات معلقة"}
        />
        <Link
          href="/reservations/proof-review"
          className="bg-white border-2 border-brand-gold rounded-[14px] p-[15px] flex flex-col gap-1.5 text-slate-900 no-underline"
        >
          <span className="text-[13px] text-[#7A4D00] font-semibold">إيصالات للمراجعة</span>
          <span className="text-[30px] font-bold">{toArabicDigits(data.proofsAwaitingReview.length)}</span>
          <span className="text-xs text-navy-900 font-semibold underline">راجع الآن ←</span>
        </Link>
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
          <span className={`text-[13px] ${muted}`}>إشغال اليوم</span>
          <span className="text-[30px] font-bold">{percent(data.todayOccupancy.percent)}</span>
          <div className="h-1.5 rounded-[3px] bg-[#E4E7EC] overflow-hidden" aria-hidden="true">
            <div className="h-full bg-navy-900" style={{ width: `${Math.min(100, data.todayOccupancy.percent)}%` }} />
          </div>
        </div>
      </div>

      {data.todayRevenue && data.monthRevenue && <FinanceStrip today={data.todayRevenue} month={data.monthRevenue} />}

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_520px] min-w-0 ${card} rounded-[18px] overflow-hidden`} aria-labelledby="timeline-title">
          <div className="px-5 py-4 flex items-center justify-between border-b border-[#E4E7EC]">
            <h2 id="timeline-title" className="text-base font-bold">
              حجوزات النهارده
            </h2>
            <Link href="/calendar?view=day" className="text-[13px] font-semibold text-navy-900 hover:text-brand-red">
              فتح التقويم
            </Link>
          </div>
          {data.timeline.length === 0 ? (
            <p className={`px-5 py-8 text-sm text-center ${muted}`}>مفيش حجوزات النهارده لسه.</p>
          ) : (
            <BookingTable rows={data.timeline} pitchName={pitchName} />
          )}
        </section>

        <div className="flex-[1_1_300px] min-w-0 flex flex-col gap-4">
          <ProofsCard rows={data.proofsAwaitingReview} pitchName={pitchName} />
          <OccupancyCard points={occupancy} range={range} setRange={setRange} />
        </div>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <UpcomingCard rows={data.upcoming} pitchName={pitchName} />
        {data.revenueTrend && <RevenueTrend points={data.revenueTrend} />}
      </div>

      <ActivityCard items={data.recentActivity} />
    </>
  );
}

function QuickActions() {
  return (
    <div className="flex flex-wrap gap-2">
      <Link
        href="/calendar?view=day"
        className="min-h-[44px] px-4 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-sm font-semibold flex items-center gap-2 hover:bg-slate-50"
      >
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
          <circle cx="12" cy="12" r="9" />
          <path d="M6 6l12 12" />
        </svg>
        حظر ميعاد
      </Link>
      <Link
        href="/reservations/manual"
        className={`min-h-[44px] px-[18px] rounded-[10px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold flex items-center gap-2 ${redShadow}`}
      >
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" aria-hidden="true">
          <path d="M12 5v14M5 12h14" />
        </svg>
        حجز يدوي
      </Link>
    </div>
  );
}

function Kpi({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
      <span className={`text-[13px] ${muted}`}>{label}</span>
      <span className="text-[30px] font-bold">{value}</span>
      <span className={`text-xs ${muted}`}>{hint}</span>
    </div>
  );
}

function FinanceStrip({ today, month }: { today: Schemas["DashboardRevenue"]; month: Schemas["DashboardRevenue"] }) {
  const items = [
    { label: "قيمة الحجوزات المؤكدة اليوم", value: money(today.confirmedValue, today.currency), tone: "" },
    { label: "عربون معتمد (محصّل)", value: money(today.depositsCollected, today.currency), tone: "text-[#1B6E3E]" },
    { label: "متبقي يُحصّل في الملعب", value: money(today.dueAtVenue, today.currency), tone: "" },
    { label: "عربون قيد المراجعة — غير محصّل", value: money(today.depositsPendingReview, today.currency), tone: "text-[#7A4D00]" },
    { label: "إيراد الشهر المؤكد", value: money(month.confirmedValue, month.currency), tone: "" },
  ];
  return (
    <section aria-label="ملخص مالي" className={`${card} rounded-[18px] grid grid-cols-[repeat(auto-fit,minmax(min(190px,100%),1fr))]`}>
      {items.map((f, i) => (
        <div key={f.label} className={`px-5 py-4 flex flex-col gap-1 ${i < items.length - 1 ? "border-l border-[#E4E7EC]" : ""}`}>
          <span className={`text-[13px] ${f.tone || muted}`}>{f.label}</span>
          <span className={`text-[22px] font-bold ${f.tone}`}>{f.value}</span>
        </div>
      ))}
    </section>
  );
}

function BookingTable({ rows, pitchName }: { rows: Row[]; pitchName: (id: string) => string }) {
  return (
    <div className="overflow-x-auto" tabIndex={0} role="region" aria-label="جدول حجوزات النهارده">
      <table className="w-full min-w-[560px] border-collapse text-sm">
        <thead>
          <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
            <th scope="col" className="px-5 py-2.5 font-semibold">الميعاد</th>
            <th scope="col" className="px-3 py-2.5 font-semibold">الملعب</th>
            <th scope="col" className="px-3 py-2.5 font-semibold">العميل</th>
            <th scope="col" className="px-3 py-2.5 font-semibold">العربون</th>
            <th scope="col" className="px-5 py-2.5 font-semibold">الحالة</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((b) => (
            <tr key={b.bookingId} className="border-t border-[#EEF0F5]">
              <td className="px-5 py-3 font-semibold whitespace-nowrap">{cairoTime(b.startUtc)}</td>
              <td className="p-3 whitespace-nowrap">{pitchName(b.pitchId)}</td>
              <td className="p-3">
                {b.customerName || "—"}
                {b.isManual && <span className={`text-xs ${muted}`}> · يدوي</span>}
              </td>
              <td className="p-3 whitespace-nowrap text-[#333B4A]">
                {b.depositAmount > 0 ? `${money(b.depositAmount, b.currency)} — ` : ""}
                {DEPOSIT_STATE[b.depositState] ?? b.depositState}
              </td>
              <td className="px-5 py-3">
                <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ProofsCard({ rows, pitchName }: { rows: Row[]; pitchName: (id: string) => string }) {
  return (
    <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`} aria-labelledby="proofs-title">
      <div className="flex justify-between items-center">
        <h2 id="proofs-title" className="text-base font-bold">
          إيصالات بانتظارك
        </h2>
        <Link href="/reservations/proof-review" className="text-xs font-semibold text-navy-900">
          الكل
        </Link>
      </div>
      {rows.length === 0 && <p className={`text-sm ${muted}`}>مفيش إيصالات محتاجة مراجعة.</p>}
      {rows.slice(0, 4).map((p) => (
        <Link
          key={p.bookingId}
          href={`/reservations/proof-review?booking=${p.bookingId}`}
          className="flex gap-3 items-center p-2.5 rounded-[10px] bg-[#F8F9FB] text-slate-900 no-underline hover:bg-[#F1F3F7]"
        >
          <span className="w-10 h-12 rounded-md bg-[repeating-linear-gradient(135deg,#DCE0E8_0_5px,#EEF0F5_5px_10px)]" aria-hidden="true" />
          <span className="flex-1 flex flex-col gap-0.5 min-w-0">
            <span className="text-sm font-bold truncate">
              {p.customerName || "لاعب"} · {money(p.depositAmount, p.currency)}
            </span>
            <span className={`text-xs ${muted}`}>
              {pitchName(p.pitchId)} · {cairoDateTime(p.startUtc)}
            </span>
          </span>
          {p.deadlineUtc && <span className="text-xs font-bold text-[#7A4D00] whitespace-nowrap">{timeLeft(p.deadlineUtc)}</span>}
        </Link>
      ))}
    </section>
  );
}

function OccupancyCard({
  points,
  range,
  setRange,
}: {
  points: Schemas["OccupancyPoint"][];
  range: "this" | "last";
  setRange: (r: "this" | "last") => void;
}) {
  const label = points.map((p) => `${WEEKDAY_SHORT[new Date(`${p.date}T12:00:00Z`).getUTCDay()]} ${Math.round(p.percent)}`).join("، ");
  return (
    <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-3.5`} aria-labelledby="occupancy-title">
      <div className="flex justify-between items-center gap-2">
        <h2 id="occupancy-title" className="text-base font-bold">
          الإشغال
        </h2>
        <div className="flex gap-1 bg-[#F4F5F7] rounded-lg p-[3px]" role="group" aria-label="الفترة">
          {(
            [
              ["this", "آخر ٧ أيام"],
              ["last", "اللي قبلها"],
            ] as const
          ).map(([id, text]) => (
            <button
              key={id}
              type="button"
              aria-pressed={range === id}
              onClick={() => setRange(id)}
              className={`min-h-[30px] px-2.5 rounded-md text-xs ${
                range === id ? "bg-white text-navy-900 font-bold shadow-[0_1px_2px_rgba(15,27,61,0.12)]" : `bg-transparent ${muted}`
              }`}
            >
              {text}
            </button>
          ))}
        </div>
      </div>
      <div role="img" aria-label={`نسبة الإشغال اليومية بالمئة: ${label}`} className="grid grid-cols-7 gap-2 items-end h-[150px]">
        {points.map((p, i) => (
          <div key={p.date} className="flex flex-col items-center gap-1 h-full justify-end">
            <span className="text-[11px] text-[#3B4352]">{percent(p.percent)}</span>
            <div
              className={`w-full max-w-[32px] rounded-t-md rounded-b-sm ${i === points.length - 1 && range === "this" ? "bg-brand-red" : "bg-navy-900"}`}
              style={{ height: Math.max(2, Math.round(p.percent * 1.15)) }}
            />
          </div>
        ))}
      </div>
      <div className={`grid grid-cols-7 gap-2 text-[11px] ${muted} text-center -mt-1.5`} aria-hidden="true">
        {points.map((p) => (
          <span key={p.date}>{WEEKDAY_SHORT[new Date(`${p.date}T12:00:00Z`).getUTCDay()]}</span>
        ))}
      </div>
    </section>
  );
}

function UpcomingCard({ rows, pitchName }: { rows: Row[]; pitchName: (id: string) => string }) {
  return (
    <section className={`flex-[1_1_320px] min-w-0 ${card} rounded-[18px] px-5 py-4 flex flex-col gap-2.5`} aria-labelledby="upcoming-title">
      <h2 id="upcoming-title" className="text-base font-bold">
        الحجوزات الجاية
      </h2>
      {rows.length === 0 && <p className={`text-sm ${muted}`}>مفيش حجوزات جاية.</p>}
      {rows.map((b) => (
        <div key={b.bookingId} className="flex items-center gap-3 text-sm">
          <span className="font-semibold whitespace-nowrap w-[120px]">{cairoDateTime(b.startUtc)}</span>
          <span className="flex-1 min-w-0 truncate">
            {b.customerName || "—"} · {pitchName(b.pitchId)}
          </span>
          <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
        </div>
      ))}
    </section>
  );
}

function RevenueTrend({ points }: { points: Schemas["RevenuePoint"][] }) {
  const max = Math.max(1, ...points.map((p) => p.confirmedValue));
  const total = points.reduce((sum, p) => sum + p.confirmedValue, 0);
  return (
    <section className={`flex-[1_1_420px] min-w-0 ${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`} aria-labelledby="trend-title">
      <div className="flex justify-between items-center">
        <h2 id="trend-title" className="text-base font-bold">
          الإيراد المؤكد — آخر ٣٠ يوم
        </h2>
        <span className="text-sm font-bold">{money(total)}</span>
      </div>
      <div role="img" aria-label={`إجمالي الإيراد المؤكد في آخر ٣٠ يوم ${money(total)}`} className="flex items-end gap-[3px] h-[120px]">
        {points.map((p) => (
          <div
            key={p.date}
            title={`${p.date}: ${money(p.confirmedValue)}`}
            className="flex-1 bg-navy-900/80 rounded-t-sm"
            style={{ height: `${Math.max(2, (p.confirmedValue / max) * 100)}%` }}
          />
        ))}
      </div>
    </section>
  );
}

const ACTIVITY_TEXT: Record<number, string> = {
  3: "رفع إيصال عربون",
  4: "الإيصال قيد المراجعة",
  5: "اتأكد الحجز",
  6: "اترفض الإيصال",
  7: "انتهت مهلة العربون",
  8: "اتلغى الحجز",
  9: "اكتمل الحجز",
  10: "اتسجل عدم حضور",
};

function ActivityCard({ items }: { items: Schemas["DashboardActivity"][] }) {
  return (
    <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-2.5`} aria-labelledby="activity-title">
      <h2 id="activity-title" className="text-base font-bold">
        آخر النشاط
      </h2>
      {items.length === 0 && <p className={`text-sm ${muted}`}>مفيش نشاط لسه.</p>}
      {items.map((a) => (
        <div key={`${a.bookingId}-${a.changedAtUtc}-${a.toStatus}`} className="flex gap-2.5 text-sm items-center">
          <span
            className={`w-2 h-2 rounded-full ${BOOKING_STATUS[a.toStatus].tone === "bad" ? "bg-[#A8261C]" : BOOKING_STATUS[a.toStatus].tone === "ok" ? "bg-[#1B6E3E]" : "bg-brand-gold"}`}
            aria-hidden="true"
          />
          <span className="flex-1">
            {ACTIVITY_TEXT[a.toStatus] ?? BOOKING_STATUS[a.toStatus].label} — <span dir="ltr">{a.reference}</span>
            {a.reason ? <span className={muted}> · {a.reason}</span> : null}
          </span>
          <span className={`text-xs ${muted} whitespace-nowrap`}>{cairoDateTime(a.changedAtUtc)}</span>
        </div>
      ))}
    </section>
  );
}

function SkeletonGrid() {
  return (
    <div className="grid gap-3 grid-cols-2 md:grid-cols-4" aria-busy="true" aria-label="جاري التحميل">
      {[0, 1, 2, 3].map((i) => (
        <div key={i} className={`${card} rounded-[18px] p-4 h-[112px] animate-pulse bg-[#F4F5F7]`} />
      ))}
    </div>
  );
}

function EmptyVenue() {
  return (
    <div className={`${card} rounded-[18px] p-6 flex flex-col items-start gap-3`}>
      <h2 className="text-lg font-bold">لسه مضفتش ملعب</h2>
      <p className={`text-sm ${muted}`}>ابدأ بإضافة بيانات الملعب ومواعيده عشان الحجوزات تبدأ توصلك.</p>
      <Link href="/setup" className={`min-h-[44px] px-[18px] rounded-[10px] bg-brand-red text-white text-sm font-bold flex items-center ${redShadow}`}>
        إعداد الملعب
      </Link>
    </div>
  );
}
