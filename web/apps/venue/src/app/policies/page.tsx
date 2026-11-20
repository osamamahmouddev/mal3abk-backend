"use client";

import React, { useEffect, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, Toast, card, chip, muted, redShadow, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";

type Policy = Schemas["BookingPolicyApiRequest"];
type Option = [label: string, patch: Partial<Policy>];
type Item = [label: string, hint: string, options: Option[]];

const GROUPS: [string, Item[]][] = [
  [
    "العربون والتأكيد",
    [
      [
        "مهلة رفع إثبات الدفع",
        "لو اللاعب مارفعش في المهلة الحجز بيتلغي",
        [
          ["١٥ دقيقة", { proofSubmissionWindowMinutes: 15 }],
          ["٣٠ دقيقة", { proofSubmissionWindowMinutes: 30 }],
          ["ساعة", { proofSubmissionWindowMinutes: 60 }],
        ],
      ],
      [
        "مهلة مراجعة الإثبات",
        "الوقت اللي فريق المنصة بيراجع فيه إيصال العربون",
        [
          ["٣٠ دقيقة", { manualReviewWindowMinutes: 30 }],
          ["ساعة", { manualReviewWindowMinutes: 60 }],
          ["٣ ساعات", { manualReviewWindowMinutes: 180 }],
        ],
      ],
      [
        "الحجز المستني بيقفل الميعاد؟",
        'لو "أيوه" محدش تاني يقدر يحجزه وقت المراجعة',
        [
          ["أيوه", { pendingBookingsBlockSlots: true }],
          ["لأ", { pendingBookingsBlockSlots: false }],
        ],
      ],
    ],
  ],
  [
    "الإلغاء والاسترداد",
    [
      [
        "آخر ميعاد للإلغاء المجاني",
        "قبل الماتش بـ",
        [
          ["٦ ساعات", { cancellationDeadlineHours: 6 }],
          ["١٢ ساعة", { cancellationDeadlineHours: 12 }],
          ["٢٤ ساعة", { cancellationDeadlineHours: 24 }],
          ["يومين", { cancellationDeadlineHours: 48 }],
        ],
      ],
      [
        "الاسترداد في الإلغاء المجاني",
        "بيترجع من العربون",
        [
          ["العربون كله", { refundEligibility: 0, refundPercentage: 100 }],
          ["النص", { refundEligibility: 1, refundPercentage: 50 }],
          ["مفيش استرداد", { refundEligibility: 2, refundPercentage: 0 }],
        ],
      ],
    ],
  ],
  [
    "الحضور والتغيير",
    [
      [
        "لو الفريق ماجاش",
        "No-show",
        [
          ["العربون بيتخصم", { noShowRule: 0 }],
          ["المبلغ كله", { noShowRule: 1 }],
          ["مفيش خصم", { noShowRule: 2 }],
        ],
      ],
      [
        "التأخير المسموح",
        "بعده الملعب يقدر يدّي الميعاد لحد تاني",
        [
          ["١٠ دقايق", { lateArrivalGraceMinutes: 10 }],
          ["١٥ دقيقة", { lateArrivalGraceMinutes: 15 }],
          ["٣٠ دقيقة", { lateArrivalGraceMinutes: 30 }],
        ],
      ],
      [
        "تغيير الميعاد",
        "قبل الماتش بـ",
        [
          ["مش مسموح", { allowReschedule: false, rescheduleDeadlineHours: 0 }],
          ["لحد ١٢ ساعة", { allowReschedule: true, rescheduleDeadlineHours: 12 }],
          ["لحد ٢٤ ساعة", { allowReschedule: true, rescheduleDeadlineHours: 24 }],
        ],
      ],
    ],
  ],
];

const DEFAULT_POLICY: Policy = {
  depositType: 0,
  depositValue: 33,
  proofSubmissionWindowMinutes: 30,
  manualReviewWindowMinutes: 60,
  pendingBookingsBlockSlots: true,
  cancellationDeadlineHours: 24,
  refundEligibility: 0,
  refundPercentage: 100,
  noShowRule: 0,
  lateArrivalGraceMinutes: 15,
  allowReschedule: true,
  rescheduleDeadlineHours: 24,
  terms: null,
};

/** The policy snapshot reports enums by name; the update takes their numbers. */
const ENUM_VALUES = {
  deposit: { Percentage: 0, FixedAmount: 1, Full: 2 } as Record<string, Schemas["DepositType"]>,
  refund: { FullRefund: 0, PartialRefund: 1, NoRefund: 2 } as Record<string, Schemas["RefundEligibility"]>,
  noShow: { ForfeitDeposit: 0, ForfeitFullAmount: 1, NoPenalty: 2 } as Record<string, Schemas["NoShowRule"]>,
};

const matches = (policy: Policy, patch: Partial<Policy>) =>
  Object.entries(patch).every(([key, value]) => policy[key as keyof Policy] === value);

const labelOf = (policy: Policy, label: string) => {
  for (const [, items] of GROUPS) for (const [itemLabel, , options] of items) if (itemLabel === label) return options.find(([, p]) => matches(policy, p))?.[0] ?? "—";
  return "—";
};

/** The deposit is paid to the platform (commission model), so its team sets it; the venue only sees it. */
const depositText = (policy: Policy) => {
  const value = toArabicDigits(String(policy.depositValue ?? 0));
  if (policy.depositType === 1) return `${value} ج.م`;
  if (policy.depositType === 2) return "سعر الحجز كله";
  return `${value}٪ من سعر الحجز`;
};

export default function PoliciesPage() {
  const { venue, venueId } = useVenue();
  const [policy, setPolicy] = useState<Policy>(DEFAULT_POLICY);
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<string | null>(null);

  const current = useQuery({
    queryKey: ["booking-policy", venueId],
    enabled: !!venueId,
    retry: false,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/venue-manager/venues/{venueId}/booking-policy", { params: { path: { venueId: venueId! } } });
      return response.status === 404 ? null : (data?.find((v) => v.isCurrent) ?? null);
    },
  });

  useEffect(() => {
    const snapshot = current.data?.policy;
    if (snapshot) {
      setPolicy({
        depositType: ENUM_VALUES.deposit[snapshot.depositType] ?? 0,
        depositValue: snapshot.depositValue,
        proofSubmissionWindowMinutes: snapshot.proofSubmissionWindowMinutes,
        manualReviewWindowMinutes: snapshot.manualReviewWindowMinutes,
        pendingBookingsBlockSlots: snapshot.pendingBookingsBlockSlots,
        cancellationDeadlineHours: snapshot.cancellationDeadlineHours,
        refundEligibility: ENUM_VALUES.refund[snapshot.refundEligibility] ?? 0,
        refundPercentage: snapshot.refundPercentage,
        noShowRule: ENUM_VALUES.noShow[snapshot.noShowRule] ?? 0,
        lateArrivalGraceMinutes: snapshot.lateArrivalGraceMinutes,
        allowReschedule: snapshot.allowReschedule,
        rescheduleDeadlineHours: snapshot.rescheduleDeadlineHours,
        terms: snapshot.terms ?? null,
      });
    }
  }, [current.data]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(api.PUT("/api/v1/venue-manager/venues/{venueId}/booking-policy", { params: { path: { venueId: venueId! } }, body: policy })),
    onSuccess: async () => {
      setDirty(false);
      await current.refetch();
      setToast("اتحفظت السياسات. هتطبّق على الحجوزات الجديدة من دلوقتي.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const preview = [
    `العربون: ${depositText(policy)} — حوّله لحسابات المنصة وارفع الإيصال خلال ${labelOf(policy, "مهلة رفع إثبات الدفع")}.`,
    `فريق المنصة بيراجع الإيصال خلال ${labelOf(policy, "مهلة مراجعة الإثبات")}${policy.pendingBookingsBlockSlots ? "، والميعاد محجوزلك لحد ما يتراجع." : "."}`,
    `تقدر تلغي مجاناً لحد ${labelOf(policy, "آخر ميعاد للإلغاء المجاني")} قبل الماتش، والاسترداد: ${labelOf(policy, "الاسترداد في الإلغاء المجاني")}.`,
    `لو اتأخرت أكتر من ${labelOf(policy, "التأخير المسموح")} الملعب ممكن يدّي الميعاد لحد تاني، ولو ماجيتش: ${labelOf(policy, "لو الفريق ماجاش")}.`,
    `تغيير الميعاد: ${labelOf(policy, "تغيير الميعاد")}.`,
  ];

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">سياسات الحجز</h1>
        {current.data && <span className={`text-[13px] ${muted}`}>النسخة الحالية: {current.data.version}</span>}
        {dirty && <span className="text-[13px] font-bold bg-[#FEF3D6] text-[#7A4D00] rounded-full px-3 py-1">فيه تعديلات مش محفوظة</span>}
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <div className="flex-[999_1_460px] min-w-0 flex flex-col gap-4">
          {GROUPS.map(([title, items]) => (
            <section key={title} className={`${card} rounded-[20px] px-5 py-[18px] flex flex-col`}>
              <h2 className="mb-1.5 text-[17px] font-bold">{title}</h2>
              {title === GROUPS[0][0] && (
                <div className="flex flex-wrap items-center gap-2.5 py-3 border-b border-[#EEF0F5]">
                  <span className="flex-[1_1_220px] flex flex-col gap-0.5">
                    <span className="text-sm font-bold">قيمة العربون</span>
                    <span className={`text-xs ${muted}`}>بيحددها فريق المنصة، لأن العربون بيتدفع لحساباتها وبتخصم منه العمولة</span>
                  </span>
                  <span className="min-h-[36px] px-3 rounded-[10px] text-[13px] font-bold bg-[#EEF0F5] text-[#1F2A44] inline-flex items-center">
                    {current.isLoading ? "…" : depositText(policy)}
                  </span>
                </div>
              )}
              {items.map(([label, hint, options]) => (
                <div key={label} className="flex flex-wrap items-center gap-2.5 py-3 border-b border-[#EEF0F5] last:border-b-0">
                  <span className="flex-[1_1_220px] flex flex-col gap-0.5">
                    <span className="text-sm font-bold">{label}</span>
                    <span className={`text-xs ${muted}`}>{hint}</span>
                  </span>
                  <div className="flex gap-1.5 flex-wrap">
                    {options.map(([optionLabel, patch]) => {
                      const on = matches(policy, patch);
                      return (
                        <button
                          key={optionLabel}
                          type="button"
                          aria-pressed={on}
                          onClick={() => {
                            setPolicy({ ...policy, ...patch });
                            setDirty(true);
                          }}
                          className={chip(on, "min-h-[36px] px-3 rounded-[10px] text-[13px]")}
                        >
                          {optionLabel}
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
            </section>
          ))}

          <section className={`${card} rounded-[20px] px-5 py-[18px] flex flex-col gap-2`}>
            <h2 className="text-[17px] font-bold">شروط إضافية</h2>
            <textarea
              rows={3}
              value={policy.terms ?? ""}
              onChange={(e) => {
                setPolicy({ ...policy, terms: e.target.value || null });
                setDirty(true);
              }}
              placeholder="مثلاً: ممنوع الحذاء الحديد"
              className="border border-[#C9CFD9] rounded-xl px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-navy-900"
            />
          </section>

        </div>

        <aside aria-label="معاينة اللاعب" className="flex-[1_1_320px] min-w-0 flex flex-col gap-3 sticky top-4">
          <div className={`${card} rounded-[20px] p-[18px] flex flex-col gap-2.5`}>
            <span className={`text-xs font-bold ${muted}`}>كده هتظهر للاعب قبل ما يحجز</span>
            <h2 className="text-base font-bold">سياسة {venue?.name ?? "الملعب"}</h2>
            {preview.map((p) => (
              <div key={p} className="flex gap-2 items-start text-[13px] leading-[1.6]">
                <span className="w-1.5 h-1.5 rounded-full bg-brand-red mt-2 shrink-0" />
                <span>{p}</span>
              </div>
            ))}
          </div>
          <div className="bg-[#EEF0F5] text-[#3B4352] rounded-[14px] px-3.5 py-3 text-[13px] leading-[1.6]">
            التعديلات بتطبّق على الحجوزات الجديدة بس. كل حجز موجود بيفضل بالسياسة اللي اتحجز بيها.
          </div>
          <button
            type="button"
            disabled={save.isPending || !venueId}
            onClick={() => save.mutate()}
            className={`min-h-[48px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`}
          >
            {save.isPending ? "جاري الحفظ…" : "حفظ السياسات"}
          </button>
        </aside>
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
