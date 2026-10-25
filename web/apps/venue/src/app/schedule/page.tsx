"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Badge, Page, Switch, Toast, card, chip, inputClass, muted, redShadow, toArabicDigits } from "@/components/ui";

type Tab = "week" | "closures" | "recurring";
// [open, from, to] — hour index where 0 = 6 AM … 24 = 6 AM next day
type DayHours = [boolean, number, number];

interface Closure {
  day: number;
  month: string;
  title: string;
  meta: string;
}

const DAY_NAMES = ["السبت", "الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة"];
const PITCHES = ["ملعب ١", "ملعب ٢", "ملعب ٣"];
const CLOSE_DAYS: [string, number][] = [
  ["السبت ٩ أغسطس", 9],
  ["الجمعة ١٥ أغسطس", 15],
  ["الخميس ٢١ أغسطس", 21],
  ["السبت ٢٣ أغسطس", 23],
];
const RECURRING: [string, string][] = [
  ["أكاديمية الدلتا", "كل خميس ١٠ م · ملعب ٢ · ساعتين"],
  ["فريق الشركة — أورانج", "كل اتنين ٨ م · ملعب ١ · ساعة"],
  ["دوري الحي", "كل جمعة ٤ م · ملعب ٣ · ساعة ونص"],
];

const initialWeek = (): DayHours[] => [
  [true, 10, 20],
  [true, 10, 20],
  [true, 10, 20],
  [true, 10, 20],
  [true, 8, 22],
  [true, 8, 22],
  [true, 10, 20],
];
const initialClosures: Closure[] = [
  { day: 1, month: "أغسطس", title: "صيانة النجيل", meta: "كل الملاعب · اليوم كله" },
  { day: 6, month: "أكتوبر", title: "أجازة رسمية", meta: "ملعب ٢ بس" },
];

const fmt = (i: number) => {
  const h = (6 + i) % 24;
  const h12 = h % 12 === 0 ? 12 : h % 12;
  return `${toArabicDigits(h12)} ${h < 12 ? "ص" : "م"}`;
};

const smallChip = (on: boolean) => chip(on, "min-h-[38px] px-3.5 rounded-[10px] text-[13px]");

