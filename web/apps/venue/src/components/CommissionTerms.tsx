"use client";

import React from "react";
import { BadgePercent, Banknote, CalendarCheck, ReceiptText, Smartphone } from "lucide-react";
import { useVenue } from "./Providers";
import { card, muted, toArabicDigits } from "./ui";

type Terms = { commissionType?: string | null; commissionRate?: number | null };

/** The venue's commission in words; the API only sends terms an admin set, otherwise the platform default applies. */
function commissionLabel({ commissionType, commissionRate }: Terms): string {
  const rate = commissionRate != null ? toArabicDigits(String(commissionRate)) : null;
  switch (commissionType) {
    case "Percentage":
      return rate ? `${rate}٪ من سعر الحجز` : "نسبة من سعر الحجز";
    case "PerHourFixed":
      return rate ? `${rate} ج.م عن كل ساعة` : "مبلغ ثابت عن كل ساعة";
    case "Exempt":
      return "ملعبك معفي من العمولة";
    default:
      return "العمولة الافتراضية للمنصة، وفريقنا هيأكدها معاك";
  }
}

/**
 * How a venue earns on Malabak (commission model): players pay the deposit to the platform, the platform reviews it, keeps
 * its commission and settles the rest; the remainder of the price is paid at the venue. Manual bookings carry no commission.
 */
export function CommissionTerms({ compact = false }: { compact?: boolean }) {
  const { venue } = useVenue();
  const terms = (venue ?? {}) as Terms;
  const steps: [React.ComponentType<{ className?: string }>, string][] = [
    [Smartphone, "اللاعب بيحجز من التطبيق وبيدفع العربون لحسابات المنصة (قيمة العربون بيحددها فريق المنصة)."],
    [ReceiptText, "فريق المنصة بيراجع إيصال العربون ويأكد الحجز، وبيوصلك إشعار."],
    [BadgePercent, `بنخصم العمولة من العربون (${commissionLabel(terms)})، والباقي بيتحوّل لك وتتابعه من صفحة مستحقاتك.`],
    [Banknote, "باقي سعر الحجز اللاعب بيدفعه لك في الملعب."],
    [CalendarCheck, "الحجوزات اليدوي اللي بتسجلها بنفسك من غير عمولة."],
  ];
  return (
    <section className={`${card} rounded-[18px] ${compact ? "p-4" : "p-5"} flex flex-col gap-3`} aria-labelledby="commission-title">
      <div className="flex items-center gap-2.5">
        <span className="grid place-items-center size-9 rounded-xl bg-[#FDECEA] text-brand-red shrink-0">
          <BadgePercent className="size-[18px]" />
        </span>
        <div className="flex flex-col">
          <h2 id="commission-title" className="text-[15px] font-bold">
            إزاي بتكسب مع ملعبك
          </h2>
          <span className={`text-xs ${muted}`}>من غير اشتراك شهري: بتدفع عمولة على الحجوزات اللي بتيجي من التطبيق بس.</span>
        </div>
      </div>
      <ol className="flex flex-col gap-2">
        {steps.map(([Icon, text], i) => (
          <li key={i} className="flex items-start gap-2.5 text-sm text-slate-800">
            <Icon className="size-4 shrink-0 mt-0.5 text-[#8A93A3]" />
            <span>{text}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}
