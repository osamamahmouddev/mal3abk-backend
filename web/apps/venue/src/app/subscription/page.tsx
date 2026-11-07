"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Toast, card, formatEGP, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { api, unwrap } from "@/lib/api";

type Plan = Schemas["PlanDto"];

const STATUS: Record<string, { label: string; tone: "ok" | "warn" | "bad" | "info" }> = {
  Trial: { label: "فترة تجربة", tone: "info" },
  Active: { label: "نشطة", tone: "ok" },
  Grace: { label: "فترة سماح", tone: "warn" },
  Expired: { label: "منتهية", tone: "bad" },
  Suspended: { label: "موقوفة", tone: "bad" },
  Cancelled: { label: "ملغية", tone: "bad" },
};
const FEATURE_NAMES: Record<string, string> = {
  "reports.advanced": "تقارير متقدمة",
  "reports.csv_export": "تصدير CSV",
  "venues.multiple": "أكتر من ملعب",
  "support.priority": "دعم أولوية",
};

const fmtDate = (iso?: string | null) =>
  iso ? new Intl.DateTimeFormat("ar-EG", { dateStyle: "medium", timeZone: "Africa/Cairo" }).format(new Date(iso)) : "—";
const limit = (v?: number | null) => (v === null || v === undefined ? "بلا حدود" : toArabicDigits(v));

