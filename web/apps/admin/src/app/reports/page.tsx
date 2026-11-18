"use client";

import React, { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Page, PageHeader, SearchBox, btnOutline, btnPrimary, btnSmall, card, money, muted, toArabicDigits } from "@/components/ui";
import { Field, QueryState, TextInput, num } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import {
  AlertTriangle,
  Building2,
  CheckCircle2,
  Coins,
  Download,
  Flame,
  LayoutGrid,
  Percent,
  Search,
  ShieldAlert,
  Table2,
  TrendingUp,
  Users,
  Wallet,
} from "lucide-react";

const METHOD: Record<string, string> = { Cash: "كاش", BankTransfer: "تحويل بنكي", InstaPay: "إنستاباي", MobileWallet: "محفظة" };

const DEFINITIONS: [string, string][] = [
  ["MRR", "الإيراد الشهري المتكرر دلوقتي: سعر الباقة الشهري للاشتراكات الشهرية، والسنوي ÷ ١٢ للسنوية. الاشتراكات النشطة وفترة السماح بس، من غير التجربة."],
  ["الإيراد المحصّل", "فلوس اتسجل استلامها فعلًا في الفترة (بتاريخ الدفع)، من غير الدفعات اللي فاتورتها اتلغت. ده كاش، مش MRR."],
  ["عمولة المنصة", "مجموع عمولات منصة ملعبك المحتسبة عن ساعات الحجز المنفذة في كل ملعب وتدخل حساب المنصة."],
  ["العربون المحصّل", "مبالغ العربون المدفوعة مقدماً للمنصة لتثبيت الحجز، وتخصم منها عمولة المنصة."],
  ["صافي مستحقات الملعب", "إجمالي نصيب صاحب الملعب الفعلي بعد خصم عمولة المنصة، ويشمل ما تم تحصيله أو ما سيُحصّل كاش عند الحضور."],
  ["المطلوب تحصيله كاش بالملعب", "المبالغ المتبقية على اللاعبين التي تُسدد مباشرة لصاحب الملعب عند الحضور."],
  ["إحالات استرداد العربون", "مبالغ العرابين المحالة لإدارة المنصة لردها للاعبين نتيجة إلغاء الملاعب للحجوزات."],
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
  currency: string;
}

