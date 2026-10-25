"use client";

import React, { useState } from "react";
import { Page, Toast, card, chip, muted, toArabicDigits } from "@/components/ui";

type Range = "week" | "month" | "quarter";
type PitchFilter = "all" | "p1" | "p2" | "p3";

const DATA: Record<Range, { labels: string[]; booked: number[] }> = {
  week: { labels: ["سبت", "أحد", "اتنين", "تلات", "أربع", "خميس", "جمعة"], booked: [2400, 1500, 1350, 1650, 1800, 2700, 3000] },
  month: { labels: ["أسبوع ١", "أسبوع ٢", "أسبوع ٣", "أسبوع ٤"], booked: [12800, 14200, 13100, 15600] },
  quarter: { labels: ["مايو", "يونيو", "يوليو"], booked: [48000, 52500, 55700] },
};
const SHARE: Record<PitchFilter, number> = { all: 1, p1: 0.34, p2: 0.44, p3: 0.22 };
const COUNTS: Record<Range, number> = { week: 92, month: 368, quarter: 1080 };
const OCCUPANCY: [string, number][] = [
  ["ملعب ١", 71],
  ["ملعب ٢", 84],
  ["ملعب ٣", 52],
];
const HOT_HOURS: [string, number][] = [
  ["٨ م", 3],
  ["٩ م", 3],
  ["١٠ م", 2],
  ["٧ م", 2],
  ["١١ م", 1],
  ["٥ م", 1],
];
const STATUS_ROWS: [string, number, string][] = [
  ["مأكد واتلعب", 0.72, "bg-[#E5F5EB] text-[#1B6E3E]"],
  ["مأكد — لسه جاي", 0.14, "bg-[#EEF0F5] text-[#3B4352]"],
  ["مستني إثبات / مراجعة", 0.05, "bg-[#FEF3D6] text-[#7A4D00]"],
  ["اتلغى", 0.07, "bg-[#FDECEA] text-[#A8261C]"],
  ["ماجوش (No-show)", 0.02, "bg-[#FDECEA] text-[#A8261C]"],
];

const money = (n: number) => `${toArabicDigits(Math.round(n).toLocaleString("en-US"))} ج.م`;
const filterChip = (on: boolean) => chip(on, "min-h-[40px] px-3.5 rounded-[10px] text-[13px]");

