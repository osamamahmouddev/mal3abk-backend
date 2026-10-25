"use client";

import React, { useState } from "react";
import { Page, Toast, card, chip, money, muted, toArabicDigits } from "@/components/ui";

type Range = "week" | "month" | "quarter";
type Region = "all" | "cai" | "giz" | "alx";

const MULT: Record<Range, number> = { week: 0.25, month: 1, quarter: 3 };
// name, region, venues, confirmed bookings (per month), cancel rate, no-show rate
const AREAS: [string, Exclude<Region, "all"> | "del", number, number, number, number][] = [
  ["القاهرة", "cai", 142, 2310, 0.07, 0.02],
  ["الجيزة", "giz", 61, 980, 0.06, 0.03],
  ["الإسكندرية", "alx", 18, 240, 0.09, 0.02],
  ["الدلتا", "del", 10, 120, 0.11, 0.04],
];
const FUNNEL: [string, number][] = [
  ["بحث عن ملعب", 48200],
  ["فتح صفحة ملعب", 21900],
  ["بدأ الحجز", 9100],
  ["رفع إثبات العربون", 4620],
  ["اتأكد", 3650],
];
const TOP: [string, number][] = [
  ["ستاد الشباب", 520],
  ["ملعب النجوم — مدينة نصر", 312],
  ["ملاعب الرواد", 268],
  ["زايد أرينا", 244],
  ["ستاد التجمع", 201],
];
// Average booking value used for the platform-wide estimate.
const AVG_BOOKING = 185;

export default function ReportsPage() {
  const [range, setRange] = useState<Range>("month");
  const [region, setRegion] = useState<Region>("all");
  const [toast, setToast] = useState<string | null>(null);

  const mult = MULT[range];
  const rows = AREAS.filter((a) => region === "all" || a[1] === region);
  const booked = rows.reduce((t, a) => t + a[3], 0) * mult;
  const share = booked / (3650 * mult) || 1;
  const fmt = (n: number) => toArabicDigits(Math.round(n).toLocaleString("en-US")).replace(/,/g, "٬");

  const kpis: [string, string, string, boolean][] = [
    ["حجوزات مأكدة", fmt(booked), "+١٢٪ عن الفترة اللي فاتت", true],
    ["قيمة الحجوزات", money(booked * AVG_BOOKING), "+٩٪", true],
    ["لاعيبة نشطين", fmt(9800 * mult * share), "+٦١٠ جديد", true],
    ["متوسط وقت مراجعة الإيصال", "٢٢ دقيقة", "+٤ دقايق — محتاج متابعة", false],
  ];

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_200px] text-[26px] font-bold">تقارير المنصة</h1>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["week", "الأسبوع"],
              ["month", "الشهر"],
              ["quarter", "الربع"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={range === id} onClick={() => setRange(id)} className={chip(range === id)}>
              {label}
            </button>
          ))}
        </div>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "كل مصر"],
              ["cai", "القاهرة"],
              ["giz", "الجيزة"],
              ["alx", "إسكندرية"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={region === id} onClick={() => setRegion(id)} className={chip(region === id)}>
              {label}
            </button>
          ))}
        </div>
        <button
          type="button"
          onClick={() => setToast("التقرير بيتجهّز وهيوصل على إيميلك.")}
          className="min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold"
        >
          تصدير Excel
        </button>
      </div>

      <div className="grid gap-3 grid-cols-[repeat(auto-fit,minmax(190px,1fr))]">
        {kpis.map(([l, v, d, good]) => (
          <div key={l} className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
            <span className={`text-[13px] ${muted}`}>{l}</span>
            <span className="text-2xl font-bold">{v}</span>
            <span className={`text-xs font-bold ${good ? "text-[#1B6E3E]" : "text-[#A8261C]"}`}>{d}</span>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap gap-4 items-stretch">
        <section className={`flex-[999_1_440px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
          <h2 className="text-[17px] font-bold">قمع الحجز</h2>
          {FUNNEL.map(([l, v], i) => (
            <div key={l} className="flex flex-col gap-1.5">
              <div className="flex justify-between text-sm">
                <span className="font-semibold">{l}</span>
                <span>
                  <b>{fmt(v * mult * share)}</b>{" "}
                  <span className={muted}>· {i === 0 ? "١٠٠٪" : `${toArabicDigits(Math.round((v / FUNNEL[0][1]) * 100))}٪`}</span>
                </span>
              </div>
              <div className="h-3.5 rounded-[7px] bg-[#EEF0F5] overflow-hidden">
                <div
                  className={`h-full rounded-[7px] ${i === FUNNEL.length - 1 ? "bg-brand-red" : "bg-ink"}`}
                  style={{ width: `${((v / FUNNEL[0][1]) * 100).toFixed(1)}%` }}
                />
              </div>
            </div>
          ))}
          <span className={`text-xs ${muted}`}>أكبر تسريب: بين &quot;بدأ الحجز&quot; و&quot;رفع إثبات العربون&quot;.</span>
        </section>
        <section className={`flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
          <h2 className="text-[17px] font-bold">أكتر الملاعب حجزاً</h2>
          {TOP.map(([name, v], i) => (
            <div key={name} className="flex items-center gap-2.5 text-sm">
              <span className="w-[26px] h-[26px] rounded-full bg-[#EEF0F5] flex items-center justify-center text-xs font-bold">{toArabicDigits(i + 1)}</span>
              <span className="flex-1 font-semibold">{name}</span>
              <span className="font-bold">{fmt(v * mult)}</span>
            </div>
          ))}
        </section>
      </div>

      <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 overflow-x-auto`}>
        <table className="w-full border-collapse text-sm min-w-[640px]">
          <caption className="text-right py-3 text-[17px] font-bold">حسب المنطقة</caption>
          <thead>
            <tr className={`${muted} text-[13px]`}>
              {["المنطقة", "ملاعب", "حجوزات مأكدة", "قيمة الحجوزات", "إلغاء", "غياب"].map((h) => (
                <th key={h} scope="col" className="text-right px-2 py-2.5 font-semibold">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(([a, , v, b, c, n]) => (
              <tr key={a} className="border-t border-[#EEF0F5]">
                <td className="px-2 py-3 font-bold">{a}</td>
                <td className="px-2 py-3">{toArabicDigits(v)}</td>
                <td className="px-2 py-3">{fmt(b * mult)}</td>
                <td className="px-2 py-3">{money(b * mult * AVG_BOOKING)}</td>
                <td className="px-2 py-3">{toArabicDigits(Math.round(c * 100))}٪</td>
                <td className="px-2 py-3">{toArabicDigits(Math.round(n * 100))}٪</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className={`mt-2.5 text-xs ${muted}`}>قيمة الحجوزات = اللي اتحجز على المنصة. ملعبك مش بيستلم فلوس الحجز — العربون بيروح للملعب مباشرة.</p>
      </section>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
