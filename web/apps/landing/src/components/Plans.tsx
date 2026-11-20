"use client";

import React from "react";
import { useLandingState } from "./LandingState";
import { Check } from "lucide-react";

/**
 * Venues pay no subscription: the platform earns a commission on bookings that come through the app. The rate is agreed
 * per venue (a percentage of the booking or a fixed amount per hour) and manual bookings carry none.
 */
const TERMS: { name: string; audience: string; headline: string; unit: string; items: string[]; hot: boolean }[] = [
  {
    name: "من غير اشتراك",
    audience: "تسجيل ملعبك ولوحة التحكم",
    headline: "٠",
    unit: "ج.م / شهرياً",
    items: ["تقويم وحجوزات لكل ملاعبك", "حسابات موظفين بالصلاحيات", "أسعار ذروة وخصومات", "تقارير وإيرادات"],
    hot: false,
  },
  {
    name: "عمولة على حجوزات التطبيق",
    audience: "بتدفع بس لما اللاعب يحجز من التطبيق",
    headline: "نسبة أو مبلغ بالساعة",
    unit: "حسب الاتفاق مع ملعبك",
    items: [
      "اللاعب بيدفع العربون لحسابات المنصة",
      "فريقنا بيراجع الإيصال ويأكد الحجز",
      "بنخصم العمولة من العربون والباقي بيتحوّل لك",
      "باقي سعر الحجز بيتدفع لك في الملعب",
    ],
    hot: true,
  },
  {
    name: "حجوزاتك اليدوي",
    audience: "الحجز اللي بتسجله بنفسك",
    headline: "٠",
    unit: "عمولة",
    items: ["على نفس التقويم مع حجوزات التطبيق", "التعارض بيتمنع أوتوماتيك", "من غير أي رسوم"],
    hot: false,
  },
];

export function Plans() {
  const { choosePlan } = useLandingState();

  return (
    <>
      <div className="center" style={{ marginTop: 72 }}>
        <h3 style={{ fontSize: 28, margin: "0 0 8px" }}>بتكسب إزاي مع ملعبك</h3>
        <p className="muted" style={{ margin: 0, fontSize: 15 }}>
          من غير اشتراك شهري ولا باقات: عمولة على الحجوزات اللي بتيجي من التطبيق بس.
        </p>
      </div>

      <div className="plans">
        {TERMS.map((p) => (
          <div key={p.name} className={`card plan${p.hot ? " hot" : ""}`}>
            <div className="plan-head">
              <b>{p.name}</b>
            </div>
            <p className="muted" style={{ margin: 0, fontSize: 14 }}>
              {p.audience}
            </p>
            <span className="plan-price">
              <b>{p.headline}</b>{" "}
              <span className="muted" style={{ fontSize: 14 }}>
                {p.unit}
              </span>
            </span>
            {p.items.map((item) => (
              <div key={item} className="li">
                <span aria-hidden="true">
                  <Check size={16} strokeWidth={2.75} />
                </span>
                <span>{item}</span>
              </div>
            ))}
            {p.hot && (
              <a href="#join" className="plan-cta" onClick={() => choosePlan("Commission")}>
                سجّل ملعبك
              </a>
            )}
          </div>
        ))}
      </div>
      <p className="center muted" style={{ fontSize: 13, marginTop: 18 }}>
        العربون دايماً بيغطي عمولة المنصة على الأقل. الدفع الإلكتروني المباشر من التطبيق جاي قريب.
      </p>
    </>
  );
}
