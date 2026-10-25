"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, Switch, Toast, cardShadow, chip, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Size = "5" | "7" | "11";
type Surface = "artificial" | "natural";
type Duration = "1" | "1.5" | "2";

interface PitchItem {
  id: string;
  name: string;
  size: Size;
  surface: Surface;
  durations: Duration[];
  pricePerHour: number;
  isActive: boolean;
}

interface VenueItem {
  id: string;
  name: string;
  meta: string;
  approved: boolean;
}

const PLAN_LIMIT = 5;

const SIZES: { val: Size; label: string }[] = [
  { val: "5", label: "٥ لاعب" },
  { val: "7", label: "٧ لاعب" },
  { val: "11", label: "١١ لاعب" },
];

const SURFACES: { val: Surface; label: string }[] = [
  { val: "artificial", label: "نجيل صناعي" },
  { val: "natural", label: "نجيل طبيعي" },
];

const DURATIONS: { val: Duration; label: string }[] = [
  { val: "1", label: "ساعة" },
  { val: "1.5", label: "ساعة ونص" },
  { val: "2", label: "ساعتين" },
];

const initialPitches: PitchItem[] = [
  { id: "1", name: "ملعب ١", size: "5", surface: "artificial", durations: ["1", "2"], pricePerHour: 150, isActive: true },
  { id: "2", name: "ملعب ٢", size: "7", surface: "artificial", durations: ["1", "2"], pricePerHour: 220, isActive: true },
  { id: "3", name: "ملعب ٣", size: "5", surface: "artificial", durations: ["1"], pricePerHour: 150, isActive: true },
];

const venues: VenueItem[] = [
  { id: "1", name: "ملعب النجوم — مدينة نصر", meta: "٣ ملاعب · شارع عباس العقاد", approved: true },
  { id: "2", name: "ملعب النجوم — التجمع", meta: "ملعبين · لسه بيتجهّز", approved: false },
];

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const chipClass = (on: boolean) => chip(on, "flex-1 min-h-[40px] rounded-[10px] text-[13px]");

