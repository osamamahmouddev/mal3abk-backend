"use client";

import React, { useState } from "react";
import { Page, PageHeader, Tone, TONE, btnOutline, card, muted } from "@/components/ui";

type Missing = "cr" | "lease" | null;
type State = "new" | "missing" | "approved" | "rejected" | "info";

interface Application {
  id: string;
  name: string;
  meta: string;
  owner: string;
  address: string;
  pitches: string;
  plan: string;
  missing: Missing;
}

const APPLICATIONS: Application[] = [
  { id: "v1", name: "ملاعب الرواد", meta: "التجمع الخامس · قدّم من يومين", owner: "م. خالد عبد الرحمن", address: "شارع التسعين الشمالي، التجمع الخامس", pitches: "٤ ملاعب (٥ و٧ لاعب)", plan: "Growth", missing: null },
  { id: "v2", name: "نادي الشباب", meta: "مدينة نصر · قدّم امبارح", owner: "أ. وليد فاروق", address: "شارع الطيران، مدينة نصر", pitches: "٢ ملعب (٥ لاعب)", plan: "Starter", missing: "lease" },
  { id: "v3", name: "ستاد الحرية", meta: "شبرا · قدّم من ٤ أيام", owner: "أ. رامي نصر", address: "شارع شبرا الرئيسي", pitches: "٣ ملاعب (٥ لاعب)", plan: "Starter", missing: null },
  { id: "v4", name: "ملعب الفيروز", meta: "المعادي · قدّم من ٥ أيام", owner: "م. نادين سليم", address: "شارع ٩، المعادي", pitches: "١ ملعب (٧ لاعب)", plan: "تجربة مجانية", missing: "cr" },
];

const STATES: Record<State, [string, Tone]> = {
  new: ["جديد", "warn"],
  missing: ["ناقص مستند", "neutral"],
  approved: ["✓ تمت الموافقة", "ok"],
  rejected: ["✕ مرفوض", "bad"],
  info: ["مطلوب معلومات", "neutral"],
};

const docsFor = (missing: Missing): [string, boolean][] => [
  ["سجل تجاري أو بطاقة ضريبية", missing !== "cr"],
  ["عقد ملكية أو إيجار المكان", missing !== "lease"],
  ["صورة بطاقة المالك", true],
  ["صور الملاعب (٤ على الأقل)", true],
];

const ACTIONS = {
  reject: {
    title: "سبب الرفض (مطلوب)",
    text: "المكان مش مطابق للصور المرفوعة، والعنوان على الخريطة في منطقة سكنية.",
    cta: "تأكيد الرفض",
    btn: "bg-[#A8261C] text-white",
    outcome: "rejected" as State,
  },
  info: {
    title: "المطلوب من المالك",
    text: "محتاجين صورة واضحة من عقد الإيجار ساري المفعول.",
    cta: "إرسال الطلب",
    btn: "bg-navy-900 text-white",
    outcome: "info" as State,
  },
};

