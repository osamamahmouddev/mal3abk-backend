"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, PageHeader, Pill, Tabs, Toast, Tone, card, chip, inputClass, muted, redShadow, thCls, toArabicDigits } from "@/components/ui";

// check result: 1 = ok, 0 = problem, 2 = unclear
type Check = [0 | 1 | 2, string];
// code, title, player, venue, player says, venue says, SLA, checks
type Dispute = [string, string, string, string, string, string, string, Check[]];

const DISPUTES: Dispute[] = [
  [
    "KH-2807-0151",
    "الملعب رفض إثبات دفع اللاعب شايفه صح",
    "ملعب المدينة (فريق)",
    "ملعب النجوم — مدينة نصر",
    "حوّلنا ٥٠ ج.م فودافون كاش من رقم الكابتن وعندي رسالة التأكيد. الملعب رفض وقال المبلغ ماوصلش.",
    "الإيصال اسم المحوّل فيه مختلف ومش لاقيين التحويل في الحساب لحد دلوقتي.",
    "فاضل ٣ ساعات",
    [
      [1, "المبلغ مطابق للعربون"],
      [1, "التاريخ والوقت قبل المهلة"],
      [0, "اسم المحوّل مختلف عن اسم اللاعب"],
      [2, "رقم العملية مش مستخدم قبل كده"],
    ],
  ],
  [
    "RW-2507-0410",
    "لاعب بيعترض على تسجيله No-show",
    "مازن رأفت",
    "ملاعب الرواد",
    "وصلنا متأخرين ١٠ دقايق والملعب كان اتدّى لفريق تاني.",
    "الفريق وصل بعد ٢٥ دقيقة والسياسة ١٥ دقيقة بس.",
    "فاضل يوم",
    [
      [1, "سياسة التأخير ١٥ دقيقة ظاهرة وقت الحجز"],
      [0, "مفيش دليل وقت الوصول من اللاعب"],
      [1, "الملعب سجّل الحضور من اللوحة ٨:٢٥ م"],
      [2, "أول بلاغ No-show للملعب ده"],
    ],
  ],
  [
    "SH-2407-1180",
    "لاعب ماوصلهوش استرداد العربون",
    "كريم عادل",
    "ستاد الشباب",
    "لغيت قبل الماتش بيومين والسياسة بتقول استرداد كامل، ولسه ماوصلنيش حاجة.",
    "اتعمل الاسترداد كرصيد في التطبيق مش فلوس.",
    "متأخر ساعتين",
    [
      [1, "الإلغاء قبل المهلة"],
      [1, "سياسة الملعب: رصيد للحجز الجاي"],
      [2, "رسالة الإلغاء ماوضّحتش إنه رصيد"],
      [1, "الرصيد ظاهر في حساب اللاعب"],
    ],
  ],
];

type Decision = "player" | "venue" | "split" | "info";
const DECISIONS: [Decision, string, string][] = [
  ["player", "لصالح اللاعب", "الحجز هيتأكد ولو الميعاد راح العربون يرجع للاعب."],
  ["venue", "لصالح الملعب", "القرار الأصلي بتاع الملعب يفضل زي ما هو."],
  ["split", "حل وسط", "نص العربون يرجع للاعب كرصيد والنص للملعب."],
  ["info", "طلب معلومات", "هيتبعت للطرفين طلب مستندات إضافية والمهلة تتمد ٢٤ ساعة."],
];

const CHECK_MARK: Record<0 | 1 | 2, [string, string]> = {
  1: ["✓", "bg-[#E5F5EB] text-[#1B6E3E]"],
  0: ["!", "bg-[#FDECEA] text-[#A8261C]"],
  2: ["؟", "bg-[#FEF3D6] text-[#7A4D00]"],
};

const FLAGS: [string, string, string][] = [
  ["KH-2607-0139", "نفس الصورة اترفعت لحجزين في ملعبين مختلفين", "حازم فوزي"],
  ["RW-2607-0422", "رقم عملية اتستخدم قبل كده", "محمد طارق"],
  ["KH-2407-0120", "تعديل واضح في المبلغ على الصورة", "حازم فوزي"],
];
type FlagState = "open" | "clear" | "esc";
const FLAG_STATE: Record<FlagState, [string, Tone]> = {
  open: ["محتاج مراجعة", "warn"],
  clear: ["اتراجع — سليم", "ok"],
  esc: ["اتحوّل لملف اللاعب", "bad"],
};