export default function ReportsPage() {
  const [range, setRange] = useState<Range>("week");
  const [pitch, setPitch] = useState<PitchFilter>("all");
  const [toast, setToast] = useState<string | null>(null);

  const share = SHARE[pitch];
  const d = DATA[range];
  const booked = d.booked.map((x) => x * share);
  const dep = booked.map((x, i) => x * (i % 3 === 1 ? 0.28 : 0.33));
  const totalBooked = booked.reduce((a, b) => a + b, 0);
  const totalDep = dep.reduce((a, b) => a + b, 0);
  const recognized = totalBooked * 0.78;
  const outstanding = totalBooked - totalDep - (recognized - totalDep * 0.78);
  const max = Math.max(...booked);
  const counts = COUNTS[range] * share;

  const kpis: [string, number, string, string][] = [
    ["قيمة الحجوزات", totalBooked, "كل الحجوزات المأكدة في الفترة", "bg-[#C9D3E6]"],
    ["عربون مستلم", totalDep, "اتأكد بإثبات دفع", "bg-navy-900"],
    ["متبقي يتحصّل في الملعب", Math.max(0, outstanding), "للماتشات الجاية", "bg-brand-gold"],
    ["إيرادات محققة", recognized, "ماتشات اتلعبت فعلاً", "bg-[#2E8B55]"],
  ];

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_200px] text-[26px] font-bold">التقارير</h1>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["week", "الأسبوع ده"],
              ["month", "الشهر ده"],
              ["quarter", "آخر ٣ شهور"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={range === id} onClick={() => setRange(id)} className={filterChip(range === id)}>
              {label}
            </button>
          ))}
        </div>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "كل الملاعب"],
              ["p1", "ملعب ١"],
              ["p2", "ملعب ٢"],
              ["p3", "ملعب ٣"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={pitch === id} onClick={() => setPitch(id)} className={filterChip(pitch === id)}>
              {label}
            </button>
          ))}
        </div>
        <button
          type="button"
          onClick={() => setToast("جاري تجهيز ملف Excel للفترة دي — هيوصلك على الإيميل كمان.")}
          className="min-h-[40px] px-4 rounded-[10px] border border-navy-900 bg-white text-navy-900 text-[13px] font-bold"
        >
          تصدير Excel
        </button>
      </div>

      <div className="grid gap-3 grid-cols-[repeat(auto-fit,minmax(200px,1fr))]">
        {kpis.map(([label, v, hint, dot]) => (
          <div key={label} className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
            <span className={`flex items-center gap-2 text-[13px] ${muted}`}>
              <span className={`w-2.5 h-2.5 rounded-[3px] ${dot}`} />
              {label}
            </span>
            <span className="text-2xl font-bold">{money(v)}</span>
            <span className={`text-xs ${muted}`}>{hint}</span>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap gap-4 items-stretch">
        <section className={`flex-[999_1_460px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
          <div className="flex items-center gap-3 flex-wrap">
            <h2 className="flex-1 text-[17px] font-bold">قيمة الحجوزات مقابل العربون المستلم</h2>
            <span className={`flex items-center gap-1.5 text-xs ${muted}`}>
              <span className="w-2.5 h-2.5 rounded-[3px] bg-[#C9D3E6]" />
              قيمة الحجوزات
            </span>
            <span className={`flex items-center gap-1.5 text-xs ${muted}`}>
              <span className="w-2.5 h-2.5 rounded-[3px] bg-navy-900" />
              عربون مستلم
            </span>
          </div>
          <div
            role="img"
            aria-label="رسم بياني بالأعمدة لقيمة الحجوزات والعربون لكل فترة"
            className="h-[240px] flex items-end gap-2.5 border-b border-[#DCE0E8] pt-2"
          >
            {d.labels.map((label, i) => (
              <div key={label} className="flex-1 h-full flex flex-col justify-end items-center">
                <div className="w-full max-w-[44px] h-full flex items-end gap-[3px]">
                  <div
                    title={`قيمة الحجوزات: ${money(booked[i])}`}
                    className="flex-1 rounded-t bg-[#C9D3E6]"
                    style={{ height: `${((booked[i] / max) * 100).toFixed(1)}%` }}
                  />
                  <div
                    title={`عربون: ${money(dep[i])}`}
                    className="flex-1 rounded-t bg-navy-900"
                    style={{ height: `${((dep[i] / max) * 100).toFixed(1)}%` }}
                  />
                </div>
              </div>
            ))}
          </div>
          <div className="flex gap-2.5">
            {d.labels.map((label) => (
              <span key={label} className={`flex-1 text-center text-xs ${muted}`}>
                {label}
              </span>
            ))}
          </div>
        </section>

        <section className={`flex-[1_1_300px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
          <h2 className="text-[17px] font-bold">نسبة الإشغال لكل ملعب</h2>
          {OCCUPANCY.filter((_, i) => pitch === "all" || pitch === `p${i + 1}`).map(([name, p]) => (
            <div key={name} className="flex flex-col gap-1.5">
              <div className="flex justify-between text-sm">
                <span className="font-semibold">{name}</span>
                <span className="font-bold">{toArabicDigits(p)}٪</span>
              </div>
              <div className="h-2.5 rounded-[5px] bg-[#EEF0F5] overflow-hidden">
                <div className={`h-full rounded-[5px] ${p > 80 ? "bg-brand-red" : "bg-navy-900"}`} style={{ width: `${p}%` }} />
              </div>
            </div>
          ))}
          <h2 className="mt-3 text-[17px] font-bold">أكتر الساعات طلب</h2>
          <div className="flex gap-1.5 flex-wrap">
            {HOT_HOURS.map(([label, h]) => (
              <span
                key={label}
                className={`text-[13px] font-bold rounded-full px-3 py-1.5 ${
                  h === 3 ? "bg-brand-red text-white" : h === 2 ? "bg-[#FDECEA] text-[#A8261C]" : "bg-[#EEF0F5] text-[#3B4352]"
                }`}
              >
                {label}
              </span>
            ))}
          </div>
        </section>
      </div>

      <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 overflow-x-auto`}>
        <table className="w-full border-collapse text-sm min-w-[640px]">
          <caption className="text-right py-3 text-[17px] font-bold">حالة الحجوزات في الفترة</caption>
          <thead>
            <tr className={`${muted} text-[13px]`}>
              <th scope="col" className="text-right px-2 py-2.5 font-semibold">
                الحالة
              </th>
              <th scope="col" className="text-right px-2 py-2.5 font-semibold">
                العدد
              </th>
              <th scope="col" className="text-right px-2 py-2.5 font-semibold">
                القيمة
              </th>
              <th scope="col" className="text-right px-2 py-2.5 font-semibold">
                النسبة
              </th>
            </tr>
          </thead>
          <tbody>
            {STATUS_ROWS.map(([name, p, cls]) => (
              <tr key={name} className="border-t border-[#EEF0F5]">
                <td className="px-2 py-3">
                  <span className={`text-xs font-bold rounded-full px-2.5 py-[3px] ${cls}`}>{name}</span>
                </td>
                <td className="px-2 py-3 font-bold">{toArabicDigits(Math.round(counts * p))}</td>
                <td className="px-2 py-3">{money(totalBooked * p)}</td>
                <td className={`px-2 py-3 ${muted}`}>{toArabicDigits(Math.round(p * 100))}٪</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className={`mt-2.5 text-xs ${muted}`}>
          الإيرادات المحققة = العربون + باقي المبلغ للماتشات اللي اتلعبت فعلاً. الباقي اللي في الملعب بيتسجل يدوي من صفحة الحجز.
        </p>
      </section>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
