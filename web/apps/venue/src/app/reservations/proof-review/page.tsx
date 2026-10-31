"use client";

import React, { Suspense, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, card, muted, toArabicDigits, useRevealOnSelect } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { ReasonDialog } from "@/components/ReasonDialog";
import { api, unwrap } from "@/lib/api";
import { BOOKING_STATUS, cairoDateTime, money, timeLeft } from "@/lib/booking-format";
import { P } from "@/lib/permissions";
import { useVenueRealtime } from "@/lib/realtime";
import { mediaSrc } from "@/lib/upload";

type Item = Schemas["VenueBookingItemDto"];
type Proof = Schemas["VenueProofItemDto"];

const REJECT_REASONS = [
  "المبلغ في الإيصال أقل من العربون المطلوب. حوّل الفرق وارفع الإيصال تاني.",
  "صورة الإيصال مش واضحة. ارفع صورة يظهر فيها المبلغ والتاريخ ورقم العملية.",
  "مالقيناش التحويل ده في حسابنا. اتأكد من الرقم وارفع إيصال صحيح.",
  "ملاحظة التحويل مافيهاش رقم الحجز. ابعت إيصال فيه الرقم.",
];

interface Notice {
  kind: "ok" | "info" | "bad";
  text: string;
}

export default function ProofReviewPage() {
  return (
    <Suspense>
      <ProofReview />
    </Suspense>
  );
}

function ProofReview() {
  const queryClient = useQueryClient();
  const params = useSearchParams();
  const { venue, venueId, can } = useVenue();
  const [selId, setSelId] = useState<string | null>(params.get("booking"));
  const detailsRef = useRevealOnSelect<HTMLElement>(selId);
  const [notice, setNotice] = useState<Notice | null>(null);

  const { connected } = useVenueRealtime(venueId, (u) => {
    if (u.status === "ProofSubmitted") setNotice({ kind: "info", text: `وصل إيصال جديد لحجز ${u.reference}.` });
  });

  const queue = useQuery({
    queryKey: ["proofs", venueId],
    enabled: !!venueId,
    refetchInterval: connected ? false : 30_000,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/venue-manager/venues/{venueId}/bookings", {
          params: { path: { venueId: venueId! }, query: { statuses: [3, 4], sort: "start_asc", pageSize: 50 } },
        }),
      ),
  });

  const items = queue.data?.items ?? [];
  const current = items.find((i) => i.id === selId) ?? items[0] ?? null;
  const pitchName = (id: string) => venue?.pitches.find((p) => p.id === id)?.name ?? "ملعب";

  const onReviewed = async (text: string) => {
    setNotice({ kind: "ok", text });
    setSelId(null);
    await queryClient.invalidateQueries({ predicate: (q) => ["proofs", "dashboard"].includes(String(q.queryKey[0])) || String(q.queryKey[0]).startsWith("booking") });
  };

  return (
    <Page className="gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex-[1_1_280px] flex flex-col gap-0.5">
          <span className={`text-[13px] ${muted}`}>
            <Link href="/reservations" className="hover:text-brand-red">
              الحجوزات
            </Link>{" "}
            ‹ إيصالات العربون
          </span>
          <h1 className="text-[26px] font-bold">مراجعة إيصالات العربون</h1>
        </div>
        <span className={`text-[13px] ${muted} flex items-center gap-1.5`}>
          <span className={`w-2 h-2 rounded-full ${connected ? "bg-[#2E8B55]" : "bg-[#C9CFD9]"}`} aria-hidden="true" />
          {connected ? "تحديث مباشر" : "بيتحدث كل ٣٠ ثانية"} · المنصة لا تتحقق من التحويلات تلقائياً — راجع حسابك قبل الاعتماد.
        </span>
      </div>

      {notice && (
        <div
          role="status"
          className={`rounded-xl px-4 py-3 flex gap-3 items-center text-sm font-semibold ${
            { ok: "bg-[#E5F5EB] text-[#155C33]", info: "bg-[#EEF0F5] text-[#1F2A44]", bad: "bg-[#FDECEA] text-[#A8261C]" }[notice.kind]
          }`}
        >
          <span className="flex-1">{notice.text}</span>
          {notice.kind === "ok" && (
            <Link href="/calendar" className="font-bold text-inherit underline">
              عرض في التقويم
            </Link>
          )}
          <button type="button" onClick={() => setNotice(null)} aria-label="إغلاق التنبيه">
            ✕
          </button>
        </div>
      )}

      {!can(P.proofsReview) && venueId && (
        <div role="note" className="bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-4 py-3 text-sm">
          دورك في الملعب ده مايسمحش باعتماد الإيصالات. تقدر تشوفها بس.
        </div>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section aria-label="قائمة الإيصالات" className={`flex-[1_1_280px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
          <div className="px-4 py-3.5 border-b border-[#E4E7EC] flex justify-between items-center">
            <h2 className="text-[15px] font-bold">بانتظار المراجعة</h2>
            <span className={`text-xs ${muted}`}>{toArabicDigits(queue.data?.totalCount ?? 0)} إيصالات</span>
          </div>
          {queue.isLoading && <p className={`p-6 text-sm text-center ${muted}`}>جاري التحميل…</p>}
          {queue.isError && <p className="p-6 text-sm text-center text-[#A8261C]">{(queue.error as Error).message}</p>}
          {!queue.isLoading && items.length === 0 && <p className={`p-8 text-sm text-center ${muted}`}>مفيش إيصالات محتاجة مراجعة دلوقتي. 👌</p>}
          {items.map((q) => {
            const on = q.id === current?.id;
            return (
              <button
                key={q.id}
                type="button"
                aria-pressed={on}
                onClick={() => {
                  setSelId(q.id);
                  setNotice(null);
                }}
                className={`w-full min-h-[72px] px-4 py-3 border-b border-[#EEF0F5] flex items-center gap-3 ${on ? "bg-[#EEF0F5] shadow-[inset_-4px_0_0_#D63B2F]" : "bg-white"}`}
              >
                <span className="w-10 h-[50px] shrink-0 rounded-md bg-[repeating-linear-gradient(135deg,#DCE0E8_0_5px,#EEF0F5_5px_10px)]" aria-hidden="true" />
                <span className="flex-1 min-w-0 flex flex-col gap-0.5 text-right">
                  <span className="text-sm font-bold text-slate-900">
                    {q.customerName || "لاعب"} · {money(q.depositAmount, q.currency)}
                  </span>
                  <span className={`text-xs ${muted}`}>
                    {pitchName(q.pitchId)} · {cairoDateTime(q.startUtc)}
                  </span>
                </span>
                <span className="shrink-0 text-[11px] font-bold rounded-full px-2 py-[3px] bg-[#FEF3D6] text-[#7A4D00]">{BOOKING_STATUS[q.status].label}</span>
              </button>
            );
          })}
        </section>

        {current && venueId && (
          <ReviewPanel
            key={current.id}
            venueId={venueId}
            item={current}
            panelRef={detailsRef}
            pitchName={pitchName}
            canReview={can(P.proofsReview)}
            onReviewed={onReviewed}
            onError={(text) => setNotice({ kind: "bad", text })}
          />
        )}
      </div>
    </Page>
  );
}