export default function DisputesPage() {
  const [tab, setTab] = useState<"d" | "f">("d");
  const [sel, setSel] = useState(0);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [note, setNote] = useState("");
  const [noteErr, setNoteErr] = useState(false);
  const [done, setDone] = useState<Record<number, string>>({});
  const [flagState, setFlagState] = useState<Record<number, FlagState>>({});
  const [toast, setToast] = useState<string | null>(null);

  const d = DISPUTES[sel];
  const closed = done[sel];
  const openCount = DISPUTES.filter((_, i) => !done[i]).length;

  const resolve = () => {
    if (!decision || note.trim().length < 5) {
      setNoteErr(true);
      return;
    }
    const label = DECISIONS.find((x) => x[0] === decision)![1];
    setDecision(null);
    setNote("");
    if (decision === "info") {
      setToast("اتبعت طلب معلومات للطرفين والمهلة اتمدت ٢٤ ساعة.");
      return;
    }
    setDone({ ...done, [sel]: label });
    setToast(`اتقفل النزاع ${d[0]} ${label}.`);
  };

  return (
    <Page>
      <PageHeader title="إيصالات ونزاعات" sub={`${toArabicDigits(openCount)} نزاع مفتوح · مهلة القرار ٢٤ ساعة من فتح النزاع`} />
      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          ["d", `النزاعات (${toArabicDigits(openCount)})`],
          ["f", "إيصالات مشبوهة"],
        ]}
      />

      {tab === "d" && (
        <div className="flex flex-wrap gap-4 items-start">
          <section aria-label="قائمة النزاعات" className="flex-[1_1_300px] min-w-0 flex flex-col gap-2.5">
            {DISPUTES.map((x, i) => {
              const dn = done[i];
              return (
                <button
                  key={x[0]}
                  type="button"
                  aria-pressed={i === sel}
                  onClick={() => {
                    setSel(i);
                    setDecision(null);
                    setNote("");
                    setNoteErr(false);
                  }}
                  className={`${card} text-right flex flex-col gap-1 px-4 py-3.5 rounded-[18px] text-slate-900 ${i === sel ? "!border-2 !border-ink" : ""}`}
                >
                  <span className="flex items-center gap-2">
                    <span dir="ltr" className="flex-1 text-right text-[13px] font-bold text-ink">
                      {x[0]}
                    </span>
                    <Pill tone={dn ? "ok" : "bad"}>{dn ? "اتحل" : "مفتوح"}</Pill>
                  </span>
                  <span className="text-[15px] font-bold">{x[1]}</span>
                  <span className={`text-[13px] ${muted}`}>
                    {x[2]} ضد {x[3]}
                  </span>
                  <span className={`text-xs font-bold ${dn ? "text-[#1B6E3E]" : x[6].startsWith("متأخر") ? "text-[#A8261C]" : "text-[#7A4D00]"}`}>
                    {dn ? "اتقفل" : x[6]}
                  </span>
                </button>
              );
            })}
          </section>

          <section className={`flex-[999_1_480px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
            <div className="flex items-center gap-2.5 flex-wrap">
              <h2 className="flex-1 text-[19px] font-bold">{d[1]}</h2>
              <Link href="/bookings" dir="ltr" className="text-[13px] font-bold text-ink underline">
                {d[0]}
              </Link>
            </div>
            <span className={`text-sm ${muted}`}>
              {d[2]} ضد {d[3]}
            </span>
            <div className="flex flex-wrap gap-3">
              <div className="flex-[1_1_220px] bg-surface rounded-[14px] px-3.5 py-3 flex flex-col gap-1.5">
                <span className={`text-xs font-bold ${muted}`}>اللاعب — {d[2]}</span>
                <span className="text-sm leading-[1.7]">{d[4]}</span>
              </div>
              <div className="flex-[1_1_220px] bg-[#FEF8EA] rounded-[14px] px-3.5 py-3 flex flex-col gap-1.5">
                <span className="text-xs font-bold text-[#7A4D00]">الملعب — {d[3]}</span>
                <span className="text-sm leading-[1.7]">{d[5]}</span>
              </div>
            </div>
            <div className="flex gap-3 flex-wrap items-stretch">
              <div className="flex-[0_0_150px] aspect-[3/4] rounded-[14px] bg-[linear-gradient(160deg,#E6F0EA,#CFE3D7)] flex flex-col items-center justify-center gap-1.5 text-[#1B6E3E] text-[13px] font-bold">
                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                  <rect x="4" y="3" width="16" height="18" rx="2" />
                  <path d="M8 8h8M8 12h8M8 16h5" />
                </svg>
                صورة الإيصال
              </div>
              <div className="flex-[1_1_220px] flex flex-col gap-2">
                {d[7].map(([k, l]) => (
                  <div key={l} className="flex gap-2 items-center text-sm">
                    <span className={`w-[22px] h-[22px] rounded-full shrink-0 flex items-center justify-center text-xs font-bold ${CHECK_MARK[k][1]}`}>
                      {CHECK_MARK[k][0]}
                    </span>
                    <span>{l}</span>
                  </div>
                ))}
              </div>
            </div>

            {!closed ? (
              <div className="flex flex-col gap-2.5 border-t border-[#EEF0F5] pt-3.5">
                <span className="text-sm font-bold">القرار</span>
                <div className="flex gap-1.5 flex-wrap">
                  {DECISIONS.map(([id, label]) => (
                    <button
                      key={id}
                      type="button"
                      aria-pressed={decision === id}
                      onClick={() => {
                        setDecision(id);
                        setNoteErr(false);
                      }}
                      className={chip(decision === id)}
                    >
                      {label}
                    </button>
                  ))}
                </div>
                <span className={`text-[13px] ${muted}`}>{decision ? DECISIONS.find((x) => x[0] === decision)![2] : "اختار قرار تشوف هيحصل إيه."}</span>
                <label htmlFor="dn" className="text-[13px] font-semibold">
                  ملاحظة للطرفين
                </label>
                <input
                  id="dn"
                  value={note}
                  onChange={(e) => {
                    setNote(e.target.value);
                    setNoteErr(false);
                  }}
                  placeholder="اشرح سبب القرار بوضوح"
                  className={inputClass}
                />
                {noteErr && (
                  <span role="alert" className="text-xs text-[#A8261C]">
                    اختار قرار واكتب ملاحظة.
                  </span>
                )}
                <button
                  type="button"
                  onClick={resolve}
                  className={`self-start min-h-[46px] px-6 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow}`}
                >
                  تنفيذ القرار
                </button>
              </div>
            ) : (
              <div role="status" className="bg-[#E5F5EB] text-[#1B6E3E] rounded-[14px] px-3.5 py-3 text-sm leading-[1.7]">
                اتنفّذ القرار: {closed} — الطرفين اتبلّغوا والقرار اتسجل في سجل التدقيق.
              </div>
            )}
          </section>
        </div>
      )}

      {tab === "f" && (
        <section className={`${card} rounded-[20px] overflow-x-auto`}>
          <table className="w-full border-collapse text-sm min-w-[680px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                <th scope="col" className={`${thCls} !px-4`}>
                  الإيصال
                </th>
                <th scope="col" className={thCls}>
                  سبب الاشتباه
                </th>
                <th scope="col" className={thCls}>
                  اللاعب
                </th>
                <th scope="col" className={thCls}>
                  الحالة
                </th>
                <th scope="col" className={thCls}>
                  <span className="sr-only">إجراءات</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {FLAGS.map(([code, why, player], i) => {
                const k = flagState[i] ?? "open";
                return (
                  <tr key={code} className="border-t border-[#EEF0F5]">
                    <td className="px-4 py-3 font-bold" dir="ltr">
                      {code}
                    </td>
                    <td className="px-2 py-3">{why}</td>
                    <td className="px-2 py-3">{player}</td>
                    <td className="px-2 py-3">
                      <Pill tone={FLAG_STATE[k][1]}>{FLAG_STATE[k][0]}</Pill>
                    </td>
                    <td className="px-2 py-3">
                      {k === "open" && (
                        <span className="flex gap-1.5 justify-end">
                          <button
                            type="button"
                            onClick={() => {
                              setFlagState({ ...flagState, [i]: "clear" });
                              setToast("اتعلّم الإيصال سليم.");
                            }}
                            className="min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold"
                          >
                            سليم
                          </button>
                          <Link
                            href="/users"
                            onClick={() => setFlagState({ ...flagState, [i]: "esc" })}
                            className="min-h-[38px] px-3.5 rounded-[10px] bg-brand-red text-white text-[13px] font-bold flex items-center hover:text-white"
                          >
                            افتح ملف اللاعب
                          </Link>
                        </span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </section>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
