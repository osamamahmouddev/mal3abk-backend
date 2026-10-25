"use client";

import React, { useState } from "react";
import Link from "next/link";
import { z } from "zod";
import { Page, card, chip, inputClass, muted, redShadow, toArabicDigits } from "@/components/ui";

const HOURS: readonly string[] = [
  "٤:٠٠ م",
  "٥:٠٠ م",
  "٦:٠٠ م",
  "٧:٠٠ م",
  "٨:٠٠ م",
  "٩:٠٠ م",
  "١٠:٠٠ م",
  "١١:٠٠ م",
  "١٢:٠٠ ص",
  "١:٠٠ ص",
] as const;

const DAYS: readonly string[] = [
  "السبت ٢٦",
  "الأحد ٢٧",
  "الإثنين ٢٨",
  "الثلاثاء ٢٩",
] as const;

// Key format: pitch-day-hourIndex
const BUSY_SLOTS: Record<string, string> = {
  "1-0-0": "أكاديمية الشروق",
  "1-0-1": "أكاديمية الشروق",
  "1-0-4": "أحمد سامي (قيد المراجعة)",
  "1-0-5": "يوسف حسن (بانتظار العربون)",
  "1-0-8": "فريق الصقور",
  "2-0-3": "كريم عادل",
  "2-0-7": "عمر خالد",
  "3-0-4": "م. فتحي",
  "1-1-5": "محمد إبراهيم",
};

export type CustomerType = "fathy" | "karim" | "new";
export type PaymentMethod = "cash" | "paid" | "pending" | "full";

const CUSTOMERS: readonly [CustomerType, string][] = [
  ["fathy", "م. فتحي (عميل قديم)"],
  ["karim", "كريم عادل"],
  ["new", "+ عميل جديد"],
] as const;

const PAYMENTS: readonly [PaymentMethod, string][] = [
  ["cash", "نقدي في الملعب"],
  ["paid", "العربون اتدفع"],
  ["pending", "العربون لسه"],
  ["full", "مدفوع كامل"],
] as const;

// Zod Schema for input validation (OWASP A03 / Input Validation standard)
const newCustomerSchema = z.object({
  name: z.string().trim().min(2, "الاسم يجب ألا يقل عن حرفين").max(60, "الاسم طويل جداً"),
  phone: z
    .string()
    .trim()
    .regex(/^01[0125]\d{8}$/, "رقم الهاتف غير صالح (يجب أن يبدأ بـ 01 ويتكون من 11 رقماً)"),
});

