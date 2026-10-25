"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, PageHeader, Pill, ReasonModal, ReasonRequest, Tabs, Toast, Tone, card, chip, muted, thCls, toArabicDigits } from "@/components/ui";

// venue, what, payment reference
const PAYMENTS: [string, string, string][] = [
  ["ملاعب الرواد", "تجديد Growth · شهري", "تحويل بنكي CIB · مرجع 88412"],
  ["ملعب الحي — المعادي", "تجديد Starter · شهري · متأخر ٦ أيام", "إنستاباي · مرجع IP-55120"],
  ["ستاد الشباب", "ترقية من Growth لـ Pro · سنوي", "تحويل بنكي NBE · مرجع 30017"],
];
type PayState = "open" | "ok" | "no";
const PAY_STATE: Record<PayState, [string, Tone]> = {
  open: ["مستني تأكيد", "warn"],
  ok: ["اتأكد واتفعّل", "ok"],
  no: ["اترفض", "bad"],
};

type SubState = "active" | "trial" | "overdue" | "cancelled";
// venue, plan, cycle, renewal, status
const SUBS: [string, string, string, string, SubState][] = [
  ["ملعب النجوم — مدينة نصر", "Growth", "شهري", "١٤ أغسطس", "active"],
  ["ملاعب الرواد", "Growth", "شهري", "٢ أغسطس", "active"],
  ["ستاد الشباب", "Pro", "سنوي", "١ سبتمبر", "active"],
  ["أكاديمية الدلتا — فرع ٢", "Starter", "شهري", "٢٠ أغسطس", "trial"],
  ["ملعب الحي — المعادي", "Starter", "شهري", "متأخر ٦ أيام", "overdue"],
  ["جرين فيلد", "Starter", "شهري", "—", "cancelled"],
];
const SUB_STATE: Record<SubState, [string, Tone]> = {
  active: ["نشط", "ok"],
  trial: ["فترة تجربة", "neutral"],
  overdue: ["متأخر", "warn"],
  cancelled: ["ملغي", "bad"],
};

const INVOICES: [string, string, string, "ok" | "warn" | "bad"][] = [
  ["INV-2607-018", "ملعب النجوم — مدينة نصر", "١٤ يوليو – ١٣ أغسطس", "ok"],
  ["INV-2607-016", "ملاعب الرواد", "٢ يوليو – ١ أغسطس", "ok"],
  ["INV-2607-021", "ملعب الحي — المعادي", "١٨ يوليو – ١٧ أغسطس", "warn"],
  ["INV-2606-044", "ستاد الشباب", "سنة من ١ سبتمبر", "ok"],
  ["INV-2606-039", "جرين فيلد", "٣ يونيو – ٢ يوليو", "bad"],
];
const INVOICE_LABEL = { ok: "مدفوعة", warn: "مستحقة", bad: "ملغية" };

const smallBtn = "min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold";

