"use client";

import React, { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Page, PageHeader, SearchBox, Segmented, btnOutline, card, money, muted } from "@/components/ui";
import { Field, QueryState, TextInput, num } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { AlertTriangle, Coins, Download, Info, Percent, Wallet } from "lucide-react";

const METHOD: Record<string, string> = { Cash: "كاش", BankTransfer: "تحويل بنكي", InstaPay: "إنستاباي", MobileWallet: "محفظة" };

/** How each figure is computed. Kept in step with AdminBookingService.GetVenuesFinancialSummaryAsync. */
const DEFINITIONS: [string, string][] = [
  ["إجمالي الحجز", "مجموع سعر الحجوزات المؤكدة واللي اتلعبت في الفترة. الحجز اللي لسه مستني العربون مش محسوب."],
  ["عمولة المنصة", "عمولة ملعبك على الحجوزات اللي جات من التطبيق. الحجز اليدوي اللي الملعب سجله بنفسه مالوش عمولة."],
  ["العربون المحصّل", "عربون الحجوزات المؤكدة بس. عربون الحجز اللي لسه مستني الإيصال مش محسوب لأنه ما اتدفعش."],
  ["صافي مستحقات الملعب", "نصيب الملعب من العربون: العربون ناقص عمولة المنصة. لو العمولة أكبر من العربون بيطلع بالسالب، يعني الملعب عليه الفرق للمنصة."],
  ["المدفوع في الملعب", "باقي سعر الحجز اللي اللاعب بيدفعه للملعب لما ييجي. رقم للعلم بس، مش داخل في المستحقات."],
  ["إحالات استرداد العربون", "عرابين لازم ترجع للاعبين لأن الملعب لغى الحجز."],
  ["MRR", "الإيراد الشهري المتكرر من اشتراكات الملاك النشطة وفترة السماح (السنوي ÷ ١٢)، من غير التجربة."],
];

const RANGES: [string, string][] = [
  ["7", "٧ أيام"],
  ["30", "٣٠ يوم"],
  ["90", "٣ شهور"],
  ["custom", "تاريخ معيّن"],
];

interface VenueFinancialSummaryItem {
  venueId: string;
  venueName: string;
  totalBookings: number;
  confirmedBookings: number;
  cancelledBookings: number;
  pendingConfirmationBookings: number;
  totalGrossAmount: number;
  totalDepositAmount: number;
  totalPlatformCommission: number;
  totalVenueNetAmount: number;
  totalDueAtVenueCash: number;
  venueDepositShare?: number;
  pendingRefundsCount: number;
  pendingRefundsAmount: number;
  manualBookings?: number;
  commissionCollected?: number;
  commissionOutstanding?: number;
}

