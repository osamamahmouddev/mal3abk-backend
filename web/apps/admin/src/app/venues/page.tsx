"use client";

import React, { useState } from "react";
import Link from "next/link";
import { KV, Page, PageHeader, Pill, ReasonModal, ReasonRequest, Switch, Toast, Tone, btnSmall, card, chip, inputClass, muted, rowCls, thCls, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Status = "live" | "pending" | "overdue" | "suspended";
type City = "cai" | "giz" | "del";
// name, area, city, plan, pitches, bookings this month, rating, status, owner, renewal
type Venue = [string, string, City, string, number, number, number, Status, string, string];

const VENUES: Venue[] = [
  ["ملعب النجوم — مدينة نصر", "مدينة نصر", "cai", "Growth", 3, 312, 4.8, "live", "محمود عبد الله", "١٤ أغسطس"],
  ["ملاعب الرواد", "مصر الجديدة", "cai", "Growth", 4, 268, 4.6, "live", "هشام الرواد", "٢ أغسطس"],
  ["أكاديمية الدلتا — فرع ٢", "المنصورة", "del", "Starter", 2, 41, 4.4, "live", "وليد السيد", "٢٠ أغسطس"],
  ["ستاد الشباب", "الشيخ زايد", "giz", "Pro", 6, 520, 4.9, "live", "شركة الشباب للرياضة", "١ سبتمبر"],
  ["ملعب الحي — المعادي", "المعادي", "cai", "Starter", 1, 0, 0, "overdue", "سامح لطفي", "متأخر ٦ أيام"],
  ["ملعب النجوم — التجمع", "التجمع الخامس", "cai", "Growth", 2, 0, 0, "pending", "محمود عبد الله", "—"],
  ["جرين فيلد", "الهرم", "giz", "Starter", 2, 18, 3.1, "suspended", "عادل منير", "—"],
];

const STATUS: Record<Status, [string, Tone]> = {
  live: ["منشور", "ok"],
  pending: ["مستني موافقة", "warn"],
  overdue: ["اشتراك متأخر", "warn"],
  suspended: ["موقوف", "bad"],
};

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const actionBtn = `${btnSmall} !min-h-[38px] !px-3.5 !rounded-[10px] !text-[13px] flex items-center`;

export default function VenuesPage() {
  const [q, setQ] = useState("");
  const [filter, setFilter] = useState<"all" | Exclude<Status, "pending">>("all");
  const [city, setCity] = useState<"all" | City>("all");
  const [sel, setSel] = useState(0);
  const detailsRef = useRevealOnSelect<HTMLElement>(sel);
  const [statusOverride, setStatusOverride] = useState<Record<number, Status>>({});
  const [hidden, setHidden] = useState<Record<number, boolean>>({});
  const [notes, setNotes] = useState<Record<number, string>>({});
  const [modal, setModal] = useState<ReasonRequest | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const statusOf = (i: number) => statusOverride[i] ?? VENUES[i][7];
  const query = q.trim();
  const idx = VENUES.map((_, i) => i).filter(
    (i) =>
      (filter === "all" || statusOf(i) === filter) &&
      (city === "all" || VENUES[i][2] === city) &&
      (!query || VENUES[i][0].includes(query) || VENUES[i][8].includes(query))
  );
  const v = VENUES[sel];
  const vst = statusOf(sel);
  const visible = vst === "live" && !hidden[sel];
  const note =
    notes[sel] ||
    (vst === "suspended"
      ? "موقوف: شكاوي متكررة من رفض إثباتات دفع صحيحة."
      : vst === "overdue"
        ? "الاشتراك متأخر — الملعب هيتخفى أوتوماتيك بعد ٧ أيام."
        : "");

  return (
    <Page>
      <PageHeader title="الملاعب" sub={`${toArabicDigits(231)} ملعب على المنصة · ${toArabicDigits(idx.length)} في النتيجة`}>
        <Link href="/approvals" className="min-h-[42px] px-4 rounded-xl bg-ink text-white text-sm font-bold flex items-center gap-2 hover:text-white">
          طلبات الموافقة <span className="bg-brand-red rounded-[10px] px-2 text-xs">٤</span>
        </Link>
      </PageHeader>

      <div className="flex flex-wrap gap-2.5 items-center">
        <input aria-label="بحث" value={q} onChange={(e) => setQ(e.target.value)} placeholder="ابحث باسم الملعب أو المالك" className={`${inputClass} flex-[1_1_240px] min-w-0`} />
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "الكل"],
              ["live", "منشور"],
              ["overdue", "اشتراك متأخر"],
              ["suspended", "موقوف"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={filter === id} onClick={() => setFilter(id)} className={chip(filter === id)}>
              {label}
            </button>
          ))}
        </div>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "كل المحافظات"],
              ["cai", "القاهرة"],
              ["giz", "الجيزة"],
              ["del", "الدلتا"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={city === id} onClick={() => setCity(id)} className={chip(city === id)}>
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_520px] min-w-0 ${card} rounded-[20px] overflow-x-auto`}>
          <table className="w-full border-collapse text-sm min-w-[640px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                <th scope="col" className={`${thCls} !px-4`}>
                  الملعب
                </th>
                {["الباقة", "ملاعب", "حجوزات الشهر", "التقييم", "الحالة"].map((h) => (
                  <th key={h} scope="col" className={thCls}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {idx.map((i) => {
                const r = VENUES[i];
                const [label, tone] = STATUS[statusOf(i)];
                return (
                  <tr key={i} className={rowCls(i === sel)} onClick={() => setSel(i)}>
                    <td className="px-4 py-3">
                      <button type="button" aria-pressed={i === sel} onClick={() => setSel(i)} className="bg-transparent p-0 text-right flex flex-col gap-0.5 text-slate-900">
                        <span className="text-sm font-bold">{r[0]}</span>
                        <span className={`text-xs ${muted}`}>{r[1]}</span>
                      </button>
                    </td>
                    <td className="px-2 py-3">{r[3]}</td>
                    <td className="px-2 py-3">{toArabicDigits(r[4])}</td>
                    <td className="px-2 py-3 font-bold">{toArabicDigits(r[5])}</td>
                    <td className="px-2 py-3">{r[6] ? `★ ${toArabicDigits(String(r[6]).replace(".", "٫"))}` : "—"}</td>
                    <td className="px-2 py-3">
                      <Pill tone={tone}>{label}</Pill>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {idx.length === 0 && <p className={`p-8 text-center ${muted}`}>مفيش ملاعب بالفلتر ده.</p>}
        </section>

        <aside ref={detailsRef} aria-label="تفاصيل الملعب" className={`scroll-mt-20 flex-[1_1_320px] min-w-0 ${card} rounded-[20px] overflow-hidden flex flex-col`}>
          <div className="h-[110px]" style={{ background: pitchPattern }} />
          <div className="px-5 pt-[18px] pb-5 flex flex-col gap-3">
            <div className="flex items-center gap-2">
              <h2 className="flex-1 text-lg font-bold">{v[0]}</h2>
              <Pill tone={STATUS[vst][1]}>{STATUS[vst][0]}</Pill>
            </div>
            <KV k="المالك">
              <b className="font-semibold">{v[8]}</b>
            </KV>
            <KV k="المنطقة">
              <b className="font-semibold">{v[1]}</b>
            </KV>
            <KV k="الباقة">
              <b className="font-semibold">
                {v[3]} · تجديد {v[9]}
              </b>
            </KV>
            <KV k="الملاعب">
              <b className="font-semibold">{toArabicDigits(v[4])}</b>
            </KV>
            <KV k="حجوزات الشهر">
              <b className="font-semibold">{toArabicDigits(v[5])}</b>
            </KV>
            {note && <div className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-3 py-2.5 text-[13px]">{note}</div>}
            <div className="flex items-center gap-2.5 bg-surface rounded-xl px-3 py-2.5">
              <span className="flex-1 flex flex-col">
                <span className="text-sm font-semibold">ظاهر في البحث</span>
                <span className={`text-xs ${muted}`}>
                  {vst === "live" ? (visible ? "اللاعيبة شايفينه ويقدروا يحجزوا" : "مخفي بقرار إداري") : "مش متاح في الحالة دي"}
                </span>
              </span>
              <Switch
                on={visible}
                label="إظهار الملعب في البحث"
                disabled={vst !== "live"}
                onToggle={() => {
                  if (vst !== "live") return;
                  setHidden({ ...hidden, [sel]: visible });
                  setToast(visible ? "الملعب اتخفى من البحث. الحجوزات الحالية زي ما هي." : "الملعب رجع ظاهر.");
                }}
              />
            </div>
            <div className="flex gap-2 flex-wrap">
              <Link href="/billing" className={actionBtn}>
                الاشتراك
              </Link>
              <Link href="/featured" className={actionBtn}>
                تمييز
              </Link>
              <button type="button" onClick={() => setToast(`اتفتحت تذكرة دعم مع ${v[8]}.`)} className={actionBtn}>
                كلّم المالك
              </button>
              {(vst === "live" || vst === "overdue") && (
                <button
                  type="button"
                  onClick={() =>
                    setModal({
                      title: `إيقاف ${v[0]}؟`,
                      body: "الملعب هيختفي ومش هيستقبل حجوزات جديدة. الحجوزات المأكدة هنتواصل مع أصحابها.",
                      confirmLabel: "إيقاف",
                      onConfirm: (r) => {
                        setStatusOverride({ ...statusOverride, [sel]: "suspended" });
                        setNotes({ ...notes, [sel]: `موقوف: ${r}` });
                        setToast("اتوقف الملعب واتبعت إشعار للمالك.");
                      },
                    })
                  }
                  className="min-h-[38px] px-3.5 rounded-[10px] bg-brand-red text-white text-[13px] font-bold"
                >
                  إيقاف الملعب
                </button>
              )}
              {vst === "suspended" && (
                <button
                  type="button"
                  onClick={() =>
                    setModal({
                      title: `إعادة تشغيل ${v[0]}؟`,
                      body: "الملعب هيرجع منشور ويقبل حجوزات.",
                      confirmLabel: "إعادة التشغيل",
                      onConfirm: () => {
                        setStatusOverride({ ...statusOverride, [sel]: "live" });
                        setNotes({ ...notes, [sel]: "" });
                        setToast("الملعب رجع منشور.");
                      },
                    })
                  }
                  className="min-h-[38px] px-3.5 rounded-[10px] bg-ink text-white text-[13px] font-bold"
                >
                  إعادة التشغيل
                </button>
              )}
              {vst === "pending" && (
                <Link href="/approvals" className="min-h-[38px] px-3.5 rounded-[10px] bg-ink text-white text-[13px] font-bold flex items-center hover:text-white">
                  راجع الطلب
                </Link>
              )}
            </div>
          </div>
        </aside>
      </div>

      <ReasonModal request={modal} onClose={() => setModal(null)} />
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
