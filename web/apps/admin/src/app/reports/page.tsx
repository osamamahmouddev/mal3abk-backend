"use client";

import React, { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Page, PageHeader, SearchBox, Segmented, btnOutline, card, money, muted, toArabicDigits } from "@/components/ui";
import { Field, QueryState, TextInput, num } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { ArrowDownLeft, ArrowUpRight, Download, Info } from "lucide-react";

const METHOD: Record<string, string> = { Cash: "كاش", BankTransfer: "تحويل بنكي", InstaPay: "إنستاباي", MobileWallet: "محفظة" };

/** How each figure is computed. Kept in step with AdminBookingService.GetVenuesFinancialSummaryAsync. */
const DEFINITIONS: [string, string][] = [
  ["قيمة الحجوزات", "مجموع سعر الحجوزات المؤكدة واللي اتلعبت في الفترة. الحجز اللي لسه مستني العربون مش محسوب."],
  ["عمولة المنصة", "عمولة ملعبك على الحجوزات اللي جات من التطبيق. الحجز اليدوي اللي الملعب سجله بنفسه مالوش عمولة."],
  ["صافي الملاعب", "قيمة الحجوزات ناقص العمولة: اللي من حق أصحاب الملاعب في الآخر."],
  ["فين الفلوس", "العربون اللي اللاعب حوّله على محفظة الملعب بيروح للملعب على طول، والباقي بيتدفع كاش في الملعب. المنصة ماسكة بس اللي اتدفع أونلاين."],
  ["التسوية", "اللي المنصة ماسكاه أونلاين بعد خصم العمولة بيتحوّل للملعب، والعمولة اللي لسه ما اتحصلتش الملعب بيدفعها للمنصة. الرقم النهائي هو الفرق."],
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
  pendingRefundsCount: number;
  pendingRefundsAmount: number;
  manualBookings?: number;
  platformCollectedOnline?: number;
  commissionCollected?: number;
  commissionOutstanding?: number;
  platformOwesVenue?: number;
}

