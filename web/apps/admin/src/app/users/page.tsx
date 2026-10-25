"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, PageHeader, Pill, ReasonModal, ReasonRequest, Toast, Tone, btnSmall, card, chip, inputClass, muted, rowCls, thCls, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Status = "active" | "flagged" | "suspended";
// name, phone, area, joined, bookings, no-shows, reports, status, teams
type Player = [string, string, string, string, number, number, number, Status, string];

const PLAYERS: Player[] = [
  ["أحمد سامي", "0100 234 5678", "مدينة نصر", "مارس", 23, 0, 0, "active", "فريق النجوم (كابتن)"],
  ["يوسف حسن", "0111 765 4321", "مدينة نصر", "أبريل", 9, 1, 0, "active", "فريق النجوم"],
  ["عمر خالد", "0122 345 6789", "مصر الجديدة", "يناير", 31, 0, 0, "active", "فريق النجوم، الصقور"],
  ["مازن رأفت", "0155 111 2233", "التجمع", "مايو", 6, 3, 2, "flagged", "شباب التجمع"],
  ["كريم عادل", "0100 999 8877", "المعادي", "فبراير", 14, 0, 0, "active", "المعادي يونايتد"],
  ["حازم فوزي", "0128 444 5566", "الشيخ زايد", "يونيو", 2, 2, 3, "suspended", "من غير فريق"],
  ["محمد طارق", "0109 777 6655", "مدينة نصر", "يوليو", 1, 0, 0, "active", "من غير فريق"],
];

const STATUS: Record<Status, [string, Tone]> = {
  active: ["نشط", "ok"],
  flagged: ["عليه بلاغات", "warn"],
  suspended: ["موقوف", "bad"],
};

const ACTIVITY: Record<number, string[]> = {
  0: ["حجز ملعب ١ — ملعب النجوم، السبت ٨ م", "رفع إثبات عربون ٥٠ ج.م", "تحدّى فريق الصقور"],
  3: ["اتعلّم عليه No-show في ملعب الرواد", "بلاغ من كابتن فريق: ألفاظ في الشات", "بلاغ من ملعب: ماحضرش وماردش"],
  5: ["اتوقف بعد ٣ بلاغات مؤكدة", "رفع إثبات دفع متكرر لنفس الصورة", "لغى ٢ حجز بعد المهلة"],
};

