"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, Switch, Toast, card, chip, muted, redShadow, toArabicDigits } from "@/components/ui";

type RuleKey = "base" | "peak" | "weekend" | "holiday";
type Toggleable = Exclude<RuleKey, "base">;

interface PitchRules {
  base: number;
  peak: number;
  weekend: number;
  holiday: number;
  on: Record<Toggleable, boolean>;
}

interface Special {
  title: string;
  date: string;
  price: number;
}

const PITCHES = ["ملعب ١", "ملعب ٢", "ملعب ٣"];
const RULES: [RuleKey, string, string][] = [
  ["base", "السعر الأساسي", "كل الأيام والساعات"],
  ["peak", "ساعات الذروة", "من ٦ م لـ ١١ م"],
  ["weekend", "آخر الأسبوع", "الخميس والجمعة"],
  ["holiday", "الأجازات الرسمية", "بيتطبّق أوتوماتيك"],
];
// label, isWeekend, isHoliday
const PREVIEW_DAYS: [string, boolean, boolean][] = [
  ["الاتنين", false, false],
  ["الجمعة", true, false],
  ["يوم العيد", false, true],
];
// label, isPeak
const PREVIEW_HOURS: [string, boolean][] = [
  ["٤ م", false],
  ["٨ م", true],
  ["١٢ ص", false],
];

const smallChip = (on: boolean) => chip(on, "min-h-[38px] px-3.5 rounded-[10px] text-[13px]");
const darkChip = (on: boolean) =>
  `min-h-[36px] px-3 rounded-[10px] text-[13px] font-semibold border ${
    on ? "bg-brand-gold text-navy-900 border-brand-gold" : "bg-white/[0.08] text-white border-white/20"
  }`;