function ReviewPanel({
  venueId,
  item,
  panelRef,
  pitchName,
  canReview,
  onReviewed,
  onError,
}: {
  venueId: string;
  item: Item;
  panelRef: React.Ref<HTMLElement>;
  pitchName: (id: string) => string;
  canReview: boolean;
  onReviewed: (text: string) => Promise<void>;
  onError: (text: string) => void;
}) {
  const [approveOpen, setApproveOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [amountChecked, setAmountChecked] = useState(false);
  const path = { venueId, id: item.id };

  const detail = useQuery({
    queryKey: ["booking", venueId, item.id],
    // Signed receipt links live 5 minutes; refresh before they expire while the panel is open.
    refetchInterval: 4 * 60_000,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/bookings/{id}", { params: { path } })),
  });

  const review = useMutation({
    mutationFn: (input: { approved: boolean; rejectionReason?: string }) =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/{id}/proof/review", {
          params: { path },
          body: { approved: input.approved, rejectionReason: input.rejectionReason ?? null },
        }),
      ),
    onSuccess: (_, input) =>
      onReviewed(input.approved ? `اتعمد العربون واتأكد حجز ${item.customerName || item.reference}.` : `اترفض الإيصال ووصل للاعب السبب.`),
    onError: (e: Error) => onError(e.message),
  });

  const b = detail.data;
  const proofs = b?.proofs ?? [];
  const latest: Proof | undefined = [...proofs].sort((x, y) => y.uploadedAtUtc.localeCompare(x.uploadedAtUtc))[0];
  const earlier = proofs.filter((p) => p !== latest);

  return (
    <section ref={panelRef} aria-label="تفاصيل الإيصال" className={`scroll-mt-20 flex-[999_1_560px] min-w-0 ${card} rounded-[18px] p-5 flex flex-wrap gap-6`}>
      <div className="flex-[1_1_260px] flex flex-col gap-2.5">
        {latest ? <ReceiptViewer proof={latest} /> : <div className={`aspect-[3/4] rounded-xl bg-[#F4F5F7] flex items-center justify-center text-sm ${muted}`}>{detail.isLoading ? "جاري التحميل…" : "مفيش إيصال"}</div>}
      </div>

      <div className="flex-[1.4_1_300px] min-w-0 flex flex-col gap-3.5">
        <div className="flex justify-between items-center gap-2">
          <h2 className="text-xl font-bold">{item.customerName || "لاعب"}</h2>
          <span dir="ltr" className={`text-[13px] ${muted}`}>
            {item.reference}
          </span>
        </div>
        <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          {(
            [
              ["الملعب والميعاد", `${pitchName(item.pitchId)} · ${cairoDateTime(item.startUtc)}`, "font-semibold"],
              ["الهاتف", b?.customerPhone ?? "—"],
              ["إجمالي الحجز", money(item.totalPrice, item.currency)],
              ["العربون المطلوب", money(item.depositAmount, item.currency), "font-bold"],
              ["وقت الرفع", latest ? cairoDateTime(latest.uploadedAtUtc) : "—"],
              ["مهلة المراجعة", b?.holdExpiresAtUtc ? timeLeft(b.holdExpiresAtUtc) : "—", "text-[#7A4D00] font-bold"],
            ] as [string, string, string?][]
          ).map(([k, v, cls]) => (
            <div key={k} className="flex flex-col gap-0.5">
              <dt className={`text-xs ${muted}`}>{k}</dt>
              <dd className={cls ?? ""}>{v}</dd>
            </div>
          ))}
        </dl>
        {latest && !latest.isScanClean && (
          <p role="alert" className="text-[13px] bg-[#FDECEA] text-[#A8261C] rounded-xl px-3 py-2">
            الملف لسه بيتفحص ضد الفيروسات أو اتعلّم كمشبوه — ماتفتحهوش برّه المنصة.
          </p>
        )}
        <div className="bg-[#F4F5F7] rounded-[10px] px-3.5 py-3 flex flex-col gap-1.5">
          <span className="text-[13px] font-bold">قبل الاعتماد اتأكد من</span>
          <span className="text-[13px] text-[#333B4A]">• المبلغ وصل فعلاً في المحفظة/الحساب</span>
          <span className="text-[13px] text-[#333B4A]">• رقم الحجز مكتوب في ملاحظة التحويل</span>
          <span className="text-[13px] text-[#333B4A]">• تاريخ التحويل بعد وقت الطلب</span>
        </div>
        <div className="flex flex-col gap-1.5">
          <span className="text-[13px] font-bold">سجل المراجعة</span>
          {earlier.length === 0 && <span className={`text-[13px] ${muted}`}>أول إيصال لهذا الحجز.</span>}
          {earlier.map((p) => (
            <span key={p.id} className={`text-[13px] ${muted}`}>
              إيصال {cairoDateTime(p.uploadedAtUtc)} — {p.isApproved === false ? `مرفوض: «${p.rejectionReason ?? ""}»` : "اتبدل"}
            </span>
          ))}
        </div>
        {canReview && (
          <div className="flex flex-wrap gap-2 mt-1">
            <button type="button" onClick={() => setApproveOpen(true)} className="flex-[2_1_180px] min-h-[48px] rounded-[10px] bg-navy-900 text-white text-[15px] font-bold">
              اعتماد العربون
            </button>
            <button type="button" onClick={() => setRejectOpen(true)} className="flex-[1_1_120px] min-h-[48px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] text-[15px] font-bold">
              رفض
            </button>
          </div>
        )}
      </div>

      {approveOpen && (
        <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
          <div role="dialog" aria-modal="true" aria-labelledby="approve-title" className="w-full max-w-[440px] bg-white rounded-[20px] p-6 flex flex-col gap-3">
            <h2 id="approve-title" className="text-[19px] font-bold">
              اعتماد عربون {money(item.depositAmount, item.currency)}
            </h2>
            <label className="flex items-start gap-2 text-sm">
              <input type="checkbox" checked={amountChecked} onChange={(e) => setAmountChecked(e.target.checked)} className="mt-1 w-4 h-4" />
              <span>
                اتأكدت إن مبلغ <b>{money(item.depositAmount, item.currency)}</b> وصل فعلاً لحساب الملعب.
              </span>
            </label>
            <p className={`text-[13px] ${muted}`}>بعد الاعتماد الحجز بيتأكد واللاعب بيوصله إشعار. الباقي ({money(item.totalPrice - item.depositAmount, item.currency)}) بيتدفع في الملعب.</p>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={!amountChecked || review.isPending}
                onClick={() => review.mutate({ approved: true })}
                className={`min-h-[46px] px-5 rounded-xl text-[15px] font-bold ${amountChecked && !review.isPending ? "bg-navy-900 text-white" : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"}`}
              >
                {review.isPending ? "جاري…" : "تأكيد الاعتماد"}
              </button>
              <button type="button" onClick={() => setApproveOpen(false)} className="min-h-[46px] px-5 rounded-xl border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold">
                رجوع
              </button>
            </div>
          </div>
        </div>
      )}

      {rejectOpen && (
        <ReasonDialog
          title="رفض الإيصال"
          presets={REJECT_REASONS}
          note="اللاعب بيقدر يرفع إيصال جديد طول ما مهلة الحجز لسه شغالة."
          confirmLabel="تأكيد الرفض"
          busy={review.isPending}
          onConfirm={(reason) => review.mutate({ approved: false, rejectionReason: reason })}
          onClose={() => setRejectOpen(false)}
        />
      )}
    </section>
  );
}

