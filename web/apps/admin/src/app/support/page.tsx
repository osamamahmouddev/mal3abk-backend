"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, PageHeader, Pill, Toast, Tone, card, chip, inputClass, muted, redShadow, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Kind = "player" | "venue";
type Priority = "high" | "med" | "low";
type Filter = "open" | "player" | "venue" | "closed";
// sender: p = customer, a = support agent ("Name: text")
type Message = ["p" | "a", string];
// who, kind, priority, subject, time, related link, link label, messages
type Ticket = [string, Kind, Priority, string, string, string | null, string, Message[]];

const TICKETS: Ticket[] = [
  ["عمر خالد", "player", "high", "دفعت العربون والحجز لسه تحت المراجعة", "من ١٠ دقايق", "/bookings", "الحجز KH-2607-0145", [["p", "حوّلت ٥٠ ج.م من ساعة والملعب لسه مأكدش، والماتش النهارده."]]],
  [
    "محمود عبد الله — ملعب النجوم",
    "venue",
    "high",
    "عايز أفتح الفرع التاني (التجمع)",
    "من ساعة",
    "/approvals",
    "طلب الموافقة",
    [
      ["p", "رفعت كل الورق من يومين ولسه الفرع تحت المراجعة."],
      ["a", "سارة: بنراجع السجل التجاري وهنرد النهارده."],
      ["p", "تمام، مستني."],
    ],
  ],
  ["مازن رأفت", "player", "med", "اتسجلت عليّ غياب ظلم", "من ٣ ساعات", "/disputes", "النزاع", [["p", "وصلنا متأخرين ١٠ دقايق بس."]]],
  ["هشام — ملاعب الرواد", "venue", "med", "الفاتورة فيها اسم الشركة غلط", "امبارح", "/billing", "الفواتير", [["p", 'محتاجين الفاتورة باسم "الرواد للاستثمار الرياضي".']]],
  ["كريم عادل", "player", "low", "فين الرصيد بتاع الإلغاء؟", "امبارح", "/disputes", "النزاع", [["p", "مش لاقي الرصيد في حسابي."]]],
  ["أحمد سامي", "player", "low", "عايز أغيّر كابتن الفريق", "من يومين", null, "", [["p", "هسافر شهر وعايز يوسف يبقى كابتن."]]],
  ["سامح — ملعب الحي", "venue", "med", "الملعب اتخفى ليه؟", "من يومين", "/venues", "ملف الملعب", [["p", "الملعب مش ظاهر للاعيبة من امبارح."]]],
  [
    "يوسف حسن",
    "player",
    "low",
    "التطبيق بيقفل وأنا برفع الإيصال",
    "من ٤ أيام",
    null,
    "",
    [
      ["p", "سامسونج A12 أندرويد ١١."],
      ["a", "عمرو: اتصلح في التحديث الأخير، جرّب تاني."],
    ],
  ],
];

const KIND: Record<Kind, [string, Tone]> = { player: ["لاعب", "neutral"], venue: ["ملعب", "navy"] };
const PRIORITY: Record<Priority, [string, Tone]> = { high: ["عاجل", "bad"], med: ["متوسط", "warn"], low: ["عادي", "neutral"] };
const QUICK_REPLIES: [string, string][] = [
  ["بنراجع", "شكراً إنك كلمتنا، بنراجع الموضوع وهنرد عليك خلال ساعة."],
  ["محتاج صورة", "ممكن تبعتلنا صورة واضحة من الإيصال أو الشاشة؟"],
  ["اتحلت", "المشكلة اتحلت، جرّب تاني ولو فيه حاجة قولنا."],
];

// TODO: the signed-in agent's name comes from the session.
const ME = "سارة";
const smallBtn = "min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold";

