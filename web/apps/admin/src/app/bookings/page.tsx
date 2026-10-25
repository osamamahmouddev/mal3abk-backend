"use client";

import React, { useState } from "react";
import Link from "next/link";
import {
  KV,
  Page,
  PageHeader,
  Pill,
  ReasonModal,
  ReasonRequest,
  Toast,
  Tone,
  btnSmall,
  card,
  chip,
  inputClass,
  money,
  muted,
  rowCls,
  thCls,
  toArabicDigits,
} from "@/components/ui";

type Status = "wait" | "review" | "ok" | "dispute" | "noshow" | "cancel" | "admin";
type Filter = "all" | "open" | "ok" | "problem" | "cancel";
// code, player, venue, when, deposit, total, status
type Booking = [string, string, string, string, number, number, Status];

const BOOKINGS: Booking[] = [
  ["KH-2607-0142", "أحمد سامي", "ملعب النجوم — ملعب ١", "السبت ٢٦ يوليو · ٨ م", 50, 150, "review"],
  ["KH-2607-0147", "يوسف حسن", "ملعب النجوم — ملعب ٢", "الأحد ٢٧ يوليو · ٩ م", 70, 220, "wait"],
  ["KH-2607-0145", "عمر خالد", "ملعب النجوم — ملعب ٢", "السبت ٢٦ يوليو · ١١ م", 70, 220, "review"],
  ["KH-2807-0151", "ملعب المدينة (فريق)", "ملعب النجوم — ملعب ٣", "الاتنين ٢٨ يوليو · ٧ م", 50, 150, "dispute"],
  ["KH-3107-0098", "أكاديمية الدلتا", "ملعب النجوم — ملعب ٢", "الخميس ٣١ يوليو · ١٠ م", 70, 200, "ok"],
  ["RW-2507-0410", "مازن رأفت", "ملاعب الرواد — ملعب ٤", "الجمعة ٢٥ يوليو · ٦ م", 60, 180, "noshow"],
  ["SH-2407-1180", "كريم عادل", "ستاد الشباب — ملعب ٢", "الخميس ٢٤ يوليو · ٩ م", 100, 300, "cancel"],
];

const STATUS: Record<Status, [string, Tone]> = {
  wait: ["مستني عربون", "neutral"],
  review: ["تحت المراجعة", "warn"],
  ok: ["مأكد", "ok"],
  dispute: ["نزاع مفتوح", "bad"],
  noshow: ["ماجوش", "bad"],
  cancel: ["اتلغى", "neutral"],
  admin: ["اتلغى إدارياً", "bad"],
};

const FILTER_GROUPS: Record<Filter, Status[] | null> = {
  all: null,
  open: ["wait", "review"],
  ok: ["ok"],
  problem: ["dispute", "noshow", "admin"],
  cancel: ["cancel", "admin"],
};

// Per-status progress of the 4 timeline steps: 0 = not yet, 1 = done, 2 = problem.
const TIMELINE: Record<Status, number[]> = {
  wait: [1, 0, 0, 0],
  review: [1, 1, 0, 0],
  ok: [1, 1, 1, 0],
  dispute: [1, 1, 2, 0],
  noshow: [1, 1, 1, 2],
  cancel: [1, 0, 0, 2],
  admin: [1, 1, 2, 2],
};

const actionBtn = `${btnSmall} !min-h-[38px] !px-3.5 !rounded-[10px] !text-[13px] flex items-center`;

