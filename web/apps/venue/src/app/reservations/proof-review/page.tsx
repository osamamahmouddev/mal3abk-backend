"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, card, chip, muted, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Outcome = "approved" | "rejected" | "requested";

interface Proof {
  id: string;
  who: string;
  amount: string;
  ref: string;
  slot: string;
  phone: string;
  total: string;
  method: string;
  claimed: string;
  uploaded: string;
  deadline: string;
  history: string;
}

const QUEUE: Proof[] = [
  { id: "q1", who: "أحمد سامي", amount: "٥٠ ج.م", ref: "KH-2607-0142", slot: "ملعب ١ · السبت ٢٦ يوليو ٨:٠٠ م", phone: "٠١٠١٢٣٤٥٦٧٨", total: "١٥٠ ج.م", method: "فودافون كاش", claimed: "٥٠ ج.م", uploaded: "اليوم ٧:٠٩ م", deadline: "حتى ٩:٠٩ م", history: "أول إيصال لهذا الحجز." },
  { id: "q2", who: "عمر خالد", amount: "٦٠ ج.م", ref: "KH-2607-0145", slot: "ملعب ٢ · السبت ٢٦ يوليو ١١:٠٠ م", phone: "٠١١٤٤٣٣٢٢١١", total: "١٨٠ ج.م", method: "إنستاباي", claimed: "٦٠ ج.م", uploaded: "اليوم ٧:٤٧ م", deadline: "حتى ٩:٤٧ م", history: "إيصال سابق رُفض ٧:٣٠ م — «الصورة غير واضحة»." },
  { id: "q3", who: "هاني سعيد", amount: "٥٠ ج.م", ref: "KH-3107-0107", slot: "ملعب ١ · الخميس ٣١ يوليو ١:٠٠ ص", phone: "٠١٠٤٤٥٥٦٦٧٧", total: "١٥٠ ج.م", method: "فودافون كاش", claimed: "٥٠ ج.م", uploaded: "اليوم ٦:٢٠ م", deadline: "حتى ٨:٢٠ م", history: "أول إيصال لهذا الحجز." },
];

const BADGES: Record<Outcome | "open", [string, string]> = {
  open: ["جديد", "bg-[#FEF3D6] text-[#7A4D00]"],
  approved: ["✓ معتمد", "bg-[#E5F5EB] text-[#1B6E3E]"],
  rejected: ["✕ مرفوض", "bg-[#FDECEA] text-[#A8261C]"],
  requested: ["طُلب جديد", "bg-[#EEF0F5] text-[#3B4352]"],
};

const REASONS: [string, string, string][] = [
  ["amount", "المبلغ ناقص", "المبلغ في الإيصال أقل من العربون المطلوب. حوّل الفرق وارفع الإيصال تاني."],
  ["unclear", "الصورة مش واضحة", "صورة الإيصال مش واضحة. ارفع صورة يظهر فيها المبلغ والتاريخ ورقم العملية."],
  ["notfound", "التحويل ماوصلش", "مالقيناش التحويل ده في حسابنا. اتأكد من الرقم وارفع إيصال صحيح."],
  ["ref", "رقم الحجز مش موجود", "ملاحظة التحويل مافيهاش رقم الحجز. ابعت إيصال فيه الرقم."],
];

interface Notice {
  kind: "ok" | "info";
  text: string;
}

