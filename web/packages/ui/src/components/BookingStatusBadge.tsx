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

interface BookingStatusBadgeProps {
  status: BookingStatus;
  label: string;
}

export const BookingStatusBadge: React.FC<BookingStatusBadgeProps> = ({
  status,
  label,
}) => {
  const getBadgeStyle = () => {
    switch (status) {
      case "Confirmed":
      case "Completed":
        return "bg-emerald-50 text-emerald-700 border-emerald-200";
      case "PendingDeposit":
      case "UnderReview":
      case "ProofSubmitted":
        return "bg-amber-50 text-amber-700 border-amber-200";
      case "Rejected":
      case "Cancelled":
      case "NoShow":
      case "Expired":
        return "bg-rose-50 text-rose-700 border-rose-200";
      default:
        return "bg-slate-50 text-slate-700 border-slate-200";
    }
  };

  return (
    <span
      className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium border ${getBadgeStyle()}`}
    >
      <span className="w-1.5 h-1.5 rounded-full bg-current" aria-hidden="true" />
      <span>{label}</span>
    </span>
  );
};
