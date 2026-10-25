"use client";

import React, { useState } from "react";
import { Badge, Page, Toast, card, muted, redShadow } from "@/components/ui";

type PlanId = "starter" | "growth" | "pro";

const CURRENT: PlanId = "growth";
const RANK: Record<PlanId, number> = { starter: 0, growth: 1, pro: 2 };
const PLANS: [string, PlanId][] = [
  ["Starter", "starter"],
  ["Growth", "growth"],
  ["Pro", "pro"],
];
const USAGE: [string, number, number, string][] = [
  ["الملاعب", 3, 5, "٣ من ٥"],
  ["الفروع", 1, 2, "١ من ٢"],
  ["حسابات الموظفين", 2, 5, "٢ من ٥"],
];
const FEATURES: [string, string, string, string][] = [
  ["عدد الفروع", "١", "٢", "حسب الاتفاق"],
  ["عدد الملاعب", "٢", "٥", "حسب الاتفاق"],
  ["إدارة الحجوزات والعربون", "✓", "✓", "✓"],
  ["الجداول الأسبوعية", "أساسية", "متقدمة", "متقدمة"],
  ["التقارير", "أساسية", "قياسية", "متقدمة"],
  ["حسابات الموظفين", "١", "٥", "١٥"],
  ["تصدير Excel/CSV", "—", "✓", "✓"],
  ["دعم أولوية", "—", "اختياري", "✓"],
];
const INVOICES: [string, string, string, boolean][] = [
  ["INV-2607-018", "١٤ يوليو – ١٣ أغسطس", "Growth", true],
  ["INV-2606-011", "١٤ يونيو – ١٣ يوليو", "Growth", true],
  ["INV-2605-004", "١٤ مايو – ١٣ يونيو", "Starter (تجربة)", false],
];

