"use client";

import React, { useState } from "react";
import { Page, PageHeader, Pill, btnOutline, card, inputClass, muted, toArabicDigits } from "@/components/ui";

type PlanId = "starter" | "growth" | "pro";
type Feature = "schedules" | "exports" | "reports" | "priority";
type LimitKey = "venues" | "pitches" | "staff" | "trial" | "grace";

interface Plan {
  name: string;
  subs: number;
  venues: number;
  pitches: number;
  staff: number;
  trial: number;
  grace: number;
  f: Record<Feature, boolean>;
}

const BASE: Record<PlanId, Plan> = {
  starter: { name: "Starter", subs: 31, venues: 1, pitches: 2, staff: 1, trial: 14, grace: 7, f: { schedules: false, exports: false, priority: false, reports: false } },
  growth: { name: "Growth", subs: 29, venues: 2, pitches: 5, staff: 5, trial: 14, grace: 7, f: { schedules: true, exports: true, priority: false, reports: false } },
  pro: { name: "Pro", subs: 6, venues: 5, pitches: 20, staff: 15, trial: 14, grace: 14, f: { schedules: true, exports: true, priority: true, reports: true } },
};

const LIMITS: [LimitKey, string, number, number][] = [
  ["venues", "عدد الفروع", 1, 50],
  ["pitches", "عدد الملاعب", 1, 100],
  ["staff", "حسابات الموظفين", 1, 50],
  ["trial", "أيام التجربة المجانية", 0, 60],
  ["grace", "أيام السماح بعد الانتهاء", 0, 30],
];

const FEATURES: [Feature, string][] = [
  ["schedules", "جداول متقدمة"],
  ["exports", "تصدير Excel/CSV"],
  ["reports", "تقارير متقدمة"],
  ["priority", "دعم أولوية"],
];