export default function SubscriptionPage() {
  const queryClient = useQueryClient();
  const [cycle, setCycle] = useState<"month" | "year">("month");
  const [toast, setToast] = useState<string | null>(null);
  const [requested, setRequested] = useState<{ plan: string; ticket: string } | null>(null);

  const billing = useQuery({ queryKey: ["billing"], queryFn: () => unwrap(api.GET("/api/v1/venue-manager/billing")) });
  const plans = useQuery({ queryKey: ["public-plans"], queryFn: () => unwrap(api.GET("/api/v1/plans")) });

  // No self-service payment: an upgrade is a request to the Malabak team (Flow G), tracked as a support ticket.
  const requestPlan = useMutation({
    mutationFn: (plan: Plan) =>
      unwrap(
        api.POST("/api/v1/venue-manager/support/tickets", {
          body: {
            category: "Subscription",
            subject: `طلب تغيير الباقة إلى ${plan.name}`,
            message: `أرغب في الاشتراك في باقة ${plan.name} (${plan.code}) بدفع ${cycle === "year" ? "سنوي" : "شهري"}. برجاء التواصل لتأكيد الدفع.`,
            relatedBookingId: null,
            relatedVenueId: null,
          },
        }),
      ),
    onSuccess: async (ticket, plan) => {
      setRequested({ plan: plan.name, ticket: ticket.ticket.number });
      await queryClient.invalidateQueries({ queryKey: ["billing"] });
    },
    onError: (e: Error) => setToast(e.message),
  });

  const downloadInvoice = async (invoice: Schemas["InvoiceDto"]) => {
    const response = await fetch(`/venue/bff/api/v1/venue-manager/billing/invoices/${invoice.id}/pdf`, { credentials: "same-origin" });
    if (!response.ok) {
      setToast("تعذّر تحميل الفاتورة.");
      return;
    }
    const url = URL.createObjectURL(await response.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = `${invoice.number}.pdf`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 30_000);
  };

  const data = billing.data;
  const sub = data?.subscription;
  const plan = sub?.plan ?? data?.effectivePlan;
  const status = sub ? STATUS[sub.status] : undefined;

  return (
    <Page className="gap-5">
      <h1 className="text-[26px] font-bold">الاشتراك والفواتير</h1>

      {requested && (
        <div role="status" className="bg-[#FEF3D6] text-[#6B4400] rounded-xl px-4 py-3.5 text-sm">
          <strong>طلبك لباقة {requested.plan} وصل (تذكرة {requested.ticket}).</strong> فريق ملعبك هيتواصل معاك خلال يوم عمل لتأكيد الدفع. باقتك الحالية شغالة عادي لحد
          التفعيل.
        </div>
      )}
      {billing.isLoading && <div className={`${card} rounded-[18px] h-40 animate-pulse`} />}
      {billing.error && (
        <div role="alert" className={`${card} rounded-[18px] p-6 text-sm`}>
          {(billing.error as Error).message}
        </div>
      )}

      {data && (
        <div className="flex flex-wrap gap-4">
          <section className={`flex-[1_1_320px] ${card} rounded-[18px] p-5 flex flex-col gap-3`}>
            <div className="flex items-center gap-2.5">
              <h2 className="text-xl font-bold">{plan ? `باقة ${plan.name}` : "من غير باقة"}</h2>
              {status && <Badge tone={status.tone}>{status.label}</Badge>}
            </div>
            {sub && (
              <span className={`text-sm ${muted}`}>
                {formatEGP(sub.cycle === "Annual" ? sub.plan.annualPrice : sub.plan.monthlyPrice)} / {sub.cycle === "Annual" ? "سنة" : "شهر"} · الفترة بتنتهي {fmtDate(sub.currentPeriodEndUtc)}
              </span>
            )}
            {sub?.status === "Grace" && <span className="text-[13px] text-[#8A5A00]">فترة السماح بتنتهي {fmtDate(sub.graceEndsAtUtc)}. جدّد عشان تقدر تضيف ملاعب وموظفين.</span>}
            {sub && !sub.allowsNewResources && (
              <span className="text-[13px] text-[#A8261C]">مش هتقدر تضيف ملاعب أو موظفين جداد لحد ما الاشتراك يتجدد. الحجوزات الحالية مش بتتلغي.</span>
            )}
            {!sub && plan && <span className={`text-sm ${muted}`}>دي الباقة الافتراضية لحد ما تختار باقة.</span>}
            <span className="text-[13px] text-[#333B4A]">التجديد يدوي: هيوصلك تذكير قبل ٧ أيام ويوم واحد بتعليمات الدفع.</span>
          </section>
          <section className={`flex-[1_1_320px] ${card} rounded-[18px] p-5 flex flex-col gap-3.5`}>
            <h2 className="text-base font-bold">الاستخدام</h2>
            <Usage label="الملاعب" used={data.usage.venues} max={plan?.maxVenues} />
            <Usage label="الملاعب الفرعية" used={data.usage.pitches} max={plan?.maxPitches} />
            <Usage label="أكبر عدد موظفين في ملعب" used={data.usage.maxStaffInOneVenue} max={plan?.maxStaffPerVenue} />
          </section>
        </div>
      )}

      <section className={`${card} rounded-[18px] overflow-hidden`}>
        <div className="px-5 py-4 flex flex-wrap gap-3 items-center justify-between border-b border-[#E4E7EC]">
          <h2 className="text-base font-bold">مقارنة الباقات</h2>
          <div role="group" aria-label="مدة الاشتراك" className="flex bg-[#F4F5F7] rounded-[9px] p-[3px] gap-0.5">
            {([["month", "شهري"], ["year", "سنوي"]] as const).map(([id, label]) => (
              <button
                key={id}
                type="button"
                aria-pressed={cycle === id}
                onClick={() => setCycle(id)}
                className={`min-h-[34px] px-3.5 rounded-[7px] text-[13px] ${cycle === id ? "bg-white text-navy-900 font-bold shadow-[0_1px_2px_rgba(15,27,61,0.12)]" : `bg-transparent ${muted}`}`}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        {plans.data && (
          <div className="grid md:grid-cols-2 xl:grid-cols-3 gap-3 p-4">
            {plans.data.map((p) => {
              const current = plan?.id === p.id;
              const tooSmall =
                !!data &&
                ((p.maxVenues !== null && p.maxVenues !== undefined && data.usage.venues > p.maxVenues) ||
                  (p.maxPitches !== null && p.maxPitches !== undefined && data.usage.pitches > p.maxPitches));
              return (
                <article key={p.id} className={`rounded-[16px] border p-4 flex flex-col gap-2 ${current ? "border-brand-red" : "border-[#E4E7EC]"}`}>
                  <div className="flex items-center gap-2">
                    <strong className="text-[17px] flex-1">{p.name}</strong>
                    {current && <Badge tone="ok">باقتك</Badge>}
                  </div>
                  {p.description && <span className={`text-[13px] ${muted}`}>{p.description}</span>}
                  <strong className="text-[20px]">
                    {formatEGP(cycle === "year" ? p.annualPrice : p.monthlyPrice)} <span className={`text-sm font-normal ${muted}`}>/ {cycle === "year" ? "سنة" : "شهر"}</span>
                  </strong>
                  <ul className="text-[13px] flex flex-col gap-1">
                    <li>الملاعب: {limit(p.maxVenues)}</li>
                    <li>الملاعب الفرعية: {limit(p.maxPitches)}</li>
                    <li>موظفين لكل ملعب: {limit(p.maxStaffPerVenue)}</li>
                    {p.trialDays > 0 && <li>تجربة {toArabicDigits(p.trialDays)} يوم</li>}
                    {p.features.map((f) => (
                      <li key={f}>✓ {FEATURE_NAMES[f] ?? f}</li>
                    ))}
                  </ul>
                  {!current && (
                    <button
                      type="button"
                      className={tooSmall ? secondaryBtn : primaryBtn}
                      disabled={tooSmall || requestPlan.isPending}
                      onClick={() => requestPlan.mutate(p)}
                      title={tooSmall ? "استخدامك الحالي أكبر من حدود الباقة دي" : undefined}
                    >
                      {tooSmall ? "استخدامك أكبر من حدودها" : "اطلب الباقة دي"}
                    </button>
                  )}
                </article>
              );
            })}
          </div>
        )}
        <p className={`px-5 pb-4 text-[13px] ${muted}`}>مفيش دفع أونلاين: بعد الطلب فريق ملعبك بيتواصل معاك بتعليمات الدفع (تحويل، إنستاباي، محفظة أو كاش) وبيفعّل الباقة.</p>
      </section>

      <section className={`${card} rounded-[18px] p-5 flex flex-col gap-2`}>
        <h2 className="text-base font-bold">الفواتير</h2>
        {data && data.invoices.length === 0 && <span className={`text-sm ${muted}`}>مفيش فواتير لسه.</span>}
        {data?.invoices.map((inv) => (
          <div key={inv.id} className="flex flex-wrap gap-2 items-center border-t border-[#EDF0F5] pt-2 text-sm">
            <span dir="ltr" className="font-inter">{inv.number}</span>
            <span className={muted}>{fmtDate(inv.periodStartUtc)} – {fmtDate(inv.periodEndUtc)} · {inv.planName}</span>
            <strong className="ms-auto">{formatEGP(inv.amount)}</strong>
            {inv.status === "Void" ? <Badge tone="bad">ملغية</Badge> : <button type="button" className={secondaryBtn} onClick={() => downloadInvoice(inv)}>تحميل PDF</button>}
          </div>
        ))}
      </section>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function Usage({ label, used, max }: { label: string; used: number; max?: number | null }) {
  const unlimited = max === null || max === undefined;
  const ratio = unlimited ? 0 : Math.min(1, used / Math.max(1, max));
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex justify-between text-sm">
        <span>{label}</span>
        <span className="font-bold">{unlimited ? `${toArabicDigits(used)} (بلا حدود)` : `${toArabicDigits(used)} من ${toArabicDigits(max)}`}</span>
      </div>
      {!unlimited && (
        <div className="h-2 rounded bg-[#E4E7EC] overflow-hidden" role="progressbar" aria-label={label} aria-valuenow={used} aria-valuemin={0} aria-valuemax={max}>
          <div className={`h-full ${ratio >= 0.8 ? "bg-brand-red" : "bg-navy-900"}`} style={{ width: `${Math.round(ratio * 100)}%` }} />
        </div>
      )}
    </div>
  );
}
