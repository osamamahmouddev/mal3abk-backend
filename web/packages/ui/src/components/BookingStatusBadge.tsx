import React from "react";

export type BookingStatus =
  | "Draft"
  | "PendingDeposit"
  | "ProofSubmitted"
  | "UnderReview"
  | "Confirmed"
  | "Rejected"
  | "Expired"
  | "Cancelled"
  | "Completed"
  | "NoShow";

export interface BookingStatusBadgeProps {
  status: BookingStatus;
  label?: string;
  locale?: "ar" | "en";
  className?: string;
}

const defaultStatusLabels: Record<BookingStatus, { ar: string; en: string }> = {
  Draft: { ar: "مسودة", en: "Draft" },
  PendingDeposit: { ar: "في انتظار العربون", en: "Pending Deposit" },
  ProofSubmitted: { ar: "تم رفع الإيصال", en: "Proof Submitted" },
  UnderReview: { ar: "قيد المراجعة", en: "Under Review" },
  Confirmed: { ar: "مؤكد", en: "Confirmed" },
  Rejected: { ar: "مرفوض", en: "Rejected" },
  Expired: { ar: "منتهي الصلاحية", en: "Expired" },
  Cancelled: { ar: "ملغي", en: "Cancelled" },
  Completed: { ar: "مكتمل", en: "Completed" },
  NoShow: { ar: "لم يحضر", en: "No Show" },
};

export const BookingStatusBadge: React.FC<BookingStatusBadgeProps> = ({
  status,
  label,
  locale = "ar",
  className = "",
}) => {
  const displayLabel = label ?? defaultStatusLabels[status]?.[locale] ?? status;

  const getStyleAndIcon = () => {
    switch (status) {
      case "Confirmed":
      case "Completed":
        return {
          containerClass: "bg-emerald-50 text-emerald-800 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800",
          icon: (
            <svg className="w-3.5 h-3.5 me-1.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg>
          ),
        };
      case "PendingDeposit":
      case "ProofSubmitted":
      case "UnderReview":
        return {
          containerClass: "bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-800",
          icon: (
            <svg className="w-3.5 h-3.5 me-1.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          ),
        };
      case "Rejected":
      case "Cancelled":
      case "Expired":
        return {
          containerClass: "bg-rose-50 text-rose-800 border-rose-200 dark:bg-rose-950/40 dark:text-rose-300 dark:border-rose-800",
          icon: (
            <svg className="w-3.5 h-3.5 me-1.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          ),
        };
      case "NoShow":
        return {
          containerClass: "bg-purple-50 text-purple-800 border-purple-200 dark:bg-purple-950/40 dark:text-purple-300 dark:border-purple-800",
          icon: (
            <svg className="w-3.5 h-3.5 me-1.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l18 18" />
            </svg>
          ),
        };
      case "Draft":
      default:
        return {
          containerClass: "bg-slate-50 text-slate-800 border-slate-200 dark:bg-slate-900 dark:text-slate-300 dark:border-slate-800",
          icon: (
            <svg className="w-3.5 h-3.5 me-1.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
            </svg>
          ),
        };
    }
  };

  const { containerClass, icon } = getStyleAndIcon();

  return (
    <span
      role="status"
      aria-label={`Booking status: ${displayLabel}`}
      className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold border ${containerClass} ${className}`}
    >
      {icon}
      <span>{displayLabel}</span>
    </span>
  );
};
