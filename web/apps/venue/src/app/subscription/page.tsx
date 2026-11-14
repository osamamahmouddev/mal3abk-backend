"use client";

import React, { useState, useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Toast, card, formatEGP, inputClass, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { api, unwrap } from "@/lib/api";
import { uploadImage } from "@/lib/upload";
import { InvoiceModal } from "@/components/invoice-modal";

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
  const [viewInvoice, setViewInvoice] = useState<Schemas["InvoiceDto"] | null>(null);

  const [receiptMediaId, setReceiptMediaId] = useState<string | null>(null);
  const [receiptFileName, setReceiptFileName] = useState<string | null>(null);
  const [transferNotes, setTransferNotes] = useState("");
  const [transferReference, setTransferReference] = useState("");
  const [isUploadingReceipt, setIsUploadingReceipt] = useState(false);
  const [isEditingProof, setIsEditingProof] = useState(false);

  const billing = useQuery({ queryKey: ["billing"], queryFn: () => unwrap(api.GET("/api/v1/venue-manager/billing")) });
  const plans = useQuery({
    queryKey: ["public-plans"],
    queryFn: () => unwrap(api.GET("/api/v1/plans")),
    staleTime: 30_000,
  });

  const data = billing.data;
  const sub = data?.subscription;
  const plan = sub?.plan ?? data?.effectivePlan;
  const status = sub ? STATUS[sub.status] : undefined;
  const pendingReq = data?.pendingRequest;

  useEffect(() => {
    if (pendingReq) {
      if (pendingReq.transferNotes && !transferNotes) {
        setTransferNotes(pendingReq.transferNotes);
      }
      if (pendingReq.transferReference && !transferReference) {
        setTransferReference(pendingReq.transferReference);
      }
      if (pendingReq.paymentProofMediaId && !receiptMediaId) {
        setReceiptMediaId(pendingReq.paymentProofMediaId);
      }
    }
  }, [pendingReq]);

  const reportPayment = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/billing/requests/report-payment", {
          body: {
            paymentProofMediaId: receiptMediaId || pendingReq?.paymentProofMediaId || null,
            transferReference: transferReference.trim() || null,
            transferNotes: transferNotes.trim() || null,
          },
        }),
      ),
    onSuccess: async () => {
      setToast("تم إرسال إيصال السداد بنجاح! الإدارة تراجع الطلب وتفعّل الباقة فوراً.");
      setIsEditingProof(false);
      await queryClient.invalidateQueries({ queryKey: ["billing"] });
    },
    onError: (e: Error) => setToast(e.message),
  });

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      setIsUploadingReceipt(true);
      const mediaId = await uploadImage(file, 3 as Schemas["MediaPurpose"]);
      setReceiptMediaId(mediaId);
      setReceiptFileName(file.name);
      setToast("تم رفع صورة الإيصال بنجاح. كمّل باقي البيانات واضغط تأكيد التحويل.");
    } catch (err: any) {
      setToast(err.message || "فشل رفع صورة الإيصال.");
    } finally {
      setIsUploadingReceipt(false);
    }
  };

  const requestPlan = useMutation({
    mutationFn: (plan: Plan) =>
      unwrap(
        api.POST("/api/v1/venue-manager/billing/requests", {
          body: {
            planId: plan.id,
            cycle: (cycle === "year" ? 1 : 0) as Schemas["BillingCycle"],
            notes: `طلب ترقية إلى باقة ${plan.name} بدفع ${cycle === "year" ? "سنوي" : "شهري"}.`,
          },
        }),
      ),
    onSuccess: async (res) => {
      setToast(`تم إرسال طلب الترقية إلى باقة ${res.planName} بنجاح.`);
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

  return (
    <Page className="gap-5">
      <h1 className="text-[26px] font-bold">الاشتراك والفواتير</h1>

      {pendingReq && pendingReq.status === "Pending" && (
        <div role="status" className="bg-[#FFFBEB] border border-[#FDE68A] text-[#92400E] rounded-2xl p-4 sm:p-5 text-sm flex flex-col gap-3.5 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2 font-bold text-base text-[#78350F]">
              <span className="w-2.5 h-2.5 rounded-full bg-amber-500 animate-pulse" />
              <span>طلب ترقية إلى باقة {pendingReq.planName} ({pendingReq.cycle === "Annual" ? "سنوي" : "شهري"}) قيد المراجعة</span>
            </div>
            {pendingReq.isPaymentReported ? (
              <Badge tone="ok">تم سداد الرسوم 🧾</Badge>
            ) : (
              <Badge tone="warn">في انتظار السداد والتأكيد</Badge>
            )}
          </div>
          <p className="text-sm text-[#92400E]">
            طلبك قيد المراجعة لدى فريق الإدارة. باقتك الحالية شغالة عادي لحد تفعيل الباقة الجديدة.
          </p>

          {pendingReq.adminReply ? (
            <div className="bg-white/90 border border-amber-200 rounded-xl p-3.5 flex flex-col gap-1.5 shadow-xs">
              <strong className="text-navy-900 text-xs font-bold uppercase tracking-wider flex items-center gap-1.5">
                <span>💬</span> تعليمات السداد من الإدارة:
              </strong>
              <p className="whitespace-pre-line text-[#1F2A37] font-semibold text-sm leading-relaxed">
                {pendingReq.adminReply}
              </p>
            </div>
          ) : (
            <span className="text-xs text-[#B45309]">
              💡 هيوصلك إشعار وتعليمات الدفع (إنستاباي / محفظة فودافون كاش) هنا في الإشعارات بمجرد مراجعة الإدارة، وتقدر كمان تحوّل وترفق الإيصال هنا فوراً.
            </span>
          )}

          {pendingReq.isPaymentReported && !isEditingProof ? (
            <div className="mt-1 bg-[#F0FDF4] border border-[#BBF7D0] rounded-xl p-4 flex flex-col gap-3 shadow-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="w-5 h-5 rounded-full bg-emerald-600 text-white flex items-center justify-center text-xs font-bold">✓</span>
                  <span className="font-bold text-[#166534] text-sm sm:text-base">تم إرسال إيصال السداد، والطلب قيد المراجعة لتفعيل الباقة فورياً</span>
                </div>
                <button
                  type="button"
                  onClick={() => setIsEditingProof(true)}
                  className="text-xs text-emerald-800 underline hover:text-emerald-950 font-semibold cursor-pointer"
                >
                  تعديل الإيصال أو البيانات
                </button>
              </div>

              <div className="grid sm:grid-cols-3 gap-3 text-xs bg-white/90 rounded-lg p-3 border border-emerald-100">
                <div>
                  <span className="text-gray-500 block mb-0.5">تفاصيل الحساب المحول منه:</span>
                  <span className="font-semibold text-gray-900">{pendingReq.transferNotes || "—"}</span>
                </div>
                <div>
                  <span className="text-gray-500 block mb-0.5">رقم العملية / الحوالة:</span>
                  <span className="font-semibold font-mono text-gray-900">{pendingReq.transferReference || "—"}</span>
                </div>
                <div>
                  <span className="text-gray-500 block mb-0.5">تاريخ الإرسال:</span>
                  <span className="font-semibold text-gray-900">{fmtDate(pendingReq.paymentTransferredAtUtc)}</span>
                </div>
              </div>

              {pendingReq.paymentProofUrl && (
                <div className="flex items-center gap-3 pt-1">
                  <a
                    href={pendingReq.paymentProofUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-2 px-3.5 py-2 rounded-xl bg-emerald-700 hover:bg-emerald-800 text-white text-xs font-bold transition shadow-xs"
                  >
                    <span>🧾</span>
                    <span>عرض صورة إيصال التحويل</span>
                  </a>
                </div>
              )}
            </div>
          ) : (
            <div className="mt-1 bg-white rounded-xl p-4 sm:p-5 border border-amber-300 shadow-sm flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 font-bold text-gray-900 text-sm sm:text-base">
                  <span>🧾</span>
                  <span>تأكيد التحويل وإرسال صورة الإيصال</span>
                </div>
                {isEditingProof && (
                  <button
                    type="button"
                    onClick={() => setIsEditingProof(false)}
                    className="text-xs text-gray-500 hover:text-gray-800 underline cursor-pointer"
                  >
                    إلغاء التعديل
                  </button>
                )}
              </div>
              <p className="text-xs text-gray-600">
                بعد ما تحوّل المبلغ (سواء عبر إنستاباي أو محفظة فودافون كاش أو تحويل بنكي)، ارفع صورة إيصال التحويل واكتب بياناتك لتفعيل الباقة فورياً.
              </p>

              <div className="flex flex-col gap-3.5 pt-1">
                <div>
                  <label className="block text-xs font-bold text-gray-700 mb-1.5">
                    صورة إيصال التحويل (سكرين شوت أو صورة الإيصال) *
                  </label>
                  <div className="flex flex-wrap items-center gap-3">
                    <input
                      type="file"
                      accept="image/*"
                      disabled={isUploadingReceipt}
                      onChange={handleFileSelect}
                      className="text-xs text-gray-600 file:me-3 file:py-2 file:px-3 file:rounded-xl file:border-0 file:text-xs file:font-bold file:bg-navy-900 file:text-white hover:file:bg-navy-800 cursor-pointer"
                    />
                    {isUploadingReceipt && (
                      <span className="text-xs text-amber-700 font-semibold animate-pulse">جاري رفع صورة الإيصال...</span>
                    )}
                    {receiptMediaId && !isUploadingReceipt && (
                      <span className="text-xs text-emerald-700 font-semibold flex items-center gap-1">
                        <span>✓</span> تم اختيار صورة الإيصال {receiptFileName ? `(${receiptFileName})` : ""}
                      </span>
                    )}
                  </div>
                </div>

                <div className="grid sm:grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-bold text-gray-700 mb-1.5">
                      تفاصيل الحساب المحول منه (اسم الحساب / رقم المحفظة)
                    </label>
                    <input
                      type="text"
                      value={transferNotes}
                      onChange={(e) => setTransferNotes(e.target.value)}
                      placeholder="مثال: فودافون كاش 01012345678 أو إنستاباي osama@instapay"
                      className={inputClass}
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-bold text-gray-700 mb-1.5">
                      رقم العملية أو الحوالة (اختياري)
                    </label>
                    <input
                      type="text"
                      value={transferReference}
                      onChange={(e) => setTransferReference(e.target.value)}
                      placeholder="مثال: REF-839210"
                      className={inputClass}
                    />
                  </div>
                </div>

                <div className="flex justify-end gap-2 pt-1">
                  <button
                    type="button"
                    disabled={reportPayment.isPending || isUploadingReceipt || (!receiptMediaId && !pendingReq.paymentProofMediaId && !transferNotes.trim())}
                    onClick={() => reportPayment.mutate()}
                    className={primaryBtn}
                  >
                    {reportPayment.isPending ? "جاري الإرسال..." : "تأكيد التحويل وإرسال الإيصال"}
                  </button>
                </div>
              </div>
            </div>
          )}
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
        {plans.isLoading && (
          <div className="p-5 bg-[#F7F8FB]">
            <div className="grid md:grid-cols-2 xl:grid-cols-3 gap-4">
              {[1, 2, 3].map((i) => (
                <div key={i} className="h-80 rounded-[18px] bg-white border border-[#E4E7EC] skeleton-shimmer" />
              ))}
            </div>
          </div>
        )}
        {plans.isError && (
          <div className="p-8 text-center flex flex-col items-center gap-2 text-sm text-[#A8261C] bg-[#F7F8FB]">
            <p>تعذّر تحميل الباقات: {(plans.error as Error).message}</p>
            <button
              type="button"
              onClick={() => plans.refetch()}
              className="text-navy-900 underline font-semibold cursor-pointer"
            >
              إعادة المحاولة
            </button>
          </div>
        )}
        {!plans.isLoading && !plans.isError && (!plans.data || plans.data.length === 0) && (
          <div className="p-8 text-center text-sm text-[#5B6475] bg-[#F7F8FB]">
            لا توجد باقات متاحة حالياً.
          </div>
        )}
        {plans.data && plans.data.length > 0 && (
          <div className="grid md:grid-cols-2 xl:grid-cols-3 gap-4 p-5 bg-[#F7F8FB] items-stretch">
            {[...plans.data]
              .sort((x, y) => x.monthlyPrice - y.monthlyPrice)
              .map((p) => {
                const current = plan?.id === p.id;
                const tooSmall =
                  !!data &&
                  ((p.maxVenues !== null && p.maxVenues !== undefined && data.usage.venues > p.maxVenues) ||
                    (p.maxPitches !== null && p.maxPitches !== undefined && data.usage.pitches > p.maxPitches));
                const price = cycle === "year" ? p.annualPrice : p.monthlyPrice;
                return (
                  <article
                    key={p.id}
                    className={`relative rounded-[18px] bg-white p-5 flex flex-col gap-4 border ${current ? "border-brand-red ring-2 ring-brand-red/20" : "border-[#E4E7EC]"}`}
                  >
                    <header className="flex items-center gap-2 min-h-[28px]">
                      <h3 className="text-[18px] font-bold flex-1">{p.name}</h3>
                      {current && <Badge tone="ok">باقتك الحالية</Badge>}
                    </header>

                    <div className="flex flex-col gap-1">
                      <div className="flex items-baseline gap-1.5">
                        <strong className="text-[30px] leading-none font-bold">{price > 0 ? formatEGP(price) : "مجانية"}</strong>
                        {price > 0 && <span className={`text-sm ${muted}`}>/ {cycle === "year" ? "سنة" : "شهر"}</span>}
                      </div>
                      <p className={`text-[13px] leading-5 min-h-[40px] ${muted}`}>{p.description ?? ""}</p>
                    </div>

                    <ul className="flex-1 flex flex-col gap-2.5 border-t border-[#EDF0F5] pt-4 text-[14px]">
                      <li className="flex justify-between gap-3">
                        <span className={muted}>الملاعب</span>
                        <strong>{limit(p.maxVenues)}</strong>
                      </li>
                      <li className="flex justify-between gap-3">
                        <span className={muted}>الملاعب الفرعية</span>
                        <strong>{limit(p.maxPitches)}</strong>
                      </li>
                      <li className="flex justify-between gap-3">
                        <span className={muted}>موظفين لكل ملعب</span>
                        <strong>{limit(p.maxStaffPerVenue)}</strong>
                      </li>
                      {p.trialDays > 0 && (
                        <li className="flex justify-between gap-3">
                          <span className={muted}>فترة تجربة</span>
                          <strong>{toArabicDigits(p.trialDays)} يوم</strong>
                        </li>
                      )}
                      {p.features.map((f) => (
                        <li key={f} className="flex items-center gap-2">
                          <span aria-hidden="true" className="w-5 h-5 shrink-0 rounded-full bg-[#E7F6EC] text-[#1F7A3E] text-xs font-bold flex items-center justify-center">
                            ✓
                          </span>
                          {FEATURE_NAMES[f] ?? f}
                        </li>
                      ))}
                    </ul>

                    {current ? (
                      <div className="min-h-[44px] rounded-xl bg-[#F3F5F9] text-[#5B6475] text-sm font-semibold flex items-center justify-center">دي باقتك دلوقتي</div>
                    ) : pendingReq && pendingReq.planId === p.id && pendingReq.status === "Pending" ? (
                      <div className="min-h-[44px] rounded-xl bg-amber-50 text-amber-800 border border-amber-200 text-sm font-semibold flex items-center justify-center gap-1.5">
                        <span className="w-2 h-2 rounded-full bg-amber-500 animate-pulse" />
                        <span>الطلب قيد المراجعة</span>
                      </div>
                    ) : (
                      <button
                        type="button"
                        className={`${tooSmall ? secondaryBtn : primaryBtn} w-full`}
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
        <p className={`px-5 py-4 text-[13px] ${muted}`}>مفيش دفع أونلاين: بعد الطلب فريق ملعبك بيتواصل معاك بتعليمات الدفع (تحويل، إنستاباي، محفظة أو كاش) وبيفعّل الباقة.</p>
      </section>

      <section className={`${card} rounded-[18px] p-5 flex flex-col gap-3`}>
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-navy-900 flex items-center gap-2">
            <span>🧾</span>
            <span>فواتير الاشتراكات</span>
          </h2>
          {data && data.invoices.length > 0 && (
            <span className="text-xs text-gray-500 font-semibold">
              إجمالي {toArabicDigits(data.invoices.length)} فاتورة
            </span>
          )}
        </div>
        {data && data.invoices.length === 0 && (
          <div className="p-8 text-center text-sm text-gray-500 bg-gray-50 rounded-xl border border-dashed border-gray-200">
            لا توجد فواتير اشتراكات بعد. ستظهر الفواتير هنا فور سداد وتفعيل أي باقة.
          </div>
        )}
        {data?.invoices.map((inv) => (
          <div
            key={inv.id}
            className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border border-[#EDF0F5] hover:border-gray-300 p-4 rounded-xl transition-all duration-150 bg-white"
          >
            <div className="flex flex-col gap-1">
              <div className="flex items-center gap-2">
                <span dir="ltr" className="font-inter font-bold text-[#071832]">
                  {inv.number}
                </span>
                {inv.status === "Void" ? (
                  <Badge tone="bad">ملغاة</Badge>
                ) : (
                  <Badge tone="ok">مسددة بالكامل ✓</Badge>
                )}
              </div>
              <span className={`text-xs ${muted}`}>
                باقة {inv.planName} · التغطية: {fmtDate(inv.periodStartUtc)} إلى {fmtDate(inv.periodEndUtc)}
              </span>
            </div>

            <div className="flex items-center justify-between sm:justify-end gap-3 border-t sm:border-t-0 pt-2 sm:pt-0">
              <strong className="text-base font-black text-navy-900">
                {formatEGP(inv.amount)} ج.م
              </strong>
              <div className="flex items-center gap-1.5">
                <button
                  type="button"
                  className={`${primaryBtn} min-h-[36px] px-3.5 text-xs bg-[#071832] hover:bg-slate-800`}
                  onClick={() => setViewInvoice(inv)}
                >
                  <span>🧾</span>
                  <span>عرض الفاتورة</span>
                </button>
                <button
                  type="button"
                  className={`${secondaryBtn} min-h-[36px] px-3 text-xs`}
                  onClick={() => downloadInvoice(inv)}
                >
                  PDF
                </button>
              </div>
            </div>
          </div>
        ))}
      </section>

      <InvoiceModal invoice={viewInvoice} onClose={() => setViewInvoice(null)} />
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
