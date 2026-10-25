"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Badge, Page, Tone, card, muted, redShadow, toArabicDigits } from "@/components/ui";

const rows: [string, string, string, string, string, Tone][] = [
  ["٧:٠٠ م", "ملعب ٢", "كريم عادل", "٦٠ ج.م معتمد", "✓ مؤكد", "ok"],
  ["٨:٠٠ م", "ملعب ١", "أحمد سامي", "٥٠ ج.م — إيصال مرفوع", "قيد المراجعة", "warn"],
  ["٨:٠٠ م", "ملعب ٣", "حجز يدوي — م. فتحي", "نقدي في الملعب", "✓ مؤكد", "ok"],
  ["٩:٠٠ م", "ملعب ١", "يوسف حسن", "لم يُرفع بعد", "بانتظار العربون", "wait"],
  ["١١:٠٠ م", "ملعب ٢", "عمر خالد", "٦٠ ج.م — إيصال مرفوع", "قيد المراجعة", "warn"],
  ["١٢:٠٠ ص +١", "ملعب ١", "فريق الصقور", "٥٠ ج.م معتمد", "✓ مؤكد", "ok"],
];

const occupancy = {
  this: [72, 58, 61, 66, 80, 91, 68],
  last: [64, 49, 55, 60, 71, 88, 74],
};
const weekDays = ["سبت", "أحد", "إثنين", "ثلاثاء", "أربعاء", "خميس", "جمعة"];

const proofs = [
  { who: "أحمد سامي · ٥٠ ج.م", meta: "ملعب ١ · ٨:٠٠ م · فودافون كاش", left: "١ س ٤٨ د" },
  { who: "عمر خالد · ٦٠ ج.م", meta: "ملعب ٢ · ١١:٠٠ م · إنستاباي", left: "١ س ١٠ د" },
];

const finance = [
  { label: "قيمة الحجوزات المؤكدة اليوم", value: "١٬٦٥٠ ج.م", labelCls: muted, valueCls: "" },
  { label: "عربون معتمد (محصّل)", value: "٥٥٠ ج.م", labelCls: muted, valueCls: "text-[#1B6E3E]" },
  { label: "متبقي يُحصّل في الملعب", value: "١٬١٠٠ ج.م", labelCls: muted, valueCls: "" },
  { label: "عربون قيد المراجعة — غير محصّل", value: "١١٠ ج.م", labelCls: "text-[#7A4D00]", valueCls: "text-[#7A4D00]" },
];