export default function SupportPage() {
  const [filter, setFilter] = useState<Filter>("open");
  const [sel, setSel] = useState(0);
  const detailsRef = useRevealOnSelect<HTMLElement>(sel);
  const [reply, setReply] = useState("");
  const [extra, setExtra] = useState<Record<number, Message[]>>({});
  const [status, setStatus] = useState<Record<number, "open" | "closed">>({});
  const [owners, setOwners] = useState<Record<number, string>>({});
  const [toast, setToast] = useState<string | null>(null);

  const statusOf = (i: number) => status[i] ?? (i === 7 ? "closed" : "open");
  const idx = TICKETS.map((_, i) => i).filter((i) =>
    filter === "open" ? statusOf(i) === "open" : filter === "closed" ? statusOf(i) === "closed" : TICKETS[i][1] === filter
  );
  const t = TICKETS[sel];
  const tst = statusOf(sel);
  const msgs = [...t[7], ...(extra[sel] ?? [])];
  const firstAgent = t[7].find((m) => m[0] === "a");
  const owner = owners[sel] ?? (firstAgent ? firstAgent[1].split(":")[0] : "مش متعيّن");
  const openCount = TICKETS.filter((_, i) => statusOf(i) === "open").length;

  const send = () => {
    const text = reply.trim();
    if (!text) return;
    setExtra({ ...extra, [sel]: [...(extra[sel] ?? []), ["a", `${ME}: ${text}`]] });
    if (owner === "مش متعيّن") setOwners({ ...owners, [sel]: ME });
    setReply("");
    setToast("اتبعت الرد بإشعار في التطبيق.");
  };

  return (
    <Page>
      <PageHeader title="تذاكر الدعم" sub={`${toArabicDigits(openCount)} تذكرة مفتوحة · متوسط أول رد ١٨ دقيقة`}>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["open", "مفتوحة"],
              ["player", "لاعيبة"],
              ["venue", "ملاعب"],
              ["closed", "مقفولة"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={filter === id} onClick={() => setFilter(id)} className={chip(filter === id)}>
              {label}
            </button>
          ))}
        </div>
      </PageHeader>

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="التذاكر" className={`flex-[1_1_300px] min-w-0 ${card} rounded-[20px] overflow-hidden`}>
          {idx.map((i) => {
            const x = TICKETS[i];
            const k = statusOf(i);
            return (
              <button
                key={i}
                type="button"
                aria-pressed={i === sel}
                onClick={() => {
                  setSel(i);
                  setReply("");
                }}
                className={`w-full text-right flex flex-col gap-1.5 px-4 py-3.5 border-b border-[#EEF0F5] text-slate-900 ${
                  i === sel ? "bg-[#F3F5FB] shadow-[inset_-3px_0_0_#D63B2F]" : "bg-white"
                }`}
              >
                <span className="flex items-center gap-2">
                  <span className="flex-1 text-sm font-bold">{x[0]}</span>
                  <span className="text-xs text-[#8A94AA]">{x[4]}</span>
                </span>
                <span className="text-[13px] text-[#3B4352]">{x[3]}</span>
                <span className="flex gap-1.5">
                  <Pill tone={KIND[x[1]][1]}>{KIND[x[1]][0]}</Pill>
                  <Pill tone={PRIORITY[x[2]][1]}>{PRIORITY[x[2]][0]}</Pill>
                  <Pill tone={k === "open" ? "warn" : "ok"}>{k === "open" ? "مفتوحة" : "مقفولة"}</Pill>
                </span>
              </button>
            );
          })}
          {idx.length === 0 && <p className={`p-8 text-center ${muted}`}>مفيش تذاكر هنا.</p>}
        </section>

        <section ref={detailsRef} className={`scroll-mt-20 flex-[999_1_460px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
          <div className="flex items-center gap-2.5 flex-wrap">
            <h2 className="flex-1 text-lg font-bold">{t[3]}</h2>
            <Pill tone={tst === "open" ? "warn" : "ok"}>{tst === "open" ? "مفتوحة" : "مقفولة"}</Pill>
          </div>
          <div className={`flex gap-2 flex-wrap text-[13px] ${muted}`}>
            <span>{t[0]}</span>
            <span>·</span>
            <span>{KIND[t[1]][0]}</span>
            <span>·</span>
            <span>
              مسؤول: <b className="text-ink">{owner}</b>
            </span>
            {t[5] && (
              <Link href={t[5]} className="font-bold text-ink underline">
                {t[6]}
              </Link>
            )}
          </div>
          <div className="flex flex-col gap-2.5 bg-surface rounded-2xl p-3.5">
            {msgs.map(([from, text], i) => {
              const fromAgent = from === "a";
              const body = fromAgent && text.includes(":") ? text.split(":").slice(1).join(":").trim() : text;
              return (
                <div key={i} className={`flex ${fromAgent ? "justify-end" : "justify-start"}`}>
                  <div className={`max-w-[80%] rounded-[14px] px-3 py-2.5 text-sm leading-[1.6] ${fromAgent ? "bg-ink text-white" : "bg-white text-slate-900"}`}>
                    <span className="block text-xs font-bold mb-1 opacity-75">{fromAgent ? "فريق ملعبك" : t[0]}</span>
                    {body}
                  </div>
                </div>
              );
            })}
          </div>

          {tst === "open" ? (
            <div className="flex flex-col gap-2.5">
              <div className="flex gap-1.5 flex-wrap">
                {QUICK_REPLIES.map(([label, text]) => (
                  <button
                    key={label}
                    type="button"
                    onClick={() => setReply(text)}
                    className="min-h-[34px] px-3 rounded-full border border-[#DCE0E8] bg-white text-xs font-semibold text-[#3B4352]"
                  >
                    {label}
                  </button>
                ))}
              </div>
              <label htmlFor="rp" className="text-[13px] font-semibold">
                الرد
              </label>
              <input
                id="rp"
                value={reply}
                onChange={(e) => setReply(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && send()}
                placeholder="اكتب ردك..."
                className={inputClass}
              />
              <div className="flex gap-2 flex-wrap items-center">
                <button
                  type="button"
                  onClick={() => {
                    setOwners({ ...owners, [sel]: ME });
                    setToast("التذكرة بقت عليكي.");
                  }}
                  className={smallBtn}
                >
                  {owner === ME ? "متعيّنة ليك" : "خليها عليّا"}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setStatus({ ...status, [sel]: "closed" });
                    setToast("اتقفلت التذكرة واتبعت استبيان رضا.");
                  }}
                  className={smallBtn}
                >
                  قفل التذكرة
                </button>
                <span className="flex-1" />
                <button
                  type="button"
                  disabled={!reply.trim()}
                  onClick={send}
                  className={`min-h-[44px] px-6 rounded-xl text-sm font-bold ${
                    reply.trim() ? `bg-brand-red hover:bg-brand-red-hover text-white ${redShadow}` : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
                  }`}
                >
                  إرسال
                </button>
              </div>
            </div>
          ) : (
            <div className="flex items-center gap-2.5 bg-[#E5F5EB] text-[#1B6E3E] rounded-xl px-3.5 py-2.5 text-sm">
              <span className="flex-1">التذكرة مقفولة.</span>
              <button type="button" onClick={() => setStatus({ ...status, [sel]: "open" })} className={smallBtn}>
                إعادة فتح
              </button>
            </div>
          )}
        </section>
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