export default function ProofReviewPage() {
  const [selId, setSelId] = useState("q1");
  const detailsRef = useRevealOnSelect<HTMLElement>(selId);
  const [modal, setModal] = useState<"approve" | "reject" | null>(null);
  const [reason, setReason] = useState<string | null>(null);
  const [message, setMessage] = useState("");
  const [outcome, setOutcomes] = useState<Record<string, Outcome>>({});
  const [notice, setNotice] = useState<Notice | null>(null);

  const cur = QUEUE.find((q) => q.id === selId) ?? QUEUE[0];
  const r = REASONS.find((x) => x[0] === reason);
  const pending = QUEUE.filter((q) => !outcome[q.id]).length;
  const isOpen = !outcome[cur.id];

  const setOutcome = (val: Outcome, n: Notice) => {
    setOutcomes({ ...outcome, [cur.id]: val });
    setModal(null);
    setReason(null);
    setNotice(n);
  };

  const details: [string, string, string?][] = [
    ["الملعب والميعاد", cur.slot, "font-semibold"],
    ["الهاتف", cur.phone],
    ["إجمالي الحجز", cur.total],
    ["العربون المطلوب", cur.amount, "font-bold"],
    ["طريقة الدفع", cur.method],
    ["المبلغ حسب اللاعب", cur.claimed],
    ["وقت الرفع", cur.uploaded],
    ["مهلة المراجعة", cur.deadline, "text-[#7A4D00] font-bold"],
  ];

  return (
    <Page className="gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex-[1_1_280px] flex flex-col gap-0.5">
          <span className={`text-[13px] ${muted}`}>
            <Link href="/reservations" className="hover:text-brand-red">
              الحجوزات
            </Link>{" "}
            ‹ إيصالات العربون
          </span>
          <h1 className="text-[26px] font-bold">مراجعة إيصالات العربون</h1>
        </div>
        <span className={`text-[13px] ${muted}`}>المنصة لا تتحقق من التحويلات تلقائياً — راجع حسابك قبل الاعتماد.</span>
      </div>

      {notice && (
        <div
          role="status"
          className={`rounded-xl px-4 py-3 flex gap-3 items-center text-sm font-semibold ${
            notice.kind === "ok" ? "bg-[#E5F5EB] text-[#155C33]" : "bg-[#EEF0F5] text-[#1F2A44]"
          }`}
        >
          <span className="flex-1">{notice.text}</span>
          {notice.kind === "ok" && (
            <Link href="/calendar" className="font-bold text-inherit underline">
              عرض في التقويم
            </Link>
          )}
        </div>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="قائمة الإيصالات" className={`flex-[1_1_280px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
          <div className="px-4 py-3.5 border-b border-[#E4E7EC] flex justify-between items-center">
            <h2 className="text-[15px] font-bold">بانتظار المراجعة</h2>
            <span className={`text-xs ${muted}`}>{toArabicDigits(pending)} إيصالات</span>
          </div>
          {QUEUE.map((q) => {
            const on = q.id === cur.id;
            const b = BADGES[outcome[q.id] ?? "open"];
            return (
              <button
                key={q.id}
                type="button"
                aria-pressed={on}
                onClick={() => {
                  setSelId(q.id);
                  setNotice(null);
                }}
                className={`w-full min-h-[72px] px-4 py-3 border-b border-[#EEF0F5] flex items-center gap-3 ${
                  on ? "bg-[#EEF0F5] shadow-[inset_-4px_0_0_#D63B2F]" : "bg-white"
                }`}
              >
                <span className="w-10 h-[50px] shrink-0 rounded-md bg-[repeating-linear-gradient(135deg,#DCE0E8_0_5px,#EEF0F5_5px_10px)]" />
                <span className="flex-1 min-w-0 flex flex-col gap-0.5 text-right">
                  <span className="text-sm font-bold text-slate-900">
                    {q.who} · {q.amount}
                  </span>
                  <span className={`text-xs ${muted}`}>{q.slot}</span>
                </span>
                <span className={`shrink-0 text-[11px] font-bold rounded-full px-2 py-[3px] ${b[1]}`}>{b[0]}</span>
              </button>
            );
          })}
        </section>

        <section ref={detailsRef} aria-label="تفاصيل الإيصال" className={`scroll-mt-20 flex-[999_1_560px] min-w-0 ${card} rounded-[18px] p-5 flex flex-wrap gap-6`}>
          <div className="flex-[1_1_260px] flex flex-col gap-2.5">
            <div className={`aspect-[3/4] rounded-xl border border-[#E4E7EC] bg-[repeating-linear-gradient(135deg,#E4E7EC_0_8px,#F4F5F7_8px_16px)] flex items-center justify-center font-mono text-xs ${muted}`}>
              PHOTO: transfer receipt
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => setNotice({ kind: "info", text: "الإيصال اتفتح بالحجم الكامل." })}
                className="flex-1 min-h-[40px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
              >
                تكبير
              </button>
              <button
                type="button"
                onClick={() => setNotice({ kind: "info", text: "اتحمّلت صورة الإيصال." })}
                className="flex-1 min-h-[40px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
              >
                تحميل
              </button>
            </div>
          </div>

          <div className="flex-[1.4_1_300px] min-w-0 flex flex-col gap-3.5">
            <div className="flex justify-between items-center gap-2">
              <h2 className="text-xl font-bold">{cur.who}</h2>
              <span dir="ltr" className={`text-[13px] ${muted}`}>
                {cur.ref}
              </span>
            </div>
            <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              {details.map(([k, v, cls]) => (
                <div key={k} className="flex flex-col gap-0.5">
                  <dt className={`text-xs ${muted}`}>{k}</dt>
                  <dd className={cls ?? ""}>{v}</dd>
                </div>
              ))}
            </dl>
            <div className="bg-[#F4F5F7] rounded-[10px] px-3.5 py-3 flex flex-col gap-1.5">
              <span className="text-[13px] font-bold">قبل الاعتماد اتأكد من</span>
              <span className="text-[13px] text-[#333B4A]">• المبلغ وصل فعلاً في المحفظة/الحساب</span>
              <span className="text-[13px] text-[#333B4A]">• رقم الحجز مكتوب في ملاحظة التحويل</span>
              <span className="text-[13px] text-[#333B4A]">• تاريخ التحويل بعد وقت الطلب</span>
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-bold">سجل المراجعة</span>
              <span className={`text-[13px] ${muted}`}>{cur.history}</span>
            </div>
            {isOpen && (
              <div className="flex flex-wrap gap-2 mt-1">
                <button
                  type="button"
                  onClick={() => setModal("approve")}
                  className="flex-[2_1_180px] min-h-[48px] rounded-[10px] bg-navy-900 text-white text-[15px] font-bold"
                >
                  اعتماد العربون وتأكيد الحجز
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setReason(null);
                    setMessage("");
                    setModal("reject");
                  }}
                  className="flex-[1_1_120px] min-h-[48px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] text-[15px] font-bold"
                >
                  رفض
                </button>
                <button
                  type="button"
                  onClick={() =>
                    setOutcome("requested", { kind: "info", text: `اتطلب من ${cur.who} يرفع إيصال جديد. الميعاد محجوز ٤٥ دقيقة.` })
                  }
                  className="flex-[1_1_140px] min-h-[48px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-sm font-semibold"
                >
                  طلب إيصال جديد
                </button>
              </div>
            )}
          </div>
        </section>
      </div>

      {modal === "approve" && (
        <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
          <div role="dialog" aria-modal="true" aria-labelledby="ap-t" className="w-full max-w-[440px] bg-white rounded-2xl p-6 flex flex-col gap-3.5">
            <h2 id="ap-t" className="text-[19px] font-bold">
              اعتماد عربون {cur.amount}؟
            </h2>
            <p className="text-sm leading-[23px] text-[#333B4A]">
              بالاعتماد إنت بتأكد إن التحويل وصل حسابك. الحجز{" "}
              <span dir="ltr" className="font-bold">
                {cur.ref}
              </span>{" "}
              هيتأكد، واللاعب هيوصله إشعار. الإجراء ده بيتسجل باسمك.
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() =>
                  setOutcome("approved", { kind: "ok", text: `تم اعتماد العربون وتأكيد الحجز ${cur.ref} — اتبعت إشعار لـ${cur.who}.` })
                }
                className="min-h-[46px] px-5 rounded-[10px] bg-navy-900 text-white text-[15px] font-bold"
              >
                نعم، اعتمد
              </button>
              <button
                type="button"
                onClick={() => setModal(null)}
                className="min-h-[46px] px-5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold"
              >
                إلغاء
              </button>
            </div>
          </div>
        </div>
      )}

      {modal === "reject" && (
        <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
          <div role="dialog" aria-modal="true" aria-labelledby="rj-t" className="w-full max-w-[480px] bg-white rounded-2xl p-6 flex flex-col gap-3.5">
            <h2 id="rj-t" className="text-[19px] font-bold">
              رفض الإيصال
            </h2>
            <span className="text-sm font-semibold">السبب (مطلوب)</span>
            <div className="flex flex-wrap gap-2">
              {REASONS.map(([id, label, text]) => (
                <button
                  key={id}
                  type="button"
                  aria-pressed={reason === id}
                  onClick={() => {
                    setReason(id);
                    setMessage(text);
                  }}
                  className={chip(reason === id, "min-h-[40px] px-3.5 rounded-[10px] text-[13px]")}
                >
                  {label}
                </button>
              ))}
            </div>
            <label htmlFor="rj-n" className={`text-[13px] ${muted}`}>
              رسالة للاعب
            </label>
            <textarea
              id="rj-n"
              rows={3}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              className="border border-[#C9CFD9] rounded-[10px] px-3 py-2.5 text-sm leading-[22px] resize-none text-slate-900"
            />
            <div className="bg-[#FEF3D6] rounded-[10px] px-3 py-2.5 text-[13px] text-[#6B4400]">
              الميعاد هيفضل محجوز للاعب ٤٥ دقيقة عشان يعيد الرفع، بعدها يتاح.
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={!r}
                onClick={() => r && setOutcome("rejected", { kind: "info", text: `اترفض الإيصال واتبعت السبب لـ${cur.who}: ${r[1]}.` })}
                className={`min-h-[46px] px-5 rounded-[10px] text-[15px] font-bold ${
                  r ? "bg-[#A8261C] text-white" : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
                }`}
              >
                تأكيد الرفض
              </button>
              <button
                type="button"
                onClick={() => setModal(null)}
                className="min-h-[46px] px-5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold"
              >
                إلغاء
              </button>
            </div>
          </div>
        </div>
      )}
    </Page>
  );
}