export default function PitchesPage() {
  const [tab, setTab] = useState<"pitches" | "venues">("pitches");
  const [pitches, setPitches] = useState<PitchItem[]>(initialPitches);
  const [selectedId, setSelectedId] = useState<string>(initialPitches[0].id);
  const detailsRef = useRevealOnSelect<HTMLElement>(selectedId);
  const [published, setPublished] = useState<Record<string, boolean>>({ "1": true, "2": false });
  const [toast, setToast] = useState<string | null>(null);

  const selected = pitches.find((p) => p.id === selectedId) ?? pitches[0];
  const atLimit = pitches.length >= PLAN_LIMIT;

  const updateSelected = (patch: Partial<PitchItem>) =>
    setPitches((prev) => prev.map((p) => (p.id === selected.id ? { ...p, ...patch } : p)));

  const addPitch = () => {
    if (atLimit) return;
    const id = String(Date.now());
    const newPitch: PitchItem = {
      id,
      name: `ملعب ${toArabicDigits(pitches.length + 1)}`,
      size: "5",
      surface: "artificial",
      durations: ["1"],
      pricePerHour: 150,
      isActive: false,
    };
    setPitches([...pitches, newPitch]);
    setSelectedId(id);
    setTab("pitches");
  };

  const toggleDuration = (d: Duration) =>
    updateSelected({
      durations: selected.durations.includes(d)
        ? selected.durations.filter((x) => x !== d)
        : [...selected.durations, d],
    });

  const durationsLabel = (p: PitchItem) =>
    DURATIONS.filter((d) => p.durations.includes(d.val))
      .map((d) => d.label)
      .join("، ");

  return (
    <Page>
      {/* Header */}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold text-slate-900">الفروع والملاعب</h1>
        <span className="text-[13px] text-[#5B6475]">
          {toArabicDigits(pitches.length)} من {toArabicDigits(PLAN_LIMIT)} ملاعب في باقة Growth
        </span>
        <button
          type="button"
          onClick={addPitch}
          disabled={atLimit}
          className={`min-h-[44px] px-[18px] rounded-xl text-sm font-bold transition ${
            atLimit
              ? "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
              : "bg-brand-red hover:bg-brand-red-hover text-white shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)]"
          }`}
        >
          {atLimit ? "وصلت للحد — رقّي الباقة" : "+ إضافة ملعب"}
        </button>
      </div>

      {/* Tabs */}
      <div role="tablist" className="flex gap-1.5">
        {(
          [
            ["pitches", "الملاعب"],
            ["venues", "الفروع"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setTab(id)}
            className={`min-h-[42px] px-[18px] rounded-xl text-sm font-semibold border transition ${
              tab === id
                ? "bg-navy-900 text-white border-navy-900"
                : "bg-white text-slate-900 border-[#DCE0E8] hover:bg-slate-50"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {/* Venues tab */}
      {tab === "venues" && (
        <div className="grid gap-4 grid-cols-[repeat(auto-fit,minmax(min(320px,100%),1fr))]">
          {venues.map((v) => {
            const on = !!published[v.id];
            return (
              <article
                key={v.id}
                className={`bg-white border border-[#EDF0F5] rounded-[20px] overflow-hidden flex flex-col ${cardShadow}`}
              >
                <div className="h-[120px]" style={{ background: pitchPattern }} />
                <div className="p-4 flex flex-col gap-2.5">
                  <div className="flex justify-between items-center gap-2">
                    <span className="text-[17px] font-bold">{v.name}</span>
                    <span
                      className={`text-xs font-bold rounded-full px-2.5 py-[3px] whitespace-nowrap ${
                        v.approved ? "bg-[#E5F5EB] text-[#1B6E3E]" : "bg-[#FEF3D6] text-[#7A4D00]"
                      }`}
                    >
                      {v.approved ? "✓ معتمد من ملعبك" : "تحت المراجعة"}
                    </span>
                  </div>
                  <span className="text-[13px] text-[#5B6475]">{v.meta}</span>
                  <div className="flex items-center gap-2.5 bg-surface rounded-xl px-3 py-2.5">
                    <span className="flex-1 flex flex-col">
                      <span className="text-sm font-semibold">ظاهر للاعبين</span>
                      <span className="text-xs text-[#5B6475]">
                        {v.approved
                          ? on
                            ? "اللاعبين شايفينه ويقدروا يحجزوا"
                            : "مخفي — مش بيظهر في البحث"
                          : "هيتاح النشر بعد الموافقة"}
                      </span>
                    </span>
                    <Switch
                      on={on}
                      label="نشر الفرع"
                      disabled={!v.approved}
                      onToggle={() => {
                        if (!v.approved) return;
                        setPublished({ ...published, [v.id]: !on });
                        setToast(
                          on ? "الفرع اتخفى من التطبيق. الحجوزات الحالية زي ما هي." : "الفرع بقى ظاهر للاعبين."
                        );
                      }}
                    />
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      )}

      {/* Pitches tab */}
      {tab === "pitches" && (
        <div className="flex flex-wrap gap-4 items-start">
          <section className="flex-[999_1_460px] min-w-0 flex flex-col gap-3">
            {pitches.map((item) => {
              const isSelected = item.id === selected.id;
              return (
                <article
                  key={item.id}
                  className={`bg-white rounded-[18px] px-4 py-3.5 flex flex-wrap sm:flex-nowrap items-center gap-3.5 ${cardShadow} ${
                    isSelected ? "border-2 border-navy-900" : "border border-[#EDF0F5]"
                  }`}
                >
                  <span
                    className={`w-[72px] h-[72px] shrink-0 rounded-[14px] ${item.isActive ? "" : "opacity-45"}`}
                    style={{ background: pitchPattern }}
                  />
                  <span className="flex-1 min-w-[150px] flex flex-col gap-[3px]">
                    <span className="text-base font-bold">{item.name}</span>
                    <span className="text-[13px] text-[#5B6475]">
                      {SIZES.find((s) => s.val === item.size)?.label} ·{" "}
                      {item.surface === "artificial" ? "نجيل صناعي" : "نجيل طبيعي"} · مدد: {durationsLabel(item)}
                    </span>
                    <span
                      className={`text-xs font-bold ${item.isActive ? "text-[#1B6E3E]" : "text-[#8A94AA]"}`}
                    >
                      {item.isActive ? "● شغال" : "● متوقف"}
                    </span>
                  </span>
                  {/* Phones: price and action move to their own row under the details */}
                  <span className="w-full sm:w-auto flex items-center justify-between sm:justify-start gap-3.5 pt-3 sm:pt-0 border-t sm:border-t-0 border-[#EEF0F5]">
                    <span className="text-[15px] font-bold whitespace-nowrap">
                      {toArabicDigits(item.pricePerHour)} ج.م/ساعة
                    </span>
                    <button
                      type="button"
                      onClick={() => setSelectedId(item.id)}
                      className="min-h-[40px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white hover:bg-slate-50 text-navy-900 text-[13px] font-semibold transition"
                    >
                      تعديل
                    </button>
                  </span>
                </article>
              );
            })}
          </section>

          <aside
            ref={detailsRef}
            aria-label="تعديل الملعب"
            className={`flex-[1_1_340px] min-w-0 bg-white border border-[#EDF0F5] rounded-[20px] p-5 flex flex-col gap-3.5 ${cardShadow}`}
          >
            <h2 className="text-lg font-bold">تعديل {selected.name}</h2>

            <div className="flex flex-col gap-1.5">
              <label htmlFor="pitch-name" className="text-[13px] font-semibold">
                اسم الملعب
              </label>
              <input
                id="pitch-name"
                type="text"
                value={selected.name}
                onChange={(e) => updateSelected({ name: e.target.value })}
                className="min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900"
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">المقاس</span>
              <div className="flex gap-1.5">
                {SIZES.map((s) => (
                  <button
                    key={s.val}
                    type="button"
                    aria-pressed={selected.size === s.val}
                    onClick={() => updateSelected({ size: s.val })}
                    className={chipClass(selected.size === s.val)}
                  >
                    {s.label}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">الأرضية</span>
              <div className="flex gap-1.5">
                {SURFACES.map((t) => (
                  <button
                    key={t.val}
                    type="button"
                    aria-pressed={selected.surface === t.val}
                    onClick={() => updateSelected({ surface: t.val })}
                    className={chipClass(selected.surface === t.val)}
                  >
                    {t.label}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">مدد الحجز المسموحة</span>
              <div className="flex gap-1.5">
                {DURATIONS.map((d) => {
                  const on = selected.durations.includes(d.val);
                  return (
                    <button
                      key={d.val}
                      type="button"
                      aria-pressed={on}
                      onClick={() => toggleDuration(d.val)}
                      className={chipClass(on)}
                    >
                      {d.label}
                    </button>
                  );
                })}
              </div>
            </div>

            <div className="flex items-center justify-between bg-surface rounded-xl px-3 py-2.5">
              <span className="text-sm">السعر الأساسي</span>
              <Link href="/pricing" className="text-sm font-bold text-navy-900 hover:text-brand-red">
                {toArabicDigits(selected.pricePerHour)} ج.م — من قواعد الأسعار
              </Link>
            </div>

            <div className="flex items-center gap-2.5">
              <span className="flex-1 flex flex-col">
                <span className="text-sm font-semibold">الملعب شغال</span>
                <span className="text-xs text-[#5B6475]">
                  لو قفلته، الحجوزات الجاية مش بتتلغي — بس مش هيتحجز جديد.
                </span>
              </span>
              <Switch
                on={selected.isActive}
                label="تشغيل الملعب"
                onToggle={() => updateSelected({ isActive: !selected.isActive })}
              />
            </div>

            <button
              type="button"
              onClick={() => setToast(`اتحفظت بيانات ${selected.name}. التغيير بيظهر في التطبيق فوراً.`)}
              className="min-h-[48px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)] transition"
            >
              حفظ
            </button>
          </aside>
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