export default function UsersPage() {
  const [q, setQ] = useState("");
  const [filter, setFilter] = useState<"all" | Status>("all");
  const [sel, setSel] = useState(0);
  const detailsRef = useRevealOnSelect<HTMLElement>(sel);
  const [statusOverride, setStatusOverride] = useState<Record<number, Status>>({});
  const [notes, setNotes] = useState<Record<number, string>>({});
  const [modal, setModal] = useState<ReasonRequest | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const statusOf = (i: number) => statusOverride[i] ?? PLAYERS[i][7];
  const query = q.trim();
  const idx = PLAYERS.map((_, i) => i).filter(
    (i) =>
      (filter === "all" || statusOf(i) === filter) &&
      (!query || PLAYERS[i][0].includes(query) || PLAYERS[i][1].replace(/\s/g, "").includes(query.replace(/\s/g, "")))
  );
  const u = PLAYERS[sel];
  const ust = statusOf(sel);
  const note = notes[sel] || (ust === "suspended" ? "موقوف: رفع نفس إثبات الدفع لأكتر من حجز." : "");

  return (
    <Page>
      <PageHeader title="المستخدمين (اللاعيبة)" sub={`${toArabicDigits("21,480").replace(",", "٬")} لاعب · ${toArabicDigits(idx.length)} في النتيجة`}>
        <button
          type="button"
          onClick={() => setToast("الملف بيتجهّز — من غير أرقام الموبايل كاملة لحماية البيانات.")}
          className="min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold"
        >
          تصدير CSV
        </button>
      </PageHeader>

      <div className="flex flex-wrap gap-2.5 items-center">
        <input aria-label="بحث" value={q} onChange={(e) => setQ(e.target.value)} placeholder="ابحث بالاسم أو رقم الموبايل" className={`${inputClass} flex-[1_1_260px] min-w-0`} />
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "الكل"],
              ["active", "نشط"],
              ["flagged", "عليه بلاغات"],
              ["suspended", "موقوف"],
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
          <table className="w-full border-collapse text-sm min-w-[620px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                <th scope="col" className={`${thCls} !px-4`}>
                  اللاعب
                </th>
                {["المنطقة", "الحجوزات", "ماجاش", "بلاغات", "الحالة"].map((h) => (
                  <th key={h} scope="col" className={thCls}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {idx.map((i) => {
                const [name, phone, area, , b, ns, rp] = PLAYERS[i];
                const [label, tone] = STATUS[statusOf(i)];
                return (
                  <tr key={i} className={rowCls(i === sel)} onClick={() => setSel(i)}>
                    <td className="px-4 py-3">
                      <button type="button" aria-pressed={i === sel} onClick={() => setSel(i)} className="bg-transparent p-0 text-right flex flex-col gap-0.5 text-slate-900">
                        <span className="text-sm font-bold">{name}</span>
                        <span className={`text-xs ${muted}`} dir="ltr">
                          {toArabicDigits(phone)}
                        </span>
                      </button>
                    </td>
                    <td className="px-2 py-3">{area}</td>
                    <td className="px-2 py-3 font-bold">{toArabicDigits(b)}</td>
                    <td className="px-2 py-3">{toArabicDigits(ns)}</td>
                    <td className="px-2 py-3">{toArabicDigits(rp)}</td>
                    <td className="px-2 py-3">
                      <Pill tone={tone}>{label}</Pill>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {idx.length === 0 && <p className={`p-8 text-center ${muted}`}>مفيش لاعيبة بالبحث ده.</p>}
        </section>

        <aside ref={detailsRef} aria-label="ملف اللاعب" className={`scroll-mt-20 flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
          <div className="flex items-center gap-3">
            <span className="w-[52px] h-[52px] rounded-full bg-ink text-white flex items-center justify-center text-xl font-bold">{u[0].charAt(0)}</span>
            <span className="flex-1 flex flex-col gap-0.5">
              <span className="text-lg font-bold">{u[0]}</span>
              <span className={`text-[13px] ${muted}`}>
                {u[2]} · عضو من {u[3]}
              </span>
            </span>
            <Pill tone={STATUS[ust][1]}>{STATUS[ust][0]}</Pill>
          </div>
          <div className="grid grid-cols-3 gap-2">
            {(
              [
                [u[4], "حجز"],
                [u[5], "ماجاش"],
                [u[6], "بلاغات"],
              ] as const
            ).map(([v, l]) => (
              <div key={l} className="bg-surface rounded-xl p-2.5 text-center flex flex-col gap-0.5">
                <span className="text-xl font-bold">{toArabicDigits(v)}</span>
                <span className={`text-xs ${muted}`}>{l}</span>
              </div>
            ))}
          </div>
          <div className="flex flex-col gap-1.5">
            <span className="text-[13px] font-bold">الفرق</span>
            <span className="text-sm text-[#3B4352]">{u[8]}</span>
          </div>
          <div className="flex flex-col gap-2">
            <span className="text-[13px] font-bold">آخر النشاط</span>
            {(ACTIVITY[sel] ?? ["حجز آخر ماتش من أسبوع", "عدّل بيانات الملف الشخصي", "انضم لفريق بكود دعوة"]).map((l) => (
              <div key={l} className="flex gap-2 text-[13px]">
                <span className="w-1.5 h-1.5 rounded-full bg-brand-red mt-[7px] shrink-0" />
                <span className="flex-1">{l}</span>
              </div>
            ))}
          </div>
          {note && <div className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-3 py-2.5 text-[13px]">{note}</div>}
          <div className="flex gap-2 flex-wrap">
            <Link href="/bookings" className={`${btnSmall} !min-h-[38px] !px-3.5 !rounded-[10px] !text-[13px] flex items-center`}>
              حجوزاته
            </Link>
            <button
              type="button"
              onClick={() => setToast(`اتبعت تحذير لـ ${u[0]} بإشعار و SMS.`)}
              className={`${btnSmall} !min-h-[38px] !px-3.5 !rounded-[10px] !text-[13px]`}
            >
              إرسال تحذير
            </button>
            {ust !== "suspended" ? (
              <button
                type="button"
                onClick={() =>
                  setModal({
                    title: `إيقاف حساب ${u[0]}؟`,
                    body: "مش هيقدر يحجز أو يتحدى. حجوزاته المأكدة الجاية هتفضل، والملاعب هتتبلّغ.",
                    confirmLabel: "إيقاف",
                    onConfirm: (r) => {
                      setStatusOverride({ ...statusOverride, [sel]: "suspended" });
                      setNotes({ ...notes, [sel]: `موقوف: ${r}` });
                      setToast("اتوقف الحساب واتسجل في سجل التدقيق.");
                    },
                  })
                }
                className="min-h-[38px] px-3.5 rounded-[10px] bg-brand-red text-white text-[13px] font-bold"
              >
                إيقاف الحساب
              </button>
            ) : (
              <button
                type="button"
                onClick={() =>
                  setModal({
                    title: `رفع الإيقاف عن ${u[0]}؟`,
                    body: "هيرجع يقدر يحجز عادي.",
                    confirmLabel: "رفع الإيقاف",
                    onConfirm: () => {
                      setStatusOverride({ ...statusOverride, [sel]: "active" });
                      setNotes({ ...notes, [sel]: "" });
                      setToast("الحساب رجع نشط.");
                    },
                  })
                }
                className="min-h-[38px] px-3.5 rounded-[10px] bg-ink text-white text-[13px] font-bold"
              >
                رفع الإيقاف
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
