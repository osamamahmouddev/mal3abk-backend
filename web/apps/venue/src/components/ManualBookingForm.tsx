"use client";

import React, { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";
import { inputClass, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { addDays, cairoDay, cairoDayStartUtc, cairoTime, cairoToday, computeStartMinutes, formatBusinessSlotSummary, money, parseStartMinutes } from "@/lib/booking-format";
import { CalendarDays, Check, ChevronLeft, ChevronRight, Clock, Lock, Moon, RefreshCw, Search, Sun, TriangleAlert, UserCheck, Users, X } from "lucide-react";

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
  const norm = ((h % 24) + 24) % 24;
  const h12 = norm % 12 === 0 ? 12 : norm % 12;
  const period = (h >= 24 || norm < 5 || norm >= 17) ? "بليل" : norm < 12 ? "الصبح" : "الضهر";
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

function ArabicTimePicker({
  date,
  valueMinutes,
  onChange,
  error,
}: {
  date: string;
  valueMinutes: number;
  onChange: (minutes: number) => void;
  error?: string;
}) {
  const { hour12, minute, period } = parseStartMinutes(valueMinutes);

  const quickChips = [
    { label: "8:00 بليل", mins: 20 * 60 },
    { label: "9:00 بليل", mins: 21 * 60 },
    { label: "10:00 بليل", mins: 22 * 60 },
    { label: "11:00 بليل", mins: 23 * 60 },
    { label: "12:00 بليل", mins: 24 * 60 },
    { label: "1:00 بليل", mins: 25 * 60 },
    { label: "2:00 بليل", mins: 26 * 60 },
  ];

  return (
    <div className="flex flex-col gap-2 sm:col-span-2">
      <div className="flex items-center justify-between">
        <label htmlFor="arabic-time-hour" className="text-sm font-semibold text-slate-800">
          الساعة والميعاد
        </label>
        <span className="text-xs text-slate-500">حجوزات السهرة بعد 12 بليل تُربط بسهرة نفس اليوم تلقائياً</span>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-12 gap-2.5 p-3.5 bg-slate-50 border border-slate-200 rounded-2xl">
        {/* Hour */}
        <div className="sm:col-span-5 flex flex-col gap-1">
          <label htmlFor="arabic-time-hour" className="text-xs font-semibold text-slate-600">
            الساعة
          </label>
          <select
            id="arabic-time-hour"
            value={hour12}
            onChange={(e) => {
              const newH = Number(e.target.value);
              onChange(computeStartMinutes(newH, minute, period));
            }}
            className={`${inputClass} bg-white font-bold text-slate-900`}
          >
            <option value={12}>12 ({period === "night" ? "منتصف الليل" : "الظهر"})</option>
            <option value={1}>1</option>
            <option value={2}>2</option>
            <option value={3}>3</option>
            <option value={4}>4</option>
            <option value={5}>5</option>
            <option value={6}>6</option>
            <option value={7}>7</option>
            <option value={8}>8</option>
            <option value={9}>9</option>
            <option value={10}>10</option>
            <option value={11}>11</option>
          </select>
        </div>

        {/* Minute */}
        <div className="sm:col-span-3 flex flex-col gap-1">
          <label htmlFor="arabic-time-minute" className="text-xs font-semibold text-slate-600">
            الدقيقة
          </label>
          <select
            id="arabic-time-minute"
            value={minute}
            onChange={(e) => {
              const newM = Number(e.target.value);
              onChange(computeStartMinutes(hour12, newM, period));
            }}
            className={`${inputClass} bg-white text-slate-900`}
          >
            <option value={0}>:00 (على رأس الساعة)</option>
            <option value={15}>:15 (وربع)</option>
            <option value={30}>:30 (ونصف)</option>
            <option value={45}>:45 (إلا ربع)</option>
          </select>
        </div>

        {/* Period (الصبح أو بليل) */}
        <div className="sm:col-span-4 flex flex-col gap-1">
          <span className="text-xs font-semibold text-slate-600">الفترة</span>
          <div className="grid grid-cols-2 gap-1.5 h-[42px]" role="group" aria-label="فترة الحجز">
            <button
              type="button"
              onClick={() => onChange(computeStartMinutes(hour12, minute, "night"))}
              className={`rounded-xl text-xs font-bold flex items-center justify-center gap-1.5 transition-all cursor-pointer ${
                period === "night"
                  ? "bg-navy-900 text-white shadow-xs"
                  : "bg-white border border-slate-300 text-slate-700 hover:bg-slate-100"
              }`}
            >
              <Moon className="size-3.5" />
              <span>بليل</span>
            </button>
            <button
              type="button"
              onClick={() => onChange(computeStartMinutes(hour12, minute, "day"))}
              className={`rounded-xl text-xs font-bold flex items-center justify-center gap-1.5 transition-all cursor-pointer ${
                period === "day"
                  ? "bg-amber-600 text-white shadow-xs"
                  : "bg-white border border-slate-300 text-slate-700 hover:bg-slate-100"
              }`}
            >
              <Sun className="size-3.5" />
              <span>الصبح</span>
            </button>
          </div>
        </div>
      </div>

      {/* Quick match time chips */}
      <div className="flex flex-wrap items-center gap-1.5 pt-0.5">
        <span className="text-[11px] text-slate-500 font-medium">مواعيد سريعة:</span>
        {quickChips.map((chip) => (
          <button
            key={chip.mins}
            type="button"
            onClick={() => onChange(chip.mins)}
            className={`px-2.5 py-1 text-[11px] rounded-lg border font-semibold transition-all cursor-pointer ${
              valueMinutes === chip.mins
                ? "bg-emerald-700 text-white border-emerald-700 shadow-2xs font-bold"
                : "bg-white border-slate-200 text-slate-700 hover:bg-slate-100 hover:border-slate-300"
            }`}
          >
            {chip.label}
          </button>
        ))}
      </div>

      {/* Reassurance Banner */}
      <div
        className={`p-2.5 rounded-xl border text-xs flex items-center gap-2 transition-all ${
          valueMinutes >= 1440
            ? "bg-indigo-50/90 border-indigo-200 text-indigo-950 font-medium shadow-2xs"
            : "bg-emerald-50/90 border-emerald-200 text-emerald-950 font-medium shadow-2xs"
        }`}
      >
        <Clock className="size-4 shrink-0 text-slate-500" />
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="font-bold">الميعاد المحدد:</span>
          <span>{formatBusinessSlotSummary(date, valueMinutes)}</span>
        </div>
      </div>

      {error && (
        <span role="alert" className="text-xs font-normal text-[#A8261C]">
          {error}
        </span>
      )}
    </div>
  );
}

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
  const initialMinutes = typeof defaults?.startMinutes === "number" && defaults.startMinutes >= 0
    ? defaults.startMinutes
    : 20 * 60;
  const [startMinutes, setStartMinutes] = useState<number>(initialMinutes);
  const [duration, setDuration] = useState<number>(60);
  const [customerName, setCustomerName] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [price, setPrice] = useState<string>("");
  const [notes, setNotes] = useState("");
  const [payment, setPayment] = useState<Payment>(PAYMENT.full);
  const [deposit, setDeposit] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);

  // Customer autocomplete & picker state
  const [customerSearch, setCustomerSearch] = useState("");
  const [isCustomerDropdownOpen, setIsCustomerDropdownOpen] = useState(false);
  const [selectedCustomerMeta, setSelectedCustomerMeta] = useState<{
    totalBookings: number;
    noShows: number;
    isRegisteredPlayer: boolean;
  } | null>(null);
  const customerDropdownRef = useRef<HTMLDivElement>(null);

  // Debounced search query for venue customers
  const [debouncedCustomerSearch, setDebouncedCustomerSearch] = useState("");
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedCustomerSearch(customerSearch.trim());
    }, 250);
    return () => clearTimeout(timer);
  }, [customerSearch]);

  const customersQuery = useQuery({
    queryKey: ["venue-customers-quick", venueId, debouncedCustomerSearch],
    enabled: !!venueId && isCustomerDropdownOpen,
    staleTime: 60_000,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/venue-manager/venues/{venueId}/customers", {
          params: {
            path: { venueId: venueId! },
            query: {
              search: debouncedCustomerSearch || undefined,
              page: 1,
              pageSize: 20,
            },
          },
        }),
      ),
  });

  // Close customer dropdown on outside click
  useEffect(() => {
    if (!isCustomerDropdownOpen) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (customerDropdownRef.current && !customerDropdownRef.current.contains(e.target as Node)) {
        setIsCustomerDropdownOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [isCustomerDropdownOpen]);

  const handleSelectCustomer = (customer: {
    name: string;
    phone?: string | null;
    totalBookings: number;
    noShows: number;
    isRegisteredPlayer: boolean;
  }) => {
    setCustomerName(customer.name);
    if (customer.phone) {
      setCustomerPhone(customer.phone);
    }
    setSelectedCustomerMeta({
      totalBookings: customer.totalBookings,
      noShows: customer.noShows,
      isRegisteredPlayer: customer.isRegisteredPlayer,
    });
    setIsCustomerDropdownOpen(false);
    setErrors((prev) => {
      const next = { ...prev };
      delete next.customerName;
      delete next.customerPhone;
      return next;
    });
  };

  useEffect(() => {
    if (typeof defaults?.startMinutes === "number" && defaults.startMinutes >= 0) {
      setStartMinutes(defaults.startMinutes);
    }
    if (defaults?.date) {
      setDate(defaults.date);
    }
    if (defaults?.pitchId) {
      setPitchId(defaults.pitchId);
    }
  }, [defaults?.startMinutes, defaults?.date, defaults?.pitchId]);

  // Synchronize pitchId when pitches load asynchronously to avoid desync with <select>
  useEffect(() => {
    if (pitches.length > 0) {
      const isValid = pitches.some((p) => p.id === pitchId);
      if (!isValid) {
        const nextId =
          defaults?.pitchId && pitches.some((p) => p.id === defaults.pitchId)
            ? defaults.pitchId
            : pitches[0].id;
        setPitchId(nextId);
        setErrors((prev) => {
          if (!prev.pitchId) return prev;
          const { pitchId: _, ...rest } = prev;
          return rest;
        });
      }
    }
  }, [pitches, pitchId, defaults?.pitchId]);

  const startUtc = new Date(new Date(cairoDayStartUtc(date)).getTime() + startMinutes * 60_000).toISOString();
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

    const effectivePitchId = pitchId || (pitches.length > 0 ? pitches[0].id : "");

    const parsed = schema.safeParse({
      pitchId: effectivePitchId,
      customerName,
      customerPhone,
      price: price === "" ? undefined : Number(price),
      notes: notes || undefined,
    });
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
          <select
            id="pitchId"
            value={pitchId || pitches[0]?.id || ""}
            onChange={(e) => {
              setPitchId(e.target.value);
              setErrors((prev) => {
                if (!prev.pitchId) return prev;
                const { pitchId: _, ...rest } = prev;
                return rest;
              });
            }}
            className={inputClass}
            aria-invalid={!!errors.pitchId}
          >
            {pitches.length === 0 ? (
              <option value="">لا توجد ملاعب نشطة</option>
            ) : (
              pitches.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))
            )}
          </select>,
        )}
        {field("date", "اليوم", <input id="date" type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputClass} />)}
        <ArabicTimePicker
          date={date}
          valueMinutes={startMinutes}
          onChange={setStartMinutes}
          error={errors.time}
        />
        <fieldset className="flex flex-col gap-1 text-sm font-semibold sm:col-span-2">
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
        {/* Customer Name with Registered Customers Autocomplete */}
        <div className="flex flex-col gap-1 text-sm font-semibold relative" ref={customerDropdownRef}>
          <div className="flex items-center justify-between">
            <label htmlFor="customerName">اسم العميل</label>
            <button
              type="button"
              onClick={() => {
                setIsCustomerDropdownOpen((v) => !v);
                setCustomerSearch(customerName || "");
              }}
              className="text-xs font-semibold text-emerald-700 hover:text-emerald-800 flex items-center gap-1 cursor-pointer transition-colors"
            >
              <Users className="size-3.5" />
              <span>{isCustomerDropdownOpen ? "إغلاق القائمة" : "عميل مسجل مسبقاً؟"}</span>
            </button>
          </div>

          <div className="relative">
            <input
              id="customerName"
              value={customerName}
              maxLength={100}
              onChange={(e) => {
                setCustomerName(e.target.value);
                if (selectedCustomerMeta) setSelectedCustomerMeta(null);
                if (errors.customerName) {
                  setErrors((prev) => {
                    const next = { ...prev };
                    delete next.customerName;
                    return next;
                  });
                }
              }}
              placeholder="اكتب اسم العميل أو اختر عميلاً مسجلاً"
              className={inputClass}
              aria-invalid={!!errors.customerName}
            />
          </div>

          {errors.customerName && (
            <span id="customerName-error" role="alert" className="text-xs font-normal text-[#A8261C]">
              {errors.customerName}
            </span>
          )}

          {/* Autocomplete Dropdown */}
          {isCustomerDropdownOpen && (
            <div className="absolute top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-2xl shadow-2xl z-40 p-2.5 max-h-[300px] flex flex-col gap-2 animate-modal-in">
              <div className="flex items-center gap-2 px-2.5 py-1.5 bg-slate-50 border border-slate-200 rounded-xl">
                <Search className="size-3.5 text-slate-400 shrink-0" />
                <input
                  type="text"
                  value={customerSearch}
                  onChange={(e) => setCustomerSearch(e.target.value)}
                  placeholder="ابحث بالاسم أو رقم الموبايل…"
                  className="w-full text-xs font-medium outline-none bg-transparent placeholder-slate-400 text-slate-900"
                  autoFocus
                />
                {customerSearch && (
                  <button
                    type="button"
                    onClick={() => setCustomerSearch("")}
                    className="text-xs text-slate-400 hover:text-slate-600"
                  >
                    مسح
                  </button>
                )}
              </div>

              <div className="overflow-y-auto max-h-[220px] flex flex-col gap-1 divide-y divide-slate-100">
                {customersQuery.isLoading ? (
                  <div className="p-4 text-center text-xs text-slate-500 flex items-center justify-center gap-2">
                    <RefreshCw className="size-3.5 animate-spin text-slate-400" />
                    <span>جاري البحث في قاعدة عملاء الملعب…</span>
                  </div>
                ) : (customersQuery.data?.items ?? []).length === 0 ? (
                  <div className="p-4 text-center text-xs text-slate-500">
                    {customerSearch
                      ? "لا يوجد عملاء يطابقون هذا البحث."
                      : "لا يوجد عملاء سابقين مسجلين لهذا الملعب حتى الآن."}
                  </div>
                ) : (
                  (customersQuery.data?.items ?? []).map((c) => (
                    <button
                      key={c.customerKey}
                      type="button"
                      onClick={() => handleSelectCustomer(c)}
                      className="flex items-center justify-between p-2 rounded-xl hover:bg-slate-50 text-right transition-colors cursor-pointer group pt-2"
                    >
                      <div className="flex flex-col">
                        <span className="text-sm font-bold text-slate-900 group-hover:text-emerald-700">
                          {c.name}
                        </span>
                        <span className="text-xs text-slate-500 font-mono" dir="ltr">
                          {c.phone || "بدون رقم مسجل"}
                        </span>
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        {c.totalBookings > 0 && (
                          <span className="text-[10px] font-semibold px-2 py-0.5 rounded-md bg-slate-100 text-slate-700">
                            {c.totalBookings} حجز
                          </span>
                        )}
                        {c.noShows > 0 && (
                          <span className="text-[10px] font-bold px-2 py-0.5 rounded-md bg-rose-100 text-rose-800">
                            {c.noShows} غياب
                          </span>
                        )}
                        <span className="text-xs font-bold text-emerald-700 bg-emerald-50 group-hover:bg-emerald-100 px-2.5 py-1 rounded-lg transition-colors">
                          اختيار
                        </span>
                      </div>
                    </button>
                  ))
                )}
              </div>
            </div>
          )}
        </div>

        {/* Customer Phone */}
        {field(
          "customerPhone",
          "موبايل العميل",
          <input
            id="customerPhone"
            dir="ltr"
            inputMode="tel"
            value={customerPhone}
            onChange={(e) => {
              setCustomerPhone(e.target.value);
              if (errors.customerPhone) {
                setErrors((prev) => {
                  const next = { ...prev };
                  delete next.customerPhone;
                  return next;
                });
              }
            }}
            placeholder="01xxxxxxxxx"
            className={inputClass}
            aria-invalid={!!errors.customerPhone}
          />,
        )}

        {/* Selected Registered Customer Info Badge */}
        {selectedCustomerMeta && (
          <div className="sm:col-span-2 flex flex-wrap items-center justify-between gap-2 p-2.5 rounded-xl bg-emerald-50/90 border border-emerald-200 text-xs">
            <div className="flex items-center gap-2 text-emerald-950 font-medium">
              <UserCheck className="size-4 text-emerald-600 shrink-0" />
              <span>
                تم اختيار عميل مسجل: <b>{customerName}</b> ({selectedCustomerMeta.totalBookings} حجز سابق)
              </span>
              {selectedCustomerMeta.noShows > 0 && (
                <span className="font-bold text-rose-800 bg-rose-100 px-2 py-0.5 rounded-md text-[11px]">
                  ⚠️ لديه {selectedCustomerMeta.noShows} عدم حضور سابق
                </span>
              )}
            </div>
            <button
              type="button"
              onClick={() => {
                setSelectedCustomerMeta(null);
                setCustomerName("");
                setCustomerPhone("");
              }}
              className="text-slate-500 hover:text-rose-700 font-semibold cursor-pointer underline text-[11px] flex items-center gap-1"
            >
              <X className="size-3" />
              <span>تغيير العميل</span>
            </button>
          </div>
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
      {/* Primary Column: Booking Form */}
      <div className="lg:col-span-7 xl:col-span-8 bg-white rounded-[20px] p-5 sm:p-6 shadow-sm border border-slate-200">
        <div className="flex items-center justify-between pb-3 mb-4 border-b border-slate-100">
          <h2 className="text-base font-extrabold text-slate-900 flex items-center gap-2">
            <span className="size-2 rounded-full bg-emerald-600" />
            بيانات الحجز
          </h2>
          <span className="text-xs text-slate-500 font-medium">خطوة واحدة للتأكيد</span>
        </div>
        {formElement}
      </div>

      {/* Side Column: Live Interactive Day Schedule */}
      <div className="lg:col-span-5 xl:col-span-4 bg-white rounded-[20px] p-4 sm:p-5 shadow-sm border border-slate-200 flex flex-col gap-3.5">
        {/* Header & Date Controls */}
        <div className="flex flex-col gap-2.5 pb-3 border-b border-slate-100">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <div className="size-8 rounded-xl bg-emerald-50 text-emerald-800 flex items-center justify-center font-bold">
                <CalendarDays className="size-4" />
              </div>
              <div>
                <h2 className="text-sm font-extrabold text-slate-900">جدول المواعيد</h2>
                <p className="text-[11px] text-slate-500">اضغط أي متاح لاختياره فوراً</p>
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
                    className={`px-2 py-0.5 text-xs rounded-lg font-bold transition-all ${
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
          <div className="flex items-center justify-between gap-1.5 p-2 rounded-xl bg-slate-50 border border-slate-200">
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => setDate(cairoToday())}
                className={`px-2 py-0.5 text-xs font-bold rounded-lg transition-all ${
                  date === cairoToday()
                    ? "bg-emerald-700 text-white shadow-2xs"
                    : "bg-white border border-slate-300 text-slate-700 hover:bg-slate-100"
                }`}
              >
                اليوم
              </button>
              <div className="flex items-center gap-0.5">
                <button
                  type="button"
                  title="اليوم السابق"
                  onClick={() => setDate(addDays(date, -1))}
                  className="p-1 rounded-lg border border-slate-300 bg-white text-slate-600 hover:bg-slate-100 cursor-pointer"
                >
                  <ChevronRight className="size-3.5" />
                </button>
                <span className="text-xs font-bold text-slate-900 px-1 min-w-[95px] text-center">
                  {cairoDay(cairoDayStartUtc(date))}
                </span>
                <button
                  type="button"
                  title="اليوم التالي"
                  onClick={() => setDate(addDays(date, 1))}
                  className="p-1 rounded-lg border border-slate-300 bg-white text-slate-600 hover:bg-slate-100 cursor-pointer"
                >
                  <ChevronLeft className="size-3.5" />
                </button>
              </div>
            </div>

            <div className="flex items-center gap-1 text-xs">
              <span className="font-mono text-[11px] text-slate-600 bg-white px-1.5 py-0.5 rounded border border-slate-200" dir="ltr">
                {date}
              </span>
            </div>
          </div>

          {/* Color Legend */}
          <div className="flex flex-wrap items-center gap-2 text-[11px] pt-0.5 text-slate-600">
            <span className="flex items-center gap-1 font-medium">
              <span className="size-2 rounded-full bg-emerald-500" />
              متاح
            </span>
            <span className="flex items-center gap-1 font-medium">
              <span className="size-2 rounded-full bg-navy-900" />
              محجوز
            </span>
            <span className="flex items-center gap-1 font-medium">
              <span className="size-2 rounded-full bg-amber-400" />
              عربون
            </span>
            <span className="flex items-center gap-1 font-medium">
              <span className="size-2 rounded-full bg-slate-400" />
              مغلق
            </span>
          </div>
        </div>

        {/* Slots Timeline */}
        <div className="flex flex-col gap-1.5 max-h-[600px] overflow-y-auto pr-1">
          {calendarQuery.isLoading ? (
            <div className="p-8 text-center text-sm text-slate-500 flex items-center justify-center gap-2">
              <RefreshCw className="size-4 animate-spin text-emerald-600" />
              <span>جاري تحميل مواعيد اليوم…</span>
            </div>
          ) : (
            HOURS.map((hourVal) => {
              const hourMinutes = hourVal * 60;
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
              const isStartingHour = hourMinutes === startMinutes;

              if (overlapping.length > 0) {
                const first = overlapping[0];
                const isBlocked = first.status === "Blocked";
                const isPending = first.status === "PendingDeposit" || first.status === "ProofSubmitted" || first.status === "UnderReview";

                return (
                  <div
                    key={hourVal}
                    className={`p-2.5 rounded-xl border flex items-center justify-between gap-2 text-xs transition-all ${
                      isSelectedByForm
                        ? "border-red-400 bg-red-50/60 ring-2 ring-red-400"
                        : isBlocked
                          ? "bg-[repeating-linear-gradient(135deg,#F1F3F7_0_6px,#E8ECF2_6px_12px)] border-slate-300 text-slate-700"
                          : isPending
                            ? "bg-amber-50 border-amber-300 text-amber-900"
                            : "bg-slate-50 border-slate-300 text-slate-900"
                    }`}
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      <span className="font-mono text-xs font-bold text-slate-600 bg-white px-1.5 py-0.5 rounded border border-slate-200 shrink-0">
                        {hourLabel(hourVal)}
                      </span>
                      <div className="flex flex-col min-w-0">
                        <div className="flex items-center gap-1 font-bold truncate">
                          {isBlocked ? (
                            <>
                              <Lock className="size-3 text-slate-500 shrink-0" />
                              <span className="text-slate-800 text-xs truncate">{first.reason || "محظور ومغلق"}</span>
                            </>
                          ) : (
                            <span className="truncate text-xs">{first.customerName || "حجز مسجل"}</span>
                          )}
                        </div>
                        <span className="text-[10px] text-slate-500">
                          {cairoTime(first.startUtc)} – {cairoTime(first.endUtc)}
                        </span>
                      </div>
                    </div>

                    <div className="flex items-center gap-1.5 shrink-0">
                      {isSelectedByForm && (
                        <span className="text-[10px] font-bold text-red-600 bg-white px-1.5 py-0.2 rounded border border-red-200">
                          يتعارض!
                        </span>
                      )}
                      <span
                        className={`font-bold px-2 py-0.5 rounded text-[10px] ${
                          isBlocked
                            ? "bg-slate-200 text-slate-700"
                            : isPending
                              ? "bg-amber-200 text-amber-900"
                              : "bg-navy-900 text-white"
                        }`}
                      >
                        {isBlocked ? "محظور" : isPending ? "عربون" : "محجوز"}
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
                  onClick={() => setStartMinutes(hourMinutes)}
                  className={`p-2.5 rounded-xl border flex items-center justify-between gap-2 text-xs text-right transition-all group cursor-pointer ${
                    isStartingHour
                      ? "bg-emerald-50 border-emerald-600 ring-2 ring-emerald-600 text-emerald-950 font-bold shadow-xs"
                      : isSelectedByForm
                        ? "bg-emerald-50/60 border-emerald-400 text-emerald-900"
                        : "bg-white border-slate-200 hover:border-emerald-500 hover:bg-emerald-50/30 text-slate-800"
                  }`}
                >
                  <div className="flex items-center gap-2 min-w-0">
                    <span
                      className={`font-mono text-xs px-1.5 py-0.5 rounded border shrink-0 transition-colors ${
                        isStartingHour
                          ? "bg-emerald-700 text-white border-emerald-700 font-bold"
                          : "bg-slate-100 text-slate-700 border-slate-200 group-hover:bg-white"
                      }`}
                    >
                      {hourLabel(hourVal)}
                    </span>
                    <span className="font-semibold text-emerald-800 flex items-center gap-1 text-xs truncate">
                      <span className="size-2 rounded-full bg-emerald-500 shrink-0" />
                      متاح للحجز
                    </span>
                  </div>

                  <div className="flex items-center gap-1 shrink-0">
                    {isStartingHour ? (
                      <span className="bg-emerald-700 text-white text-[10px] font-bold px-1.5 py-0.5 rounded flex items-center gap-1">
                        <Check className="size-3" />
                        المختار
                      </span>
                    ) : (
                      <span className="text-[11px] text-slate-400 group-hover:text-emerald-700 font-medium transition-colors">
                        اختيار ‹
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