export default function PricingPage() {
  const [pitch, setPitch] = useState(0);
  const [pvDay, setPvDay] = useState(0);
  const [pvHour, setPvHour] = useState(2);
  const [rules, setRules] = useState<PitchRules[]>([
    { base: 150, peak: 180, weekend: 170, holiday: 200, on: { peak: true, weekend: true, holiday: false } },
    { base: 220, peak: 260, weekend: 250, holiday: 280, on: { peak: true, weekend: true, holiday: false } },
    { base: 150, peak: 180, weekend: 170, holiday: 200, on: { peak: true, weekend: false, holiday: false } },
  ]);
  const [specials, setSpecials] = useState<Special[]>([
    { title: "نهائي كأس الأمم", date: "الأحد ١٧ أغسطس", price: 250 },
    { title: "أول يوم في العيد", date: "الخميس ٢١ أغسطس", price: 230 },
  ]);
  const [toast, setToast] = useState<string | null>(null);

  const r = rules[pitch];
  const setR = (patch: Partial<PitchRules>) => setRules(rules.map((x, i) => (i === pitch ? { ...x, ...patch } : x)));

  // Effective price: highest active rule that matches the preview slot.
  const [dName, isWeekend, isHol] = PREVIEW_DAYS[pvDay];
  const [hName, isPeak] = PREVIEW_HOURS[pvHour];
  const candidates: [number, string][] = [];
  if (isHol && r.on.holiday) candidates.push([r.holiday, "الأجازات الرسمية"]);
  if (isWeekend && r.on.weekend) candidates.push([r.weekend, "آخر الأسبوع"]);
  if (isPeak && r.on.peak) candidates.push([r.peak, "ساعات الذروة"]);
  candidates.sort((a, b) => b[0] - a[0]);
  const [price, rule] = candidates[0] ?? [r.base, "السعر الأساسي"];

  const stepBtn = "w-9 h-9 rounded-[10px] border border-[#DCE0E8] bg-white text-lg";

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">الأسعار</h1>
        <div className="flex gap-1.5 flex-wrap">
          {PITCHES.map((label, i) => (
            <button key={label} type="button" aria-pressed={pitch === i} onClick={() => setPitch(i)} className={smallChip(pitch === i)}>
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="bg-[#EEF0F5] text-[#3B4352] rounded-[14px] px-4 py-3 text-sm flex gap-2.5 items-center">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 8h.01M11 12h1v5h1" />
        </svg>
        <span>أي تعديل في الأسعار بيطبّق على الحجوزات الجديدة بس. الحجوزات الموجودة بتفضل بسعرها وقت الحجز.</span>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <div className="flex-[999_1_440px] min-w-0 flex flex-col gap-4">
          <section className={`${card} rounded-[20px] p-5 flex flex-col gap-1`}>
            <h2 className="mb-2 text-[17px] font-bold">قواعد السعر — {PITCHES[pitch]}</h2>
            {RULES.map(([k, name, when]) => {
              const on = k === "base" ? true : r.on[k];
              return (
                <div key={k} className="flex flex-wrap items-center gap-3 py-3 border-b border-[#EEF0F5] last:border-b-0">
                  <span className="flex-[1_1_200px] flex flex-col gap-0.5">
                    <span className="text-[15px] font-bold">{name}</span>
                    <span className={`text-[13px] ${muted}`}>{on ? when : `${when} · متوقفة`}</span>
                  </span>
                  <div className="flex items-center gap-1.5">
                    <button type="button" aria-label="نقّص ١٠" className={stepBtn} onClick={() => setR({ [k]: Math.max(50, r[k] - 10) })}>
                      −
                    </button>
                    <span className="min-w-[96px] text-center text-base font-bold">{toArabicDigits(r[k])} ج.م</span>
                    <button type="button" aria-label="زوّد ١٠" className={stepBtn} onClick={() => setR({ [k]: r[k] + 10 })}>
                      +
                    </button>
                  </div>
                  {k !== "base" && (
                    <Switch on={on} label={`تفعيل ${name}`} onToggle={() => setR({ on: { ...r.on, [k]: !on } })} />
                  )}
                </div>
              );
            })}
          </section>

          <section className={`${card} rounded-[20px] p-5 flex flex-col gap-1`}>
            <div className="flex items-center gap-2.5 mb-2">
              <h2 className="flex-1 text-[17px] font-bold">تواريخ خاصة</h2>
              <button
                type="button"
                onClick={() => {
                  setSpecials([...specials, { title: "ماتش مهم", date: "السبت ٣٠ أغسطس", price: r.base + 50 }]);
                  setToast(`اتضاف تاريخ خاص بسعر ${toArabicDigits(r.base + 50)} ج.م.`);
                }}
                className="min-h-[38px] px-3.5 rounded-[10px] border border-navy-900 bg-white text-navy-900 text-[13px] font-bold"
              >
                + إضافة تاريخ
              </button>
            </div>
            {specials.map((x, i) => (
              <div key={`${x.title}-${i}`} className="flex items-center gap-3 py-3 border-b border-[#EEF0F5] last:border-b-0">
                <span className="flex-1 flex flex-col gap-0.5">
                  <span className="text-[15px] font-bold">{x.title}</span>
                  <span className={`text-[13px] ${muted}`}>{x.date}</span>
                </span>
                <span className="text-[15px] font-bold">{toArabicDigits(x.price)} ج.م</span>
                <button
                  type="button"
                  aria-label="حذف"
                  onClick={() => {
                    setSpecials(specials.filter((_, j) => j !== i));
                    setToast(`اتشال سعر ${x.title}.`);
                  }}
                  className="min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-[#A8261C] text-[13px] font-semibold"
                >
                  حذف
                </button>
              </div>
            ))}
          </section>
        </div>

        <aside
          aria-label="معاينة"
          className="flex-[1_1_320px] min-w-0 bg-navy-900 text-white rounded-[20px] p-5 flex flex-col gap-3 bg-[radial-gradient(circle_at_0%_0%,rgba(245,182,43,0.18),transparent_45%),radial-gradient(circle_at_100%_100%,rgba(214,59,47,0.22),transparent_45%)]"
        >
          <h2 className="text-[17px] font-bold">اللاعب هيشوف إيه؟</h2>
          <span className="text-[13px] text-[#AEB7CA]">اختار يوم وساعة تشوف السعر الفعلي.</span>
          <div className="flex gap-1.5 flex-wrap">
            {PREVIEW_DAYS.map(([label], i) => (
              <button key={label} type="button" aria-pressed={pvDay === i} onClick={() => setPvDay(i)} className={darkChip(pvDay === i)}>
                {label}
              </button>
            ))}
          </div>
          <div className="flex gap-1.5 flex-wrap">
            {PREVIEW_HOURS.map(([label], i) => (
              <button key={label} type="button" aria-pressed={pvHour === i} onClick={() => setPvHour(i)} className={darkChip(pvHour === i)}>
                {label}
              </button>
            ))}
          </div>
          <div className="bg-white text-slate-900 rounded-2xl p-4 flex flex-col gap-2">
            <span className={`text-[13px] ${muted}`}>
              {PITCHES[pitch]} · {dName} · {hName}
            </span>
            <span className="text-[30px] font-bold">
              {toArabicDigits(price)} <span className="text-[15px] font-semibold">ج.م/ساعة</span>
            </span>
            <span className={`text-[13px] ${muted}`}>
              القاعدة المطبّقة: <b className="text-navy-900">{rule}</b>
            </span>
            <span className={`text-[13px] ${muted}`}>
              العربون: {toArabicDigits(Math.round(price / 3 / 10) * 10)} ج.م ·{" "}
              <Link href="/policies" className="text-navy-900 underline hover:text-brand-red">
                من سياسات الحجز
              </Link>
            </span>
          </div>
          <button
            type="button"
            onClick={() => setToast(`اتحفظت أسعار ${PITCHES[pitch]}. الحجوزات اللي اتعملت قبل كده بسعرها القديم.`)}
            className={`min-h-[48px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold ${redShadow}`}
          >
            حفظ الأسعار
          </button>
        </aside>
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