/** Receipt preview: images zoom (buttons, wheel) and pan (drag); PDFs open in the browser's own viewer. */
function ReceiptViewer({ proof }: { proof: Proof }) {
  const [scale, setScale] = useState(1);
  const [offset, setOffset] = useState({ x: 0, y: 0 });
  const drag = useRef<{ x: number; y: number } | null>(null);
  const src = proof.downloadUrl ? mediaSrc(proof.downloadUrl) : null;
  const isPdf = proof.contentType === "application/pdf";

  useEffect(() => {
    setScale(1);
    setOffset({ x: 0, y: 0 });
  }, [proof.id]);

  if (!src) {
    return <div className={`aspect-[3/4] rounded-xl bg-[#F4F5F7] flex items-center justify-center text-sm ${muted}`}>الملف مش متاح للعرض.</div>;
  }

  const zoom = (delta: number) => setScale((s) => Math.min(5, Math.max(1, Math.round((s + delta) * 10) / 10)));

  return (
    <>
      <div
        className="aspect-[3/4] rounded-xl border border-[#E4E7EC] bg-[#F4F5F7] overflow-hidden relative touch-none select-none"
        onWheel={(e) => !isPdf && zoom(e.deltaY < 0 ? 0.25 : -0.25)}
        onPointerDown={(e) => {
          if (isPdf || scale === 1) return;
          drag.current = { x: e.clientX - offset.x, y: e.clientY - offset.y };
          (e.target as HTMLElement).setPointerCapture(e.pointerId);
        }}
        onPointerMove={(e) => drag.current && setOffset({ x: e.clientX - drag.current.x, y: e.clientY - drag.current.y })}
        onPointerUp={() => (drag.current = null)}
      >
        {isPdf ? (
          // Frames are denied site-wide (X-Frame-Options), so PDFs open in the browser's own viewer instead.
          <a href={src} target="_blank" rel="noopener noreferrer" className="w-full h-full flex flex-col items-center justify-center gap-2 text-navy-900">
            <span className="text-4xl" aria-hidden="true">📄</span>
            <span className="text-sm font-bold underline">فتح إيصال PDF</span>
            <span className={`text-xs ${muted}`}>{proof.originalFileName}</span>
          </a>
        ) : (
          // eslint-disable-next-line @next/next/no-img-element -- signed, short-lived media URL served through the BFF
          <img
            src={src}
            alt={`إيصال التحويل المرفوع ${cairoDateTime(proof.uploadedAtUtc)}`}
            draggable={false}
            className="w-full h-full object-contain"
            style={{ transform: `translate(${offset.x}px, ${offset.y}px) scale(${scale})`, cursor: scale > 1 ? "grab" : "default" }}
          />
        )}
      </div>
      <div className="flex gap-2">
        {!isPdf && (
          <>
            <button type="button" onClick={() => zoom(0.5)} aria-label="تكبير" className="flex-1 min-h-[40px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold">
              تكبير +
            </button>
            <button
              type="button"
              onClick={() => {
                setScale(1);
                setOffset({ x: 0, y: 0 });
              }}
              aria-label="الحجم الأصلي"
              className="flex-1 min-h-[40px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
            >
              {toArabicDigits(Math.round(scale * 100))}٪
            </button>
            <button type="button" onClick={() => zoom(-0.5)} aria-label="تصغير" className="flex-1 min-h-[40px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold">
              تصغير −
            </button>
          </>
        )}
        <a href={src} target="_blank" rel="noopener noreferrer" className="flex-1 min-h-[40px] rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold flex items-center justify-center">
          فتح بالحجم الكامل
        </a>
      </div>
    </>
  );
}
