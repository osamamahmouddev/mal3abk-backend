"use client";

import React, { useState } from "react";
import { Page, PageHeader, Pill, ReasonModal, ReasonRequest, Tabs, Toast, Tone, card, muted, thCls, toArabicDigits } from "@/components/ui";

// title, body, meta, action label, glyph
const REPORTS: [string, string, string, string, string][] = [
  ["اسم فريق مسيء", "فريق اسمه فيه ألفاظ خارجة ظاهر في الترتيب العام.", "بلاغين من لاعيبة · من ساعتين", "إخفاء الاسم", "ف"],
  ["ألفاظ في شات التحدي", "كابتن «شباب التجمع» بعت رسايل مسيئة لفريق تاني بعد ما رفضوا التحدي.", "بلاغ من كابتن الصقور · امبارح", "إيقاف الكابتن", "ش"],
  ["صورة فريق مش مناسبة", "صورة البروفايل فيها لوجو نادي بحقوق ملكية.", "بلاغ أوتوماتيك · من ٣ أيام", "شيل الصورة", "ص"],
];
type ReportState = "open" | "done" | "dismissed" | "warned";
const REPORT_STATE: Record<ReportState, [string, Tone]> = {
  open: ["مفتوح", "warn"],
  done: ["اتاخد إجراء", "ok"],
  dismissed: ["اتجاهل", "neutral"],
  warned: ["اتبعت تحذير", "neutral"],
};

// team A, team B, score reported by A, score reported by B, meta
const RESULTS: [string, string, string, string, string][] = [
  ["فريق النجوم", "الصقور", "4 - 2", "2 - 4", "ملعب النجوم · السبت ١٩ يوليو"],
  ["المعادي يونايتد", "شباب التجمع", "3 - 3", "3 - 1", "ستاد الشباب · الجمعة ١٨ يوليو"],
];

// name, captain, players, W-D-L
const TEAMS: [string, string, number, string][] = [
  ["المعادي يونايتد", "كريم عادل", 11, "18-2-3"],
  ["الصقور", "أحمد فؤاد", 9, "16-4-2"],
  ["ستاد الشباب FC", "مروان علي", 10, "15-3-5"],
  ["فريق النجوم", "أحمد سامي", 8, "14-3-5"],
  ["شباب التجمع", "مازن رأفت", 7, "9-2-8"],
  ["الهرم سبورت", "تامر حسني", 6, "7-1-9"],
];

const smallBtn = "min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold";
const dangerBtn = "min-h-[38px] px-3.5 rounded-[10px] bg-brand-red text-white text-[13px] font-bold";

