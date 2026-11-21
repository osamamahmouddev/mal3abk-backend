"use client";

import React from "react";
import { useQuery } from "@tanstack/react-query";
import { Banknote, HandCoins, ReceiptText, Wallet } from "lucide-react";
import { Page, card, muted } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { CommissionTerms } from "@/components/CommissionTerms";
import { api, unwrap } from "@/lib/api";
import { money } from "@/lib/booking-format";

const METHOD_LABEL: Record<string, string> = { InstaPay: "إنستاباي", Wallet: "محفظة كاش", BankTransfer: "تحويل بنكي", Cash: "كاش" };
const STATUS_LABEL: Record<string, string> = { Confirmed: "مؤكد", Completed: "اتلعب", Cancelled: "ملغي", NoShow: "ماجاش" };

const date = (iso: string) => new Date(iso).toLocaleDateString("ar-EG", { day: "numeric", month: "short", year: "numeric", timeZone: "Africa/Cairo" });

function Figure({ label, value, hint, icon: Icon }: { label: string; value: string; hint?: string; icon: React.ComponentType<{ className?: string }> }) {
  return (
    <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1.5 min-w-0`}>
      <span className={`text-[13px] font-semibold ${muted} flex items-center gap-1.5`}>
        <Icon className="size-4 text-[#8A93A3]" />
        {label}
      </span>
      <strong className="text-[22px] font-extrabold truncate">{value}</strong>
      {hint && <span className={`text-xs ${muted}`}>{hint}</span>}
    </div>
  );
}

/**
 * What the platform owes this venue (commission model): its share of the deposits players paid to the platform, after the
 * commission and any refunds, and the transfers the platform already made.
 */
export default function PayoutsPage() {
  const { venueId } = useVenue();
  const statement = useQuery({
    queryKey: ["venue-payouts", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/payouts", { params: { path: { venueId: venueId! } } })),
  });

  const s = statement.data;
  return (
    <Page>
      <div className="flex flex-col gap-1">
        <h1 className="text-[26px] font-bold">مستحقاتك</h1>
        <p className={`text-sm ${muted}`}>
          اللاعب بيدفع العربون لحسابات المنصة. بعد ما الحجز يتأكد بنخصم العمولة، والباقي بنحوّله لك وبيظهر هنا.
        </p>
      </div>

      {statement.isLoading && <div className="h-40 rounded-[18px] skeleton-shimmer" />}
      {statement.isError && (
        <div className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-4 py-3 text-sm">{(statement.error as Error).message}</div>
      )}

      {s && (
        <>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <Figure label="مستحق لك دلوقتي" value={money(s.balance.balance)} icon={HandCoins} hint={s.balance.balance > 0 ? "هيتحوّلك قريب" : "كل حاجة متسوّية"} />
            <Figure label="عرابين وصلت المنصة" value={money(s.balance.depositsReceived)} icon={Wallet} hint={`${s.balance.settledBookings.toLocaleString("ar-EG")} حجز`} />
            <Figure label="عمولة المنصة" value={money(s.balance.commission)} icon={ReceiptText} />
            <Figure label="اتحوّلك" value={money(s.balance.paidOut)} icon={Banknote} />
          </div>

          <section className={`${card} rounded-[18px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[16px] font-bold">التحويلات اللي وصلتك</h2>
            {s.payouts.length === 0 ? (
              <p className={`text-sm ${muted}`}>لسه مفيش تحويلات.</p>
            ) : (
              <ul className="flex flex-col divide-y divide-[#EEF0F5]">
                {s.payouts.map((p) => (
                  <li key={p.id} className="py-2.5 flex items-center justify-between gap-3 text-sm">
                    <span className="flex flex-col">
                      <strong>{money(p.amount)}</strong>
                      <span className={`text-xs ${muted}`}>
                        {METHOD_LABEL[p.method] ?? p.method} · رقم التحويل <span dir="ltr">{p.reference}</span>
                      </span>
                    </span>
                    <span className={`text-xs ${muted}`}>{date(p.paidAtUtc)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className={`${card} rounded-[18px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[16px] font-bold">الحجوزات اللي ورا المستحق</h2>
            {s.bookings.length === 0 ? (
              <p className={`text-sm ${muted}`}>أول ما فريق المنصة يأكد عربون حجز من التطبيق هيظهر هنا.</p>
            ) : (
              <ul className="flex flex-col divide-y divide-[#EEF0F5]">
                {s.bookings.map((b) => (
                  <li key={b.bookingId} className="py-2.5 flex items-center justify-between gap-3 text-sm">
                    <span className="flex flex-col">
                      <span dir="ltr" className="font-mono text-right">{b.reference}</span>
                      <span className={`text-xs ${muted}`}>
                        {date(b.startUtc)} · {STATUS_LABEL[b.status] ?? b.status}
                      </span>
                    </span>
                    <span className="flex flex-col items-end">
                      <strong>{money(b.venueShare)}</strong>
                      <span className={`text-xs ${muted}`}>
                        عربون {money(b.depositReceived)} − عمولة {money(b.commission)}
                        {b.refunded ? ` − اترجع للاعب ${money(b.refunded)}` : ""}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}

      <CommissionTerms compact />
    </Page>
  );
}