export default function OverviewPage() {
  const [range, setRange] = useState<"this" | "last">("this");
  const vals = occupancy[range];

  return (
    <Page className="gap-5">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_280px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">نظرة عامة</h1>
          <span className={`text-sm ${muted}`}>السبت ٢٦ يوليو · ٣ ملاعب نشطة · مفتوح ٤:٠٠ م – ٢:٠٠ ص</span>
        </div>
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

      {/* KPIs */}
      <div className="grid gap-3 grid-cols-2 md:grid-cols-[repeat(auto-fit,minmax(min(190px,100%),1fr))]">
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
          <span className={`text-[13px] ${muted}`}>حجوزات اليوم</span>
          <span className="text-[30px] font-bold">١١</span>
          <span className={`text-xs ${muted}`}>٩ مؤكدة · ٢ بعد منتصف الليل</span>
        </div>
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
          <span className={`text-[13px] ${muted}`}>طلبات بانتظار العربون</span>
          <span className="text-[30px] font-bold">٣</span>
          <span className={`text-xs ${muted}`}>أقرب انتهاء خلال ٢٨ دقيقة</span>
        </div>
        <Link
          href="/reservations/proof-review"
          className="bg-white border-2 border-brand-gold rounded-[14px] p-[15px] flex flex-col gap-1.5 text-slate-900 no-underline"
        >
          <span className="text-[13px] text-[#7A4D00] font-semibold">إيصالات للمراجعة</span>
          <span className="text-[30px] font-bold">٢</span>
          <span className="text-xs text-navy-900 font-semibold underline">راجع الآن ←</span>
        </Link>
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5`}>
          <span className={`text-[13px] ${muted}`}>إشغال اليوم</span>
          <span className="text-[30px] font-bold">٦٨٪</span>
          <div className="h-1.5 rounded-[3px] bg-[#E4E7EC] overflow-hidden">
            <div className="h-full bg-navy-900" style={{ width: "68%" }} />
          </div>
        </div>
      </div>

      {/* Finance strip */}
      <section aria-label="ملخص مالي لليوم" className={`${card} rounded-[18px] grid grid-cols-[repeat(auto-fit,minmax(min(200px,100%),1fr))]`}>
        {finance.map((f, i) => (
          <div key={f.label} className={`px-5 py-4 flex flex-col gap-1 ${i < finance.length - 1 ? "border-l border-[#E4E7EC]" : ""}`}>
            <span className={`text-[13px] ${f.labelCls}`}>{f.label}</span>
            <span className={`text-[22px] font-bold ${f.valueCls}`}>{f.value}</span>
          </div>
        ))}
      </section>

      <div className="flex flex-wrap gap-4 items-start">
        {/* Upcoming table */}
        <section className={`flex-[999_1_520px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
          <div className="px-5 py-4 flex items-center justify-between border-b border-[#E4E7EC]">
            <h2 className="text-base font-bold">الحجوزات الجاية النهارده</h2>
            <Link href="/calendar" className="text-[13px] font-semibold text-navy-900 hover:text-brand-red">
              فتح التقويم
            </Link>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[560px] border-collapse text-sm">
              <thead>
                <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
                  <th className="px-5 py-2.5 font-semibold">الميعاد</th>
                  <th className="px-3 py-2.5 font-semibold">الملعب</th>
                  <th className="px-3 py-2.5 font-semibold">العميل</th>
                  <th className="px-3 py-2.5 font-semibold">العربون</th>
                  <th className="px-5 py-2.5 font-semibold">الحالة</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(([time, pitch, who, dep, status, tone], i) => (
                  <tr key={i} className="border-t border-[#EEF0F5]">
                    <td className="px-5 py-3 font-semibold whitespace-nowrap">{time}</td>
                    <td className="p-3 whitespace-nowrap">{pitch}</td>
                    <td className="p-3">{who}</td>
                    <td className="p-3 whitespace-nowrap text-[#333B4A]">{dep}</td>
                    <td className="px-5 py-3">
                      <Badge tone={tone}>{status}</Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <div className="flex-[1_1_300px] min-w-0 flex flex-col gap-4">
          {/* Proofs */}
          <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`}>
            <div className="flex justify-between items-center">
              <h2 className="text-base font-bold">إيصالات بانتظارك</h2>
              <span className={`text-xs ${muted}`}>مهلة المراجعة ساعتين</span>
            </div>
            {proofs.map((p) => (
              <Link
                key={p.who}
                href="/reservations/proof-review"
                className="flex gap-3 items-center p-2.5 rounded-[10px] bg-[#F8F9FB] text-slate-900 no-underline hover:bg-[#F1F3F7]"
              >
                <span className="w-10 h-12 rounded-md bg-[repeating-linear-gradient(135deg,#DCE0E8_0_5px,#EEF0F5_5px_10px)]" />
                <span className="flex-1 flex flex-col gap-0.5">
                  <span className="text-sm font-bold">{p.who}</span>
                  <span className={`text-xs ${muted}`}>{p.meta}</span>
                </span>
                <span className="text-xs font-bold text-[#7A4D00]">{p.left}</span>
              </Link>
            ))}
          </section>

          {/* Occupancy chart */}
          <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-3.5`}>
            <div className="flex justify-between items-center gap-2">
              <h2 className="text-base font-bold">الإشغال</h2>
              <div className="flex gap-1 bg-[#F4F5F7] rounded-lg p-[3px]">
                {(
                  [
                    ["this", "هذا الأسبوع"],
                    ["last", "السابق"],
                  ] as const
                ).map(([id, label]) => (
                  <button
                    key={id}
                    type="button"
                    aria-pressed={range === id}
                    onClick={() => setRange(id)}
                    className={`min-h-[30px] px-2.5 rounded-md text-xs ${
                      range === id ? "bg-white text-navy-900 font-bold shadow-[0_1px_2px_rgba(15,27,61,0.12)]" : `bg-transparent ${muted}`
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
            <div
              role="img"
              aria-label={`نسبة الإشغال اليومية: ${vals.join("، ")} بالمئة من السبت للجمعة`}
              className="grid grid-cols-7 gap-2 items-end h-[150px]"
            >
              {vals.map((v, i) => (
                <div key={i} className="flex flex-col items-center gap-1 h-full justify-end">
                  <span className="text-[11px] text-[#3B4352]">{toArabicDigits(v)}٪</span>
                  <div
                    className={`w-full max-w-[32px] rounded-t-md rounded-b-sm ${i === 0 && range === "this" ? "bg-brand-red" : "bg-navy-900"}`}
                    style={{ height: Math.round(v * 1.15) }}
                  />
                </div>
              ))}
            </div>
            <div className={`grid grid-cols-7 gap-2 text-[11px] ${muted} text-center -mt-1.5`}>
              {weekDays.map((d) => (
                <span key={d}>{d}</span>
              ))}
            </div>
          </section>
        </div>
      </div>

      {/* Activity */}
      <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-2.5`}>
        <h2 className="text-base font-bold">آخر النشاط</h2>
        <div className="flex gap-2.5 text-sm items-center">
          <span className="w-2 h-2 rounded-full bg-brand-gold" />
          <span className="flex-1">
            أحمد سامي رفع إيصال عربون لحجز <span dir="ltr">KH-2607-0142</span>
          </span>
          <span className={`text-xs ${muted}`}>٧:٠٩ م</span>
        </div>
        <div className="flex gap-2.5 text-sm items-center">
          <span className="w-2 h-2 rounded-full bg-[#A8261C]" />
          <span className="flex-1">
            محمود (موظف) رفض إيصال لحجز <span dir="ltr">KH-2807-0151</span> — المبلغ ناقص
          </span>
          <span className={`text-xs ${muted}`}>٧:١٢ م</span>
        </div>
        <div className="flex gap-2.5 text-sm items-center">
          <span className="w-2 h-2 rounded-full bg-navy-900" />
          <span className="flex-1">حظر ملعب ٣ للصيانة الأحد ٤:٠٠ م – ٦:٠٠ م</span>
          <span className={`text-xs ${muted}`}>٥:٣٠ م</span>
        </div>
      </section>
    </Page>
  );
}
