"use client";

import React, { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";
import { inputClass, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { addDays, cairoDay, cairoDayStartUtc, cairoTime, cairoToday, money } from "@/lib/booking-format";
import { CalendarDays, Check, ChevronLeft, ChevronRight, Clock, Lock, RefreshCw, TriangleAlert } from "lucide-react";

export interface ManualBookingDefaults {
  pitchId?: string;
  date?: string;
  /** Minutes after local midnight, e.g. 20 * 60 for 8 PM (values past 24h fall on the next calendar day). */
  startMinutes?: number;
}

const DURATIONS = [60, 90, 120] as const;

/** Grid hours: 08:00 to 02:00 next day (matches venue calendar operation window). */
const FIRST_HOUR = 8;
const LAST_HOUR = 26;
const HOURS = Array.from({ length: LAST_HOUR - FIRST_HOUR }, (_, i) => FIRST_HOUR + i);

const hourLabel = (h: number) => {
  const norm = h % 24;
  const h12 = norm % 12 === 0 ? 12 : norm % 12;
  const period = norm < 12 ? "ص" : "م";
  return `${toArabicDigits(h12)}:00 ${period}`;
};

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
  showSchedule = false,
}: {
  defaults?: ManualBookingDefaults;
  onDone: (message: string) => void;
  onCancel?: () => void;
  showSchedule?: boolean;
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

  // Fetch all calendar slots for this venue & date range
  const calendarQuery = useQuery({
    queryKey: ["manual-booking-calendar", venueId, date, pitchId],
    enabled: !!venueId && !!pitchId && !!date,
    refetchInterval: 30_000,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/venue-manager/venues/{venueId}/calendar", {
          params: {
            path: { venueId: venueId! },
            query: {
              rangeStartUtc: cairoDayStartUtc(date),
              rangeEndUtc: cairoDayStartUtc(addDays(date, 2)),
              pitchId: pitchId || undefined,
            },
          },
        }),
      ),
  });

  const slots = useMemo(
    () => (calendarQuery.data ?? []).filter((s) => !pitchId || s.pitchId === pitchId),
    [calendarQuery.data, pitchId],
  );

  const reqStartMs = new Date(startUtc).getTime();
  const reqEndMs = new Date(endUtc).getTime();

  // Check if currently selected time window overlaps any booked/blocked slot
  const conflictSlot = useMemo(() => {
    return slots.find((s) => {
      if (pitchId && s.pitchId !== pitchId) return false;
      const sStartMs = new Date(s.startUtc).getTime();
      const sEndMs = new Date(s.endUtc).getTime();
      return reqStartMs < sEndMs && reqEndMs > sStartMs;
    });
  }, [slots, pitchId, reqStartMs, reqEndMs]);

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
      await queryClient.invalidateQueries({ predicate: (q) => ["calendar", "dashboard", "manual-booking-calendar"].includes(String(q.queryKey[0])) || String(q.queryKey[0]).startsWith("booking") });
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

    if (conflictSlot) {
      setErrors({
        time: conflictSlot.status === "Blocked"
          ? `هذا الميعاد محظور وغير متاح (${conflictSlot.reason || "محظور"})`
          : `هذا الميعاد محجوز مسبقاً (${conflictSlot.customerName || "حجز قائم"})`,
      });
      return;
    }

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

  const formElement = (
    <form onSubmit={submit} noValidate className="flex flex-col gap-3.5">
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
                className={`flex-1 min-h-[44px] rounded-xl border text-sm ${duration === d ? "bg-navy-900 text-white border-navy-900 font-bold shadow-xs" : "bg-white border-[#C9CFD9] hover:bg-slate-50"}`}
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
                className={`flex-1 min-w-[120px] min-h-[44px] rounded-xl border text-sm ${payment === option.value ? "bg-navy-900 text-white border-navy-900 font-bold shadow-xs" : "bg-white border-[#C9CFD9] hover:bg-slate-50"}`}
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

      {/* Overlap & Double-booking Alert */}
      {conflictSlot && (
        <div role="alert" className="p-3.5 rounded-xl bg-[#FDECEA] border border-[#F5C2C7] text-[#842029] text-xs flex items-start gap-2.5 animate-in fade-in duration-150">
          <TriangleAlert className="size-5 shrink-0 text-[#A8261C] mt-0.5" />
          <div className="flex flex-col gap-0.5">
            <strong className="font-bold text-[13px] text-[#A8261C]">الميعاد المحدد غير متاح للحجز!</strong>
            <span>
              الوقت المختار ({cairoTime(startUtc)} – {cairoTime(endUtc)}) يتداخل مع{" "}
              {conflictSlot.status === "Blocked"
                ? `فترة مغلقة أو محظورة (${conflictSlot.reason || "محظور للصيانة"})`
                : `حجز العميل ${conflictSlot.customerName || "حجز مؤكد"} (${cairoTime(conflictSlot.startUtc)} – ${cairoTime(conflictSlot.endUtc)})`}.
              برجاء اختيار وقت آخر متاح من الجدول.
            </span>
          </div>
        </div>
      )}

      {serverError && (
        <p role="alert" className="text-sm bg-[#FDECEA] text-[#A8261C] rounded-xl px-3 py-2">
          {serverError}
        </p>
      )}

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={create.isPending || !!conflictSlot}
          className={`${primaryBtn} disabled:opacity-50 disabled:cursor-not-allowed`}
        >
          {conflictSlot
            ? "الوقت غير متاح (محجوز مسبقاً)"
            : create.isPending
              ? "بيتسجل…"
              : payment === PAYMENT.awaiting
                ? "احجز الميعاد"
                : "تأكيد الحجز"}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel} className={secondaryBtn}>
            رجوع
          </button>
        )}
      </div>
    </form>
  );

  if (!showSchedule) {
    return formElement;
  }

  // Selected Pitch info
  const currentPitch = pitches.find((p) => p.id === pitchId);

  return (
    <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
      {/* Left Column: Booking Form */}
      <div className="lg:col-span-5 xl:col-span-5 bg-white rounded-[20px] p-5 shadow-sm border border-slate-200">
        <div className="flex items-center justify-between pb-3 mb-4 border-b border-slate-100">
          <h2 className="text-base font-extrabold text-slate-900 flex items-center gap-2">
            <span className="size-2 rounded-full bg-emerald-600" />
            بيانات الحجز
          </h2>
          <span className="text-xs text-slate-500 font-medium">خطوة واحدة للتأكيد</span>
        </div>
        {formElement}
      </div>

      {/* Right Column: Live Interactive Day Schedule */}
      <div className="lg:col-span-7 xl:col-span-7 bg-white rounded-[20px] p-5 shadow-sm border border-slate-200 flex flex-col gap-4">
        {/* Header & Date Controls */}
        <div className="flex flex-col gap-3 pb-3 border-b border-slate-100">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <div className="size-9 rounded-xl bg-emerald-50 text-emerald-800 flex items-center justify-center font-bold">
                <CalendarDays className="size-5" />
              </div>
              <div>
                <h2 className="text-base font-extrabold text-slate-900">جدول الحجوزات والمواعيد</h2>
                <p className="text-xs text-slate-500">اضغط على أي وقت متاح لاختياره تلقائياً للنموذج</p>
              </div>
            </div>

            {/* Pitch Pills */}
            {pitches.length > 1 && (
              <div className="flex flex-wrap gap-1">
                {pitches.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => setPitchId(p.id)}
                    className={`px-2.5 py-1 text-xs rounded-lg font-bold transition-all ${
                      pitchId === p.id
                        ? "bg-navy-900 text-white shadow-xs"
                        : "bg-slate-100 text-slate-600 hover:bg-slate-200"
                    }`}
                  >
                    {p.name}
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* Date Navigator Bar */}
          <div className="flex flex-wrap items-center justify-between gap-2 p-2.5 rounded-xl bg-slate-50 border border-slate-200">
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setDate(cairoToday())}
                className={`px-2.5 py-1 text-xs font-bold rounded-lg transition-all ${
                  date === cairoToday()
                    ? "bg-emerald-700 text-white shadow-2xs"
                    : "bg-white border border-slate-300 text-slate-700 hover:bg-slate-100"
                }`}
              >
                اليوم
              </button>
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  title="اليوم السابق"
                  onClick={() => setDate(addDays(date, -1))}
                  className="p-1 rounded-lg border border-slate-300 bg-white text-slate-600 hover:bg-slate-100"
                >
                  <ChevronRight className="size-4" />
                </button>
                <span className="text-xs font-bold text-slate-900 px-2 min-w-[130px] text-center">
                  {cairoDay(cairoDayStartUtc(date))}
                </span>
                <button
                  type="button"
                  title="اليوم التالي"
                  onClick={() => setDate(addDays(date, 1))}
                  className="p-1 rounded-lg border border-slate-300 bg-white text-slate-600 hover:bg-slate-100"
                >
                  <ChevronLeft className="size-4" />
                </button>
              </div>
            </div>

            <div className="flex items-center gap-2 text-xs">
              <span className="font-mono text-slate-600 bg-white px-2 py-0.5 rounded border border-slate-200" dir="ltr">
                {date}
              </span>
              {currentPitch && (
                <span className="font-bold text-emerald-800 bg-emerald-50 px-2 py-0.5 rounded border border-emerald-200">
                  {currentPitch.name}
                </span>
              )}
            </div>
          </div>

          {/* Color Legend */}
          <div className="flex flex-wrap items-center gap-3 text-xs pt-1 text-slate-600">
            <span className="flex items-center gap-1.5 font-medium">
              <span className="size-3 rounded-full bg-emerald-500 border border-emerald-600" />
              متاح (اضغط لاختياره)
            </span>
            <span className="flex items-center gap-1.5 font-medium">
              <span className="size-3 rounded-full bg-navy-900" />
              محجوز ومؤكد
            </span>
            <span className="flex items-center gap-1.5 font-medium">
              <span className="size-3 rounded-full bg-amber-400 border border-amber-500" />
              قيد المراجعة / العربون
            </span>
            <span className="flex items-center gap-1.5 font-medium">
              <span className="size-3 rounded-full bg-slate-400 border border-slate-500" />
              محظور ومغلق
            </span>
          </div>
        </div>

        {/* Slots Timeline */}
        <div className="flex flex-col gap-2 max-h-[580px] overflow-y-auto pr-1">
          {calendarQuery.isLoading ? (
            <div className="p-8 text-center text-sm text-slate-500 flex items-center justify-center gap-2">
              <RefreshCw className="size-4 animate-spin text-emerald-600" />
              <span>جاري تحميل مواعيد اليوم…</span>
            </div>
          ) : (
            HOURS.map((hourVal) => {
              const hourMinutes = hourVal * 60;
              const slotTimeStr = toHhmm(hourMinutes);
              const slotStartMs = new Date(cairoDayStartUtc(date)).getTime() + hourMinutes * 60_000;
              const slotEndMs = slotStartMs + 60 * 60_000;

              // Find if any booking or blocked slot overlaps this specific hour
              const overlapping = slots.filter((s) => {
                const sStart = new Date(s.startUtc).getTime();
                const sEnd = new Date(s.endUtc).getTime();
                return sStart < slotEndMs && sEnd > slotStartMs;
              });

              // Is this slot currently requested by the form?
              const isSelectedByForm = slotStartMs < reqEndMs && slotEndMs > reqStartMs;
              const isStartingHour = slotTimeStr === time;

              if (overlapping.length > 0) {
                const first = overlapping[0];
                const isBlocked = first.status === "Blocked";
                const isPending = first.status === "PendingDeposit" || first.status === "ProofSubmitted" || first.status === "UnderReview";

                return (
                  <div
                    key={hourVal}
                    className={`p-3 rounded-xl border flex items-center justify-between gap-3 text-xs transition-all ${
                      isSelectedByForm
                        ? "border-red-400 bg-red-50/60 ring-2 ring-red-400"
                        : isBlocked
                          ? "bg-[repeating-linear-gradient(135deg,#F1F3F7_0_6px,#E8ECF2_6px_12px)] border-slate-300 text-slate-700"
                          : isPending
                            ? "bg-amber-50 border-amber-300 text-amber-900"
                            : "bg-slate-50 border-slate-300 text-slate-900"
                    }`}
                  >
                    <div className="flex items-center gap-2.5 min-w-0">
                      <span className="font-mono font-bold text-slate-600 bg-white px-2 py-1 rounded border border-slate-200 shrink-0">
                        {hourLabel(hourVal)}
                      </span>
                      <div className="flex flex-col min-w-0">
                        <div className="flex items-center gap-1.5 font-bold truncate">
                          {isBlocked ? (
                            <>
                              <Lock className="size-3.5 text-slate-500 shrink-0" />
                              <span className="text-slate-800">{first.reason || "فترة محظورة ومغلقة"}</span>
                            </>
                          ) : (
                            <>
                              <span className="truncate">{first.customerName || "حجز مسجل"}</span>
                              {first.bookingReference && (
                                <span className="font-mono text-[10px] text-slate-500 bg-white px-1.5 py-0.2 rounded border border-slate-200">
                                  #{first.bookingReference}
                                </span>
                              )}
                            </>
                          )}
                        </div>
                        <span className="text-[11px] text-slate-500 mt-0.5">
                          {cairoTime(first.startUtc)} – {cairoTime(first.endUtc)}
                        </span>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 shrink-0">
                      {isSelectedByForm && (
                        <span className="text-[11px] font-bold text-red-600 bg-white px-2 py-0.5 rounded border border-red-200">
                          يتعارض مع اختيارك!
                        </span>
                      )}
                      <span
                        className={`font-bold px-2.5 py-1 rounded-lg text-[11px] ${
                          isBlocked
                            ? "bg-slate-200 text-slate-700"
                            : isPending
                              ? "bg-amber-200 text-amber-900"
                              : "bg-navy-900 text-white"
                        }`}
                      >
                        {isBlocked ? "محظور" : isPending ? "بانتظار العربون" : "محجوز"}
                      </span>
                    </div>
                  </div>
                );
              }

              // Slot is FREE / AVAILABLE
              return (
                <button
                  key={hourVal}
                  type="button"
                  onClick={() => setTime(slotTimeStr)}
                  className={`p-3 rounded-xl border flex items-center justify-between gap-3 text-xs text-right transition-all group ${
                    isStartingHour
                      ? "bg-emerald-50 border-emerald-600 ring-2 ring-emerald-600 text-emerald-950 font-bold shadow-xs"
                      : isSelectedByForm
                        ? "bg-emerald-50/60 border-emerald-400 text-emerald-900"
                        : "bg-white border-slate-200 hover:border-emerald-500 hover:bg-emerald-50/30 text-slate-800"
                  }`}
                >
                  <div className="flex items-center gap-2.5">
                    <span
                      className={`font-mono px-2 py-1 rounded border shrink-0 transition-colors ${
                        isStartingHour
                          ? "bg-emerald-700 text-white border-emerald-700 font-bold"
                          : "bg-slate-100 text-slate-700 border-slate-200 group-hover:bg-white"
                      }`}
                    >
                      {hourLabel(hourVal)}
                    </span>
                    <span className="font-semibold text-emerald-800 flex items-center gap-1.5">
                      <span className="size-2 rounded-full bg-emerald-500 shrink-0" />
                      متاح للحجز ({hourLabel(hourVal)} إلى {hourLabel(hourVal + 1)})
                    </span>
                  </div>

                  <div className="flex items-center gap-1.5 shrink-0">
                    {isStartingHour ? (
                      <span className="bg-emerald-700 text-white text-[11px] font-bold px-2 py-0.5 rounded-md flex items-center gap-1">
                        <Check className="size-3" />
                        الميعاد المختار حالياً
                      </span>
                    ) : (
                      <span className="text-[11px] text-slate-400 group-hover:text-emerald-700 font-medium transition-colors">
                        اضغط للاختيار ‹
                      </span>
                    )}
                  </div>
                </button>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