const ManualBookingPage: React.FC = () => {
  const [customerType, setCustomerType] = useState<CustomerType>("fathy");
  const [newCustomerName, setNewCustomerName] = useState<string>("");
  const [newCustomerPhone, setNewCustomerPhone] = useState<string>("");
  const [selectedPitch, setSelectedPitch] = useState<number>(1);
  const [selectedDay, setSelectedDay] = useState<number>(0);
  const [selectedTimeHour, setSelectedTimeHour] = useState<number>(6);
  const [selectedDuration, setSelectedDuration] = useState<1 | 2>(1);
  const [selectedPayment, setSelectedPayment] = useState<PaymentMethod>("cash");
  const [bookingNotes, setBookingNotes] = useState<string>("حجز بالتليفون — هيجيبوا الكورة معاهم.");
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [isSubmittedSuccessfully, setIsSubmittedSuccessfully] = useState<boolean>(false);
  const [submissionError, setSubmissionError] = useState<string | null>(null);

  // Conflict detection
  const getConflictAt = (hourIndex: number): string | undefined =>
    BUSY_SLOTS[`${selectedPitch}-${selectedDay}-${hourIndex}`];

  const conflictingBooking =
    getConflictAt(selectedTimeHour) ||
    (selectedDuration === 2 ? getConflictAt(selectedTimeHour + 1) : undefined);

  // Pricing calculation
  const baseRate = selectedPitch === 2 ? 220 : 150;
  const isPeakHour = selectedTimeHour >= 4 && selectedTimeHour <= 7;
  const peakRate = isPeakHour ? 20 : 0;
  const totalPrice = (baseRate + peakRate) * selectedDuration;

  // Validation
  const validationResult =
    customerType === "new"
      ? newCustomerSchema.safeParse({
          name: newCustomerName,
          phone: newCustomerPhone.replace(/\s/g, ""),
        })
      : { success: true };

  const isCustomerFormValid = validationResult.success;
  const canConfirmBooking = !conflictingBooking && isCustomerFormValid && !isSubmitting;

  const displayCustomerName =
    customerType === "new"
      ? newCustomerName.trim() || "—"
      : customerType === "fathy"
      ? "م. فتحي"
      : "كريم عادل";

  const summaryShort = `ملعب ${toArabicDigits(selectedPitch)} · ${DAYS[selectedDay]} · ${
    HOURS[selectedTimeHour]
  }${selectedDuration === 2 ? " (ساعتين)" : ""}`;

  const currentPaymentLabel =
    PAYMENTS.find(([key]) => key === selectedPayment)?.[1] ?? "نقدي في الملعب";

  // Event Handlers
  const handleSelectCustomerType: (type: CustomerType) => void = (type) => {
    setCustomerType(type);
    setSubmissionError(null);
  };

  const handleNameChange: (e: React.ChangeEvent<HTMLInputElement>) => void = (e) => {
    setNewCustomerName(e.target.value);
    setSubmissionError(null);
  };

  const handlePhoneChange: (e: React.ChangeEvent<HTMLInputElement>) => void = (e) => {
    setNewCustomerPhone(e.target.value);
    setSubmissionError(null);
  };

  const handleSelectPitch: (pitchNumber: number) => void = (pitchNumber) => {
    setSelectedPitch(pitchNumber);
  };

  const handleSelectDay: (dayIndex: number) => void = (dayIndex) => {
    setSelectedDay(dayIndex);
  };

  const handleSelectHour: (hourIndex: number) => void = (hourIndex) => {
    setSelectedTimeHour(hourIndex);
  };

  const handleSelectDuration: (durationHours: 1 | 2) => void = (durationHours) => {
    setSelectedDuration(durationHours);
  };

  const handleSelectPayment: (method: PaymentMethod) => void = (method) => {
    setSelectedPayment(method);
  };

  const handleResetBooking: () => void = () => {
    setIsSubmittedSuccessfully(false);
    setSelectedTimeHour(6);
    setSubmissionError(null);
  };

  const handleConfirmBooking: () => Promise<void> = async () => {
    if (!canConfirmBooking) {
      return;
    }

    setIsSubmitting(true);
    setSubmissionError(null);

    try {
      // Robust client simulation with schema guarantee
      if (customerType === "new") {
        newCustomerSchema.parse({
          name: newCustomerName,
          phone: newCustomerPhone.replace(/\s/g, ""),
        });
      }

      // Simulated network call with error boundary
      await new Promise((resolve) => setTimeout(resolve, 300));
      setIsSubmittedSuccessfully(true);
    } catch (err: unknown) {
      const errorMessage =
        err instanceof z.ZodError
          ? err.issues[0]?.message ?? "بيانات العميل غير صحيحة"
          : "تعذر تسجيل الحجز، يرجى المحاولة مرة أخرى.";
      setSubmissionError(errorMessage);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Page className="gap-[18px]">
      <div className="flex items-center gap-3">
        <Link
          href="/calendar"
          aria-label="رجوع للتقويم"
          className="w-11 h-11 rounded-[14px] bg-white shadow-[0_1px_3px_rgba(15,27,61,0.10)] flex items-center justify-center text-navy-900 transition hover:bg-slate-50"
        >
          <svg
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M5 12h14M13 6l6 6-6 6" />
          </svg>
        </Link>
        <h1 className="text-[26px] font-bold">حجز يدوي</h1>
      </div>

      {isSubmittedSuccessfully && (
        <div
          role="status"
          className="bg-[#E5F5EB] text-[#155C33] rounded-2xl px-[18px] py-4 flex flex-wrap gap-3 items-center text-[15px] font-semibold"
        >
          <span className="flex-1">
            ✓ اتسجّل الحجز <span dir="ltr">KH-2607-M011</span> لـ{displayCustomerName} — {summaryShort}. بعتنا له SMS بالتأكيد.
          </span>
          <Link
            href="/calendar"
            className="min-h-[40px] px-3.5 rounded-[10px] bg-navy-900 text-white text-sm flex items-center transition hover:bg-navy-900/90"
          >
            عرض في التقويم
          </Link>
          <button
            type="button"
            onClick={handleResetBooking}
            className="min-h-[40px] px-3.5 rounded-[10px] border border-[#1B6E3E] bg-transparent text-[#155C33] text-sm font-semibold transition hover:bg-[#1B6E3E]/10"
          >
            حجز تاني
          </button>
        </div>
      )}

      {submissionError && (
        <div
          role="alert"
          className="bg-[#FDECEA] text-[#8A1F17] rounded-xl px-4 py-3 text-sm font-medium"
        >
          {submissionError}
        </div>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_560px] min-w-0 ${card} rounded-[20px] p-6 flex flex-col gap-5`}>
          {/* Customer Selection */}
          <div className="flex flex-col gap-2.5">
            <h2 className="text-base font-bold">العميل</h2>
            <div className="flex gap-2 flex-wrap">
              {CUSTOMERS.map(([id, label]) => (
                <button
                  key={id}
                  type="button"
                  aria-pressed={customerType === id}
                  onClick={() => handleSelectCustomerType(id)}
                  className={chip(customerType === id)}
                >
                  {label}
                </button>
              ))}
            </div>

            {customerType === "new" && (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 mt-1">
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="customer-name" className="text-[13px] font-semibold">
                    اسم العميل
                  </label>
                  <input
                    id="customer-name"
                    type="text"
                    placeholder="مثلاً: أحمد محمود"
                    value={newCustomerName}
                    onChange={handleNameChange}
                    className={inputClass}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="customer-phone" className="text-[13px] font-semibold">
                    رقم الموبايل
                  </label>
                  <input
                    id="customer-phone"
                    type="tel"
                    dir="ltr"
                    placeholder="01012345678"
                    value={newCustomerPhone}
                    onChange={handlePhoneChange}
                    className={`${inputClass} text-right`}
                  />
                </div>
              </div>
            )}
          </div>

          {/* Pitch, Day, Time */}
          <div className="flex flex-col gap-2.5">
            <h2 className="text-base font-bold">الميعاد والملعب</h2>

            <div className="flex gap-2">
              {[1, 2, 3].map((num) => (
                <button
                  key={num}
                  type="button"
                  aria-pressed={selectedPitch === num}
                  onClick={() => handleSelectPitch(num)}
                  className={chip(selectedPitch === num, "flex-1 min-h-[42px] rounded-xl text-sm")}
                >
                  ملعب {toArabicDigits(num)}
                </button>
              ))}
            </div>

            <div className="flex gap-2 flex-wrap">
              {DAYS.map((label, i) => (
                <button
                  key={label}
                  type="button"
                  aria-pressed={selectedDay === i}
                  onClick={() => handleSelectDay(i)}
                  className={chip(selectedDay === i)}
                >
                  {label}
                </button>
              ))}
            </div>

            <div className="grid grid-cols-2 sm:grid-cols-5 gap-2">
              {HOURS.map((label, i) => {
                const isSlotBusy = !!getConflictAt(i);
                const isSelected = i === selectedTimeHour;
                const slotClass = isSelected
                  ? isSlotBusy
                    ? "bg-[#A8261C] text-white border-[#A8261C]"
                    : "bg-navy-900 text-white border-navy-900"
                  : isSlotBusy
                  ? "bg-slate-100 text-[#8A94AA] border-[#EDF0F5] line-through"
                  : "bg-white text-slate-900 border-[#DCE0E8]";

                return (
                  <button
                    key={label}
                    type="button"
                    aria-pressed={isSelected}
                    onClick={() => handleSelectHour(i)}
                    className={`min-h-[42px] rounded-[10px] text-[13px] font-semibold border transition ${slotClass}`}
                  >
                    {label}
                    {isSlotBusy ? " •" : ""}
                  </button>
                );
              })}
            </div>

            <div className="flex gap-2">
              {(
                [
                  [1, "ساعة"],
                  [2, "ساعتين"],
                ] as const
              ).map(([d, label]) => (
                <button
                  key={d}
                  type="button"
                  aria-pressed={selectedDuration === d}
                  onClick={() => handleSelectDuration(d)}
                  className={chip(selectedDuration === d)}
                >
                  {label}
                </button>
              ))}
            </div>

            {conflictingBooking && (
              <div
                role="alert"
                className="bg-[#FDECEA] text-[#8A1F17] rounded-xl px-3.5 py-3 text-sm leading-[22px]"
              >
                الميعاد ده متعارض مع حجز <strong>{conflictingBooking}</strong>. اختار ساعة تانية أو ملعب تاني — مسموح بس بحجز واحد في نفس الوقت.
              </div>
            )}
          </div>

          {/* Payment */}
          <div className="flex flex-col gap-2.5">
            <h2 className="text-base font-bold">الدفع</h2>
            <div className="flex gap-2 flex-wrap">
              {PAYMENTS.map(([id, label]) => (
                <button
                  key={id}
                  type="button"
                  aria-pressed={selectedPayment === id}
                  onClick={() => handleSelectPayment(id)}
                  className={chip(selectedPayment === id)}
                >
                  {label}
                </button>
              ))}
            </div>

            <label htmlFor="booking-notes" className="text-[13px] font-semibold">
              ملاحظات (بيشوفها الموظفين بس)
            </label>
            <textarea
              id="booking-notes"
              rows={2}
              value={bookingNotes}
              onChange={(e) => setBookingNotes(e.target.value)}
              className="border border-[#C9CFD9] rounded-xl px-3 py-2.5 text-sm resize-none text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900"
            />
          </div>
        </section>

        {/* Booking Summary Aside */}
        <aside className={`flex-[1_1_300px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
          <h2 className="text-base font-bold">ملخص الحجز</h2>
          <div className="flex justify-between text-sm">
            <span className={muted}>العميل</span>
            <span className="font-semibold">{displayCustomerName}</span>
          </div>
          <div className="flex justify-between text-sm">
            <span className={muted}>الميعاد</span>
            <span className="font-semibold">{summaryShort}</span>
          </div>
          <div className="flex justify-between text-sm">
            <span className={muted}>السعر (من قواعد الأسعار)</span>
            <span className="font-bold">
              {toArabicDigits(totalPrice)} ج.م{isPeakHour ? " (سعر الذروة)" : ""}
            </span>
          </div>
          <div className="flex justify-between text-sm">
            <span className={muted}>الدفع</span>
            <span className="font-semibold">{currentPaymentLabel}</span>
          </div>

          <button
            type="button"
            onClick={handleConfirmBooking}
            disabled={!canConfirmBooking}
            className={`min-h-[50px] rounded-[14px] text-[15px] font-bold mt-1 transition ${
              canConfirmBooking
                ? `bg-brand-red hover:bg-brand-red-hover text-white ${redShadow}`
                : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
            }`}
          >
            {conflictingBooking
              ? "فيه تعارض — غيّر الميعاد"
              : !isCustomerFormValid
              ? "كمّل بيانات العميل"
              : isSubmitting
              ? "جاري التأكيد..."
              : "تأكيد الحجز"}
          </button>
          <span className={`text-xs ${muted}`}>
            نفس فحص التعارض اللي في التطبيق — مفيش حجزين على نفس الملعب في نفس الوقت.
          </span>
        </aside>
      </div>
    </Page>
  );
};

export default ManualBookingPage;