export default function SchedulePage() {
  const [pitch, setPitch] = useState(0);
  const [tab, setTab] = useState<Tab>("week");
  const [week, setWeek] = useState<DayHours[]>(initialWeek);
  const [brk, setBrk] = useState("none");
  const [notice, setNotice] = useState(1);
  const [win, setWin] = useState(14);
  const [closures, setClosures] = useState<Closure[]>(initialClosures);
  const [cDay, setCDay] = useState(0);
  const [cReason, setCReason] = useState("");
  const [cAll, setCAll] = useState(true);
  const [rec, setRec] = useState<Record<number, boolean>>({ 0: true, 1: true, 2: false });
  const [toast, setToast] = useState<string | null>(null);

  const setDay = (i: number, patch: (r: DayHours) => void) =>
    setWeek(
      week.map((r, j) => {
        if (j !== i) return r;
        const copy = [...r] as DayHours;
        patch(copy);
        return copy;
      })
    );

  // Confirmed bookings that would fall outside the new hours.
  const affected: string[] = [];
  if (!week[0][0] || week[0][2] <= 14) affected.push("أحمد سامي — السبت ٨ م");
  if (!week[5][0] || week[5][2] <= 16) affected.push("أكاديمية الدلتا — الخميس ١٠ م");

  const addClosure = () => {
    const [label, dnum] = CLOSE_DAYS[cDay];
    setClosures([
      ...closures,
      {
        day: dnum,
        month: "أغسطس",
        title: cReason.trim() || "قفل يوم",
        meta: `${cAll ? "كل الملاعب" : `${PITCHES[pitch]} بس`} · اليوم كله`,
      },
    ]);
    setCReason("");
    setToast(`اتقفل ${label}. مفيش حجوزات عليه — مفيش حد متأثر.`);
  };

  const reset = () => {
    setWeek(initialWeek());
    setBrk("none");
    setNotice(1);
    setWin(14);
    setClosures(initialClosures);
    setRec({ 0: true, 1: true, 2: false });
    setToast("رجعت المواعيد للنسخة المحفوظة.");
  };

  const stepBtn = "w-8 h-9 rounded-lg border border-[#DCE0E8] bg-white";

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">المواعيد وساعات التشغيل</h1>
        <div role="tablist" className="flex gap-1.5 flex-wrap">
          {PITCHES.map((label, i) => (
            <button
              key={label}
              type="button"
              role="tab"
              aria-selected={pitch === i}
              onClick={() => {
                setPitch(i);
                setToast(`بتعدّل مواعيد ${label} — كل ملعب ليه جدوله.`);
              }}
              className={smallChip(pitch === i)}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8]">
        {(
          [
            ["week", "الجدول الأسبوعي"],
            ["closures", "أيام مقفولة وأجازات"],
            ["recurring", "حجوزات ثابتة"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setTab(id)}
            className={`min-h-[44px] px-4 bg-transparent text-sm -mb-px border-b-[3px] ${
              tab === id ? "text-navy-900 font-bold border-brand-red" : `${muted} font-medium border-transparent`
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {affected.length > 0 && (
        <div role="alert" className="bg-[#FEF3D6] text-[#7A4D00] rounded-[14px] px-4 py-3 flex gap-3 items-center flex-wrap">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <path d="M12 3 2 21h20L12 3z" />
            <path d="M12 10v5M12 18h.01" />
          </svg>
          <span className="flex-[1_1_260px] text-sm">
            التعديل ده بيخبط في {toArabicDigits(affected.length)} حجز مأكد: {affected.join("، ")}. الحجوزات دي هتفضل زي ما هي — لازم تكلّم
            أصحابها لو عايز تلغيها.
          </span>
          <Link href="/reservations" className="text-sm font-bold text-[#7A4D00] underline">
            شوف الحجوزات المتأثرة
          </Link>
        </div>
      )}

      {tab === "week" && (
        <div className="flex flex-col gap-4">
          <section className={`${card} rounded-[20px] px-5 py-2`}>
            {week.map(([open, from, to], i) => (
              <div key={i} className="flex flex-wrap items-center gap-3 py-3 border-b border-[#EEF0F5] last:border-b-0">
                <span className="w-[84px] text-[15px] font-bold">{DAY_NAMES[i]}</span>
                <Switch on={open} label={`فتح يوم ${DAY_NAMES[i]}`} onToggle={() => setDay(i, (r) => (r[0] = !r[0]))} />
                {open ? (
                  <div className="flex items-center gap-2 flex-[1_1_360px] flex-wrap">
                    <span className={`text-[13px] ${muted}`}>من</span>
                    <button type="button" aria-label="أبكر" className={stepBtn} onClick={() => setDay(i, (r) => (r[1] = Math.max(0, r[1] - 1)))}>
                      ‹
                    </button>
                    <span className="min-w-[76px] text-center text-sm font-bold">{fmt(from)}</span>
                    <button type="button" aria-label="أتأخر" className={stepBtn} onClick={() => setDay(i, (r) => (r[1] = Math.min(r[2] - 1, r[1] + 1)))}>
                      ›
                    </button>
                    <span className={`text-[13px] ${muted}`}>لحد</span>
                    <button type="button" aria-label="أبكر" className={stepBtn} onClick={() => setDay(i, (r) => (r[2] = Math.max(r[1] + 1, r[2] - 1)))}>
                      ‹
                    </button>
                    <span className="min-w-[76px] text-center text-sm font-bold">{fmt(to)}</span>
                    <button type="button" aria-label="أتأخر" className={stepBtn} onClick={() => setDay(i, (r) => (r[2] = Math.min(24, r[2] + 1)))}>
                      ›
                    </button>
                    {6 + to > 24 && <Badge tone="wait">بيعدّي نص الليل</Badge>}
                    <span className={`text-[13px] ${muted} ms-auto`}>{toArabicDigits(to - from)} ساعة</span>
                  </div>
                ) : (
                  <span className="text-sm text-[#8A94AA]">مقفول</span>
                )}
              </div>
            ))}
          </section>

          <div className="grid gap-4 grid-cols-[repeat(auto-fit,minmax(260px,1fr))]">
            <section className={`${card} rounded-[18px] p-[18px] flex flex-col gap-2.5`}>
              <h2 className="text-base font-bold">استراحة يومية</h2>
              <span className={`text-[13px] ${muted}`}>وقت صيانة أو نضافة مش بيتحجز فيه.</span>
              <div className="flex gap-1.5 flex-wrap">
                {(
                  [
                    ["none", "مفيش"],
                    ["15", "٣ – ٤ م"],
                    ["17", "٥ – ٦ م"],
                  ] as const
                ).map(([id, label]) => (
                  <button key={id} type="button" aria-pressed={brk === id} onClick={() => setBrk(id)} className={smallChip(brk === id)}>
                    {label}
                  </button>
                ))}
              </div>
            </section>
            <section className={`${card} rounded-[18px] p-[18px] flex flex-col gap-2.5`}>
              <h2 className="text-base font-bold">أقل مهلة للحجز</h2>
              <span className={`text-[13px] ${muted}`}>اللاعب يحجز قبل الماتش بأد إيه على الأقل.</span>
              <div className="flex gap-1.5 flex-wrap">
                {(
                  [
                    [0, "فوراً"],
                    [1, "ساعة"],
                    [3, "٣ ساعات"],
                    [24, "يوم"],
                  ] as const
                ).map(([id, label]) => (
                  <button key={id} type="button" aria-pressed={notice === id} onClick={() => setNotice(id)} className={smallChip(notice === id)}>
                    {label}
                  </button>
                ))}
              </div>
            </section>
            <section className={`${card} rounded-[18px] p-[18px] flex flex-col gap-2.5`}>
              <h2 className="text-base font-bold">أقصى مدة حجز مقدماً</h2>
              <span className={`text-[13px] ${muted}`}>المواعيد بتفتح للحجز قبلها بأد إيه.</span>
              <div className="flex gap-1.5 flex-wrap">
                {(
                  [
                    [7, "أسبوع"],
                    [14, "أسبوعين"],
                    [30, "شهر"],
                  ] as const
                ).map(([id, label]) => (
                  <button key={id} type="button" aria-pressed={win === id} onClick={() => setWin(id)} className={smallChip(win === id)}>
                    {label}
                  </button>
                ))}
              </div>
            </section>
          </div>
        </div>
      )}

      {tab === "closures" && (
        <div className="flex flex-wrap gap-4 items-start">
          <section className={`flex-[999_1_420px] min-w-0 ${card} rounded-[20px] px-5 py-2`}>
            {closures.map((c, i) => (
              <div key={`${c.title}-${i}`} className="flex items-center gap-3 py-3.5 border-b border-[#EEF0F5] last:border-b-0">
                <span className="w-14 h-14 rounded-[14px] bg-[#FDECEA] text-[#A8261C] flex flex-col items-center justify-center shrink-0">
                  <span className="text-lg font-bold">{toArabicDigits(c.day)}</span>
                  <span className="text-[11px]">{c.month}</span>
                </span>
                <span className="flex-1 flex flex-col gap-0.5">
                  <span className="text-[15px] font-bold">{c.title}</span>
                  <span className={`text-[13px] ${muted}`}>{c.meta}</span>
                </span>
                <button
                  type="button"
                  onClick={() => {
                    setClosures(closures.filter((_, j) => j !== i));
                    setToast(`اتلغى قفل ${c.title} والمواعيد رجعت متاحة.`);
                  }}
                  className="min-h-[38px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-[#A8261C] text-[13px] font-semibold"
                >
                  إلغاء القفل
                </button>
              </div>
            ))}
            {closures.length === 0 && <p className={`py-6 text-center ${muted}`}>مفيش أيام مقفولة جاية.</p>}
          </section>
          <aside className={`flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[17px] font-bold">قفل يوم أو أجازة</h2>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">اليوم</span>
              <div className="flex gap-1.5 flex-wrap">
                {CLOSE_DAYS.map(([label], i) => (
                  <button key={label} type="button" aria-pressed={cDay === i} onClick={() => setCDay(i)} className={smallChip(cDay === i)}>
                    {label}
                  </button>
                ))}
              </div>
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="cr" className="text-[13px] font-semibold">
                السبب (بيظهر للاعيبة)
              </label>
              <input id="cr" value={cReason} onChange={(e) => setCReason(e.target.value)} placeholder="مثلاً: صيانة النجيل" className={inputClass} />
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">الملاعب</span>
              <div className="flex gap-1.5">
                {(
                  [
                    [true, "كل الملاعب"],
                    [false, "الملعب المختار بس"],
                  ] as const
                ).map(([id, label]) => (
                  <button key={label} type="button" aria-pressed={cAll === id} onClick={() => setCAll(id)} className={smallChip(cAll === id)}>
                    {label}
                  </button>
                ))}
              </div>
            </div>
            <button type="button" onClick={addClosure} className="min-h-[46px] rounded-[14px] bg-navy-900 text-white text-sm font-bold">
              قفل اليوم ده
            </button>
          </aside>
        </div>
      )}

      {tab === "recurring" && (
        <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 flex flex-col`}>
          {RECURRING.map(([who, when], i) => {
            const on = !!rec[i];
            return (
              <div key={who} className="flex flex-wrap items-center gap-3 py-3.5 border-b border-[#EEF0F5]">
                <span className="flex-[1_1_220px] flex flex-col gap-0.5">
                  <span className="text-[15px] font-bold">{who}</span>
                  <span className={`text-[13px] ${muted}`}>{when}</span>
                </span>
                <Badge tone={on ? "ok" : "wait"}>{on ? "شغال" : "متوقف مؤقتاً"}</Badge>
                <button
                  type="button"
                  onClick={() => {
                    setRec({ ...rec, [i]: !on });
                    setToast(on ? `اتوقف الحجز الثابت لـ ${who}. المواعيد بقت متاحة.` : `رجع الحجز الثابت لـ ${who}.`);
                  }}
                  className="min-h-[38px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
                >
                  {on ? "إيقاف مؤقت" : "تشغيل"}
                </button>
              </div>
            );
          })}
          <Link
            href="/reservations/manual"
            className={`mt-3.5 self-start min-h-[44px] px-[18px] rounded-xl bg-brand-red text-white text-sm font-bold flex items-center ${redShadow}`}
          >
            + حجز ثابت جديد
          </Link>
        </section>
      )}

      <div className="flex gap-2.5 justify-end">
        <button
          type="button"
          onClick={reset}
          className="min-h-[46px] px-5 rounded-[14px] border border-[#C9CFD9] bg-white text-navy-900 text-sm font-semibold"
        >
          رجّع زي ما كان
        </button>
        <button
          type="button"
          onClick={() =>
            setToast(
              affected.length
                ? "اتحفظت المواعيد. الحجوزات المأكدة اللي برّه الساعات الجديدة متلغتش."
                : "اتحفظت المواعيد وبقت ظاهرة في التطبيق."
            )
          }
          className={`min-h-[46px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow}`}
        >
          حفظ المواعيد
        </button>
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