function isoDay(offsetDays: number) {
  return new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

/** Deposit minus commission, signed. Older API responses without the field get the same formula. */
const depositShareOf = (v: VenueFinancialSummaryItem) => v.venueDepositShare ?? v.totalDepositAmount - v.totalPlatformCommission;

/** A signed amount: negative shows as "عليه" so a venue's debt never reads like money it is owed. */
function Signed({ value }: { value: number }) {
  return value < 0 ? <span className="text-[#A8261C]">عليه {money(-value)}</span> : <>{money(value)}</>;
}

export default function ReportsPage() {
  const [range, setRange] = useState("30");
  const [customFrom, setCustomFrom] = useState(isoDay(-29));
  const [customTo, setCustomTo] = useState(isoDay(0));
  const [venueSearch, setVenueSearch] = useState("");
  const from = range === "custom" ? customFrom : isoDay(-(Number(range) - 1));
  const to = range === "custom" ? customTo : isoDay(0);
  const valid = from <= to;

  const dashboard = useQuery({
    queryKey: ["reports-dashboard", from, to],
    enabled: valid,
    queryFn: () => unwrap(api.GET("/api/v1/admin/dashboard", { params: { query: { from, to } } })),
  });

  const reconciliation = useQuery({
    queryKey: ["reconciliation", from, to],
    enabled: valid,
    queryFn: () => unwrap(api.GET("/api/v1/admin/invoices/reconciliation", { params: { query: { from, to } } })),
  });

  const summary = useQuery({
    queryKey: ["admin-venues-financial-summary", from, to],
    enabled: valid,
    queryFn: () =>
      unwrap(
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (api.GET as any)("/api/v1/admin/bookings/financial-summary", {
          params: { query: { fromUtc: `${from}T00:00:00Z`, toUtc: `${to}T23:59:59Z` } },
        }),
      ) as Promise<VenueFinancialSummaryItem[]>,
  });

  const venues = useMemo(() => summary.data ?? [], [summary.data]);
  const shown = useMemo(() => {
    const term = venueSearch.trim().toLowerCase();
    const list = term ? venues.filter((v) => v.venueName.toLowerCase().includes(term)) : venues;
    return [...list].sort((a, b) => b.totalGrossAmount - a.totalGrossAmount);
  }, [venues, venueSearch]);

  const t = useMemo(
    () =>
      venues.reduce(
        (a, v) => ({
          bookings: a.bookings + v.totalBookings,
          confirmed: a.confirmed + v.confirmedBookings,
          cancelled: a.cancelled + v.cancelledBookings,
          manual: a.manual + (v.manualBookings ?? 0),
          gross: a.gross + v.totalGrossAmount,
          deposits: a.deposits + v.totalDepositAmount,
          commission: a.commission + v.totalPlatformCommission,
          collected: a.collected + (v.commissionCollected ?? 0),
          outstanding: a.outstanding + (v.commissionOutstanding ?? 0),
          depositShare: a.depositShare + depositShareOf(v),
          dueAtVenue: a.dueAtVenue + v.totalDueAtVenueCash,
          venueNet: a.venueNet + v.totalVenueNetAmount,
          refunds: a.refunds + v.pendingRefundsAmount,
          refundsCount: a.refundsCount + v.pendingRefundsCount,
        }),
        { bookings: 0, confirmed: 0, cancelled: 0, manual: 0, gross: 0, deposits: 0, commission: 0, collected: 0, outstanding: 0, depositShare: 0, dueAtVenue: 0, venueNet: 0, refunds: 0, refundsCount: 0 },
      ),
    [venues],
  );

  const exportCsv = () => {
    if (venues.length === 0) return;
    const headers = ["اسم الملعب", "إجمالي الحجوزات", "حجوزات مؤكدة", "حجوزات ملغية", "حجوزات يدوية", "إجمالي الحجز (ج.م)", "العربون المحصل (ج.م)", "عمولة المنصة (ج.م)", "صافي مستحقات الملعب = العربون - العمولة (ج.م)", "إحالات استرداد معلقة (عدد)", "مبالغ الاسترداد المعلقة (ج.م)", "المدفوع في الملعب - للعلم (ج.م)"];
    const rows = venues.map((v) => [
      `"${v.venueName.replace(/"/g, '""')}"`,
      v.totalBookings,
      v.confirmedBookings,
      v.cancelledBookings,
      v.manualBookings ?? 0,
      v.totalGrossAmount.toFixed(2),
      v.totalDepositAmount.toFixed(2),
      v.totalPlatformCommission.toFixed(2),
      depositShareOf(v).toFixed(2),
      v.pendingRefundsCount,
      v.pendingRefundsAmount.toFixed(2),
      v.totalDueAtVenueCash.toFixed(2),
    ]);
    const csv = "﻿" + [headers.join(","), ...rows.map((r) => r.join(","))].join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8;" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `malabak-venues-financial-summary-${from}-to-${to}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const th = "py-2.5 px-2.5 font-semibold whitespace-nowrap";
  const td = "py-3 px-2.5 whitespace-nowrap";

  return (
    <Page className="gap-5">
      <PageHeader
        title="الإيرادات والحسابات والتقارير المالية"
        sub="عمولات المنصة، والعرابين، وصافي مستحقات الملاعب بعد العمولة، وإحالات الاسترداد."
      >
        <button type="button" onClick={exportCsv} disabled={venues.length === 0} className={`${btnOutline} text-[13px]`}>
          <Download className="size-4" aria-hidden />
          تصدير تقرير الملاعب (CSV)
        </button>
      </PageHeader>

      <div className="flex flex-wrap items-end gap-3">
        <Segmented label="الفترة" items={RANGES} value={range} onChange={setRange} />
        {range === "custom" && (
          <div className="flex flex-wrap items-end gap-2">
            <Field label="من تاريخ">
              <TextInput type="date" value={customFrom} max={customTo} onChange={(e) => setCustomFrom(e.target.value)} />
            </Field>
            <Field label="إلى تاريخ">
              <TextInput type="date" value={customTo} min={customFrom} onChange={(e) => setCustomTo(e.target.value)} />
            </Field>
          </div>
        )}
        {!valid && <span className="text-[13px] text-[#A8261C]">تاريخ البداية لازم يكون قبل النهاية.</span>}
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-3">
        <Kpi
          icon={<Percent className="size-4" aria-hidden />}
          tone="red"
          label="عمولة المنصة"
          value={money(t.commission)}
          hint={t.commission > 0 ? `اتحصّل ${money(t.collected)} · لسه ${money(t.outstanding)}` : "مفيش عمولة في الفترة دي"}
        />
        <Kpi
          icon={<Coins className="size-4" aria-hidden />}
          tone="navy"
          label="إجمالي العرابين المحصّلة"
          value={money(t.deposits)}
          hint={`عربون ${num(t.confirmed)} حجز مؤكد`}
        />
        <Kpi
          icon={<Wallet className="size-4" aria-hidden />}
          tone="green"
          label="صافي مستحقات الملاعب"
          value={<Signed value={t.depositShare} />}
          hint="نصيب الملاعب من العربون بعد خصم العمولة"
        />
        <Kpi
          icon={<AlertTriangle className="size-4" aria-hidden />}
          tone="amber"
          label="إحالات استرداد معلّقة"
          value={money(t.refunds)}
          hint={t.refundsCount ? `${num(t.refundsCount)} حجز لغاه الملعب` : "مفيش"}
        />
      </div>

      <p className={`text-[13px] ${muted} -mt-1`}>
        للعلم: المدفوع في الملاعب كاش <strong className="text-slate-700">{money(t.dueAtVenue)}</strong> (مش داخل في المستحقات).
      </p>

      <section className={`${card} rounded-2xl p-5 flex flex-col gap-4`}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-col gap-0.5">
            <h2 className="text-[16px] font-bold">التقرير المالي لكل ملعب</h2>
            <span className={`text-[13px] ${muted}`}>الحجوزات، والعرابين، وعمولة المنصة، وصافي مستحقات كل ملعب في الفترة.</span>
          </div>
          <div className="w-full sm:w-72">
            <SearchBox value={venueSearch} onChange={setVenueSearch} placeholder="ابحث باسم الملعب" />
          </div>
        </div>

        {summary.isLoading ? (
          <div className="flex flex-col gap-2" aria-busy="true">
            {[0, 1, 2].map((i) => (
              <div key={i} className="h-12 rounded-xl bg-slate-100 animate-pulse" />
            ))}
          </div>
        ) : summary.isError ? (
          <p className="p-6 text-center text-sm text-[#A8261C]">مقدرناش نجيب الأرقام. جرّب تاني.</p>
        ) : shown.length === 0 ? (
          <p className={`p-8 text-center text-sm ${muted} border border-dashed border-slate-200 rounded-xl`}>
            {venueSearch ? "مفيش ملعب بالاسم ده." : "مفيش حجوزات في الفترة دي."}
          </p>
        ) : (
          <div className="overflow-x-auto -mx-5 px-5">
            <table className="w-full min-w-[980px] text-[14px]">
              <thead>
                <tr className={`text-right text-[12px] ${muted} border-b border-slate-200`}>
                  <th scope="col" className={th}>الملعب</th>
                  <th scope="col" className={`${th} text-center`}>إجمالي الحجوزات</th>
                  <th scope="col" className={`${th} text-center`}>المؤكدة</th>
                  <th scope="col" className={`${th} text-center`}>الملغية</th>
                  <th scope="col" className={th}>إجمالي الحجز</th>
                  <th scope="col" className={th}>العربون المحصّل</th>
                  <th scope="col" className={`${th} text-[#C7362A]`}>عمولة المنصة</th>
                  <th scope="col" className={`${th} text-[#1F6B3A]`}>صافي مستحقات الملعب</th>
                  <th scope="col" className={th}>إحالات الاسترداد</th>
                  <th scope="col" className={`${th} text-[11px] font-normal`}>المدفوع في الملعب (للعلم)</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {shown.map((v) => (
                  <tr key={v.venueId} className="hover:bg-slate-50/70 transition-colors">
                    <td className={`${td} font-bold text-slate-900`}>{v.venueName}</td>
                    <td className={`${td} text-center font-semibold`}>{num(v.totalBookings)}</td>
                    <td className={`${td} text-center`}>
                      {num(v.confirmedBookings)}
                      {v.manualBookings ? <span className={`block text-[11px] ${muted}`}>{num(v.manualBookings)} يدوي</span> : null}
                    </td>
                    <td className={`${td} text-center ${v.cancelledBookings ? "text-[#A8261C]" : "text-slate-400"}`}>{num(v.cancelledBookings)}</td>
                    <td className={`${td} font-semibold`}>{money(v.totalGrossAmount)}</td>
                    <td className={td}>{money(v.totalDepositAmount)}</td>
                    <td className={`${td} font-semibold text-[#C7362A]`}>{money(v.totalPlatformCommission)}</td>
                    <td className={`${td} font-bold text-[#1F6B3A] bg-[#F1F9F3]`}><Signed value={depositShareOf(v)} /></td>
                    <td className={td}>
                      {v.pendingRefundsCount > 0 ? (
                        <span className="text-[#9A5B00] font-semibold">{money(v.pendingRefundsAmount)} ({num(v.pendingRefundsCount)})</span>
                      ) : (
                        <span className="text-slate-400">لا يوجد</span>
                      )}
                    </td>
                    <td className={`${td} text-[12px] text-slate-400`}>{money(v.totalDueAtVenueCash)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t-2 border-slate-200 font-bold bg-slate-50">
                  <td className={td}>الإجمالي الكلي</td>
                  <td className={`${td} text-center`}>{num(t.bookings)}</td>
                  <td className={`${td} text-center`}>{num(t.confirmed)}</td>
                  <td className={`${td} text-center`}>{num(t.cancelled)}</td>
                  <td className={td}>{money(t.gross)}</td>
                  <td className={td}>{money(t.deposits)}</td>
                  <td className={`${td} text-[#C7362A]`}>{money(t.commission)}</td>
                  <td className={`${td} text-[#1F6B3A] bg-[#E6F4EA]`}><Signed value={t.depositShare} /></td>
                  <td className={td}>{t.refundsCount ? money(t.refunds) : "—"}</td>
                  <td className={`${td} text-[12px] font-normal text-slate-400`}>{money(t.dueAtVenue)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </section>

      <QueryState query={dashboard}>
        {(d) => (
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <Stat label="MRR (الاشتراكات الشهرية)" value={money(d.figures["mrr"] ?? 0)} />
            <Stat label="الإيراد المحصّل (اشتراكات)" value={money(d.figures["revenue.collected"] ?? 0)} />
            <Stat label="اشتراكات ملاك نشطة" value={num(d.figures["subscriptions.active"])} />
            <Stat label="ملاك في فترة التجربة" value={num(d.figures["subscriptions.trial"])} />
          </div>
        )}
      </QueryState>

      <section className={`${card} rounded-2xl p-5 flex flex-col gap-3`}>
        <h2 className="text-[16px] font-bold">مطابقة تحصيل الاشتراكات حسب طريقة الدفع</h2>
        <QueryState query={reconciliation}>
          {(r) => (
            <div className="flex flex-col">
              <div className="flex flex-wrap gap-x-5 gap-y-1 text-[14px] pb-2">
                <span>فواتير الاشتراكات: <strong>{num(r.invoices)}</strong></span>
                <span>المحصّل: <strong>{money(r.collected)}</strong></span>
                <span>الملغي: <strong>{money(r.voided)}</strong></span>
              </div>
              {r.byMethod.length === 0 ? (
                <p className={`text-[13px] ${muted} border-t border-slate-100 pt-2`}>مفيش فواتير اشتراكات في الفترة دي.</p>
              ) : (
                r.byMethod.map((m) => (
                  <div key={m.method} className="flex justify-between text-[14px] border-t border-slate-100 py-2">
                    <span>{METHOD[m.method] ?? m.method} <span className={muted}>({num(m.count)})</span></span>
                    <strong>{money(m.amount)}</strong>
                  </div>
                ))
              )}
            </div>
          )}
        </QueryState>
      </section>

      <details className={`${card} rounded-2xl p-5`}>
        <summary className="flex items-center gap-2 cursor-pointer text-[15px] font-bold list-none">
          <Info className="size-4 text-slate-500" aria-hidden />
          تعريفات المصطلحات المالية
        </summary>
        <dl className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-3 mt-4">
          {DEFINITIONS.map(([term, text]) => (
            <div key={term}>
              <dt className="text-[14px] font-bold">{term}</dt>
              <dd className={`text-[13px] ${muted} leading-relaxed`}>{text}</dd>
            </div>
          ))}
        </dl>
      </details>
    </Page>
  );
}

const TONES = {
  red: "bg-[#FDECEA] text-[#C7362A]",
  navy: "bg-[#EAEEF6] text-[#0B1A3A]",
  green: "bg-[#E6F4EA] text-[#1F6B3A]",
  amber: "bg-[#FDF3DC] text-[#9A5B00]",
} as const;

function Kpi({ icon, tone, label, value, hint }: { icon: React.ReactNode; tone: keyof typeof TONES; label: string; value: React.ReactNode; hint: string }) {
  return (
    <div className={`${card} rounded-2xl p-4 flex flex-col gap-2`}>
      <div className="flex items-center justify-between gap-2">
        <span className={`text-[13px] font-semibold ${muted}`}>{label}</span>
        <span className={`size-8 rounded-xl flex items-center justify-center ${TONES[tone]}`}>{icon}</span>
      </div>
      <strong className="text-[24px] leading-tight font-extrabold text-slate-900">{value}</strong>
      <span className={`text-[12px] ${muted} leading-snug`}>{hint}</span>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className={`${card} rounded-2xl p-4 flex flex-col gap-1`}>
      <span className={`text-[13px] ${muted}`}>{label}</span>
      <strong className="text-[20px] font-extrabold text-slate-900">{value}</strong>
    </div>
  );
}
