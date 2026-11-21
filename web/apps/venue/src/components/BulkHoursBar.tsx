"use client";

import React, { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CalendarCheck, CalendarX2, X } from "lucide-react";
import { inputClass, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { api, unwrap } from "@/lib/api";

export interface PickedHour {
  pitchId: string;
  startUtc: string;
  endUtc: string;
}

/** An existing manual booking or blocked period picked to be freed. */
export interface PickedTaken {
  id: string;
  blocked: boolean;
}

const STATUS = { confirmed: 0, tentative: 3 } as const;

/**
 * The bar under the calendar while hours are being picked: book them all at once (confirmed or tentative, customer optional),
 * or free the manual bookings and blocked periods picked so players can book those hours in the app.
 */
export function BulkHoursBar({
  venueId,
  hours,
  taken,
  canUnblock,
  onClear,
  onDone,
}: {
  venueId: string;
  hours: PickedHour[];
  taken: PickedTaken[];
  canUnblock: boolean;
  onClear: () => void;
  onDone: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const [booking, setBooking] = useState(false);
  const [status, setStatus] = useState<0 | 3>(STATUS.confirmed);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [price, setPrice] = useState("");
  const [error, setError] = useState<string | null>(null);

  const refresh = () =>
    queryClient.invalidateQueries({ predicate: (q) => ["calendar", "dashboard", "manual-booking-calendar"].includes(String(q.queryKey[0])) || String(q.queryKey[0]).startsWith("booking") });

  const book = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/manual/bulk", {
          params: { path: { venueId } },
          body: {
            slots: hours,
            paymentStatus: status,
            customerName: name.trim() || null,
            customerPhone: phone.trim() || null,
            pricePerHour: price.trim() ? Number(price) : null,
          },
        }),
      ),
    onSuccess: async (r) => {
      await refresh();
      setBooking(false);
      const failed = r.slots.filter((s) => !s.succeeded);
      const why = failed[0]?.errorCode === "Booking.PriceUnavailable" ? " (مفيش سعر للميعاد ده: اكتب سعر الساعة)" : failed.length ? " (كانت محجوزة أو مقفولة)" : "";
      onDone(
        `${status === STATUS.tentative ? "اتحجز مبدئياً" : "اتحجز"} ${toArabicDigits(r.succeeded)} ميعاد${
          failed.length ? `، و${toArabicDigits(failed.length)} ماتحجزش${why}` : ""
        }.`,
      );
    },
    onError: (e: Error) => setError(e.message),
  });

  const release = useMutation({
    mutationFn: async () => {
      const bookingIds = taken.filter((t) => !t.blocked).map((t) => t.id);
      const blocks = taken.filter((t) => t.blocked);
      let freed = 0;
      let failed = 0;
      if (bookingIds.length) {
        const r = await unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/manual/bulk-release", { params: { path: { venueId } }, body: { bookingIds } }));
        freed += r.succeeded;
        failed += r.failed;
      }

      for (const block of blocks) {
        const { response } = await api.DELETE("/api/v1/venue-manager/venues/{venueId}/blocked-slots/{id}", { params: { path: { venueId, id: block.id } } });
        if (response.ok) freed++;
        else failed++;
      }

      return { freed, failed };
    },
    onSuccess: async ({ freed, failed }) => {
      await refresh();
      onDone(`اتفتح ${toArabicDigits(freed)} ميعاد وبقى متاح للحجز في التطبيق${failed ? `، و${toArabicDigits(failed)} مااتفتحش` : ""}.`);
    },
    onError: (e: Error) => onDone(e.message),
  });

  const blockedPicked = taken.some((t) => t.blocked);
  const canRelease = taken.length > 0 && (!blockedPicked || canUnblock);

  return (
    <>
      <div
        role="region"
        aria-label="الساعات المحددة"
        className="sticky bottom-3 z-30 bg-navy-900 text-white rounded-2xl shadow-2xl px-4 py-3 flex flex-wrap items-center gap-3 animate-fade-in-up"
      >
        <span className="text-sm font-semibold flex-1 min-w-[180px]">
          {hours.length === 0 && taken.length === 0
            ? "دوس على الساعات الفاضية عشان تحجزها، أو على الحجوزات اليدوي والمقفول عشان تفتحه."
            : [hours.length ? `${toArabicDigits(hours.length)} ساعة فاضية` : null, taken.length ? `${toArabicDigits(taken.length)} محجوزة/مقفولة` : null]
                .filter(Boolean)
                .join(" · ")}
        </span>
        {hours.length > 0 && (
          <button type="button" onClick={() => { setError(null); setBooking(true); }} className="min-h-[40px] px-4 rounded-xl bg-brand-red hover:bg-brand-red-hover text-sm font-bold inline-flex items-center gap-2">
            <CalendarCheck className="size-4" />
            احجزهم
          </button>
        )}
        {taken.length > 0 && (
          <button
            type="button"
            disabled={!canRelease || release.isPending}
            onClick={() => release.mutate()}
            title={canRelease ? undefined : "دورك مايسمحش بفتح المواعيد المقفولة"}
            className="min-h-[40px] px-4 rounded-xl bg-white text-navy-900 text-sm font-bold inline-flex items-center gap-2 disabled:opacity-50"
          >
            <CalendarX2 className="size-4" />
            {release.isPending ? "جاري…" : "خليهم فاضيين"}
          </button>
        )}
        <button type="button" onClick={onClear} aria-label="إلغاء التحديد" className="w-10 h-10 rounded-xl bg-white/10 hover:bg-white/20 grid place-items-center">
          <X className="size-4" />
        </button>
      </div>

      {booking && (
        <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] backdrop-blur-sm flex items-center justify-center p-4">
          <form
            role="dialog"
            aria-modal="true"
            aria-labelledby="bulk-title"
            className="bg-white rounded-[20px] p-6 w-full max-w-[440px] flex flex-col gap-3 shadow-2xl"
            onSubmit={(e) => {
              e.preventDefault();
              if (!book.isPending) book.mutate();
            }}
          >
            <h2 id="bulk-title" className="text-[19px] font-bold">
              حجز {toArabicDigits(hours.length)} ساعة
            </h2>
            <p className={`text-xs ${muted}`}>الساعات اللي ورا بعض في نفس الملعب بتبقى حجز واحد. كلها هتبان محجوزة في التطبيق.</p>
            <fieldset className="flex gap-2">
              <legend className="text-[13px] font-semibold mb-1">حالة الحجز</legend>
              {(
                [
                  [STATUS.confirmed, "متثبت"],
                  [STATUS.tentative, "مبدئي (لسه مش متثبت)"],
                ] as const
              ).map(([value, label]) => (
                <button
                  key={value}
                  type="button"
                  aria-pressed={status === value}
                  onClick={() => setStatus(value)}
                  className={`flex-1 min-h-[42px] rounded-xl border text-sm ${status === value ? "bg-navy-900 text-white border-navy-900 font-bold" : "bg-white border-[#C9CFD9] hover:bg-slate-50"}`}
                >
                  {label}
                </button>
              ))}
            </fieldset>
            <label className="flex flex-col gap-1 text-[13px] font-semibold">
              محجوز لمين؟ (اختياري)
              <input value={name} maxLength={100} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="اسم العميل أو الفريق" />
            </label>
            <label className="flex flex-col gap-1 text-[13px] font-semibold">
              موبايله (اختياري)
              <input value={phone} dir="ltr" inputMode="tel" onChange={(e) => setPhone(e.target.value)} className={inputClass} placeholder="01xxxxxxxxx" />
            </label>
            <label className="flex flex-col gap-1 text-[13px] font-semibold">
              سعر الساعة (اختياري)
              <input value={price} type="number" min={1} inputMode="numeric" onChange={(e) => setPrice(e.target.value)} className={inputClass} placeholder="حسب أسعار الملعب" />
            </label>
            {error && (
              <p role="alert" className="text-xs bg-[#FDECEA] text-[#A8261C] rounded-lg px-3 py-2">
                {error}
              </p>
            )}
            <div className="flex gap-2 justify-end">
              <button type="button" onClick={() => setBooking(false)} className={secondaryBtn}>
                رجوع
              </button>
              <button type="submit" disabled={book.isPending} className={primaryBtn}>
                {book.isPending ? "جاري…" : "احجزهم"}
              </button>
            </div>
          </form>
        </div>
      )}
    </>
  );
}
