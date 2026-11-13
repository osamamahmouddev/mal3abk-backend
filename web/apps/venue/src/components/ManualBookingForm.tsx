"use client";

import React, { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";
import { inputClass, muted, primaryBtn, secondaryBtn } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { cairoDayStartUtc, cairoToday, money } from "@/lib/booking-format";

export interface ManualBookingDefaults {
  pitchId?: string;
  date?: string;
  /** Minutes after local midnight, e.g. 20 * 60 for 8 PM (values past 24h fall on the next calendar day). */
  startMinutes?: number;
}

const DURATIONS = [60, 90, 120] as const;

/** What the customer has paid so far; the numbers are the API's `paymentStatus` values. */
const PAYMENT = {
  full: 0,
  deposit: 1,
  awaiting: 2,
} as const;
type Payment = (typeof PAYMENT)[keyof typeof PAYMENT];

const PAYMENT_OPTIONS: { value: Payment; label: string; hint: string }[] = [
  { value: PAYMENT.full, label: "مدفوع بالكامل", hint: "الحجز بيتأكد على طول." },
  { value: PAYMENT.deposit, label: "دفع عربون", hint: "الحجز بيتأكد والباقي بيتدفع في الملعب." },
  { value: PAYMENT.awaiting, label: "في انتظار العربون", hint: "الميعاد بيتحجز للعميل لحد ما العربون يوصل، ولو المهلة خلصت بيتفتح تاني." },
];

const schema = z.object({
  pitchId: z.string().uuid("اختار الملعب"),
  customerName: z.string().trim().min(2, "اكتب اسم العميل").max(100),
  customerPhone: z
    .string()
    .trim()
    .regex(/^(\+?20|0)?1[0125]\d{8}$/, "اكتب رقم موبايل مصري صحيح"),
  price: z.number({ message: "اكتب السعر" }).positive("السعر لازم يكون أكبر من صفر").max(100_000),
  notes: z.string().max(500).optional(),
});

/** The partial-payment amount: more than zero, and below the price when it was already paid (use "paid in full" otherwise). */
function depositError(payment: Payment, price: number | undefined, deposit: string): string | undefined {
  if (payment === PAYMENT.full) return undefined;
  const amount = Number(deposit);
  if (deposit.trim() === "" || !Number.isFinite(amount) || amount <= 0) {
    return payment === PAYMENT.deposit ? "اكتب العربون اللي اتدفع" : "اكتب العربون المطلوب";
  }
  if (price !== undefined && payment === PAYMENT.deposit && amount >= price) return "العربون لازم يكون أقل من السعر — لو دفع كله اختار «مدفوع بالكامل»";
  if (price !== undefined && amount > price) return "العربون ما ينفعش يزيد عن السعر";
  return undefined;
}

const pad = (n: number) => String(n).padStart(2, "0");
const toHhmm = (minutes: number) => `${pad(Math.floor(minutes / 60) % 24)}:${pad(minutes % 60)}`;

/**
 * Manual (phone / walk-in) booking: confirmed straight away, nothing paid through the app. The price is suggested from the
 * venue's price rules and can be changed. Shared by the calendar's slot dialog and the manual booking page.
 */
export function ManualBookingForm({
  defaults,
  onDone,
  onCancel,
}: {
  defaults?: ManualBookingDefaults;
  onDone: (message: string) => void;
  onCancel?: () => void;
}) {
  const queryClient = useQueryClient();
  const { venue, venueId } = useVenue();
  const pitches = venue?.pitches.filter((p) => p.isActive) ?? [];
  const [pitchId, setPitchId] = useState(defaults?.pitchId ?? pitches[0]?.id ?? "");
  const [date, setDate] = useState(defaults?.date ?? cairoToday());
  const [time, setTime] = useState(toHhmm(defaults?.startMinutes ?? 20 * 60));
  const [duration, setDuration] = useState<number>(60);
  const [customerName, setCustomerName] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [price, setPrice] = useState<string>("");
  const [notes, setNotes] = useState("");
  const [payment, setPayment] = useState<Payment>(PAYMENT.full);
  const [deposit, setDeposit] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);

  const [h, m] = time.split(":").map(Number);
  const startUtc = new Date(new Date(cairoDayStartUtc(date)).getTime() + (h * 60 + m) * 60_000).toISOString();
  const endUtc = new Date(new Date(startUtc).getTime() + duration * 60_000).toISOString();

  // Suggest the price from the venue's rules whenever the slot changes (the staff can still edit it).
  useEffect(() => {
    if (!venueId || !pitchId) return;
    let cancelled = false;
    api
      .POST("/api/v1/venue-manager/venues/{venueId}/price-rules/preview", { params: { path: { venueId } }, body: { pitchId, startUtc, endUtc } })
      .then(({ data, response }) => {
        const total = (data as unknown as { current?: { total?: number } } | undefined)?.current?.total;
        if (!cancelled && response.ok && typeof total === "number") setPrice(String(total));
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [venueId, pitchId, startUtc, endUtc]);

  const create = useMutation({
    mutationFn: (body: { pitchId: string; startUtc: string; endUtc: string; price: number; customerName: string; customerPhone: string; notes?: string; paymentStatus: Payment; depositAmount: number | null }) =>
      unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/manual", { params: { path: { venueId: venueId! } }, body: { ...body, currency: "EGP", notes: body.notes || null } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ predicate: (q) => ["calendar", "dashboard"].includes(String(q.queryKey[0])) || String(q.queryKey[0]).startsWith("booking") });
      const name = customerName.trim();
      onDone(
        payment === PAYMENT.awaiting
          ? `اتسجل حجز ${name} وبيستنى العربون (${money(Number(deposit))}).`
          : payment === PAYMENT.deposit
            ? `اتسجل حجز ${name} ومؤكد بعربون ${money(Number(deposit))}.`
            : `اتسجل حجز ${name} ومؤكد على طول.`,
      );
    },
    onError: (e: Error) => setServerError(e.message),
  });

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setServerError(null);
    const parsed = schema.safeParse({ pitchId, customerName, customerPhone, price: price === "" ? undefined : Number(price), notes: notes || undefined });
    const found: Record<string, string> = parsed.success ? {} : Object.fromEntries(parsed.error.issues.map((i) => [String(i.path[0]), i.message]));
    const problem = depositError(payment, price === "" ? undefined : Number(price), deposit);
    if (problem) found.deposit = problem;
    if (!parsed.success || problem) {
      setErrors(found);
      return;
    }
    setErrors({});
    create.mutate({ ...parsed.data, startUtc, endUtc, paymentStatus: payment, depositAmount: payment === PAYMENT.full ? null : Number(deposit) });
  };

  const field = (id: string, label: string, control: React.ReactNode) => (
    <label htmlFor={id} className="flex flex-col gap-1 text-sm font-semibold">
      {label}
      {control}
      {errors[id] && (
        <span id={`${id}-error`} role="alert" className="text-xs font-normal text-[#A8261C]">
          {errors[id]}
        </span>
      )}
    </label>
  );

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-3">
      <div className="grid gap-3 sm:grid-cols-2">
        {field(
          "pitchId",
          "الملعب",
          <select id="pitchId" value={pitchId} onChange={(e) => setPitchId(e.target.value)} className={inputClass} aria-invalid={!!errors.pitchId}>
            {pitches.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>,
        )}
        {field("date", "اليوم", <input id="date" type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputClass} />)}
        {field("time", "الساعة", <input id="time" type="time" step={900} value={time} onChange={(e) => setTime(e.target.value)} className={inputClass} />)}
        <fieldset className="flex flex-col gap-1 text-sm font-semibold">
          <legend className="mb-1">المدة</legend>
          <div className="flex gap-2">
            {DURATIONS.map((d) => (
              <button
                key={d}
                type="button"
                aria-pressed={duration === d}
                onClick={() => setDuration(d)}
                className={`flex-1 min-h-[44px] rounded-xl border text-sm ${duration === d ? "bg-navy-900 text-white border-navy-900 font-bold" : "bg-white border-[#C9CFD9]"}`}
              >
                {d === 60 ? "ساعة" : d === 90 ? "ساعة ونص" : "ساعتين"}
              </button>
            ))}
          </div>
        </fieldset>
        {field(
          "customerName",
          "اسم العميل",
          <input id="customerName" value={customerName} maxLength={100} onChange={(e) => setCustomerName(e.target.value)} className={inputClass} aria-invalid={!!errors.customerName} />,
        )}
        {field(
          "customerPhone",
          "موبايل العميل",
          <input
            id="customerPhone"
            dir="ltr"
            inputMode="tel"
            value={customerPhone}
            onChange={(e) => setCustomerPhone(e.target.value)}
            placeholder="01xxxxxxxxx"
            className={inputClass}
            aria-invalid={!!errors.customerPhone}
          />,
        )}
        {field(
          "price",
          "السعر (ج.م)",
          <input id="price" type="number" min={1} inputMode="numeric" value={price} onChange={(e) => setPrice(e.target.value)} className={inputClass} aria-invalid={!!errors.price} />,
        )}
        <fieldset className="flex flex-col gap-1 text-sm font-semibold sm:col-span-2">
          <legend className="mb-1">حالة الدفع</legend>
          <div className="flex flex-wrap gap-2">
            {PAYMENT_OPTIONS.map((option) => (
              <button
                key={option.value}
                type="button"
                aria-pressed={payment === option.value}
                onClick={() => setPayment(option.value)}
                className={`flex-1 min-w-[120px] min-h-[44px] rounded-xl border text-sm ${payment === option.value ? "bg-navy-900 text-white border-navy-900 font-bold" : "bg-white border-[#C9CFD9]"}`}
              >
                {option.label}
              </button>
            ))}
          </div>
          <span className={`text-xs font-normal ${muted}`}>{PAYMENT_OPTIONS.find((o) => o.value === payment)?.hint}</span>
        </fieldset>
        {payment !== PAYMENT.full &&
          field(
            "deposit",
            payment === PAYMENT.deposit ? "العربون المدفوع (ج.م)" : "العربون المطلوب (ج.م)",
            <input id="deposit" type="number" min={1} inputMode="numeric" value={deposit} onChange={(e) => setDeposit(e.target.value)} className={inputClass} aria-invalid={!!errors.deposit} />,
          )}
        {field("notes", "ملاحظة (اختياري)", <input id="notes" value={notes} maxLength={500} onChange={(e) => setNotes(e.target.value)} className={inputClass} />)}
      </div>
      <p className={`text-[13px] ${muted}`}>
        {payment === PAYMENT.full && <>الحجز اليدوي بيتأكد على طول{price ? ` (${money(Number(price))})` : ""}. </>}
        {payment === PAYMENT.deposit && price && Number(deposit) > 0 && Number(deposit) < Number(price) && (
          <>الباقي في الملعب: {money(Number(price) - Number(deposit))}. </>
        )}
        لو الميعاد اتحجز من التطبيق في نفس اللحظة هيظهرلك تنبيه.
      </p>
      {serverError && (
        <p role="alert" className="text-sm bg-[#FDECEA] text-[#A8261C] rounded-xl px-3 py-2">
          {serverError}
        </p>
      )}
      <div className="flex gap-2">
        <button type="submit" disabled={create.isPending} className={`${primaryBtn} disabled:opacity-60`}>
          {create.isPending ? "بيتسجل…" : payment === PAYMENT.awaiting ? "احجز الميعاد" : "تأكيد الحجز"}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel} className={secondaryBtn}>
            رجوع
          </button>
        )}
      </div>
    </form>
  );
}
