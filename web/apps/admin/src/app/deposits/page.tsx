"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Check, ExternalLink, FileText, Inbox, ReceiptText, X } from "lucide-react";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, btnDanger, btnPrimary, card, money, muted, toArabicDigits } from "@/components/ui";
import { fmtDateTime, useNotice } from "@/components/admin";
import { ImageLightbox } from "@/components/ImageLightbox";
import { api, unwrap } from "@/lib/api";

type Item = Schemas["AdminDepositReviewItem"];

/**
 * Deposits are paid to the platform's accounts (commission model), so its team reviews every receipt here: approving
 * confirms the booking, rejecting gives the player the reason and the rest of their hold to send a correct one.
 */
export default function DepositReviewsPage() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  const list = useQuery({
    queryKey: ["admin-deposit-reviews"],
    refetchInterval: 30_000,
    queryFn: () => unwrap(api.GET("/api/v1/admin/bookings/deposit-reviews", { params: { query: { page: 1, pageSize: 50 } } })),
  });

  const review = useMutation({
    mutationFn: (input: { bookingId: string; approved: boolean; rejectionReason?: string }) =>
      unwrap(
        api.POST("/api/v1/admin/bookings/{bookingId}/review-deposit", {
          params: { path: { bookingId: input.bookingId } },
          body: { approved: input.approved, rejectionReason: input.rejectionReason ?? null },
        }),
      ),
    onSuccess: async (_, input) => {
      notice.ok(input.approved ? "اتأكد العربون والحجز اتثبت." : "اترفض الإيصال واللاعب وصله السبب.");
      await queryClient.invalidateQueries({ queryKey: ["admin-deposit-reviews"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-bookings"] });
    },
    onError: notice.fail,
  });

  const items = list.data?.items ?? [];
  const isImage = (i: Item) => (i.contentType ?? "").startsWith("image/");

  return (
    <Page>
      <PageHeader
        title="مراجعة العرابين"
        sub="العربون بيتدفع لحسابات المنصة. اتأكد إن المبلغ وصل فعلاً قبل الموافقة: الموافقة بتثبّت الحجز للاعب والملعب."
      >
        <Pill tone={items.length ? "warn" : "ok"}>{items.length ? `${toArabicDigits(list.data?.total ?? items.length)} مستني` : "مفيش حاجة مستنية"}</Pill>
      </PageHeader>
      {notice.node}

      {list.isLoading && <div className="h-48 rounded-[20px] skeleton-shimmer" />}
      {list.isError && <div className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-4 py-3 text-sm">{(list.error as Error).message}</div>}

      {!list.isLoading && items.length === 0 && (
        <div className={`${card} rounded-[20px] py-14 flex flex-col items-center gap-3 text-center animate-fade-in`}>
          <span className="grid place-items-center size-14 rounded-2xl bg-[#F3F5F9] text-[#8A93A3]">
            <Inbox className="size-7" strokeWidth={1.5} />
          </span>
          <strong className="text-[15px]">مفيش إيصالات مستنية</strong>
          <span className={`text-[13px] ${muted}`}>أول ما لاعب يرفع إيصال عربون هيظهر هنا.</span>
        </div>
      )}

      <div className="grid gap-3 lg:grid-cols-2">
        {items.map((i) => (
          <article key={i.bookingId} className={`${card} rounded-[20px] p-4 flex gap-4 animate-fade-in-up`}>
            <button
              type="button"
              onClick={() => i.downloadUrl && (isImage(i) ? setPreview(i.downloadUrl) : window.open(i.downloadUrl, "_blank", "noopener"))}
              disabled={!i.downloadUrl}
              aria-label="عرض الإيصال"
              className="relative w-28 shrink-0 aspect-[3/4] rounded-xl overflow-hidden bg-[#F3F5F9] border border-[#E4E7EC] grid place-items-center text-[#8A93A3] hover:ring-2 hover:ring-brand-red/40 transition-all cursor-pointer"
            >
              {i.downloadUrl && isImage(i) ? (
                // eslint-disable-next-line @next/next/no-img-element -- short-lived signed URL from our media service
                <img src={i.downloadUrl} alt={`إيصال ${i.reference}`} className="w-full h-full object-cover" />
              ) : i.downloadUrl ? (
                <span className="flex flex-col items-center gap-1 text-xs font-semibold">
                  <FileText className="size-7" strokeWidth={1.5} />
                  PDF
                  <ExternalLink className="size-3.5" />
                </span>
              ) : (
                <ReceiptText className="size-7" strokeWidth={1.5} />
              )}
            </button>

            <div className="flex-1 min-w-0 flex flex-col gap-2">
              <div className="flex flex-wrap items-center gap-2">
                <strong className="text-[15px] truncate">{i.customerName || "لاعب"}</strong>
                <Pill tone={i.status === "UnderReview" ? "navy" : "warn"}>{i.status === "UnderReview" ? "تحت المراجعة" : "إيصال جديد"}</Pill>
              </div>
              <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
                <dt className={muted}>العربون المطلوب</dt>
                <dd className="font-bold text-[14px] text-slate-900">{money(i.depositAmount)}</dd>
                <dt className={muted}>سعر الحجز</dt>
                <dd>{money(i.totalPrice)}</dd>
                <dt className={muted}>الملعب</dt>
                <dd className="truncate">{[i.venueName, i.pitchName].filter(Boolean).join(" · ") || "—"}</dd>
                <dt className={muted}>رقم الحجز</dt>
                <dd dir="ltr" className="text-right font-mono">{i.reference}</dd>
                <dt className={muted}>اترفع</dt>
                <dd>{fmtDateTime(i.proofUploadedAtUtc)}</dd>
                {i.customerPhone && (
                  <>
                    <dt className={muted}>موبايل اللاعب</dt>
                    <dd dir="ltr" className="text-right">{i.customerPhone}</dd>
                  </>
                )}
              </dl>
              <div className="flex flex-wrap gap-2 mt-auto pt-1">
                <button
                  type="button"
                  disabled={review.isPending}
                  onClick={() => review.mutate({ bookingId: i.bookingId, approved: true })}
                  className={`${btnPrimary} min-h-[38px] px-3.5 text-[13px]`}
                >
                  <Check className="size-4" />
                  العربون وصل، أكّد الحجز
                </button>
                <button
                  type="button"
                  disabled={review.isPending}
                  onClick={() =>
                    setReason({
                      title: "رفض الإيصال",
                      body: `اللاعب ${i.customerName || ""} هيوصله السبب ويقدر يرفع إيصال صح قبل ما مهلة الحجز تخلص.`,
                      confirmLabel: "رفض",
                      onConfirm: (text) => review.mutate({ bookingId: i.bookingId, approved: false, rejectionReason: text }),
                    })
                  }
                  className={`${btnDanger} min-h-[38px] px-3.5 text-[13px]`}
                >
                  <X className="size-4" />
                  رفض
                </button>
              </div>
            </div>
          </article>
        ))}
      </div>

      <ImageLightbox photo={preview ? { src: preview, title: "إيصال العربون" } : null} onClose={() => setPreview(null)} />
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Page>
  );
}