export default function SubscriptionPage() {
  const [cycle, setCycle] = useState<"month" | "year">("month");
  const [pick, setPick] = useState<PlanId | null>(null);
  const [request, setRequest] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const per = cycle === "month" ? "[السعر] / شهر" : "[السعر] / سنة";
  const target = PLANS.find((p) => p[1] === pick);
  const isDown = !!target && RANK[target[1]] < RANK[CURRENT];

  return (
    <Page className="gap-5">
      <h1 className="text-[26px] font-bold">الاشتراك والفواتير</h1>

      {request && (
        <div role="status" className="bg-[#FEF3D6] text-[#6B4400] rounded-xl px-4 py-3.5 flex gap-3 items-center text-sm">
          <span className="flex-1">
            <strong>طلب الترقية لباقة {request} قيد المراجعة.</strong> فريق ملعبك هيتواصل معاك خلال يوم عمل لتأكيد الدفع. باقتك الحالية شغالة
            عادي لحد التفعيل.
          </span>
          <button
            type="button"
            onClick={() => setRequest(null)}
            className="min-h-[38px] px-3.5 rounded-[9px] border border-[#D9A21B] bg-transparent text-[#6B4400] text-[13px] font-semibold"
          >
            إلغاء الطلب
          </button>
        </div>
      )}

      <div className="flex flex-wrap gap-4">
        <section className={`flex-[1_1_320px] ${card} rounded-[18px] p-5 flex flex-col gap-3`}>
          <div className="flex items-center gap-2.5">
            <h2 className="text-xl font-bold">باقة Growth</h2>
            <Badge tone="ok">✓ نشطة</Badge>
          </div>
          <span className={`text-sm ${muted}`}>[السعر] ج.م / شهرياً · تتجدد ١٤ أغسطس ٢٠٢٦</span>
          <span className="text-[13px] text-[#333B4A]">
            التجديد يدوي: هيوصلك تذكير قبل ٧ أيام بتعليمات الدفع. لو الاشتراك انتهى، الحجوزات الحالية مش بتتلغي.
          </span>
        </section>
        <section className={`flex-[1_1_320px] ${card} rounded-[18px] p-5 flex flex-col gap-3.5`}>
          <h2 className="text-base font-bold">الاستخدام</h2>
          {USAGE.map(([label, used, max, val]) => (
            <div key={label} className="flex flex-col gap-1.5">
              <div className="flex justify-between text-sm">
                <span>{label}</span>
                <span className="font-bold">{val}</span>
              </div>
              <div className="h-2 rounded bg-[#E4E7EC] overflow-hidden">
                <div className={`h-full ${used / max >= 0.8 ? "bg-brand-red" : "bg-navy-900"}`} style={{ width: `${Math.round((used / max) * 100)}%` }} />
              </div>
            </div>
          ))}
        </section>
      </div>

      <section className={`${card} rounded-[18px] overflow-hidden`}>
        <div className="px-5 py-4 flex flex-wrap gap-3 items-center justify-between border-b border-[#E4E7EC]">
          <h2 className="text-base font-bold">مقارنة الباقات</h2>
          <div role="group" aria-label="مدة الاشتراك" className="flex bg-[#F4F5F7] rounded-[9px] p-[3px] gap-0.5">
            {(
              [
                ["month", "شهري"],
                ["year", "سنوي"],
              ] as const
            ).map(([id, label]) => (
              <button
                key={id}
                type="button"
                aria-pressed={cycle === id}
                onClick={() => setCycle(id)}
                className={`min-h-[34px] px-3.5 rounded-[7px] text-[13px] ${
                  cycle === id ? "bg-white text-navy-900 font-bold shadow-[0_1px_2px_rgba(15,27,61,0.12)]" : `bg-transparent ${muted}`
                }`}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] border-collapse text-sm">
            <thead>
              <tr>
                <th className={`px-5 py-4 text-right font-semibold ${muted} text-[13px] w-[28%]`}>الميزة</th>
                {PLANS.map(([name, id]) => (
                  <th key={id} className={`px-3 py-4 text-center ${id === CURRENT ? "bg-[#F8F9FB] shadow-[inset_0_3px_0_#0F1B3D]" : ""}`}>
                    <div className="flex flex-col gap-1 items-center">
                      <span className="text-[17px] font-bold">{name}</span>
                      <span className={`text-[13px] font-medium ${muted}`}>{per}</span>
                      {id === CURRENT && (
                        <span className="text-[11px] font-bold bg-navy-900 text-white rounded-full px-2 py-0.5">باقتك الحالية</span>
                      )}
                    </div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {FEATURES.map(([label, a, b, c]) => (
                <tr key={label} className="border-t border-[#EEF0F5]">
                  <td className="px-5 py-3 font-semibold">{label}</td>
                  <td className="p-3 text-center text-[#333B4A]">{a}</td>
                  <td className="p-3 text-center text-[#333B4A] bg-[#F8F9FB]">{b}</td>
                  <td className="p-3 text-center text-[#333B4A]">{c}</td>
                </tr>
              ))}
              <tr className="border-t border-[#EEF0F5]">
                <td className="px-5 py-4" />
                {PLANS.map(([, id]) => {
                  const cur = id === CURRENT;
                  const up = RANK[id] > RANK[CURRENT];
                  return (
                    <td key={id} className="px-3 py-4 text-center">
                      <button
                        type="button"
                        disabled={cur}
                        onClick={() => !cur && setPick(id)}
                        className={`min-h-[42px] px-[18px] rounded-[10px] text-sm font-bold ${
                          cur
                            ? `bg-[#EEF0F5] ${muted} cursor-default`
                            : up
                              ? `bg-brand-red hover:bg-brand-red-hover text-white ${redShadow}`
                              : "bg-white text-navy-900 border border-[#C9CFD9]"
                        }`}
                      >
                        {cur ? "باقتك الحالية" : up ? "اطلب الترقية" : "اطلب التخفيض"}
                      </button>
                    </td>
                  );
                })}
              </tr>
            </tbody>
          </table>
        </div>
        <div className={`px-5 py-3 text-xs ${muted} border-t border-[#EEF0F5]`}>
          الأسعار والحدود دي مبدئية لحد ما تتحدد تجارياً. الدفع حالياً يدوي (تحويل بنكي أو محفظة) ويتأكد من فريق ملعبك.
        </div>
      </section>

      <section className={`${card} rounded-[18px] overflow-hidden`}>
        <h2 className="px-5 py-4 text-base font-bold border-b border-[#E4E7EC]">سجل الفواتير</h2>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] border-collapse text-sm">
            <thead>
              <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
                <th className="px-5 py-2.5 font-semibold">الفاتورة</th>
                <th className="px-3 py-2.5 font-semibold">الفترة</th>
                <th className="px-3 py-2.5 font-semibold">الباقة</th>
                <th className="px-3 py-2.5 font-semibold">الحالة</th>
                <th className="px-5 py-2.5 font-semibold" />
              </tr>
            </thead>
            <tbody>
              {INVOICES.map(([id, period, plan, paid]) => (
                <tr key={id} className="border-t border-[#EEF0F5]">
                  <td dir="ltr" className="px-5 py-3 text-right">
                    {id}
                  </td>
                  <td className="p-3">{period}</td>
                  <td className="p-3">{plan}</td>
                  <td className="p-3">{paid ? <Badge tone="ok">✓ مدفوعة</Badge> : <Badge tone="wait">مجانية</Badge>}</td>
                  <td className="px-5 py-3">
                    {paid && (
                      <button
                        type="button"
                        onClick={() => setToast("جاري تحميل الفاتورة PDF.")}
                        className="min-h-[36px] px-2.5 bg-transparent text-navy-900 text-[13px] font-semibold underline"
                      >
                        تحميل PDF
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {target && (
        <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
          <div role="dialog" aria-modal="true" aria-labelledby="up-t" className="w-full max-w-[460px] bg-white rounded-2xl p-6 flex flex-col gap-3.5">
            <h2 id="up-t" className="text-[19px] font-bold">
              {isDown ? `طلب التخفيض لباقة ${target[0]}` : `طلب الترقية لباقة ${target[0]}`}
            </h2>
            <p className="text-sm leading-[23px] text-[#333B4A]">
              {isDown
                ? "باقة Starter بتسمح بملعبين بس. عندك ٣ ملاعب، فلازم توقف ملعب منهم قبل التخفيض. الحجوزات الحالية مش هتتأثر."
                : "الدفع لسه يدوي في النسخة دي — مفيش خصم تلقائي من كارت."}
            </p>
            <ol className="list-decimal ps-5 text-sm leading-6 text-[#333B4A]">
              <li>ابعت الطلب من هنا</li>
              <li>فريق ملعبك هيبعتلك تفاصيل التحويل</li>
              <li>بعد ما نأكد الدفع، الباقة تتفعّل وتوصلك فاتورة</li>
            </ol>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => {
                  setRequest(target[0]);
                  setPick(null);
                }}
                className="min-h-[46px] px-5 rounded-[10px] bg-navy-900 text-white text-[15px] font-bold"
              >
                ابعت الطلب
              </button>
              <button
                type="button"
                onClick={() => setPick(null)}
                className="min-h-[46px] px-5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold"
              >
                إلغاء
              </button>
            </div>
          </div>
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