export default function ApprovalsPage() {
  const [selId, setSelId] = useState("v1");
  const [action, setAction] = useState<keyof typeof ACTIONS | null>(null);
  const [message, setMessage] = useState("");
  const [outcome, setOutcome] = useState<Record<string, State>>({});
  const [notice, setNotice] = useState<string | null>(null);

  const stateOf = (v: Application): State => outcome[v.id] ?? (v.missing ? "missing" : "new");
  const cur = APPLICATIONS.find((v) => v.id === selId) ?? APPLICATIONS[0];
  const [curLabel, curTone] = STATES[stateOf(cur)];
  const isOpen = !outcome[cur.id];
  const blocked = !!cur.missing;
  const A = action ? ACTIONS[action] : null;

  const finish = (val: State, text: string) => {
    setOutcome({ ...outcome, [cur.id]: val });
    setAction(null);
    setNotice(text);
  };

  const openAction = (a: keyof typeof ACTIONS) => {
    setAction(a);
    setMessage(ACTIONS[a].text);
  };

  return (
    <Page>
      <PageHeader
        title="موافقات الملاعب"
        sub="الموافقة بتسمح للمالك ينشر ملعبه. الظهور للاعبين بيتحكم فيه المالك بعد إكمال المواعيد والأسعار."
      />

      {notice && (
        <div role="status" className="bg-[#EEF0F5] rounded-xl px-4 py-3 text-sm font-semibold text-[#1F2A44]">
          {notice}
        </div>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="طلبات التسجيل" className={`flex-[1_1_300px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
          {APPLICATIONS.map((v) => {
            const on = v.id === cur.id;
            const [label, tone] = STATES[stateOf(v)];
            return (
              <button
                key={v.id}
                type="button"
                aria-pressed={on}
                onClick={() => {
                  setSelId(v.id);
                  setAction(null);
                  setNotice(null);
                }}
                className={`w-full min-h-[68px] px-4 py-2.5 border-b border-[#EEF0F5] flex items-center gap-3 ${
                  on ? "bg-[#EEF0F5] shadow-[inset_-4px_0_0_#D63B2F]" : "bg-white"
                }`}
              >
                <span className="flex-1 min-w-0 flex flex-col gap-0.5 text-right">
                  <span className="text-sm font-bold text-slate-900">{v.name}</span>
                  <span className={`text-xs ${muted}`}>{v.meta}</span>
                </span>
                <span className={`shrink-0 text-[11px] font-bold rounded-full px-2 py-[3px] ${TONE[tone]}`}>{label}</span>
              </button>
            );
          })}
        </section>

        <section className={`flex-[999_1_560px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-[18px]`}>
          <div className="flex flex-wrap justify-between gap-2 items-center">
            <div className="flex flex-col gap-0.5">
              <h2 className="text-[21px] font-bold">{cur.name}</h2>
              <span className={`text-[13px] ${muted}`}>{cur.meta}</span>
            </div>
            <span className={`text-xs font-bold rounded-full px-3 py-1 ${TONE[curTone]}`}>{curLabel}</span>
          </div>

          <div className="grid gap-x-5 gap-y-3 grid-cols-[repeat(auto-fit,minmax(180px,1fr))] text-sm">
            {[
              ["المالك", cur.owner, "font-semibold"],
              ["العنوان", cur.address, ""],
              ["الملاعب", cur.pitches, ""],
              ["الباقة المطلوبة", cur.plan, ""],
            ].map(([k, v, cls]) => (
              <div key={k} className="flex flex-col gap-0.5">
                <span className={`text-xs ${muted}`}>{k}</span>
                <span className={cls}>{v}</span>
              </div>
            ))}
          </div>

          <div className="grid grid-cols-4 gap-2">
            {["PHOTO", "PHOTO", "PHOTO", "MAP"].map((l, i) => (
              <div
                key={i}
                className={`aspect-[4/3] rounded-[10px] bg-[repeating-linear-gradient(135deg,#E4E7EC_0_6px,#F4F5F7_6px_12px)] flex items-center justify-center font-mono text-[10px] ${muted}`}
              >
                {l}
              </div>
            ))}
          </div>

          <div className="flex flex-col gap-2">
            <h3 className="text-[15px] font-bold">المستندات</h3>
            {docsFor(cur.missing).map(([label, ok]) => (
              <div key={label} className="min-h-[48px] px-3.5 py-2 rounded-[10px] bg-[#F8F9FB] flex items-center gap-3">
                <span
                  className={`w-[26px] h-[26px] rounded-full flex items-center justify-center text-[13px] font-bold ${
                    ok ? "bg-[#E5F5EB] text-[#1B6E3E]" : "bg-[#FDECEA] text-[#A8261C]"
                  }`}
                >
                  {ok ? "✓" : "!"}
                </span>
                <span className="flex-1 text-sm">{label}</span>
                {ok ? (
                  <button
                    type="button"
                    onClick={() => setNotice(`اتفتح المستند: ${label}`)}
                    className="min-h-[32px] px-2 bg-transparent text-ink text-[13px] font-semibold underline"
                  >
                    عرض
                  </button>
                ) : (
                  <span className="text-xs font-bold text-[#A8261C]">ناقص</span>
                )}
              </div>
            ))}
          </div>

          {isOpen && !A && (
            <>
              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  disabled={blocked}
                  onClick={() => !blocked && finish("approved", `تمت الموافقة على ${cur.name}. المالك وصله إشعار يكمّل الإعداد وينشر.`)}
                  className={`min-h-[46px] px-[22px] rounded-[10px] text-sm font-bold ${
                    blocked ? "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed" : "bg-ink text-white"
                  }`}
                >
                  موافقة
                </button>
                <button type="button" onClick={() => openAction("info")} className={`${btnOutline} !min-h-[46px] !rounded-[10px]`}>
                  طلب معلومات إضافية
                </button>
                <button
                  type="button"
                  onClick={() => openAction("reject")}
                  className="min-h-[46px] px-[18px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-bold"
                >
                  رفض
                </button>
              </div>
              {blocked && <span className="text-xs text-[#A8261C]">مينفعش الموافقة قبل اكتمال المستندات المطلوبة.</span>}
            </>
          )}

          {isOpen && A && (
            <div className="border border-[#E4E7EC] rounded-xl p-3.5 flex flex-col gap-2.5">
              <label htmlFor="note" className="text-sm font-bold">
                {A.title}
              </label>
              <textarea
                id="note"
                rows={3}
                value={message}
                onChange={(e) => setMessage(e.target.value)}
                className="border border-[#C9CFD9] rounded-[10px] px-3 py-2.5 text-sm leading-[22px] resize-none text-slate-900"
              />
              <span className={`text-xs ${muted}`}>الرسالة هتوصل للمالك بالإيميل وفي اللوحة، والإجراء هيتسجل في سجل التدقيق باسمك.</span>
              <div className="flex gap-2">
                <button
                  type="button"
                  disabled={!message.trim()}
                  onClick={() =>
                    finish(
                      A.outcome,
                      A.outcome === "rejected" ? `اترفض طلب ${cur.name} والسبب اتبعت للمالك.` : `اتبعت طلب المعلومات لمالك ${cur.name}.`
                    )
                  }
                  className={`min-h-[44px] px-[18px] rounded-[10px] text-sm font-bold disabled:opacity-50 ${A.btn}`}
                >
                  {A.cta}
                </button>
                <button type="button" onClick={() => setAction(null)} className={`${btnOutline} !rounded-[10px]`}>
                  إلغاء
                </button>
              </div>
            </div>
          )}
        </section>
      </div>
    </Page>
  );
}
