"use client";

import React, { useState } from "react";
import { Page, PageHeader, Toast, card, chip, muted, redShadow, toArabicDigits } from "@/components/ui";

type Area = "nasr" | "tagamoa" | "zayed" | "maadi";
// venue name, featured until
type Slot = [string, string];

const AREAS: [Area, string][] = [
  ["nasr", "مدينة نصر ومصر الجديدة"],
  ["tagamoa", "التجمع"],
  ["zayed", "الشيخ زايد وأكتوبر"],
  ["maadi", "المعادي"],
];

// Published venues per area that can be featured.
const POOL: Record<Area, string[]> = {
  nasr: ["ملعب النجوم — مدينة نصر", "ملاعب الرواد", "نادي الشمس — ملاعب", "ملعب العبور"],
  tagamoa: ["ستاد التجمع", "كابيتال سبورتس", "ملاعب الياسمين"],
  zayed: ["ستاد الشباب", "زايد أرينا", "جرين بارك"],
  maadi: ["ملعب الحي — المعادي", "دجلة سبورت"],
};

const MAX_PER_AREA = 3;
const END_DATE: Record<number, string> = { 7: "٣١ يوليو", 14: "٧ أغسطس", 30: "٢٣ أغسطس" };

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const arrowBtn = (off: boolean) => `w-9 h-9 rounded-[10px] border border-[#DCE0E8] bg-white text-base ${off ? "opacity-35 cursor-not-allowed" : ""}`;