export default function BillingPage() {
  const [tab, setTab] = useState<"pay" | "subs" | "inv">("pay");
  const [pay, setPay] = useState<Record<number, PayState>>({});
  const [subFilter, setSubFilter] = useState<"all" | "active" | "overdue" | "trial">("all");
  const [sub, setSub] = useState<Record<number, SubState>>({});
  const [modal, setModal] = useState<ReasonRequest | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const subOf = (i: number) => sub[i] ?? SUBS[i][4];
  const openPays = PAYMENTS.filter((_, i) => !pay[i]).length;

  const subAction = (i: number) => {
    const [venue, plan, cycle, renew] = SUBS[i];
    const k = subOf(i);
    if (k === "overdue") setToast(`اتبعت تذكير دفع لـ ${venue}.`);
    else if (k === "trial") setToast(`اتمدت تجربة ${venue} ٧ أيام.`);
    else if (k === "cancelled") {
      setSub({ ...sub, [i]: "trial" });
      setToast(`${venue} رجع على فترة تجربة.`);
    } else setToast(`${venue}: ${plan} · ${cycle} · التجديد ${renew}.`);
  };

  return (
    <Page>
      <PageHeader title="الاشتراكات والفواتير" sub="الأسعار في الباقات لسه [السعر] لحد ما تتحدد.">
        <Link href="/plans" className={`${smallBtn} flex items-center`}>
          تعديل الباقات
        </Link>
      </PageHeader>

      <div className="grid gap-3 grid-cols-2 md:grid-cols-[repeat(auto-fit,minmax(min(170px,100%),1fr))]">
        {[
          ["اشتراكات نشطة", toArabicDigits(184), "من ٢٣١ ملعب"],
          ["تجربة مجانية", toArabicDigits(29), "بتخلص خلال ١٤ يوم"],
          ["متأخرة", toArabicDigits(12), "بتتخفى بعد ٧ أيام"],
          ["إيراد الاشتراكات الشهري", "[الإيراد]", "MRR"],
        ].map(([l, v, h]) => (
          <div key={l} className={`${card} rounded-[18px] px-4 py-3.5 flex flex-col gap-1`}>
            <span className={`text-[13px] ${muted}`}>{l}</span>
            <span className="text-[22px] font-bold">{v}</span>
            <span className={`text-xs ${muted}`}>{h}</span>
          </div>
        ))}
      </div>

      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          ["pay", `تحويلات مستنية تأكيد (${toArabicDigits(openPays)})`],
          ["subs", "الاشتراكات"],
          ["inv", "الفواتير"],
        ]}
      />

      {tab === "pay" && (
        <div className="flex flex-col gap-3">
          {PAYMENTS.map(([venue, meta, ref], i) => {
            const k = pay[i] ?? "open";
            return (
              <article key={venue} className={`${card} rounded-[18px] px-[18px] py-4 flex flex-wrap gap-3.5 items-center`}>
                <span className="w-16 h-20 rounded-[10px] shrink-0 bg-[linear-gradient(160deg,#E6F0EA,#CFE3D7)] flex items-center justify-center text-[#1B6E3E] text-[11px] font-bold">
                  إيصال
                </span>
                <span className="flex-[1_1_260px] flex flex-col gap-[3px]">
                  <span className="text-[15px] font-bold">{venue}</span>
                  <span className={`text-[13px] ${muted}`}>{meta}</span>
                  <span className={`text-[13px] ${muted}`}>{ref}</span>
                </span>
                <Pill tone={PAY_STATE[k][1]}>{PAY_STATE[k][0]}</Pill>
                {k === "open" && (
                  <span className="flex gap-1.5">
                    <button
                      type="button"
                      onClick={() =>
                        setModal({
                          title: `رفض تحويل ${venue}؟`,
                          body: "المالك هيتبلّغ بالسبب ويقدر يرفع إيصال جديد.",
                          confirmLabel: "رفض",
                          onConfirm: () => {
                            setPay((prev) => ({ ...prev, [i]: "no" }));
                            setToast("اترفض التحويل واتبلّغ المالك.");
                          },
                        })
                      }
                      className={smallBtn}
                    >
                      رفض
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setPay({ ...pay, [i]: "ok" });
                        setToast(`اتأكد التحويل واتفعّل اشتراك ${venue} واتبعتت الفاتورة.`);
                      }}
                      className="min-h-[38px] px-3.5 rounded-[10px] bg-ink text-white text-[13px] font-bold"
                    >
                      تأكيد وتفعيل
                    </button>
                  </span>
                )}
              </article>
            );
          })}
        </div>
      )}

      {tab === "subs" && (
        <div className="flex flex-col gap-2.5">
          <div className="flex gap-1.5 flex-wrap">
            {(
              [
                ["all", "الكل"],
                ["active", "نشط"],
                ["overdue", "متأخر"],
                ["trial", "تجربة"],
              ] as const
            ).map(([id, label]) => (
              <button key={id} type="button" aria-pressed={subFilter === id} onClick={() => setSubFilter(id)} className={chip(subFilter === id)}>
                {label}
              </button>
            ))}
          </div>
          <section className={`${card} rounded-[20px] overflow-x-auto`}>
            <table className="w-full border-collapse text-sm min-w-[640px]">
              <thead>
                <tr className={`${muted} text-[13px]`}>
                  {["الملعب", "الباقة", "الدورة", "التجديد", "الحالة"].map((h, i) => (
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
                {SUBS.map((r, i) => [r, i] as const)
                  .filter(([, i]) => subFilter === "all" || subOf(i) === subFilter)
                  .map(([[venue, plan, cycle, renew], i]) => {
                    const k = subOf(i);
                    return (
                      <tr key={venue} className="border-t border-[#EEF0F5]">
                        <td className="px-4 py-3 font-bold">{venue}</td>
                        <td className="px-2 py-3">{plan}</td>
                        <td className="px-2 py-3">{cycle}</td>
                        <td className="px-2 py-3">{renew}</td>
                        <td className="px-2 py-3">
                          <Pill tone={SUB_STATE[k][1]}>{SUB_STATE[k][0]}</Pill>
                        </td>
                        <td className="px-2 py-3 text-left">
                          <button type="button" onClick={() => subAction(i)} className={smallBtn}>
                            {k === "overdue" ? "ابعت تذكير" : k === "trial" ? "مد التجربة ٧ أيام" : k === "cancelled" ? "إعادة تفعيل" : "تفاصيل"}
                          </button>
                        </td>
                      </tr>
                    );
                  })}
              </tbody>
            </table>
          </section>
        </div>
      )}

      {tab === "inv" && (
        <section className={`${card} rounded-[20px] overflow-x-auto`}>
          <table className="w-full border-collapse text-sm min-w-[640px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                {["الفاتورة", "الملعب", "الفترة", "المبلغ", "الحالة"].map((h, i) => (
                  <th key={h} scope="col" className={`${thCls} ${i === 0 ? "!px-4" : ""}`}>
                    {h}
                  </th>
                ))}
                <th scope="col" className={thCls}>
                  <span className="sr-only">تحميل</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {INVOICES.map(([id, venue, period, t]) => (
                <tr key={id} className="border-t border-[#EEF0F5]">
                  <td className="px-4 py-3 font-bold" dir="ltr">
                    {id}
                  </td>
                  <td className="px-2 py-3">{venue}</td>
                  <td className="px-2 py-3">{period}</td>
                  <td className="px-2 py-3">[السعر]</td>
                  <td className="px-2 py-3">
                    <Pill tone={t}>{INVOICE_LABEL[t]}</Pill>
                  </td>
                  <td className="px-2 py-3 text-left">
                    <button type="button" onClick={() => setToast(`جاري تحميل ${id}.`)} className={smallBtn}>
                      PDF
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      <ReasonModal request={modal} onClose={() => setModal(null)} />
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