function isoDay(offsetDays: number) {
  return new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

/** Positive: the platform pays the venue. Negative: the venue pays the platform. */
const settlementOf = (v: VenueFinancialSummaryItem) => (v.platformOwesVenue ?? 0) - (v.commissionOutstanding ?? 0);

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
          pending: a.pending + v.pendingConfirmationBookings,
          manual: a.manual + (v.manualBookings ?? 0),
          gross: a.gross + v.totalGrossAmount,
          commission: a.commission + v.totalPlatformCommission,
          collected: a.collected + (v.commissionCollected ?? 0),
          outstanding: a.outstanding + (v.commissionOutstanding ?? 0),
          venueNet: a.venueNet + v.totalVenueNetAmount,
          deposits: a.deposits + v.totalDepositAmount,
          online: a.online + (v.platformCollectedOnline ?? 0),
          cash: a.cash + v.totalDueAtVenueCash,
          owesVenues: a.owesVenues + (v.platformOwesVenue ?? 0),
          refunds: a.refunds + v.pendingRefundsAmount,
          refundsCount: a.refundsCount + v.pendingRefundsCount,
        }),
        { bookings: 0, confirmed: 0, pending: 0, manual: 0, gross: 0, commission: 0, collected: 0, outstanding: 0, venueNet: 0, deposits: 0, online: 0, cash: 0, owesVenues: 0, refunds: 0, refundsCount: 0 },
      ),
    [venues],
  );

  const exportCsv = () => {
    if (venues.length === 0) return;
    const headers = ["الملعب", "الحجوزات", "المؤكدة", "يدوي", "قيمة الحجوزات", "عمولة المنصة", "العمولة المحصلة", "العمولة المتبقية", "صافي الملعب", "العربون", "كاش في الملعب", "اتدفع أونلاين للمنصة", "التسوية (+ للملعب / - للمنصة)", "استرداد معلق"];
    const rows = venues.map((v) => [
      `"${v.venueName.replace(/"/g, '""')}"`,
      v.totalBookings,
      v.confirmedBookings,
      v.manualBookings ?? 0,
      v.totalGrossAmount.toFixed(2),
      v.totalPlatformCommission.toFixed(2),
      (v.commissionCollected ?? 0).toFixed(2),
      (v.commissionOutstanding ?? 0).toFixed(2),
      v.totalVenueNetAmount.toFixed(2),
      v.totalDepositAmount.toFixed(2),
      v.totalDueAtVenueCash.toFixed(2),
      (v.platformCollectedOnline ?? 0).toFixed(2),
      settlementOf(v).toFixed(2),
      v.pendingRefundsAmount.toFixed(2),
    ]);
    const csv = "﻿" + [headers.join(","), ...rows.map((r) => r.join(","))].join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8;" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `malabak-venues-${from}-to-${to}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const commissionShare = t.gross > 0 ? (t.commission / t.gross) * 100 : 0;
  const netSettlement = t.owesVenues - t.outstanding;

  return (
    <Page className="gap-5">
      <PageHeader title="الإيرادات والتقارير" sub="فلوس الحجوزات بين المنصة والملاعب، وإيراد الاشتراكات.">
        <button type="button" onClick={exportCsv} disabled={venues.length === 0} className={`${btnOutline} text-[13px]`}>
          <Download className="size-4" aria-hidden />
          تصدير CSV
        </button>
      </PageHeader>

      <div className="flex flex-wrap items-end gap-3">
        <Segmented label="الفترة" items={RANGES} value={range} onChange={setRange} />
        {range === "custom" && (
          <div className="flex flex-wrap items-end gap-2">
            <Field label="من">
              <TextInput type="date" value={customFrom} max={customTo} onChange={(e) => setCustomFrom(e.target.value)} />
            </Field>
            <Field label="إلى">
              <TextInput type="date" value={customTo} min={customFrom} onChange={(e) => setCustomTo(e.target.value)} />
            </Field>
          </div>
        )}
        {!valid && <span className="text-[13px] text-[#A8261C]">تاريخ البداية لازم يكون قبل النهاية.</span>}
      </div>

      {/* Headline numbers */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-3">
        <Kpi label="قيمة الحجوزات" value={money(t.gross)} hint={`${num(t.confirmed)} حجز مؤكد${t.manual ? ` (منهم ${num(t.manual)} يدوي)` : ""}${t.pending ? ` · ${num(t.pending)} مستني العربون` : ""}`} />
        <Kpi
          label="عمولة المنصة"
          value={money(t.commission)}
          accent
          hint={`${toArabicDigits(commissionShare.toFixed(1)).replace(".", "٫")}٪ من القيمة · اتحصّل ${money(t.collected)} · لسه ${money(t.outstanding)}`}
        />
        <Kpi label="صافي الملاعب" value={money(t.venueNet)} hint="قيمة الحجوزات ناقص العمولة" />
        <Kpi
          label="استرداد معلّق"
          value={t.refundsCount ? money(t.refunds) : "مفيش"}
          hint={t.refundsCount ? `${num(t.refundsCount)} حجز اتلغى من الملعب` : "مفيش عرابين مستنية ترجع للاعبين"}
          warn={t.refundsCount > 0}
        />
      </div>

      {/* Where the money is */}
      {t.gross > 0 && (
        <section className={`${card} rounded-2xl p-5 flex flex-col gap-4`}>
          <div className="flex flex-col gap-0.5">
            <h2 className="text-[16px] font-bold">الفلوس فين؟</h2>
            <span className={`text-[13px] ${muted}`}>{money(t.gross)} قيمة الحجوزات، متقسمة حسب مين استلمها.</span>
          </div>
          <MoneySplit
            parts={[
              { label: "عربون في محافظ الملاعب", value: t.deposits - t.online, color: "#0B1A3A" },
              { label: "كاش في الملعب", value: t.cash, color: "#5B6B8C" },
              { label: "أونلاين عند المنصة", value: t.online, color: "#EB4F42" },
            ]}
            total={t.gross}
          />
          <div className={`flex items-center gap-2 rounded-xl px-4 py-3 text-[14px] font-semibold ${netSettlement >= 0 ? "bg-[#E9F6EE] text-[#1F6B3A]" : "bg-[#FDECEA] text-[#A8261C]"}`}>
            {netSettlement >= 0 ? <ArrowUpRight className="size-4 shrink-0" aria-hidden /> : <ArrowDownLeft className="size-4 shrink-0" aria-hidden />}
            {netSettlement === 0
              ? "الحساب متسوّي: مفيش فلوس على حد."
              : netSettlement > 0
                ? `المنصة عليها ${money(netSettlement)} للملاعب (أونلاين بعد خصم العمولة).`
                : `الملاعب عليها ${money(-netSettlement)} للمنصة (عمولة لسه ما اتحصلتش).`}
          </div>
        </section>
      )}

      {/* Per venue */}
      <section className={`${card} rounded-2xl p-5 flex flex-col gap-4`}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-col gap-0.5">
            <h2 className="text-[16px] font-bold">حساب كل ملعب</h2>
            <span className={`text-[13px] ${muted}`}>مرتّبين من الأعلى قيمة.</span>
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
            <table className="w-full min-w-[820px] text-[14px]">
              <thead>
                <tr className={`text-right text-[12px] ${muted} border-b border-slate-200`}>
                  <th scope="col" className="py-2.5 px-2 font-semibold">الملعب</th>
                  <th scope="col" className="py-2.5 px-2 font-semibold">الحجوزات</th>
                  <th scope="col" className="py-2.5 px-2 font-semibold">قيمة الحجوزات</th>
                  <th scope="col" className="py-2.5 px-2 font-semibold">العمولة</th>
                  <th scope="col" className="py-2.5 px-2 font-semibold">صافي الملعب</th>
                  <th scope="col" className="py-2.5 px-2 font-semibold">التسوية</th>
                  <th scope="col" className="py-2.5 px-2 font-semibold">استرداد معلّق</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {shown.map((v) => (
                  <tr key={v.venueId} className="hover:bg-slate-50/70 transition-colors">
                    <td className="py-3 px-2 font-bold text-slate-900">{v.venueName}</td>
                    <td className="py-3 px-2 whitespace-nowrap">
                      <span className="font-semibold">{num(v.confirmedBookings)}</span>
                      <span className={`text-[12px] ${muted}`}>
                        {" "}مؤكد{v.manualBookings ? ` · ${num(v.manualBookings)} يدوي` : ""}
                        {v.cancelledBookings ? ` · ${num(v.cancelledBookings)} ملغي` : ""}
                      </span>
                    </td>
                    <td className="py-3 px-2 whitespace-nowrap font-semibold">{money(v.totalGrossAmount)}</td>
                    <td className="py-3 px-2 whitespace-nowrap">
                      <span className="font-semibold text-[#C7362A]">{money(v.totalPlatformCommission)}</span>
                      {(v.commissionOutstanding ?? 0) > 0 && (
                        <span className={`block text-[11px] ${muted}`}>لسه {money(v.commissionOutstanding ?? 0)}</span>
                      )}
                    </td>
                    <td className="py-3 px-2 whitespace-nowrap font-semibold">{money(v.totalVenueNetAmount)}</td>
                    <td className="py-3 px-2 whitespace-nowrap">
                      <Settlement value={settlementOf(v)} />
                    </td>
                    <td className="py-3 px-2 whitespace-nowrap">
                      {v.pendingRefundsCount > 0 ? (
                        <span className="text-[#9A5B00] font-semibold">{money(v.pendingRefundsAmount)} ({num(v.pendingRefundsCount)})</span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
              {shown.length > 1 && (
                <tfoot>
                  <tr className="border-t-2 border-slate-200 font-bold">
                    <td className="py-3 px-2">الإجمالي</td>
                    <td className="py-3 px-2">{num(t.confirmed)} مؤكد</td>
                    <td className="py-3 px-2 whitespace-nowrap">{money(t.gross)}</td>
                    <td className="py-3 px-2 whitespace-nowrap text-[#C7362A]">{money(t.commission)}</td>
                    <td className="py-3 px-2 whitespace-nowrap">{money(t.venueNet)}</td>
                    <td className="py-3 px-2 whitespace-nowrap"><Settlement value={netSettlement} /></td>
                    <td className="py-3 px-2 whitespace-nowrap">{t.refundsCount ? money(t.refunds) : "—"}</td>
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        )}
      </section>

      {/* Subscriptions */}
      <section className={`${card} rounded-2xl p-5 flex flex-col gap-4`}>
        <h2 className="text-[16px] font-bold">إيراد الاشتراكات</h2>
        <QueryState query={dashboard}>
          {(d) => (
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
              <Stat label="MRR" value={money(d.figures["mrr"] ?? 0)} />
              <Stat label="اتحصّل في الفترة" value={money(d.figures["revenue.collected"] ?? 0)} />
              <Stat label="اشتراكات نشطة" value={num(d.figures["subscriptions.active"])} />
              <Stat label="في فترة التجربة" value={num(d.figures["subscriptions.trial"])} />
            </div>
          )}
        </QueryState>
        <QueryState query={reconciliation}>
          {(r) =>
            r.byMethod.length === 0 ? (
              <p className={`text-[13px] ${muted}`}>مفيش فواتير اشتراكات في الفترة دي.</p>
            ) : (
              <div className="flex flex-col">
                <div className={`flex flex-wrap gap-x-5 gap-y-1 text-[13px] ${muted} pb-2`}>
                  <span>{num(r.invoices)} فاتورة</span>
                  <span>اتحصّل {money(r.collected)}</span>
                  {r.voided > 0 && <span>اتلغى {money(r.voided)}</span>}
                </div>
                {r.byMethod.map((m) => (
                  <div key={m.method} className="flex justify-between text-[14px] border-t border-slate-100 py-2">
                    <span>{METHOD[m.method] ?? m.method} <span className={muted}>({num(m.count)})</span></span>
                    <strong>{money(m.amount)}</strong>
                  </div>
                ))}
              </div>
            )
          }
        </QueryState>
      </section>

      <details className={`${card} rounded-2xl p-5 group`}>
        <summary className="flex items-center gap-2 cursor-pointer text-[15px] font-bold list-none">
          <Info className="size-4 text-slate-500" aria-hidden />
          الأرقام دي بتتحسب إزاي؟
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

function Kpi({ label, value, hint, accent, warn }: { label: string; value: string; hint: string; accent?: boolean; warn?: boolean }) {
  return (
    <div className={`${card} rounded-2xl p-4 flex flex-col gap-1 ${accent ? "border-t-[3px] border-t-brand-red" : ""} ${warn ? "border-t-[3px] border-t-[#E0A100]" : ""}`}>
      <span className={`text-[13px] font-semibold ${muted}`}>{label}</span>
      <strong className="text-[24px] leading-tight font-extrabold text-slate-900">{value}</strong>
      <span className={`text-[12px] ${muted} leading-snug`}>{hint}</span>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-slate-50 p-3.5 flex flex-col gap-0.5">
      <span className={`text-[12px] ${muted}`}>{label}</span>
      <strong className="text-[18px] font-extrabold">{value}</strong>
    </div>
  );
}

function Settlement({ value }: { value: number }) {
  if (Math.round(value) === 0) return <span className="text-slate-400">متسوّي</span>;
  return value > 0 ? (
    <span className="text-[#1F6B3A] font-semibold" title="المنصة تحوّل للملعب">له {money(value)}</span>
  ) : (
    <span className="text-[#A8261C] font-semibold" title="الملعب يدفع للمنصة">عليه {money(-value)}</span>
  );
}

function MoneySplit({ parts, total }: { parts: { label: string; value: number; color: string }[]; total: number }) {
  const visible = parts.filter((p) => p.value > 0);
  return (
    <div className="flex flex-col gap-3">
      <div className="flex h-3 w-full overflow-hidden rounded-full bg-slate-100" role="img" aria-label="توزيع قيمة الحجوزات">
        {visible.map((p) => (
          <div key={p.label} style={{ width: `${(p.value / total) * 100}%`, background: p.color }} className="h-full transition-all duration-500" />
        ))}
      </div>
      <div className="flex flex-wrap gap-x-6 gap-y-2">
        {parts.map((p) => (
          <div key={p.label} className="flex items-center gap-2 text-[13px]">
            <span className="size-2.5 rounded-full" style={{ background: p.color }} aria-hidden />
            <span className={muted}>{p.label}</span>
            <strong>{money(p.value)}</strong>
          </div>
        ))}
      </div>
    </div>
  );
}