function isoDay(offsetDays: number) {
  return new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

export default function ReportsPage() {
  const [from, setFrom] = useState(isoDay(-29));
  const [to, setTo] = useState(isoDay(0));
  const [venueSearch, setVenueSearch] = useState("");
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

  const financialSummaryQuery = useQuery({
    queryKey: ["admin-venues-financial-summary", from, to],
    enabled: valid,
    queryFn: () =>
      unwrap(
        (api.GET as any)("/api/v1/admin/bookings/financial-summary", {
          params: {
            query: {
              fromUtc: from ? `${from}T00:00:00Z` : undefined,
              toUtc: to ? `${to}T23:59:59Z` : undefined,
            },
          },
        }),
      ) as Promise<VenueFinancialSummaryItem[]>,
  });

  const venuesList = financialSummaryQuery.data ?? [];
  const filteredVenues = useMemo(() => {
    if (!venueSearch.trim()) return venuesList;
    const term = venueSearch.trim().toLowerCase();
    return venuesList.filter((v) => v.venueName.toLowerCase().includes(term));
  }, [venuesList, venueSearch]);

  // Aggregated totals across all venues
  const totals = useMemo(() => {
    return venuesList.reduce(
      (acc, v) => ({
        bookings: acc.bookings + v.totalBookings,
        confirmed: acc.confirmed + v.confirmedBookings,
        cancelled: acc.cancelled + v.cancelledBookings,
        gross: acc.gross + v.totalGrossAmount,
        deposits: acc.deposits + v.totalDepositAmount,
        commission: acc.commission + v.totalPlatformCommission,
        venueNet: acc.venueNet + v.totalVenueNetAmount,
        dueAtVenue: acc.dueAtVenue + v.totalDueAtVenueCash,
        refundsCount: acc.refundsCount + v.pendingRefundsCount,
        refundsAmount: acc.refundsAmount + v.pendingRefundsAmount,
      }),
      {
        bookings: 0,
        confirmed: 0,
        cancelled: 0,
        gross: 0,
        deposits: 0,
        commission: 0,
        venueNet: 0,
        dueAtVenue: 0,
        refundsCount: 0,
        refundsAmount: 0,
      },
    );
  }, [venuesList]);

  const handleExportVenuesCsv = () => {
    if (venuesList.length === 0) return;
    const headers = [
      "اسم الملعب",
      "إجمالي الحجوزات",
      "حجوزات مؤكدة",
      "حجوزات ملغية",
      "إجمالي قيمة الحجوزات (ج.م)",
      "العربون المحصل (ج.م)",
      "عمولة المنصة (ج.م)",
      "صافي فلوس الملعب (ج.م)",
      "المطلوب تحصيله كاش بالملعب (ج.م)",
      "إحالات استرداد معلقة (عدد)",
      "مبالغ الاسترداد المعلقة (ج.م)",
    ];

    const rows = venuesList.map((v) => [
      `"${v.venueName.replace(/"/g, '""')}"`,
      v.totalBookings,
      v.confirmedBookings,
      v.cancelledBookings,
      v.totalGrossAmount.toFixed(2),
      v.totalDepositAmount.toFixed(2),
      v.totalPlatformCommission.toFixed(2),
      v.totalVenueNetAmount.toFixed(2),
      v.totalDueAtVenueCash.toFixed(2),
      v.pendingRefundsCount,
      v.pendingRefundsAmount.toFixed(2),
    ]);

    const csvContent = "\uFEFF" + [headers.join(","), ...rows.map((r) => r.join(","))].join("\r\n");
    const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `malabak-venues-financial-summary-${from}-to-${to}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <Page>
      <PageHeader
        title="الإيرادات والحسابات والتقارير المالية"
        sub="تقرير شامل لعمولات المنصة، مبالغ العرابين، صافي مستحقات الملاعب بعد خصم العمولة، وإحالات الاسترداد."
      />

      {/* Date Filter Bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 bg-white p-4 rounded-2xl border border-slate-200 shadow-xs">
        <div className="flex flex-wrap items-center gap-3">
          <Field label="من تاريخ">
            <TextInput type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
          </Field>
          <Field label="إلى تاريخ">
            <TextInput type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </Field>
        </div>

        <button
          type="button"
          onClick={handleExportVenuesCsv}
          disabled={venuesList.length === 0}
          className={`${btnPrimary} gap-2 text-xs`}
        >
          <Download className="size-4" />
          تصدير تقرير الملاعب المالي (CSV)
        </button>
      </div>

      {/* Primary KPI Section for Venues & Platform Commissions */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3.5">
        {/* Total Platform Commission */}
        <div className="bg-gradient-to-br from-purple-50 to-indigo-50/70 border border-purple-200/80 rounded-2xl p-4.5 flex flex-col justify-between shadow-xs">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold text-purple-900">عمولة المنصة الإجمالية</span>
            <div className="size-9 rounded-xl bg-purple-100 text-purple-700 flex items-center justify-center">
              <Percent className="size-5" />
            </div>
          </div>
          <div className="mt-3">
            <strong className="text-2xl font-extrabold text-purple-950">{money(totals.commission)}</strong>
            <span className="text-[11px] text-purple-700 block mt-0.5">دخل المنصة المباشر من الساعات المحجوزة</span>
          </div>
        </div>

        {/* Total Deposits Handled */}
        <div className="bg-gradient-to-br from-emerald-50 to-teal-50/70 border border-emerald-200/80 rounded-2xl p-4.5 flex flex-col justify-between shadow-xs">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold text-emerald-900">إجمالي العرابين المحصلة</span>
            <div className="size-9 rounded-xl bg-emerald-100 text-emerald-700 flex items-center justify-center">
              <Coins className="size-5" />
            </div>
          </div>
          <div className="mt-3">
            <strong className="text-2xl font-extrabold text-emerald-950">{money(totals.deposits)}</strong>
            <span className="text-[11px] text-emerald-700 block mt-0.5">مدفوعة للمنصة لتثبيت الحجز</span>
          </div>
        </div>

        {/* Total Net Due to Venues */}
        <div className="bg-gradient-to-br from-blue-50 to-sky-50/70 border border-blue-200/80 rounded-2xl p-4.5 flex flex-col justify-between shadow-xs">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold text-blue-900">صافي مستحقات الملاعب</span>
            <div className="size-9 rounded-xl bg-blue-100 text-blue-700 flex items-center justify-center">
              <Wallet className="size-5" />
            </div>
          </div>
          <div className="mt-3">
            <strong className="text-2xl font-extrabold text-blue-950">{money(totals.venueNet)}</strong>
            <span className="text-[11px] text-blue-700 block mt-0.5">نصيب الملاعب بعد خصم نسبة العمولة</span>
          </div>
        </div>

        {/* Pending Refunds (Cancelled by Venues) */}
        <div className="bg-gradient-to-br from-amber-50 to-orange-50/70 border border-amber-200/80 rounded-2xl p-4.5 flex flex-col justify-between shadow-xs">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold text-amber-900">إحالات استرداد معلقة للاعبين</span>
            <div className="size-9 rounded-xl bg-amber-100 text-amber-700 flex items-center justify-center">
              <AlertTriangle className="size-5" />
            </div>
          </div>
          <div className="mt-3">
            <div className="flex items-baseline gap-2">
              <strong className="text-2xl font-extrabold text-amber-950">{money(totals.refundsAmount)}</strong>
              <span className="text-xs text-amber-800 font-semibold">({toArabicDigits(totals.refundsCount)} إحالة)</span>
            </div>
            <span className="text-[11px] text-amber-700 block mt-0.5">بسبب إلغاء الملاعب لحجوزات</span>
          </div>
        </div>
      </div>

      {/* Detailed Per-Venue Financial Summary Table */}
      <section className={`${card} rounded-2xl p-5 flex flex-col gap-4 border border-slate-200/90 shadow-xs`}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-col gap-0.5">
            <h2 className="text-base font-bold text-slate-900 flex items-center gap-2">
              <Building2 className="size-5 text-brand-red" />
              التقرير المالي وحسابات الملاعب والعمولات
            </h2>
            <span className={`text-xs ${muted}`}>
              تفصيل عدد الحجوزات، العرابين، عمولة المنصة، وصافي مستحقات كل ملعب خلال الفترة المحددة.
            </span>
          </div>

          <div className="w-full sm:w-72">
            <SearchBox
              value={venueSearch}
              onChange={setVenueSearch}
              placeholder="ابحث باسم الملعب…"
            />
          </div>
        </div>

        {financialSummaryQuery.isLoading ? (
          <div className="p-10 text-center text-sm text-slate-500">جاري تحميل بيانات الملاعب المالية…</div>
        ) : filteredVenues.length === 0 ? (
          <div className="p-10 text-center text-sm text-slate-500 border border-dashed rounded-xl">
            لا توجد حجوزات مسجلة للملاعب في هذه الفترة أو لا تطابق البحث.
          </div>
        ) : (
          <div className="overflow-x-auto -mx-5 px-5">
            <table className="w-full min-w-[960px] border-collapse text-sm">
              <thead>
                <tr className="bg-slate-50 text-slate-600 text-xs text-right border-y border-slate-200">
                  <th scope="col" className="p-3.5 font-bold">الملعب</th>
                  <th scope="col" className="p-3.5 font-bold text-center">إجمالي الحجوزات</th>
                  <th scope="col" className="p-3.5 font-bold text-center">المؤكدة</th>
                  <th scope="col" className="p-3.5 font-bold text-center">الملغية</th>
                  <th scope="col" className="p-3.5 font-bold">قيمة الحجوزات</th>
                  <th scope="col" className="p-3.5 font-bold">العربون المحصل</th>
                  <th scope="col" className="p-3.5 font-bold text-purple-700">عمولة المنصة</th>
                  <th scope="col" className="p-3.5 font-bold text-emerald-700">صافي فلوس الملعب</th>
                  <th scope="col" className="p-3.5 font-bold">المتبقي كاش بالملعب</th>
                  <th scope="col" className="p-3.5 font-bold text-amber-700">إحالات الاسترداد</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filteredVenues.map((v) => (
                  <tr key={v.venueId} className="hover:bg-slate-50/80 transition-colors">
                    <td className="p-3.5 font-bold text-slate-900">
                      <div className="flex flex-col">
                        <span>{v.venueName}</span>
                        <span className="text-[10px] text-slate-400 font-mono" dir="ltr">
                          {v.venueId.slice(0, 8)}…
                        </span>
                      </div>
                    </td>
                    <td className="p-3.5 text-center font-bold text-slate-800">
                      {toArabicDigits(v.totalBookings)}
                    </td>
                    <td className="p-3.5 text-center">
                      <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-emerald-100 text-emerald-800">
                        {toArabicDigits(v.confirmedBookings)}
                      </span>
                    </td>
                    <td className="p-3.5 text-center">
                      <span className={`px-2 py-0.5 rounded-full text-xs font-bold ${v.cancelledBookings > 0 ? "bg-red-100 text-red-800" : "bg-slate-100 text-slate-600"}`}>
                        {toArabicDigits(v.cancelledBookings)}
                      </span>
                    </td>
                    <td className="p-3.5 font-semibold text-slate-900 whitespace-nowrap">
                      {money(v.totalGrossAmount)}
                    </td>
                    <td className="p-3.5 font-semibold text-slate-800 whitespace-nowrap">
                      {money(v.totalDepositAmount)}
                    </td>
                    <td className="p-3.5 font-bold text-purple-700 whitespace-nowrap">
                      {money(v.totalPlatformCommission)}
                    </td>
                    <td className="p-3.5 font-extrabold text-emerald-700 whitespace-nowrap bg-emerald-50/40">
                      {money(v.totalVenueNetAmount)}
                    </td>
                    <td className="p-3.5 font-semibold text-slate-700 whitespace-nowrap">
                      {money(v.totalDueAtVenueCash)}
                    </td>
                    <td className="p-3.5 whitespace-nowrap">
                      {v.pendingRefundsCount > 0 ? (
                        <span className="px-2 py-1 rounded-lg text-xs font-bold bg-amber-100 text-amber-900 border border-amber-200">
                          {money(v.pendingRefundsAmount)} ({toArabicDigits(v.pendingRefundsCount)})
                        </span>
                      ) : (
                        <span className="text-slate-400 text-xs">لا يوجد</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="bg-slate-100 font-bold text-slate-900 border-t-2 border-slate-300">
                  <td className="p-3.5">الإجمالي الكلي</td>
                  <td className="p-3.5 text-center">{toArabicDigits(totals.bookings)}</td>
                  <td className="p-3.5 text-center text-emerald-800">{toArabicDigits(totals.confirmed)}</td>
                  <td className="p-3.5 text-center text-red-800">{toArabicDigits(totals.cancelled)}</td>
                  <td className="p-3.5 whitespace-nowrap">{money(totals.gross)}</td>
                  <td className="p-3.5 whitespace-nowrap">{money(totals.deposits)}</td>
                  <td className="p-3.5 whitespace-nowrap text-purple-700">{money(totals.commission)}</td>
                  <td className="p-3.5 whitespace-nowrap text-emerald-800 bg-emerald-100/40">{money(totals.venueNet)}</td>
                  <td className="p-3.5 whitespace-nowrap">{money(totals.dueAtVenue)}</td>
                  <td className="p-3.5 whitespace-nowrap text-amber-800">{money(totals.refundsAmount)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </section>

      {/* Platform General KPIs */}
      <QueryState query={dashboard}>
        {(d) => (
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <Tile label="MRR (الاشتراكات الشهرية)" value={money(d.figures["mrr"] ?? 0)} />
            <Tile label="الإيراد المحصّل (اشتراكات)" value={money(d.figures["revenue.collected"] ?? 0)} />
            <Tile label="اشتراكات ملاك نشطة" value={num(d.figures["subscriptions.active"])} />
            <Tile label="ملاك في فترة التجربة" value={num(d.figures["subscriptions.trial"])} />
          </div>
        )}
      </QueryState>

      {/* Invoices Reconciliation Section */}
      <section className={`${card} rounded-[18px] p-5 flex flex-col gap-3`}>
        <h2 className="text-[15px] font-bold">مطابقة تحصيل الاشتراكات حسب طريقة الدفع</h2>
        <QueryState query={reconciliation}>
          {(r) => (
            <>
              <div className="flex flex-wrap gap-4 text-sm font-semibold">
                <span>فواتير الاشتراكات: <strong>{num(r.invoices)}</strong></span>
                <span>المحصل: <strong>{money(r.collected)}</strong></span>
                <span>الملغي: <strong>{money(r.voided)}</strong></span>
              </div>
              {r.byMethod.map((m) => (
                <div key={m.method} className="flex justify-between text-[13px] border-t border-[#EEF0F5] pt-2">
                  <span>{METHOD[m.method] ?? m.method} ({num(m.count)})</span>
                  <strong>{money(m.amount)}</strong>
                </div>
              ))}
            </>
          )}
        </QueryState>
      </section>

      {/* Definitions Glossary */}
      <section className={`${card} rounded-[18px] p-5 flex flex-col gap-3`}>
        <h2 className="text-[15px] font-bold">تعريفات المصطلحات المالية</h2>
        <dl className="grid grid-cols-1 md:grid-cols-2 gap-3.5">
          {DEFINITIONS.map(([term, text]) => (
            <div key={term} className="bg-slate-50 p-3 rounded-xl border border-slate-100">
              <dt className="text-sm font-bold text-slate-900">{term}</dt>
              <dd className={`text-[12px] ${muted} leading-relaxed mt-0.5`}>{text}</dd>
            </div>
          ))}
        </dl>
      </section>
    </Page>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className={`${card} rounded-[16px] p-4.5 flex flex-col gap-1`}>
      <span className={`text-[13px] ${muted}`}>{label}</span>
      <strong className="text-[20px] font-extrabold text-slate-900">{value}</strong>
    </div>
  );
}