export default function BookingsPage() {
  const [q, setQ] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [sel, setSel] = useState(0);
  const [statusOverride, setStatusOverride] = useState<Record<number, Status>>({});
  const [modal, setModal] = useState<ReasonRequest | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const statusOf = (i: number) => statusOverride[i] ?? BOOKINGS[i][6];
  const query = q.trim();
  const idx = BOOKINGS.map((_, i) => i).filter(
    (i) => (!FILTER_GROUPS[filter] || FILTER_GROUPS[filter]!.includes(statusOf(i))) && (!query || BOOKINGS[i].slice(0, 3).some((x) => String(x).includes(query)))
  );
  const b = BOOKINGS[sel];
  const bst = statusOf(sel);
  const tl = TIMELINE[bst];
  const steps: [string, string][] = [
    ["اتعمل الحجز", "من التطبيق"],
    ["اترفع إثبات العربون", tl[1] ? "فودافون كاش" : "لسه"],
    [bst === "dispute" ? "الملعب رفض الإثبات — اللاعب اعترض" : "الملعب أكّد", tl[2] ? "من لوحة الملعب" : "لسه"],
    [
      bst === "noshow" ? "الملعب سجّل إنهم ماجوش" : bst === "cancel" || bst === "admin" ? "اتلغى" : "الماتش",
      tl[3] ? (bst === "admin" ? "إلغاء إداري" : "اتسجل") : b[3],
    ],
  ];

  return (
    <Page>
      <PageHeader title="كل الحجوزات" sub={`آخر ٧ أيام · ${toArabicDigits(idx.length)} في النتيجة`}>
        <button
          type="button"
          onClick={() => setToast("جاري تصدير حجوزات الفترة.")}
          className="min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold"
        >
          تصدير CSV
        </button>
      </PageHeader>

      <div className="grid gap-3 grid-cols-[repeat(auto-fit,minmax(170px,1fr))]">
        {[
          ["حجوزات مأكدة", toArabicDigits(742)],
          ["مستنية عربون/مراجعة", toArabicDigits(96)],
          ["نسبة الإلغاء", "٧٪"],
          ["نسبة الغياب", "٢٪"],
        ].map(([l, v]) => (
          <div key={l} className={`${card} rounded-[18px] px-4 py-3.5 flex flex-col gap-1`}>
            <span className={`text-[13px] ${muted}`}>{l}</span>
            <span className="text-[22px] font-bold">{v}</span>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap gap-2.5 items-center">
        <input aria-label="بحث" value={q} onChange={(e) => setQ(e.target.value)} placeholder="رقم الحجز، اللاعب أو الملعب" className={`${inputClass} flex-[1_1_240px]`} />
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "الكل"],
              ["open", "مفتوحة"],
              ["ok", "مأكدة"],
              ["problem", "فيها مشكلة"],
              ["cancel", "ملغية"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={filter === id} onClick={() => setFilter(id)} className={chip(filter === id)}>
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
                  الحجز
                </th>
                {["اللاعب", "الملعب والميعاد", "العربون", "الحالة"].map((h) => (
                  <th key={h} scope="col" className={thCls}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {idx.map((i) => {
                const r = BOOKINGS[i];
                const [label, tone] = STATUS[statusOf(i)];
                return (
                  <tr key={r[0]} className={rowCls(i === sel)} onClick={() => setSel(i)}>
                    <td className="px-4 py-3">
                      <button type="button" aria-pressed={i === sel} onClick={() => setSel(i)} dir="ltr" className="bg-transparent p-0 text-[13px] font-bold text-ink underline">
                        {r[0]}
                      </button>
                    </td>
                    <td className="px-2 py-3">{r[1]}</td>
                    <td className="px-2 py-3">
                      <span className="flex flex-col">
                        <span className="font-semibold">{r[2]}</span>
                        <span className={`text-xs ${muted}`}>{r[3]}</span>
                      </span>
                    </td>
                    <td className="px-2 py-3">
                      {toArabicDigits(r[4])} من {toArabicDigits(r[5])} ج.م
                    </td>
                    <td className="px-2 py-3">
                      <Pill tone={tone}>{label}</Pill>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {idx.length === 0 && <p className={`p-8 text-center ${muted}`}>مفيش حجوزات بالبحث ده.</p>}
        </section>

        <aside aria-label="تفاصيل الحجز" className={`flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
          <div className="flex items-center gap-2">
            <h2 dir="ltr" className="flex-1 text-lg font-bold text-right">
              {b[0]}
            </h2>
            <Pill tone={STATUS[bst][1]}>{STATUS[bst][0]}</Pill>
          </div>
          {(
            [
              ["اللاعب", b[1]],
              ["الملعب", b[2]],
              ["الميعاد", b[3]],
              ["العربون", money(b[4])],
              ["الإجمالي", money(b[5])],
            ] as const
          ).map(([k, v]) => (
            <KV key={k} k={k}>
              <b className="font-semibold">{v}</b>
            </KV>
          ))}
          <div className="flex flex-col mt-1">
            <span className="text-[13px] font-bold mb-2">مسار الحجز</span>
            {steps.map(([l, t], i) => (
              <div key={i} className="flex gap-2.5 items-stretch">
                <span className="flex flex-col items-center">
                  <span className={`w-3 h-3 rounded-full shrink-0 mt-1 ${tl[i] === 2 ? "bg-brand-red" : tl[i] ? "bg-[#1B6E3E]" : "bg-[#C9CFD9]"}`} />
                  {i < steps.length - 1 && <span className="flex-1 w-0.5 bg-[#E4E7EC]" />}
                </span>
                <span className="flex-1 pb-3 flex flex-col">
                  <span className="text-sm font-semibold">{l}</span>
                  <span className={`text-xs ${muted}`}>{t}</span>
                </span>
              </div>
            ))}
          </div>
          <div className="flex gap-2 flex-wrap">
            <Link href="/disputes" className={actionBtn}>
              {bst === "dispute" ? "افتح النزاع" : "النزاعات"}
            </Link>
            <button type="button" onClick={() => setToast(`اتبعتت رسالة للاعب وللملعب بخصوص ${b[0]}.`)} className={actionBtn}>
              تواصل مع الطرفين
            </button>
            {!["cancel", "admin", "noshow"].includes(bst) && (
              <button
                type="button"
                onClick={() =>
                  setModal({
                    title: `إلغاء الحجز ${b[0]} إدارياً؟`,
                    body: "العربون هيترجع للاعب كرصيد، والملعب هيتبلّغ. استخدمه بس في حالات الاحتيال أو الأعطال.",
                    confirmLabel: "إلغاء الحجز",
                    onConfirm: () => {
                      setStatusOverride({ ...statusOverride, [sel]: "admin" });
                      setToast("اتلغى الحجز واتسجل السبب في سجل التدقيق.");
                    },
                  })
                }
                className="min-h-[38px] px-3.5 rounded-[10px] bg-brand-red text-white text-[13px] font-bold"
              >
                إلغاء إداري
              </button>
            )}
          </div>
        </aside>
      </div>

      <ReasonModal request={modal} onClose={() => setModal(null)} />
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
