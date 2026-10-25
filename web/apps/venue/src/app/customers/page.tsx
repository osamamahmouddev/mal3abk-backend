"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Badge, Page, Toast, Tone, card, chip, formatEGP, muted, secondaryBtn, toArabicDigits, useRevealOnSelect } from "@/components/ui";

// id, initials, name, phone, bookings, cancellations, no-shows, last booking, spent
type Customer = [string, string, string, string, number, number, number, string, number];

const CUSTOMERS: Customer[] = [
  ["ahmed", "أح", "أحمد سامي", "0101 234 5678", 18, 1, 0, "السبت ٢٦ يوليو", 2450],
  ["karim", "كر", "كريم عادل", "0102 233 4455", 11, 0, 0, "السبت ٢٦ يوليو", 1980],
  ["youssef", "يو", "يوسف حسن", "0122 334 4556", 7, 2, 1, "السبت ٢٦ يوليو", 900],
  ["saqr", "صق", "فريق الصقور (يوسف)", "0109 876 5432", 14, 1, 0, "السبت ٢٦ يوليو", 2100],
  ["fathy", "فت", "م. فتحي", "0101 112 2233", 9, 0, 0, "السبت ٢٦ يوليو", 1350],
  ["omar", "عم", "عمر خالد", "0114 433 2211", 3, 0, 0, "السبت ٢٦ يوليو", 480],
  ["ziad", "زي", "زياد طارق", "0127 788 9900", 5, 1, 1, "الجمعة ٢٥ يوليو", 600],
  ["academy", "أش", "أكاديمية الشروق", "0112 004 5678", 24, 0, 0, "شهري — متكرر", 6720],
];

type Filter = "all" | "regular" | "noshow";

const histColor: Record<string, string> = {
  ok: "text-[#1B6E3E]",
  warn: "text-[#7A4D00]",
  bad: "text-[#A8261C]",
  wait: "text-[#3B4352]",
};

const tagOf = (c: Customer): [string, Tone] =>
  c[6] > 0 ? ["عنده عدم حضور", "bad"] : c[4] >= 10 ? ["منتظم", "ok"] : ["جديد نسبياً", "wait"];

