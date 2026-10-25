"use client";

import React, { useEffect, useState } from "react";
import Link from "next/link";
import { Badge, Page, Toast, Tone, card, muted, redShadow, toArabicDigits } from "@/components/ui";

type View = "day" | "week" | "month";
type Kind = "ok" | "rev" | "wait" | "blk";

const KIND: Record<Kind, { label: string; tone: Tone; box: string }> = {
  ok: { label: "✓ مؤكد", tone: "ok", box: "bg-navy-900 text-white border border-navy-900" },
  rev: { label: "قيد المراجعة", tone: "warn", box: "bg-[#FEF3D6] text-[#6B4400] border-2 border-[#D9A21B]" },
  wait: { label: "بانتظار العربون", tone: "wait", box: "bg-white text-slate-900 border-2 border-dashed border-[#8A94AA]" },
  blk: {
    label: "محظور",
    tone: "block",
    box: "bg-[repeating-linear-gradient(135deg,#DCE0E8_0_5px,#EEF0F5_5px_10px)] text-[#3B4352] border border-[#C9CFD9]",
  },
};

export default function CalendarPage() {
  const [view, setView] = useState<View>("week");
  const [toast, setToast] = useState<string | null>(null);

  useEffect(() => {
    const v = new URLSearchParams(window.location.search).get("view");
    if (v === "day" || v === "week" || v === "month") setView(v);
  }, []);

  const switchView = (v: View) => {
    setView(v);
    window.history.replaceState(null, "", v === "week" ? "/calendar" : `/calendar?view=${v}`);
  };

  return (
    <Page className="gap-4 !px-7 !pt-6 !pb-8">
      {view === "week" && <WeekView switcher={<ViewSwitch view={view} onChange={switchView} />} toast={setToast} />}
      {view === "day" && <DayView switcher={<ViewSwitch view={view} onChange={switchView} />} toast={setToast} />}
      {view === "month" && <MonthView switcher={<ViewSwitch view={view} onChange={switchView} />} onOpenDay={() => switchView("day")} />}
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function ViewSwitch({ view, onChange }: { view: View; onChange: (v: View) => void }) {
  return (
    <div role="group" aria-label="طريقة العرض" className="flex bg-white border border-[#EDF0F5] rounded-xl p-[3px] gap-0.5">
      {(
        [
          ["day", "يوم"],
          ["week", "أسبوع"],
          ["month", "شهر"],
        ] as const
      ).map(([id, label]) => (
        <button
          key={id}
          type="button"
          aria-current={view === id ? "true" : undefined}
          onClick={() => onChange(id)}
          className={`min-h-[36px] px-3.5 rounded-[9px] text-[13px] flex items-center ${
            view === id ? "bg-navy-900 text-white font-bold" : "text-[#3B4352]"
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}

function Pager({ label, onPrev, onNext, prevLabel, nextLabel, width = "min-w-[170px]" }: {
  label: string;
  onPrev: () => void;
  onNext: () => void;
  prevLabel: string;
  nextLabel: string;
  width?: string;
}) {
  const btn = "w-10 h-10 rounded-[10px] border border-[#EDF0F5] bg-white text-navy-900 flex items-center justify-center";
  return (
    <div className="flex items-center gap-1">
      <button type="button" onClick={onPrev} aria-label={prevLabel} className={btn}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M9 6l6 6-6 6" />
        </svg>
      </button>
      <span className={`${width} text-center text-sm font-bold`}>{label}</span>
      <button type="button" onClick={onNext} aria-label={nextLabel} className={btn}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M15 6l-6 6 6 6" />
        </svg>
      </button>
    </div>
  );
}

function ManualBtn() {
  return (
    <Link
      href="/reservations/manual"
      className={`min-h-[44px] px-[18px] rounded-[10px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold flex items-center gap-2 ${redShadow}`}
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" aria-hidden="true">
        <path d="M12 5v14M5 12h14" />
      </svg>
      حجز يدوي
    </Link>
  );
}

/* ---------------------------------- Week ---------------------------------- */

const WEEK_DAYS: [string, string][] = [
  ["السبت", "٢٦"],
  ["الأحد", "٢٧"],
  ["الإثنين", "٢٨"],
  ["الثلاثاء", "٢٩"],
  ["الأربعاء", "٣٠"],
  ["الخميس", "٣١"],
  ["الجمعة", "١"],
];
const WEEK_HOURS = ["٤ م", "٥ م", "٦ م", "٧ م", "٨ م", "٩ م", "١٠ م", "١١ م", "١٢ ص", "١ ص"];
const WEEKS = ["١٩ – ٢٥ يوليو", "٢٦ يوليو – ١ أغسطس", "٢ – ٨ أغسطس"];

// id, day, startRow, span, kind, who, tag, ref, price, deposit, phone, note
type WeekEvent = [string, number, number, number, Kind, string, string, string, string, string, string, string];
const WEEK_EVENTS: WeekEvent[] = [
  ["e1", 0, 0, 2, "ok", "أكاديمية الشروق", "متكرر أسبوعياً", "KH-R-0012", "٢٨٠ ج.م", "اشتراك شهري — مدفوع", "٠١١٢٠٠٤٥٦٧٨", "حجز متكرر كل سبت/أحد/إثنين ٤–٦ م حتى ٣٠ أغسطس."],
  ["e3", 0, 4, 1, "rev", "أحمد سامي", "إيصال ٥٠ ج.م", "KH-2607-0142", "١٥٠ ج.م", "٥٠ ج.م — إيصال مرفوع ٧:٠٩ م", "٠١٠١٢٣٤٥٦٧٨", "مهلة المراجعة تنتهي ٩:٠٩ م. الميعاد محجوز مؤقتاً."],
  ["e4", 0, 5, 1, "wait", "يوسف حسن", "ينتهي ٧:٣٨ م", "KH-2607-0147", "١٥٠ ج.م", "لم يُرفع — ٥٠ ج.م مطلوبة", "٠١٢٢٣٣٤٤٥٥٦", "لو مفيش إيصال قبل ٧:٣٨ م الطلب ينتهي والميعاد يتاح."],
  ["e5", 0, 8, 1, "ok", "فريق الصقور", "بعد منتصف الليل", "KH-2607-0139", "١٥٠ ج.م", "٥٠ ج.م معتمد", "٠١٠٩٨٧٦٥٤٣٢", "السبت ١٢:٠٠ ص = فجر الأحد ٢٧ يوليو."],
  ["e6", 1, 0, 2, "ok", "أكاديمية الشروق", "متكرر أسبوعياً", "KH-R-0012", "٢٨٠ ج.م", "اشتراك شهري — مدفوع", "٠١١٢٠٠٤٥٦٧٨", ""],
  ["e7", 1, 5, 1, "ok", "محمد إبراهيم", "عربون معتمد", "KH-2707-0150", "١٥٠ ج.م", "٥٠ ج.م معتمد", "٠١٠٠٥٥٥٦٦٧٧", ""],
  ["e8", 2, 0, 2, "ok", "أكاديمية الشروق", "متكرر أسبوعياً", "KH-R-0012", "٢٨٠ ج.م", "اشتراك شهري — مدفوع", "٠١١٢٠٠٤٥٦٧٨", ""],
  ["e9", 2, 4, 1, "ok", "حجز يدوي — م. فتحي", "نقدي في الملعب", "KH-2807-M004", "١٥٠ ج.م", "بدون عربون — نقدي", "٠١٠١١١٢٢٢٣٣", "أنشأه محمود (موظف) بالتليفون."],
  ["e10", 3, 3, 1, "ok", "كريم عادل", "عربون معتمد", "KH-2907-0153", "١٥٠ ج.م", "٥٠ ج.م معتمد", "٠١٠٢٢٣٣٤٤٥٥", ""],
  ["e11", 3, 7, 1, "wait", "مصطفى رمضان", "ينتهي ٨:١٠ م", "KH-2907-0158", "١٥٠ ج.م", "لم يُرفع — ٥٠ ج.م مطلوبة", "٠١١٥٥٦٦٧٧٨٨", ""],
  ["e12", 4, 0, 2, "blk", "صيانة النجيل", "محظور", "", "—", "—", "—", "حظر لمرة واحدة. أضافه المالك."],
  ["e13", 5, 4, 3, "ok", "دوري الشركات", "٣ ساعات", "KH-3107-0101", "٤٥٠ ج.م", "١٥٠ ج.م معتمد", "٠١٠٦٦٧٧٨٨٩٩", ""],
  ["e14", 5, 8, 1, "ok", "زياد طارق", "بعد منتصف الليل", "KH-3107-0104", "١٥٠ ج.م", "٥٠ ج.م معتمد", "٠١٢٧٧٨٨٩٩٠٠", ""],
  ["e15", 5, 9, 1, "rev", "هاني سعيد", "إيصال ٥٠ ج.م", "KH-3107-0107", "١٥٠ ج.م", "٥٠ ج.م — إيصال مرفوع", "٠١٠٤٤٥٥٦٦٧٧", ""],
  ["e16", 6, 2, 1, "ok", "فريق النجوم", "عربون معتمد", "KH-0108-0110", "١٥٠ ج.م", "٥٠ ج.م معتمد", "٠١٠١٢٣٤٥٦٧٨", ""],
  ["e17", 6, 5, 2, "ok", "عيد ميلاد — سيف", "ساعتين", "KH-0108-0112", "٣٠٠ ج.م", "١٠٠ ج.م معتمد", "٠١٠٣٣٤٤٥٥٦٦", ""],
];

const PITCH_FILTERS = ["ملعب ١ · ٥ لاعب", "ملعب ٢ · ٧ لاعب", "ملعب ٣ · ٥ لاعب"];

function WeekView({ switcher, toast }: { switcher: React.ReactNode; toast: (m: string | null) => void }) {
  const [selId, setSelId] = useState("e3");
  const [wk, setWk] = useState(1);
  const [pf, setPf] = useState(0);

  const cur = WEEK_EVENTS.find((e) => e[0] === selId) ?? WEEK_EVENTS[1];
  const timeOf = (row: number, span: number) => `${WEEK_HOURS[row]} – ${WEEK_HOURS[row + span] ?? "٢ ص"}`;
  const k = cur[4];

  const actionBtn = "min-h-[46px] rounded-[10px] bg-navy-900 text-white text-sm font-bold flex items-center justify-center";

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_200px] text-[26px] font-bold">تقويم الحجوزات</h1>
        {switcher}
        <Pager
          label={WEEKS[wk]}
          prevLabel="الأسبوع السابق"
          nextLabel="الأسبوع التالي"
          onPrev={() => {
            toast(wk === 0 ? "ده أقدم أسبوع متاح في المعاينة." : null);
            setWk(Math.max(0, wk - 1));
          }}
          onNext={() => {
            toast(wk === 2 ? "المواعيد بتفتح للحجز أسبوعين قدام بس." : null);
            setWk(Math.min(2, wk + 1));
          }}
        />
        <ManualBtn />
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="flex gap-1.5 flex-wrap">
          {PITCH_FILTERS.map((label, i) => {
            const on = pf === i;
            return (
              <button
                key={label}
                type="button"
                aria-pressed={on}
                onClick={() => {
                  setPf(i);
                  toast(on ? null : `بيعرض جدول ${label.split(" · ")[0]} — المعاينة بتستخدم نفس الحجوزات.`);
                }}
                className={`min-h-[36px] px-3.5 rounded-full text-[13px] border ${
                  on ? "border-navy-900 bg-navy-900 text-white font-semibold" : "border-[#DCE0E8] bg-white text-slate-900"
                }`}
              >
                {label}
              </button>
            );
          })}
          <button
            type="button"
            onClick={() => toast(null)}
            className="min-h-[36px] px-3.5 rounded-full border border-[#DCE0E8] bg-white text-slate-900 text-[13px]"
          >
            كل الملاعب (عرض اليوم)
          </button>
        </div>
        <Legend />
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="جدول الأسبوع" className={`flex-[999_1_640px] min-w-0 ${card} rounded-[18px] overflow-x-auto`}>
          <div className="min-w-[760px] grid grid-cols-[64px_repeat(7,minmax(0,1fr))] grid-rows-[56px_repeat(10,56px)]">
            <div className="col-start-1 row-start-1 border-b border-[#E4E7EC]" />
            {WEEK_DAYS.map(([dow, num], i) => (
              <div
                key={dow}
                style={{ gridColumn: i + 2, gridRow: 1 }}
                className={`flex flex-col items-center justify-center border-b border-[#E4E7EC] border-r border-r-[#EEF0F5] ${
                  i === 0 ? "bg-[#FDECEA] text-[#8A1F17]" : ""
                }`}
              >
                <span className={`text-xs ${i === 0 ? "" : muted}`}>{dow}</span>
                <span className="text-base font-bold">{num}</span>
              </div>
            ))}
            {WEEK_HOURS.map((label, i) => (
              <div
                key={label}
                style={{ gridColumn: 1, gridRow: i + 2 }}
                className={`text-[11px] px-2 pt-1 border-t border-[#EEF0F5] ${i >= 8 ? "text-[#7A4D00] font-bold" : muted}`}
              >
                {label}
              </div>
            ))}
            {Array.from({ length: 70 }, (_, n) => {
              const r = Math.floor(n / 7);
              const c = n % 7;
              return (
                <div
                  key={n}
                  style={{ gridColumn: c + 2, gridRow: r + 2 }}
                  className={`border-t border-r border-[#EEF0F5] ${c === 0 ? "bg-[#FBFBFC]" : ""}`}
                />
              );
            })}
            <div style={{ gridColumn: "1 / span 8", gridRow: 10 }} className="self-start h-0 border-t-2 border-dashed border-brand-gold relative z-[1]">
              <span className="absolute -top-[11px] right-1.5 text-[10px] font-bold text-[#7A4D00] bg-[#FEF3D6] rounded px-1.5 py-px">
                منتصف الليل — نفس يوم التشغيل
              </span>
            </div>
            {WEEK_EVENTS.map((e) => {
              const on = e[0] === selId;
              return (
                <button
                  key={e[0]}
                  type="button"
                  aria-pressed={on}
                  aria-label={`${e[5]}، ${WEEK_DAYS[e[1]][0]} ${timeOf(e[2], e[3])}، ${KIND[e[4]].label}`}
                  onClick={() => setSelId(e[0])}
                  style={{ gridColumn: e[1] + 2, gridRow: `${e[2] + 2} / span ${e[3]}` }}
                  className={`m-[3px] rounded-lg px-2 py-1 flex flex-col items-start justify-start min-w-0 text-right z-[2] ${KIND[e[4]].box} ${
                    on ? "shadow-[0_0_0_3px_#D63B2F]" : ""
                  }`}
                >
                  <span className="text-xs font-bold whitespace-nowrap overflow-hidden text-ellipsis max-w-full">{e[5]}</span>
                  <span className="text-[11px] whitespace-nowrap overflow-hidden text-ellipsis max-w-full">{e[6]}</span>
                </button>
              );
            })}
          </div>
        </section>

        <aside aria-label="تفاصيل الحجز" className={`flex-[1_1_300px] min-w-0 ${card} rounded-[18px] p-[18px] flex flex-col gap-3.5`}>
          <div className="flex items-center justify-between gap-2">
            <h2 className="text-[17px] font-bold">{cur[5]}</h2>
            <Badge tone={KIND[k].tone}>{KIND[k].label}</Badge>
          </div>
          {cur[7] && (
            <span dir="ltr" className={`text-right text-xs ${muted}`}>
              {cur[7]}
            </span>
          )}
          <div className="flex flex-col gap-2.5 text-sm">
            <div className="flex justify-between">
              <span className={muted}>الميعاد</span>
              <span className="font-semibold">
                {WEEK_DAYS[cur[1]][0]} {WEEK_DAYS[cur[1]][1]} · {timeOf(cur[2], cur[3])}
              </span>
            </div>
            <div className="flex justify-between">
              <span className={muted}>الملعب</span>
              <span>ملعب ١ · ٥ لاعب</span>
            </div>
            <div className="flex justify-between">
              <span className={muted}>السعر</span>
              <span>{cur[8]}</span>
            </div>
            <div className="flex justify-between">
              <span className={muted}>العربون</span>
              <span className="font-semibold">{cur[9]}</span>
            </div>
            <div className="flex justify-between">
              <span className={muted}>الهاتف</span>
              <span>{cur[10]}</span>
            </div>
          </div>
          {cur[11] && <div className="bg-[#F4F5F7] rounded-[10px] px-3 py-2.5 text-[13px] leading-5 text-[#333B4A]">{cur[11]}</div>}
          <div className="flex flex-col gap-2 mt-1">
            {k === "rev" && (
              <Link href="/reservations/proof-review" className={actionBtn}>
                مراجعة الإيصال
              </Link>
            )}
            {k === "wait" && (
              <button type="button" onClick={() => toast("اتبعت تذكير بالعربون للعميل SMS وإشعار.")} className={actionBtn}>
                إرسال تذكير بالعربون
              </button>
            )}
            {k === "ok" && (
              <button type="button" onClick={() => toast("اتسجل حضور الفريق. الحجز هيتحسب في الإيرادات المحققة.")} className={actionBtn}>
                تسجيل الحضور
              </button>
            )}
            {k === "blk" && (
              <button type="button" onClick={() => toast("اتشال الحظر والميعاد رجع متاح للحجز.")} className={actionBtn}>
                إزالة الحظر
              </button>
            )}
            {k !== "blk" && (
              <Link
                href="/reservations"
                className="min-h-[44px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-semibold flex items-center justify-center"
              >
                إلغاء الحجز (يتطلب سبب)
              </Link>
            )}
          </div>
        </aside>
      </div>
    </>
  );
}

function Legend() {
  return (
    <div className="flex flex-wrap gap-3.5 text-xs text-[#3B4352] ms-auto">
      <span className="flex items-center gap-1.5">
        <span className="w-3.5 h-3.5 rounded bg-navy-900" />
        مؤكد
      </span>
      <span className="flex items-center gap-1.5">
        <span className="w-3.5 h-3.5 rounded bg-[#FEF3D6] border-2 border-[#D9A21B]" />
        قيد المراجعة
      </span>
      <span className="flex items-center gap-1.5">
        <span className="w-3.5 h-3.5 rounded bg-white border-2 border-dashed border-[#8A94AA]" />
        بانتظار العربون
      </span>
      <span className="flex items-center gap-1.5">
        <span className="w-3.5 h-3.5 rounded bg-[repeating-linear-gradient(135deg,#C9CFD9_0_3px,#EEF0F5_3px_6px)]" />
        محظور
      </span>
    </div>
  );
}

/* ----------------------------------- Day ---------------------------------- */

const DAY_LABELS = ["السبت ٢٦ يوليو", "الأحد ٢٧ يوليو", "الإثنين ٢٨ يوليو"];
// id, pitch(col), row, span, kind, who, tag, [day for owner-added blocks]
type DayEvent = [string, number, number, number, Kind, string, string, number?];
const DAY_EVENTS: DayEvent[][] = [
  [
    ["e1", 0, 0, 2, "ok", "أكاديمية الشروق", "متكرر"],
    ["e2", 0, 4, 1, "rev", "أحمد سامي", "إيصال ٥٠ ج.م"],
    ["e3", 0, 5, 1, "wait", "يوسف حسن", "ينتهي ٧:٣٨ م"],
    ["e4", 0, 8, 1, "ok", "فريق الصقور", "بعد نص الليل"],
    ["e5", 1, 3, 1, "ok", "كريم عادل", "معتمد"],
    ["e6", 1, 7, 1, "rev", "عمر خالد", "إيصال ٦٠ ج.م"],
    ["e7", 2, 4, 1, "ok", "م. فتحي", "يدوي — نقدي"],
    ["e8", 2, 0, 2, "blk", "صيانة", "محظور"],
  ],
  [
    ["e1", 0, 0, 2, "ok", "أكاديمية الشروق", "متكرر"],
    ["e9", 0, 5, 1, "ok", "محمد إبراهيم", "معتمد"],
    ["e10", 1, 4, 2, "ok", "دوري الشركات", "ساعتين"],
  ],
  [
    ["e1", 0, 0, 2, "ok", "أكاديمية الشروق", "متكرر"],
    ["e11", 0, 4, 1, "ok", "م. فتحي", "يدوي"],
    ["e12", 0, 6, 1, "wait", "ملعب المدينة — تحويل", "إيصال مرفوض"],
  ],
];
const PITCH_HEADS: [string, string][] = [
  ["ملعب ١", "٥ لاعب · ١٥٠ ج.م"],
  ["ملعب ٢", "٧ لاعب · ٢٢٠ ج.م"],
  ["ملعب ٣", "٥ لاعب · ١٥٠ ج.م"],
];

function DayView({ switcher, toast }: { switcher: React.ReactNode; toast: (m: string | null) => void }) {
  const [day, setDay] = useState(0);
  const [selId, setSelId] = useState<string | null>("e2");
  const [slot, setSlot] = useState<[number, number] | null>(null);
  const [blocked, setBlocked] = useState<DayEvent[]>([]);

  const ev = DAY_EVENTS[day].concat(blocked.filter((b) => b[7] === day));
  const taken = new Set<string>();
  ev.forEach((e) => {
    for (let k = 0; k < e[3]; k++) taken.add(`${e[1]}-${e[2] + k}`);
  });
  const cur = ev.find((e) => e[0] === selId);
  const pct = Math.round((taken.size / 30) * 100);

  const go = (d: number) => {
    setDay(d);
    setSelId(null);
    setSlot(null);
  };

  const blockSlot = () => {
    if (!slot) return;
    const b: DayEvent = [`b${Date.now()}`, slot[0], slot[1], 1, "blk", "محظور", "أضافه المالك", day];
    setBlocked([...blocked, b]);
    setSlot(null);
    setSelId(b[0]);
    toast("اتحظر الميعاد. مش هيظهر للاعبين في التطبيق.");
  };

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_200px] text-[26px] font-bold">تقويم الحجوزات</h1>
        {switcher}
        <Pager
          label={DAY_LABELS[day]}
          prevLabel="اليوم السابق"
          nextLabel="اليوم التالي"
          onPrev={() => go(Math.max(0, day - 1))}
          onNext={() => go(Math.min(2, day + 1))}
        />
        <ManualBtn />
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="جدول اليوم" className={`flex-[999_1_640px] min-w-0 ${card} rounded-[18px] overflow-x-auto`}>
          <div className="min-w-[640px] grid grid-cols-[70px_repeat(3,minmax(0,1fr))] grid-rows-[56px_repeat(10,60px)]">
            <div className="col-start-1 row-start-1 border-b border-[#EDF0F5]" />
            {PITCH_HEADS.map(([name, meta], i) => (
              <div
                key={name}
                style={{ gridColumn: i + 2, gridRow: 1 }}
                className="flex flex-col items-center justify-center border-b border-[#EDF0F5] border-r border-r-[#EEF1F5]"
              >
                <span className="text-sm font-bold">{name}</span>
                <span className={`text-[11px] ${muted}`}>{meta}</span>
              </div>
            ))}
            {WEEK_HOURS.map((label, i) => (
              <div
                key={label}
                style={{ gridColumn: 1, gridRow: i + 2 }}
                className={`text-xs px-2.5 pt-1 border-t border-[#EEF1F5] ${i >= 8 ? "text-[#7A4D00] font-bold" : muted}`}
              >
                {label}
              </div>
            ))}
            {Array.from({ length: 30 }, (_, n) => {
              const r = Math.floor(n / 3);
              const c = n % 3;
              const free = !taken.has(`${c}-${r}`);
              const on = slot && slot[0] === c && slot[1] === r;
              return (
                <button
                  key={n}
                  type="button"
                  aria-label={`ملعب ${c + 1} ${WEEK_HOURS[r]}${free ? " فاضي" : ""}`}
                  onClick={() => {
                    if (!free) return;
                    setSlot([c, r]);
                    setSelId(null);
                  }}
                  style={{ gridColumn: c + 2, gridRow: r + 2 }}
                  className={`border-t border-r border-[#EEF1F5] ${on ? "bg-[#EEF1F7]" : "bg-transparent"} ${free ? "cursor-pointer" : "cursor-default"}`}
                />
              );
            })}
            {ev.map((e) => (
              <button
                key={e[0]}
                type="button"
                aria-label={`${e[5]}، ملعب ${e[1] + 1}، ${WEEK_HOURS[e[2]]}، ${KIND[e[4]].label}`}
                onClick={() => {
                  setSelId(e[0]);
                  setSlot(null);
                }}
                style={{ gridColumn: e[1] + 2, gridRow: `${e[2] + 2} / span ${e[3]}` }}
                className={`m-1 rounded-[10px] px-2.5 py-1.5 flex flex-col items-start gap-px text-right z-[2] ${KIND[e[4]].box} ${
                  e[0] === selId ? "shadow-[0_0_0_3px_#D63B2F]" : ""
                }`}
              >
                <span className="text-[13px] font-bold">{e[5]}</span>
                <span className="text-[11px]">{e[6]}</span>
              </button>
            ))}
          </div>
        </section>

        <aside className={`flex-[1_1_300px] min-w-0 ${card} rounded-[18px] p-[18px] flex flex-col gap-3`}>
          {cur && (
            <div className="flex flex-col gap-3">
              <h2 className="text-[17px] font-bold">{cur[5]}</h2>
              <Badge tone={KIND[cur[4]].tone} className="self-start">
                {KIND[cur[4]].label}
              </Badge>
              <span className="text-sm">
                ملعب {toArabicDigits(cur[1] + 1)} · {WEEK_HOURS[cur[2]]} · {cur[3] === 2 ? "ساعتين" : "ساعة"}
              </span>
              <span className={`text-[13px] ${muted}`}>{cur[6]}</span>
              <Link
                href="/reservations"
                className="min-h-[44px] rounded-xl bg-navy-900 text-white text-sm font-bold flex items-center justify-center"
              >
                فتح الحجز
              </Link>
            </div>
          )}
          {!cur && slot && (
            <div className="flex flex-col gap-3">
              <h2 className="text-[17px] font-bold">ميعاد فاضي</h2>
              <span className="text-sm">
                ملعب {toArabicDigits(slot[0] + 1)} · {DAY_LABELS[day]} · {WEEK_HOURS[slot[1]]}
              </span>
              <Link
                href="/reservations/manual"
                className="min-h-[44px] rounded-xl bg-brand-red text-white text-sm font-bold flex items-center justify-center"
              >
                حجز يدوي هنا
              </Link>
              <button
                type="button"
                onClick={blockSlot}
                className="min-h-[44px] rounded-xl border border-[#C9CFD9] bg-white text-navy-900 text-sm font-semibold"
              >
                حظر الميعاد (صيانة/استخدام خاص)
              </button>
            </div>
          )}
          <div className="border-t border-[#EEF1F5] pt-3 flex flex-col gap-1.5 text-[13px] text-[#333B4A]">
            <span className="font-bold">ملخص اليوم</span>
            <span>
              {toArabicDigits(ev.filter((e) => e[4] !== "blk").length)} حجوزات · إشغال {toArabicDigits(pct)}٪ ·{" "}
              {toArabicDigits(ev.filter((e) => e[4] === "rev").length)} إيصال بانتظار المراجعة
            </span>
          </div>
        </aside>
      </div>
    </>
  );
}

/* ---------------------------------- Month --------------------------------- */

const MONTHS = [
  { name: "يوليو", offset: 3, days: 31 },
  { name: "أغسطس", offset: 6, days: 31 },
];

function MonthView({ switcher, onOpenDay }: { switcher: React.ReactNode; onOpenDay: () => void }) {
  const [m, setM] = useState(0);
  const [sel, setSel] = useState(26);
  const M = MONTHS[m];

  const occ = (d: number) => (m === 1 && d > 10 ? ((d * 13) % 25) + 5 : ((d * 37 + m * 11) % 55) + 35);
  const cnt = (d: number) => Math.round((occ(d) / 100) * 30);
  const rev = (d: number) => (m === 0 && (d === 26 || d === 31) ? 2 : m === 0 && d === 28 ? 1 : 0);

  let total = 0;
  for (let d = 1; d <= M.days; d++) total += cnt(d);
  const sd = Math.min(sel, M.days);

  const note = rev(sd)
    ? `${toArabicDigits(rev(sd))} إيصال عربون مستني مراجعتك في اليوم ده.`
    : occ(sd) >= 80
      ? "يوم زحمة — قرّب يتقفل."
      : "فيه مواعيد فاضية كتير — ممكن تعمل عرض للساعات الأولى.";

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_200px] text-[26px] font-bold">تقويم الحجوزات</h1>
        {switcher}
        <Pager
          label={M.name}
          width="min-w-[140px]"
          prevLabel="الشهر السابق"
          nextLabel="الشهر التالي"
          onPrev={() => setM(0)}
          onNext={() => setM(1)}
        />
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="أيام الشهر" className={`flex-[999_1_640px] min-w-0 ${card} rounded-[18px] p-3.5 flex flex-col gap-2`}>
          <div className={`grid grid-cols-7 gap-1.5 text-xs font-bold ${muted} text-center`}>
            {["سبت", "أحد", "إثنين", "ثلاثاء", "أربعاء", "خميس", "جمعة"].map((d) => (
              <span key={d}>{d}</span>
            ))}
          </div>
          <div className="grid grid-cols-7 gap-1.5">
            {Array.from({ length: M.offset }, (_, i) => (
              <span key={`b${i}`} className="min-h-[84px]" />
            ))}
            {Array.from({ length: M.days }, (_, i) => {
              const d = i + 1;
              const o = occ(d);
              const on = d === sel;
              return (
                <button
                  key={d}
                  type="button"
                  onClick={() => setSel(d)}
                  aria-label={`${toArabicDigits(d)} ${M.name}، ${toArabicDigits(cnt(d))} حجز، إشغال ${toArabicDigits(o)}٪`}
                  className={`min-h-[84px] p-2 rounded-xl flex flex-col items-start justify-between gap-1 text-right text-slate-900 ${
                    on ? "bg-[#EEF1F7] border-2 border-navy-900" : "bg-white border border-[#EDF0F5]"
                  }`}
                >
                  <span className="flex justify-between w-full">
                    <span className="text-sm font-bold">{toArabicDigits(d)}</span>
                    {rev(d) > 0 && (
                      <span className="text-[10px] font-bold bg-[#FEF3D6] text-[#7A4D00] rounded-full px-1.5 py-px">{toArabicDigits(rev(d))}</span>
                    )}
                  </span>
                  <span className={`text-[11px] ${muted}`}>{toArabicDigits(cnt(d))} حجز</span>
                  <span className="w-full h-[5px] rounded-[3px] bg-[#EEF0F5] overflow-hidden">
                    <span className={`block h-full ${o >= 80 ? "bg-brand-red" : "bg-navy-900"}`} style={{ width: `${o}%` }} />
                  </span>
                </button>
              );
            })}
          </div>
          <div className="flex gap-4 text-xs text-[#3B4352] pt-1">
            <span className="flex items-center gap-1.5">
              <span className="w-[18px] h-[5px] rounded-[3px] bg-navy-900" />
              الإشغال
            </span>
            <span className="flex items-center gap-1.5">
              <span className="text-[10px] font-bold bg-[#FEF3D6] text-[#7A4D00] rounded-full px-1.5 py-px">٢</span>
              إيصالات بانتظار المراجعة
            </span>
          </div>
        </section>

        <aside className={`flex-[1_1_300px] min-w-0 ${card} rounded-[18px] p-[18px] flex flex-col gap-3`}>
          <h2 className="text-lg font-bold">
            {toArabicDigits(sd)} {M.name}
          </h2>
          <div className="grid grid-cols-2 gap-2">
            <div className="bg-surface rounded-xl px-3 py-2.5 flex flex-col">
              <span className={`text-xs ${muted}`}>الحجوزات</span>
              <span className="text-[22px] font-bold">{toArabicDigits(cnt(sd))}</span>
            </div>
            <div className="bg-surface rounded-xl px-3 py-2.5 flex flex-col">
              <span className={`text-xs ${muted}`}>الإشغال</span>
              <span className="text-[22px] font-bold">{toArabicDigits(occ(sd))}٪</span>
            </div>
          </div>
          <span className="text-[13px] text-[#333B4A]">{note}</span>
          <button type="button" onClick={onOpenDay} className="min-h-[44px] rounded-xl bg-navy-900 text-white text-sm font-bold">
            افتح اليوم ده
          </button>
          <div className="border-t border-[#EEF1F5] pt-3 flex flex-col gap-1 text-[13px] text-[#333B4A]">
            <span className="font-bold">الشهر كله</span>
            <span>
              {toArabicDigits(total)} حجز متوقع ·{" "}
              {m === 1
                ? "أغسطس لسه بيتملا — الحجوزات بتزيد قبل الميعاد بأسبوع."
                : `متوسط إشغال ${toArabicDigits(Math.round((total / M.days / 30) * 100))}٪`}
            </span>
          </div>
        </aside>
      </div>
    </>
  );
}