export default function FeaturedPage() {
  const [area, setArea] = useState<Area>("nasr");
  const [pick, setPick] = useState<string | null>(null);
  const [dur, setDur] = useState(14);
  const [featured, setFeatured] = useState<Record<Area, Slot[]>>({
    nasr: [
      ["ملعب النجوم — مدينة نصر", "٣ أغسطس"],
      ["ملاعب الرواد", "٢٠ يوليو"],
    ],
    tagamoa: [["ستاد التجمع", "١ أغسطس"]],
    zayed: [
      ["ستاد الشباب", "١٠ أغسطس"],
      ["زايد أرينا", "٢٨ يوليو"],
      ["جرين بارك", "٥ أغسطس"],
    ],
    maadi: [],
  });
  const [toast, setToast] = useState<string | null>(null);

  const list = featured[area];
  const candidates = POOL[area].filter((n) => !list.some((x) => x[0] === n));
  const full = list.length >= MAX_PER_AREA;
  const canAdd = !full && !!pick && candidates.includes(pick);
  const setList = (L: Slot[]) => setFeatured({ ...featured, [area]: L });

  const move = (i: number, dir: -1 | 1) => {
    const j = i + dir;
    if (j < 0 || j >= list.length) return;
    const L = [...list];
    [L[i], L[j]] = [L[j], L[i]];
    setList(L);
  };

  return (
    <Page>
      <PageHeader title="ملاعب مميزة" sub="بتظهر أول حاجة في الرئيسية والبحث للاعيبة في المنطقة. أقصى ٣ في كل منطقة." />
      <div className="flex gap-1.5 flex-wrap">
        {AREAS.map(([id, label]) => (
          <button
            key={id}
            type="button"
            aria-pressed={area === id}
            onClick={() => {
              setArea(id);
              setPick(null);
            }}
            className={chip(area === id)}
          >
            {label} ({toArabicDigits(featured[id].length)})
          </button>
        ))}
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <div className="flex-[999_1_460px] min-w-0 flex flex-col gap-4">
          <section className={`${card} rounded-[20px] px-5 pt-2 pb-3`}>
            {list.map(([name, end], i) => (
              <div key={name} className="flex flex-wrap items-center gap-3 py-3.5 border-b border-[#EEF0F5] last:border-b-0">
                <span className="w-8 h-8 rounded-full bg-brand-gold text-ink flex items-center justify-center font-bold">{toArabicDigits(i + 1)}</span>
                <span className="flex-[1_1_200px] flex flex-col gap-0.5">
                  <span className="text-[15px] font-bold">{name}</span>
                  <span className={`text-[13px] ${muted}`}>مميز لحد {end}</span>
                </span>
                <span className="flex gap-1">
                  <button type="button" aria-label="لفوق" disabled={i === 0} onClick={() => move(i, -1)} className={arrowBtn(i === 0)}>
                    ↑
                  </button>
                  <button type="button" aria-label="لتحت" disabled={i === list.length - 1} onClick={() => move(i, 1)} className={arrowBtn(i === list.length - 1)}>
                    ↓
                  </button>
                </span>
                <button
                  type="button"
                  onClick={() => {
                    setList(list.filter((_, j) => j !== i));
                    setToast(`اتشال ${name} من المميز.`);
                  }}
                  className="min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-[#A8261C] text-[13px] font-semibold"
                >
                  شيل
                </button>
              </div>
            ))}
            {list.length === 0 && <p className={`py-6 text-center ${muted}`}>مفيش ملاعب مميزة في المنطقة دي.</p>}
          </section>

          <section className={`${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[17px] font-bold">إضافة ملعب مميز</h2>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">الملعب</span>
              <div className="flex gap-1.5 flex-wrap">
                {candidates.map((n) => (
                  <button key={n} type="button" aria-pressed={pick === n} onClick={() => setPick(n)} className={chip(pick === n)}>
                    {n}
                  </button>
                ))}
              </div>
              {candidates.length === 0 && <span className={`text-[13px] ${muted}`}>كل الملاعب المنشورة في المنطقة دي مميزة بالفعل.</span>}
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">المدة</span>
              <div className="flex gap-1.5 flex-wrap">
                {(
                  [
                    [7, "أسبوع"],
                    [14, "أسبوعين"],
                    [30, "شهر"],
                  ] as const
                ).map(([d, label]) => (
                  <button key={d} type="button" aria-pressed={dur === d} onClick={() => setDur(d)} className={chip(dur === d)}>
                    {label}
                  </button>
                ))}
              </div>
            </div>
            {full && <div className="bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-3 py-2.5 text-[13px]">المنطقة فيها ٣ ملاعب مميزة. شيل واحد الأول.</div>}
            <button
              type="button"
              disabled={!canAdd}
              onClick={() => {
                if (!canAdd || !pick) return;
                setList([...list, [pick, END_DATE[dur]]]);
                setToast(`${pick} بقى مميز لحد ${END_DATE[dur]}.`);
                setPick(null);
              }}
              className={`self-start min-h-[46px] px-6 rounded-[14px] text-sm font-bold ${
                canAdd ? `bg-brand-red hover:bg-brand-red-hover text-white ${redShadow}` : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
              }`}
            >
              إضافة للمميز
            </button>
          </section>
        </div>

        <aside aria-label="معاينة" className="flex-[1_1_300px] min-w-0 flex justify-center">
          <div className="w-[300px] bg-surface border-8 border-ink rounded-[36px] px-3.5 py-[18px] flex flex-col gap-2.5">
            <span className={`text-xs ${muted}`}>معاينة الرئيسية · {AREAS.find((a) => a[0] === area)![1]}</span>
            <span className="text-base font-bold">ملاعب مميزة قريبة منك</span>
            {(list.length ? list.map((x) => x[0]) : ["(مفيش — هتظهر الملاعب الأقرب)"]).map((name) => (
              <div key={name} className="bg-white rounded-2xl overflow-hidden shadow-[0_6px_16px_-10px_rgba(15,27,61,0.3)]">
                <div className="h-14 relative" style={{ background: pitchPattern }}>
                  <span className="absolute top-1.5 right-1.5 text-[10px] font-bold bg-brand-gold text-ink rounded-md px-1.5 py-px">مميز</span>
                </div>
                <div className="px-2.5 py-2 text-[13px] font-bold">{name}</div>
              </div>
            ))}
          </div>
        </aside>
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