export default function PlansPage() {
  const [sel, setSel] = useState<PlanId>("growth");
  const [saved, setSaved] = useState<Record<PlanId, Plan>>(BASE);
  const [draft, setDraft] = useState<Partial<Record<PlanId, Plan>>>({});
  const [confirming, setConfirming] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const get = (id: PlanId) => draft[id] ?? saved[id];
  const cur = get(sel);
  const dirty = !!draft[sel];
  const invalid = cur.pitches < cur.venues;

  const patch = (p: Partial<Plan>) => setDraft({ ...draft, [sel]: { ...cur, ...p } });
  const reset = () => {
    const d = { ...draft };
    delete d[sel];
    setDraft(d);
  };
  const save = () => {
    setSaved({ ...saved, [sel]: cur });
    reset();
    setConfirming(false);
    setNotice(`اتحفظت تغييرات باقة ${cur.name} واتسجلت في سجل التدقيق.`);
  };

  return (
    <Page>
      <PageHeader title="باقات الاشتراك" sub="الحدود دي بتتطبق في السيرفر، مش في الواجهة بس.">
        <button
          type="button"
          onClick={() => setNotice("إضافة باقة رابعة محتاجة موافقة مالك المنصة — اتبعت طلب لمحمد الشريف.")}
          className={`${btnOutline} !rounded-[10px]`}
        >
          + باقة جديدة
        </button>
      </PageHeader>

      {notice && (
        <div role="status" className="bg-[#E5F5EB] text-[#155C33] rounded-xl px-4 py-3 text-sm font-semibold">
          {notice}
        </div>
      )}

      <div className="grid gap-3 grid-cols-[repeat(auto-fit,minmax(min(200px,100%),1fr))]">
        {(Object.keys(BASE) as PlanId[]).map((id) => {
          const p = get(id);
          const on = id === sel;
          return (
            <button
              key={id}
              type="button"
              aria-pressed={on}
              onClick={() => {
                setSel(id);
                setConfirming(false);
              }}
              className={`min-h-[100px] px-4 py-3.5 rounded-[14px] bg-white flex flex-col items-start gap-1.5 text-right ${
                on ? "border-2 border-ink" : "border border-[#E4E7EC]"
              }`}
            >
              <span className="flex justify-between items-center w-full">
                <span className="text-lg font-bold text-slate-900">{p.name}</span>
                <Pill tone="ok" className="!text-[11px] !px-2 !py-0.5">
                  نشطة
                </Pill>
              </span>
              <span className={`text-[13px] ${muted}`}>
                {toArabicDigits(p.venues)} فرع · {toArabicDigits(p.pitches)} ملعب · {toArabicDigits(p.staff)} موظف
              </span>
              <span className="text-[13px] text-[#333B4A] font-semibold">{toArabicDigits(p.subs)} اشتراك نشط</span>
            </button>
          );
        })}
      </div>

      <section className={`${card} rounded-[18px] p-5 flex flex-col gap-5`}>
        <h2 className="text-lg font-bold">تعديل باقة {cur.name}</h2>

        <div className="grid gap-4 grid-cols-[repeat(auto-fit,minmax(min(220px,100%),1fr))]">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="pm" className="text-sm font-semibold">
              السعر الشهري (ج.م)
            </label>
            <input id="pm" inputMode="numeric" placeholder="[السعر]" className={`${inputClass} !min-h-[46px] !rounded-[10px]`} />
          </div>
          <div className="flex flex-col gap-1.5">
            <label htmlFor="py" className="text-sm font-semibold">
              السعر السنوي (اختياري)
            </label>
            <input id="py" inputMode="numeric" placeholder="[السعر]" className={`${inputClass} !min-h-[46px] !rounded-[10px]`} />
          </div>
        </div>

        <div className="grid gap-3 grid-cols-[repeat(auto-fit,minmax(min(200px,100%),1fr))]">
          {LIMITS.map(([k, label, min, max]) => (
            <div key={k} className="border border-[#E4E7EC] rounded-xl px-3.5 py-3 flex flex-col gap-2">
              <span id={`lim-${k}`} className={`text-[13px] ${muted}`}>
                {label}
              </span>
              <div role="group" aria-labelledby={`lim-${k}`} className="flex items-center justify-between">
                <button
                  type="button"
                  aria-label="تقليل"
                  onClick={() => patch({ [k]: Math.max(min, cur[k] - 1) })}
                  className="w-10 h-10 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-lg"
                >
                  −
                </button>
                <span className="text-xl font-bold">{toArabicDigits(cur[k])}</span>
                <button
                  type="button"
                  aria-label="زيادة"
                  onClick={() => patch({ [k]: Math.min(max, cur[k] + 1) })}
                  className="w-10 h-10 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-lg"
                >
                  +
                </button>
              </div>
            </div>
          ))}
        </div>
        {invalid && <span className="text-[13px] font-semibold text-[#A8261C]">عدد الملاعب لازم يكون أكبر من أو يساوي عدد الفروع.</span>}

        <div className="flex flex-col gap-2">
          <span className="text-sm font-bold">المزايا</span>
          <div className="flex flex-wrap gap-2">
            {FEATURES.map(([k, label]) => {
              const on = cur.f[k];
              return (
                <button
                  key={k}
                  type="button"
                  aria-pressed={on}
                  onClick={() => patch({ f: { ...cur.f, [k]: !on } })}
                  className={`min-h-[40px] px-3.5 rounded-[10px] text-[13px] font-semibold ${
                    on ? "bg-ink text-white border border-ink" : "bg-white text-slate-900 border border-dashed border-[#9AA3B2]"
                  }`}
                >
                  {on ? "✓" : "+"} {label}
                </button>
              );
            })}
          </div>
        </div>

        <div className="flex flex-wrap gap-3 items-center justify-between border-t border-[#EEF0F5] pt-4">
          <span className={`text-[13px] ${muted}`}>
            {dirty ? "فيه تغييرات مش محفوظة · " : ""}
            {toArabicDigits(cur.subs)} اشتراك نشط على الباقة دي
          </span>
          <div className="flex gap-2">
            <button type="button" onClick={reset} className={`${btnOutline} !min-h-[46px] !rounded-[10px]`}>
              تراجع
            </button>
            <button
              type="button"
              disabled={invalid || !dirty}
              onClick={() => !invalid && dirty && setConfirming(true)}
              className={`min-h-[46px] px-[22px] rounded-[10px] text-sm font-bold ${
                invalid || !dirty ? "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed" : "bg-ink text-white"
              }`}
            >
              حفظ التغييرات
            </button>
          </div>
        </div>
      </section>

      {confirming && (
        <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] flex items-center justify-center p-4">
          <div role="dialog" aria-modal="true" aria-labelledby="pl-t" className="bg-white rounded-[20px] p-6 w-full max-w-[480px] flex flex-col gap-3">
            <h2 id="pl-t" className="text-[19px] font-bold">
              حفظ تغييرات باقة {cur.name}؟
            </h2>
            <p className={`text-sm ${muted} leading-[1.7]`}>
              التغييرات هتتطبق فوراً على الاشتراكات الجديدة، وعلى الـ{toArabicDigits(cur.subs)} اشتراك الحاليين عند التجديد الجاي. لو قلّلت حد، الملاعب
              الموجودة مش هتتقفل — بس المالك مش هيقدر يضيف جديد. الإجراء هيتسجل في سجل التدقيق.
            </p>
            <div className="flex gap-2 justify-end">
              <button type="button" onClick={() => setConfirming(false)} className={btnOutline}>
                رجوع
              </button>
              <button type="button" onClick={save} className="min-h-[44px] px-[18px] rounded-xl bg-ink text-white text-sm font-bold">
                حفظ
              </button>
            </div>
          </div>
        </div>
      )}
    </Page>
  );
}
