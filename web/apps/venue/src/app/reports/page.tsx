"use client";

import React, { useMemo, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { problemMessage } from "@malabak/api-client";
import { Page, Toast, card, chip, inputClass, muted, secondaryBtn, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { BOOKING_STATUS, addDays, cairoToday, money, percent, statusByKey } from "@/lib/booking-format";

type Report = Schemas["VenueReportDto"];
type Preset = "week" | "month" | "quarter" | "custom";

const filterChip = (on: boolean) => chip(on, "min-h-[40px] px-3.5 rounded-[10px] text-[13px]");
const DAY_SHORT = ["أحد", "إثنين", "ثلاثاء", "أربعاء", "خميس", "جمعة", "سبت"];
const GROUP = { Day: 0, Week: 1, Month: 2 } as const;

function rangeFor(preset: Exclude<Preset, "custom">): { from: string; to: string; groupBy: 0 | 1 | 2 } {
  const today = cairoToday();
  if (preset === "week") return { from: addDays(today, -6), to: today, groupBy: GROUP.Day };
  if (preset === "month") return { from: addDays(today, -29), to: today, groupBy: GROUP.Week };
  return { from: addDays(today, -89), to: today, groupBy: GROUP.Month };
}

function periodLabel(start: string, groupBy: number): string {
  const d = new Date(`${start}T12:00:00Z`);
  if (groupBy === GROUP.Month) return d.toLocaleDateString("ar-EG", { month: "long", timeZone: "UTC" });
  if (groupBy === GROUP.Week) return `أسبوع ${d.toLocaleDateString("ar-EG", { day: "numeric", month: "short", timeZone: "UTC" })}`;
  return `${DAY_SHORT[d.getUTCDay()]} ${toArabicDigits(d.getUTCDate())}`;
}

export default function ReportsPage() {
  const { venue, venueId } = useVenue();
  const [preset, setPreset] = useState<Preset>("week");
  const [custom, setCustom] = useState(() => ({ from: addDays(cairoToday(), -13), to: cairoToday() }));
  const [pitchId, setPitchId] = useState<string>("");
  const [toast, setToast] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  const range = useMemo(() => {
    if (preset !== "custom") return rangeFor(preset);
    const days = (Date.parse(custom.to) - Date.parse(custom.from)) / 86_400_000;
    return { ...custom, groupBy: (days > 92 ? GROUP.Month : days > 31 ? GROUP.Week : GROUP.Day) as 0 | 1 | 2 };
  }, [preset, custom]);

  const report = useQuery({
    queryKey: ["report", venueId, range.from, range.to, range.groupBy, pitchId],
    enabled: !!venueId && range.from <= range.to,
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/venue-manager/venues/{venueId}/reports", {
          params: { path: { venueId: venueId! }, query: { from: range.from, to: range.to, groupBy: range.groupBy, pitchId: pitchId || undefined } },
        }),
      ),
  });

  const exportCsv = async () => {
    if (!venueId) return;
    setExporting(true);
    try {
      const { data, error, response } = await api.GET("/api/v1/venue-manager/venues/{venueId}/reports/bookings.csv", {
        params: { path: { venueId }, query: { from: range.from, to: range.to, pitchId: pitchId || undefined } },
        parseAs: "blob",
      });
      if (!response.ok || !data) throw new Error(problemMessage(error));
      const url = URL.createObjectURL(data as Blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `bookings-${range.from}-${range.to}.csv`;
      link.click();
      URL.revokeObjectURL(url);
      setToast("اتنزّل ملف الحجوزات.");
    } catch (e) {
      setToast((e as Error).message);
    } finally {
      setExporting(false);
    }
  };

  const pitches = venue?.pitches ?? [];

  return (
    <Page className="gap-5">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_260px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">التقارير</h1>
          <span className={`text-[13px] ${muted}`}>الأرقام بتوقيت القاهرة، والحجز بيتحسب في يوم ميعاده.</span>
        </div>
        <button type="button" onClick={exportCsv} disabled={exporting || !venueId} className={`${secondaryBtn} rounded-xl disabled:opacity-50`}>
          {exporting ? "بيتجهّز…" : "تصدير CSV"}
        </button>
      </div>

      <div className="flex flex-wrap gap-2 items-center" role="group" aria-label="الفترة">
        {(
          [
            ["week", "آخر ٧ أيام"],
            ["month", "آخر ٣٠ يوم"],
            ["quarter", "آخر ٣ شهور"],
            ["custom", "فترة مخصصة"],
          ] as const
        ).map(([id, label]) => (
          <button key={id} type="button" aria-pressed={preset === id} onClick={() => setPreset(id)} className={filterChip(preset === id)}>
            {label}
          </button>
        ))}
        {preset === "custom" && (
          <span className="flex flex-wrap gap-2 items-center">
            <label className="flex items-center gap-1.5 text-[13px]">
              من
              <input type="date" value={custom.from} max={custom.to} onChange={(e) => setCustom({ ...custom, from: e.target.value })} className={inputClass} />
            </label>
            <label className="flex items-center gap-1.5 text-[13px]">
              إلى
              <input type="date" value={custom.to} min={custom.from} onChange={(e) => setCustom({ ...custom, to: e.target.value })} className={inputClass} />
            </label>
          </span>
        )}
        <label className="flex items-center gap-1.5 text-[13px] ms-auto">
          الملعب
          <select value={pitchId} onChange={(e) => setPitchId(e.target.value)} className={inputClass}>
            <option value="">كل الملاعب</option>
            {pitches.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      {report.isError && (
        <div role="alert" className="bg-[#FDECEA] text-[#A8261C] rounded-[14px] px-4 py-3 text-sm flex gap-3">
          <span className="flex-1">{(report.error as Error).message}</span>
          <button type="button" className="font-bold underline" onClick={() => report.refetch()}>
            جرّب تاني
          </button>
        </div>
      )}
      {report.isLoading && <div className={`${card} rounded-[20px] h-[220px] animate-pulse bg-[#F4F5F7]`} aria-busy="true" aria-label="جاري التحميل" />}
      {report.data && <ReportBody report={report.data} />}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function ReportBody({ report }: { report: Report }) {
  const r = report.revenue;
  const totalBookings = report.bookings.reduce((sum, b) => sum + b.total, 0);
  const maxBooked = Math.max(1, ...report.revenueSeries.map((b) => b.booked));
  const statuses = Object.entries(report.statusDistribution).sort((a, b) => b[1] - a[1]);
  const hours = [...report.peakHours].sort((a, b) => b.bookings - a.bookings).slice(0, 8);
  const maxHour = Math.max(1, ...hours.map((h) => h.bookings));

  const kpis: [string, number, string, string][] = [
    ["قيمة الحجوزات", r.booked, "الحجوزات اللي الملعب قبلها", "bg-[#C9D3E6]"],
    ["عربون مستلم", r.depositsReceived, "اتأكد بإثبات دفع", "bg-navy-900"],
    ["متبقي يتحصّل في الملعب", r.outstanding, "للماتشات المؤكدة الجاية", "bg-brand-gold"],
    ["إيرادات محققة", r.recognized, "ماتشات اتلعبت + عربون عدم الحضور", "bg-[#2E8B55]"],
    ["عمولة المنصة", r.platformCommission, "المبلغ المحصل للمنصة أونلاين", "bg-[#8B5CF6]"],
    ["صافي إيراد الملعب", r.venueNetRevenue, "صافي إيجار الملعب بعد الخصومات", "bg-[#0EA5A4]"],
  ];

  return (
    <>
      <div className="grid gap-3 grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        {kpis.map(([label, value, hint, dot]) => (
          <div key={label} className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
            <span className={`text-[13px] ${muted} flex items-center gap-1.5`}>
              <span className={`w-2.5 h-2.5 rounded-full ${dot}`} aria-hidden="true" />
              {label}
            </span>
            <span className="text-[24px] font-bold">{money(value, r.currency)}</span>
            <span className={`text-xs ${muted}`}>{hint}</span>
          </div>
        ))}
      </div>
      {r.pending > 0 && (
        <p className="text-[13px] bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-4 py-2.5">
          فيه {money(r.pending, r.currency)} حجوزات لسه مستنية عربون أو مراجعة — مش محسوبة في الأرقام اللي فوق.
        </p>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_460px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`} aria-labelledby="revenue-chart">
          <div className="flex flex-wrap items-center gap-3">
            <h2 id="revenue-chart" className="flex-1 text-[17px] font-bold">
              قيمة الحجوزات مقابل الإيراد المحقق
            </h2>
            <span className={`text-xs ${muted}`}>{toArabicDigits(totalBookings)} حجز في الفترة</span>
          </div>
          <div className="flex items-end gap-2 h-[200px] overflow-x-auto" role="img" aria-label={`قيمة الحجوزات ${money(r.booked)} والإيراد المحقق ${money(r.recognized)}`}>
            {report.revenueSeries.map((b) => (
              <div key={b.periodStart} className="flex-1 min-w-[28px] h-full flex flex-col justify-end items-center gap-1">
                <div className="w-full flex items-end gap-[3px] h-[170px]">
                  <div className="flex-1 bg-[#C9D3E6] rounded-t-sm" style={{ height: `${(b.booked / maxBooked) * 100}%` }} title={money(b.booked)} />
                  <div className="flex-1 bg-[#2E8B55] rounded-t-sm" style={{ height: `${(b.recognized / maxBooked) * 100}%` }} title={money(b.recognized)} />
                </div>
                <span className={`text-[10px] ${muted} whitespace-nowrap`}>{periodLabel(b.periodStart, report.groupBy)}</span>
              </div>
            ))}
          </div>
        </section>

        <section className={`flex-[1_1_300px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`} aria-labelledby="utilization">
          <h2 id="utilization" className="text-[17px] font-bold">
            نسبة الإشغال لكل ملعب
          </h2>
          {report.pitchUtilization.length === 0 && <p className={`text-sm ${muted}`}>مفيش ملاعب نشطة.</p>}
          {report.pitchUtilization.map((p) => (
            <div key={p.pitchId} className="flex flex-col gap-1">
              <div className="flex justify-between text-sm">
                <span className="font-semibold">{p.name}</span>
                <span>{percent(p.percent)}</span>
              </div>
              <div className="h-2 rounded bg-[#E4E7EC] overflow-hidden" aria-hidden="true">
                <div className="h-full bg-navy-900" style={{ width: `${Math.min(100, p.percent)}%` }} />
              </div>
            </div>
          ))}

          <h2 className="mt-3 text-[17px] font-bold">أكتر الساعات طلب</h2>
          {hours.length === 0 && <p className={`text-sm ${muted}`}>مفيش حجوزات في الفترة.</p>}
          {hours.map((h) => (
            <div key={`${h.dayOfWeek}-${h.hour}`} className="flex items-center gap-2 text-sm">
              <span className="w-[110px] whitespace-nowrap">
                {DAY_SHORT[h.dayOfWeek]} {toArabicDigits(h.hour % 12 === 0 ? 12 : h.hour % 12)} {h.hour < 12 ? "ص" : "م"}
              </span>
              <div className="flex-1 h-2 rounded bg-[#E4E7EC] overflow-hidden" aria-hidden="true">
                <div className="h-full bg-brand-gold" style={{ width: `${(h.bookings / maxHour) * 100}%` }} />
              </div>
              <span className="w-8 text-left">{toArabicDigits(h.bookings)}</span>
            </div>
          ))}
        </section>
      </div>

      <section className={`${card} rounded-[20px] px-5 py-4 flex flex-col gap-2`} aria-labelledby="statuses">
        <h2 id="statuses" className="text-[17px] font-bold">
          توزيع حالات الحجز
        </h2>
        {statuses.length === 0 && <p className={`text-sm ${muted}`}>مفيش حجوزات في الفترة.</p>}
        <div className="flex flex-wrap gap-2">
          {statuses.map(([key, count]) => {
            const status = statusByKey(key);
            const meta = status ? BOOKING_STATUS[status] : null;
            return (
              <span key={key} className="text-[13px] rounded-full px-3 py-1.5 bg-surface">
                {meta?.label ?? key}: <b>{toArabicDigits(count)}</b>
              </span>
            );
          })}
        </div>
      </section>

      <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 overflow-x-auto`} aria-label="الحجوزات حسب الفترة" tabIndex={0}>
        <table className="w-full min-w-[560px] border-collapse text-sm">
          <thead>
            <tr className={`${muted} text-xs text-right`}>
              <th scope="col" className="py-2.5 font-semibold">الفترة</th>
              <th scope="col" className="py-2.5 font-semibold">الإجمالي</th>
              <th scope="col" className="py-2.5 font-semibold">مؤكد</th>
              <th scope="col" className="py-2.5 font-semibold">مكتمل</th>
              <th scope="col" className="py-2.5 font-semibold">معلق</th>
              <th scope="col" className="py-2.5 font-semibold">ملغي</th>
              <th scope="col" className="py-2.5 font-semibold">لم يحضر</th>
            </tr>
          </thead>
          <tbody>
            {report.bookings.map((b) => (
              <tr key={b.periodStart} className="border-t border-[#EEF0F5]">
                <td className="py-2">{periodLabel(b.periodStart, report.groupBy)}</td>
                <td className="py-2 font-bold">{toArabicDigits(b.total)}</td>
                <td className="py-2">{toArabicDigits(b.confirmed)}</td>
                <td className="py-2">{toArabicDigits(b.completed)}</td>
                <td className="py-2">{toArabicDigits(b.pending)}</td>
                <td className="py-2">{toArabicDigits(b.cancelled)}</td>
                <td className="py-2">{toArabicDigits(b.noShows)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </>
  );
}