export default function TeamsPage() {
  const [tab, setTab] = useState<"r" | "m" | "t">("r");
  const [rep, setRep] = useState<Record<number, ReportState>>({});
  const [res, setRes] = useState<Record<number, string>>({});
  const [hidden, setHidden] = useState<Record<number, boolean>>({ 4: true });
  const [modal, setModal] = useState<ReasonRequest | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const openReps = REPORTS.filter((_, i) => !rep[i]).length;
  const openRes = RESULTS.filter((_, i) => !res[i]).length;

  return (
    <Page>
      <PageHeader title="الفرق والمجتمع" sub={`${toArabicDigits("1,214").replace(",", "٬")} فريق · ${toArabicDigits(38)} تحدي الأسبوع ده`} />
      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          ["r", `البلاغات (${toArabicDigits(openReps)})`],
          ["m", `نتايج متنازع عليها (${toArabicDigits(openRes)})`],
          ["t", "الترتيب العام"],
        ]}
      />

      {tab === "r" && (
        <div className="flex flex-col gap-3">
          {REPORTS.map(([title, body, meta, actLabel, glyph], i) => {
            const k = rep[i] ?? "open";
            return (
              <article key={title} className={`${card} rounded-[18px] px-[18px] py-4 flex flex-wrap gap-3.5 items-center`}>
                <span className="w-11 h-11 rounded-xl shrink-0 flex items-center justify-center text-[17px] font-bold bg-[#FDECEA] text-[#A8261C]">{glyph}</span>
                <span className="flex-[1_1_300px] min-w-0 flex flex-col gap-1">
                  <span className="flex gap-2 items-center flex-wrap">
                    <span className="text-[15px] font-bold">{title}</span>
                    <Pill tone={REPORT_STATE[k][1]}>{REPORT_STATE[k][0]}</Pill>
                  </span>
                  <span className="text-sm text-[#3B4352] leading-[1.6]">{body}</span>
                  <span className="text-xs text-[#8A94AA]">{meta}</span>
                </span>
                {k === "open" && (
                  <span className="flex gap-1.5 flex-wrap">
                    <button
                      type="button"
                      onClick={() => {
                        setRep({ ...rep, [i]: "dismissed" });
                        setToast("اتقفل البلاغ من غير إجراء.");
                      }}
                      className={smallBtn}
                    >
                      تجاهل
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setRep({ ...rep, [i]: "warned" });
                        setToast("اتبعت تحذير رسمي.");
                      }}
                      className={smallBtn}
                    >
                      تحذير
                    </button>
                    <button
                      type="button"
                      onClick={() =>
                        setModal({
                          title: `${actLabel}؟`,
                          body,
                          confirmLabel: actLabel,
                          onConfirm: () => {
                            setRep((prev) => ({ ...prev, [i]: "done" }));
                            setToast(`اتنفّذ: ${actLabel}.`);
                          },
                        })
                      }
                      className={dangerBtn}
                    >
                      {actLabel}
                    </button>
                  </span>
                )}
              </article>
            );
          })}
        </div>
      )}

      {tab === "m" && (
        <div className="grid gap-4 grid-cols-[repeat(auto-fit,minmax(340px,1fr))]">
          {RESULTS.map(([a, b, ra, rb, meta], i) => {
            const k = res[i];
            const pick = (label: string) => {
              setRes({ ...res, [i]: label });
              setToast("النتيجة اتعتمدت والترتيب اتحدّث.");
            };
            return (
              <article key={meta} className={`${card} rounded-[20px] p-[18px] flex flex-col gap-3`}>
                <div className="flex justify-between items-center gap-2">
                  <span className={`text-[13px] ${muted}`}>{meta}</span>
                  <Pill tone={k ? "ok" : "warn"}>{k || "مستني قرار"}</Pill>
                </div>
                <div className="flex items-center justify-between gap-2.5">
                  <span className="flex-1 text-[15px] font-bold">{a}</span>
                  <span className={`text-[13px] ${muted}`}>ضد</span>
                  <span className="flex-1 text-[15px] font-bold text-left">{b}</span>
                </div>
                <div className="flex gap-2">
                  {(
                    [
                      [a, ra],
                      [b, rb],
                    ] as const
                  ).map(([team, score]) => (
                    <div key={team} className="flex-1 bg-surface rounded-xl p-2.5 text-center">
                      <span className={`block text-xs ${muted}`}>نتيجة {team}</span>
                      <span className="text-[22px] font-bold" dir="ltr">
                        {toArabicDigits(score)}
                      </span>
                    </div>
                  ))}
                </div>
                {!k && (
                  <div className="flex gap-1.5 flex-wrap">
                    <button type="button" onClick={() => pick(`اتعتمدت نتيجة ${a}`)} className={smallBtn}>
                      اعتمد نتيجة {a}
                    </button>
                    <button type="button" onClick={() => pick(`اتعتمدت نتيجة ${b}`)} className={smallBtn}>
                      اعتمد نتيجة {b}
                    </button>
                    <button
                      type="button"
                      onClick={() =>
                        setModal({
                          title: "إلغاء الماتش من الترتيب؟",
                          body: "الماتش مش هيتحسب لأي فريق. الفريقين هيتبلّغوا.",
                          confirmLabel: "إلغاء من الترتيب",
                          onConfirm: () => {
                            setRes((prev) => ({ ...prev, [i]: "اتلغى من الترتيب" }));
                            setToast("اتشال الماتش من الترتيب.");
                          },
                        })
                      }
                      className={dangerBtn}
                    >
                      إلغاء الماتش من الترتيب
                    </button>
                  </div>
                )}
              </article>
            );
          })}
        </div>
      )}

      {tab === "t" && (
        <section className={`${card} rounded-[20px] overflow-x-auto`}>
          <table className="w-full border-collapse text-sm min-w-[640px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                {["#", "الفريق", "الكابتن", "لاعيبة", "ف - ت - خ", "الحالة"].map((h, i) => (
                  <th key={h} scope="col" className={`${thCls} ${i === 0 ? "!px-4" : ""}`}>
                    {h}
                  </th>
                ))}
                <th scope="col" className={thCls}>
                  <span className="sr-only">إجراء</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {TEAMS.map(([name, captain, size, rec], i) => {
                const h = !!hidden[i];
                return (
                  <tr key={name} className="border-t border-[#EEF0F5]">
                    <td className="px-4 py-3 font-bold">{toArabicDigits(i + 1)}</td>
                    <td className="px-2 py-3 font-bold">{name}</td>
                    <td className="px-2 py-3">{captain}</td>
                    <td className="px-2 py-3">{toArabicDigits(size)}</td>
                    <td className="px-2 py-3" dir="ltr">
                      {toArabicDigits(rec)}
                    </td>
                    <td className="px-2 py-3">
                      <Pill tone={h ? "bad" : "ok"}>{h ? "مخفي من الترتيب" : "ظاهر"}</Pill>
                    </td>
                    <td className="px-2 py-3 text-left">
                      <button
                        type="button"
                        onClick={() => {
                          setHidden({ ...hidden, [i]: !h });
                          setToast(h ? `${name} رجع للترتيب.` : `${name} اتخفى من الترتيب العام.`);
                        }}
                        className={smallBtn}
                      >
                        {h ? "إظهار" : "إخفاء من الترتيب"}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </section>
      )}

      <ReasonModal request={modal} onClose={() => setModal(null)} />
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
