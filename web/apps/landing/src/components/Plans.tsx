"use client";

import React, { useState } from "react";
import { useLandingState } from "./LandingState";

const PLANS: { name: string; audience: string; monthlyPrice: number; annualPrice: number; items: string[]; hot: boolean }[] = [
  {
    name: "Starter",
    audience: "ملعب واحد بيبدأ",
    monthlyPrice: 0,
    annualPrice: 0,
    items: ["فرع واحد وملعبين", "تقويم وحجوزات وعربون", "حساب موظف واحد", "تقارير أساسية"],
    hot: false,
  },
  {
    name: "Growth",
    audience: "الأنسب لأغلب الملاعب",
    monthlyPrice: 499,
    annualPrice: 4800,
    items: ["فرعين و٥ ملاعب", "أسعار ذروة وويك إند", "٥ حسابات موظفين", "تصدير Excel وتقارير كاملة", "حجوزات ثابتة"],
    hot: true,
  },
  {
    name: "Pro",
    audience: "سلاسل وأكاديميات",
    monthlyPrice: 999,
    annualPrice: 9600,
    items: ["فروع وملاعب حسب الاتفاق", "١٥ حساب موظف", "تقارير متقدمة", "دعم أولوية"],
    hot: false,
  },
];

export function Plans() {
  const [cycle, setCycle] = useState<"month" | "year">("month");
  const { plan, choosePlan } = useLandingState();

  return (
    <>
      <div className="center" style={{ marginTop: 72 }}>
        <h3 style={{ fontSize: 28, margin: "0 0 16px" }}>باقات الملاعب</h3>
        <div className="toggle" role="group" aria-label="مدة الاشتراك">
          {(
            [
              ["month", "شهري"],
              ["year", "سنوي — شهرين مجاناً"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" className="seg" aria-pressed={cycle === id} onClick={() => setCycle(id)}>
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="plans">
        {PLANS.map((p) => (
          <div key={p.name} className={`card plan${p.hot ? " hot" : ""}`}>
            <div className="plan-head">
              <b>{p.name}</b>
              {p.hot && (
                <span className="pill" style={{ background: "var(--gold)", color: "var(--navy)" }}>
                  الأكتر طلباً
                </span>
              )}
            </div>
            <p className="muted" style={{ margin: 0, fontSize: 14 }}>
              {p.audience}
            </p>
            <span className="plan-price">
              {p.monthlyPrice === 0 ? (
                <b>مجاناً</b>
              ) : (
                <>
                  <b>{(cycle === "month" ? p.monthlyPrice : p.annualPrice).toLocaleString("ar-EG")}</b>{" "}
                  <span className="muted" style={{ fontSize: 14 }}>
                    {cycle === "month" ? "ج.م / شهرياً" : "ج.م / سنوياً"}
                  </span>
                </>
              )}
            </span>
            {p.items.map((item) => (
              <div key={item} className="li">
                <span aria-hidden="true">✓</span>
                <span>{item}</span>
              </div>
            ))}
            <a href="#join" className="plan-cta" onClick={() => choosePlan(p.name)}>
              {plan === p.name ? `✓ اخترت ${p.name}` : "ابدأ تجربة مجانية"}
            </a>
          </div>
        ))}
      </div>
      <p className="center muted" style={{ fontSize: 13, marginTop: 18 }}>
        ١٤ يوم تجربة مجانية على أي باقة. الأسعار النهائية هتتعلن قبل الإطلاق.
      </p>
    </>
  );
}