export default function CustomersPage() {
  const [q, setQ] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [selId, setSelId] = useState("ahmed");
  const detailsRef = useRevealOnSelect<HTMLElement>(selId);
  const [notes, setNotes] = useState<Record<string, string>>({
    ahmed: "بيحجز كل سبت. بيحب ملعب ١.",
    youssef: "اتأخر مرتين ٢٠ دقيقة.",
  });
  const [toast, setToast] = useState<string | null>(null);

  const query = q.trim();
  const list = CUSTOMERS.filter(
    (c) =>
      (!query || c[2].includes(query) || c[3].replace(/\s/g, "").includes(query.replace(/\s/g, ""))) &&
      (filter === "all" || (filter === "regular" && c[4] >= 10) || (filter === "noshow" && c[6] > 0))
  );
  const sel = CUSTOMERS.find((c) => c[0] === selId);

  const history: [string, string, string][] = sel
    ? [
        ["ملعب ١ · السبت ٢٦ يوليو · ٨ م", sel[0] === "ahmed" ? "قيد المراجعة" : "مؤكد", sel[0] === "ahmed" ? "warn" : "ok"],
        ["ملعب ٢ · الخميس ٢٤ يوليو", sel[6] ? "عدم حضور" : "انتهت المهلة", sel[6] ? "bad" : "wait"],
        ["ملعب ١ · السبت ١٩ يوليو", "مكتمل", "ok"],
      ]
    : [];

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_260px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">العملاء</h1>
          <span className={`text-[13px] ${muted}`}>بس اللي حجزوا في ملعبك — بياناتهم مش بتظهر لأي ملعب تاني.</span>
        </div>
        <button
          type="button"
          onClick={() => setToast(`اتجهّز ملف بـ ${toArabicDigits(list.length)} عميل (الاسم والموبايل وعدد الحجوزات).`)}
          className={`${secondaryBtn} rounded-xl`}
        >
          تصدير
        </button>
      </div>

      <div className="flex flex-wrap gap-2.5 items-center">
        <div className="flex-[1_1_280px] min-w-0 flex items-center gap-2 min-h-[44px] bg-white border border-[#DCE0E8] rounded-xl px-3">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5B6475" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="M20 20l-4-4" />
          </svg>
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="ابحث بالاسم أو الموبايل"
            aria-label="بحث"
            className="flex-1 min-w-0 border-none outline-none text-sm bg-transparent text-slate-900"
          />
        </div>
        {(
          [
            ["all", "الكل"],
            ["regular", "منتظمين"],
            ["noshow", "ليهم عدم حضور"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            aria-pressed={filter === id}
            onClick={() => setFilter(id)}
            className={chip(filter === id, "min-h-[42px] px-3.5 rounded-xl text-[13px]")}
          >
            {label}
          </button>
        ))}
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_560px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[620px] border-collapse text-sm">
              <thead>
                <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
                  <th className="px-[18px] py-3 font-semibold">العميل</th>
                  <th className="p-3 font-semibold">حجوزات</th>
                  <th className="p-3 font-semibold">إلغاء</th>
                  <th className="p-3 font-semibold">عدم حضور</th>
                  <th className="p-3 font-semibold">آخر حجز</th>
                </tr>
              </thead>
              <tbody>
                {list.map((c) => (
                  <tr key={c[0]} className={`border-t border-[#EEF1F5] ${c[0] === selId ? "bg-[#F5F7FB]" : ""}`}>
                    <td className="px-[18px] py-2.5">
                      <button type="button" onClick={() => setSelId(c[0])} className="bg-transparent p-0 flex items-center gap-2.5 text-right text-slate-900">
                        <span className="w-9 h-9 rounded-full bg-[#EEF1F7] text-navy-900 flex items-center justify-center text-xs font-bold">{c[1]}</span>
                        <span className="flex flex-col">
                          <span className="text-sm font-bold">{c[2]}</span>
                          <span dir="ltr" className={`text-xs ${muted} text-right`}>
                            {c[3]}
                          </span>
                        </span>
                      </button>
                    </td>
                    <td className="px-3 py-2.5 font-bold">{toArabicDigits(c[4])}</td>
                    <td className="px-3 py-2.5">{toArabicDigits(c[5])}</td>
                    <td className="px-3 py-2.5">
                      <span className={`font-bold ${c[6] ? "text-[#A8261C]" : muted}`}>{toArabicDigits(c[6])}</span>
                    </td>
                    <td className="px-3 py-2.5 text-[#333B4A]">{c[7]}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {list.length === 0 && <div className={`p-9 text-center text-sm ${muted}`}>مفيش عميل بالاسم ده.</div>}
        </section>

        {sel && (
          <aside ref={detailsRef} aria-label="ملف العميل" className={`scroll-mt-20 flex-[1_1_340px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-3.5`}>
            <div className="flex items-center gap-3">
              <span className="w-[52px] h-[52px] rounded-full bg-navy-900 text-white flex items-center justify-center text-base font-bold">{sel[1]}</span>
              <div className="flex-1 flex flex-col">
                <span className="text-lg font-bold">{sel[2]}</span>
                <span dir="ltr" className={`text-[13px] ${muted} text-right`}>
                  {sel[3]}
                </span>
              </div>
              <Badge tone={tagOf(sel)[1]}>{tagOf(sel)[0]}</Badge>
            </div>
            <div className="grid grid-cols-3 gap-2 text-center">
              {(
                [
                  [toArabicDigits(sel[4]), "حجز"],
                  [formatEGP(sel[8]), "ج.م مدفوعة"],
                  [toArabicDigits(sel[6]), "عدم حضور مؤكد"],
                ] as const
              ).map(([v, l]) => (
                <div key={l} className="bg-surface rounded-xl py-2 flex flex-col">
                  <span className="text-xl font-bold">{v}</span>
                  <span className={`text-[11px] ${muted}`}>{l}</span>
                </div>
              ))}
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-bold">آخر الحجوزات</span>
              {history.map(([slot, st, t]) => (
                <div key={slot} className="flex justify-between text-[13px] py-1.5 border-b border-[#EEF1F5]">
                  <span>{slot}</span>
                  <span className={`font-bold ${histColor[t]}`}>{st}</span>
                </div>
              ))}
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="nt" className="text-sm font-bold">
                ملاحظات الموظفين <span className={`font-normal text-xs ${muted}`}>(مش بتظهر للعميل)</span>
              </label>
              <textarea
                id="nt"
                rows={3}
                value={notes[selId] ?? ""}
                onChange={(e) => setNotes({ ...notes, [selId]: e.target.value })}
                className="border border-[#C9CFD9] rounded-xl px-3 py-2.5 text-sm resize-none text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900"
              />
              <button
                type="button"
                onClick={() => setToast("اتحفظت الملاحظة — بيشوفها الموظفين بس.")}
                className="self-start min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
              >
                حفظ الملاحظة
              </button>
            </div>
            <Link
              href="/reservations/manual"
              className="min-h-[46px] rounded-xl bg-navy-900 text-white text-sm font-bold flex items-center justify-center"
            >
              حجز يدوي للعميل ده
            </Link>
          </aside>
        )}
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
