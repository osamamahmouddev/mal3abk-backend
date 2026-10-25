"use client";

import React, { useState } from "react";
import { Page, Toast, card, chip, muted, redShadow } from "@/components/ui";

type Value = string | number;
type Item = [key: string, label: string, hint: string, options: [Value, string][]];

const GROUPS: [string, Item[]][] = [
  [
    "العربون والتأكيد",
    [
      ["dep", "قيمة العربون", "بيتحسب من سعر الحجز", [["p25", "٢٥٪"], ["p33", "تلت السعر"], ["p50", "النص"], ["fixed", "٥٠ ج.م ثابت"]]],
      ["proof", "مهلة رفع إثبات الدفع", "لو اللاعب مارفعش في المهلة الحجز بيتلغي", [[15, "١٥ دقيقة"], [30, "٣٠ دقيقة"], [60, "ساعة"]]],
      ["review", "مهلة مراجعة الإثبات", "وقت الملعب يأكد أو يرفض", [[30, "٣٠ دقيقة"], [60, "ساعة"], [180, "٣ ساعات"]]],
      ["block", "الحجز المستني بيقفل الميعاد؟", 'لو "أيوه" محدش تاني يقدر يحجزه وقت المراجعة', [["yes", "أيوه"], ["no", "لأ"]]],
    ],
  ],
  [
    "الإلغاء والاسترداد",
    [
      ["cancel", "آخر ميعاد للإلغاء المجاني", "قبل الماتش بـ", [[6, "٦ ساعات"], [12, "١٢ ساعة"], [24, "٢٤ ساعة"], [48, "يومين"]]],
      ["refund", "الاسترداد في الإلغاء المجاني", "بيترجع من العربون", [["full", "العربون كله"], ["half", "النص"], ["credit", "رصيد للحجز الجاي"]]],
      ["late", "الإلغاء المتأخر", "بعد آخر ميعاد", [["none", "مفيش استرداد"], ["half", "نص العربون"]]],
    ],
  ],
  [
    "الحضور والتغيير",
    [
      ["noshow", "لو الفريق ماجاش", "No-show", [["keep", "العربون بيتخصم"], ["keep_flag", "يتخصم + علامة على العميل"]]],
      ["lateArr", "التأخير المسموح", "بعده الملعب يقدر يدّي الميعاد لحد تاني", [[10, "١٠ دقايق"], [15, "١٥ دقيقة"], [30, "٣٠ دقيقة"]]],
      ["resch", "تغيير الميعاد", "قبل آخر ميعاد إلغاء", [["none", "مش مسموح"], ["once", "مرة واحدة"], ["free", "مفتوح"]]],
    ],
  ],
];

const INITIAL: Record<string, Value> = {
  dep: "p33",
  proof: 30,
  review: 60,
  block: "yes",
  cancel: 24,
  refund: "full",
  late: "half",
  noshow: "keep",
  resch: "once",
  lateArr: 15,
};

export default function PoliciesPage() {
  const [v, setV] = useState<Record<string, Value>>(INITIAL);
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<string | null>(null);

  const lbl = (k: string) => {
    for (const [, items] of GROUPS)
      for (const it of items) if (it[0] === k) return it[3].find((o) => o[0] === v[k])?.[1] ?? "";
    return "";
  };

  const preview = [
    `العربون: ${lbl("dep")} — ادفع وارفع الإثبات خلال ${lbl("proof")}.`,
    `الملعب بيراجع الإثبات خلال ${lbl("review")}${v.block === "yes" ? "، والميعاد محجوزلك لحد ما يتراجع." : "."}`,
    `تقدر تلغي مجاناً لحد ${lbl("cancel")} قبل الماتش، وبيرجعلك ${lbl("refund")}.`,
    `بعد كده: ${lbl("late")}.`,
    `لو اتأخرت أكتر من ${lbl("lateArr")} الملعب ممكن يدّي الميعاد لحد تاني، ولو ماجيتش ${lbl("noshow")}.`,
    `تغيير الميعاد: ${lbl("resch")}.`,
  ];

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">سياسات الحجز</h1>
        {dirty && (
          <span className="text-[13px] font-bold bg-[#FEF3D6] text-[#7A4D00] rounded-full px-3 py-1">فيه تعديلات مش محفوظة</span>
        )}
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <div className="flex-[999_1_460px] min-w-0 flex flex-col gap-4">
          {GROUPS.map(([title, items]) => (
            <section key={title} className={`${card} rounded-[20px] px-5 py-[18px] flex flex-col`}>
              <h2 className="mb-1.5 text-[17px] font-bold">{title}</h2>
              {items.map(([k, label, hint, opts]) => (
                <div key={k} className="flex flex-wrap items-center gap-2.5 py-3 border-b border-[#EEF0F5] last:border-b-0">
                  <span className="flex-[1_1_220px] flex flex-col gap-0.5">
                    <span className="text-sm font-bold">{label}</span>
                    <span className={`text-xs ${muted}`}>{hint}</span>
                  </span>
                  <div className="flex gap-1.5 flex-wrap">
                    {opts.map(([val, olabel]) => (
                      <button
                        key={String(val)}
                        type="button"
                        aria-pressed={v[k] === val}
                        onClick={() => {
                          setV({ ...v, [k]: val });
                          setDirty(true);
                        }}
                        className={chip(v[k] === val, "min-h-[36px] px-3 rounded-[10px] text-[13px]")}
                      >
                        {olabel}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </section>
          ))}
        </div>

        <aside aria-label="معاينة اللاعب" className="flex-[1_1_320px] min-w-0 flex flex-col gap-3 sticky top-4">
          <div className={`${card} rounded-[20px] p-[18px] flex flex-col gap-2.5`}>
            <span className={`text-xs font-bold ${muted}`}>كده هتظهر للاعب قبل ما يحجز</span>
            <h2 className="text-base font-bold">سياسة ملعب النجوم</h2>
            {preview.map((p) => (
              <div key={p} className="flex gap-2 items-start text-[13px] leading-[1.6]">
                <span className="w-1.5 h-1.5 rounded-full bg-brand-red mt-2 shrink-0" />
                <span>{p}</span>
              </div>
            ))}
          </div>
          <div className="bg-[#EEF0F5] text-[#3B4352] rounded-[14px] px-3.5 py-3 text-[13px] leading-[1.6]">
            التعديلات بتطبّق على الحجوزات الجديدة بس. كل حجز موجود بيفضل بالسياسة اللي اتحجز بيها.
          </div>
          <button
            type="button"
            onClick={() => {
              setDirty(false);
              setToast("اتحفظت السياسات. هتطبّق على الحجوزات الجديدة من دلوقتي.");
            }}
            className={`min-h-[48px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold ${redShadow}`}
          >
            حفظ السياسات
          </button>
        </aside>
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
