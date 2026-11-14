"use client";

import React, { memo, useEffect, useRef } from "react";

/**
 * Format string or number with Arabic-Indic digits: 123 -> ١٢٣
 */
export const toArabicDigits: (value: number | string) => string = (
  value: number | string
): string => String(value).replace(/[0-9]/g, (digit: string) => "٠١٢٣٤٥٦٧٨٩"[Number(digit)]);

/**
 * Arabic digits with Arabic thousands separator: 1650 -> ١٬٦٥٠
 */
export const formatEGP: (amount: number) => string = (amount: number): string =>
  toArabicDigits(amount.toLocaleString("en-US")).replace(/,/g, "٬");

export const cardShadow = "shadow-[0_1px_2px_rgba(15,27,61,0.04),0_10px_24px_-14px_rgba(15,27,61,0.16)]";
export const card = `bg-white border border-[#EDF0F5] ${cardShadow} transition-all duration-200 ease-out`;
export const redShadow = "shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)]";

export const primaryBtn = `min-h-[44px] px-[18px] rounded-xl bg-brand-red hover:bg-brand-red-hover active:scale-[0.98] text-white text-sm font-bold ${redShadow} transition-all duration-150 ease-out inline-flex items-center justify-center gap-2 cursor-pointer hover:shadow-lg disabled:cursor-not-allowed disabled:active:scale-100`;
export const secondaryBtn =
  "min-h-[44px] px-4 rounded-[10px] border border-[#C9CFD9] bg-white hover:bg-slate-50 active:scale-[0.98] text-navy-900 text-sm font-semibold transition-all duration-150 ease-out inline-flex items-center justify-center gap-2 cursor-pointer hover:border-slate-400 disabled:cursor-not-allowed disabled:active:scale-100";
export const disabledBtn = "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed shadow-none hover:bg-[#E4E7EC] active:scale-100";

export const inputClass =
  "w-full min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] bg-white text-sm text-slate-900 transition-all duration-150 ease-out focus:outline-none focus:ring-2 focus:ring-navy-900 focus:shadow-[0_0_0_3px_rgba(15,27,61,0.1)]";

export const muted = "text-[#5B6475]";

/**
 * Selectable chip utility: active state in navy, inactive in clean border.
 */
export const chip: (isActive: boolean, extraClasses?: string) => string = (
  isActive: boolean,
  extraClasses: string = "min-h-[42px] px-3.5 rounded-xl text-sm"
): string =>
  `${extraClasses} font-semibold border transition-all duration-150 ease-out active:scale-[0.97] cursor-pointer ${
    isActive
      ? "bg-navy-900 text-white border-navy-900 shadow-xs"
      : "bg-white text-slate-900 border-[#DCE0E8] hover:bg-slate-50 hover:border-slate-300"
  }`;

export type Tone = "ok" | "warn" | "wait" | "block" | "bad" | "info";

export const toneClass: Record<Tone, string> = {
  ok: "bg-[#E5F5EB] text-[#1B6E3E]",
  warn: "bg-[#FEF3D6] text-[#7A4D00]",
  wait: "bg-[#EEF0F5] text-[#3B4352]",
  block: "bg-[#E4E7EC] text-[#3B4352]",
  bad: "bg-[#FDECEA] text-[#A8261C]",
  info: "bg-[#E8EEF9] text-[#0F1B3D]",
};

export interface BadgeProps {
  tone: Tone;
  children: React.ReactNode;
  className?: string;
}

export const Badge: React.FC<BadgeProps> = memo(({ tone, children, className = "" }) => (
  <span
    className={`inline-block text-xs font-bold rounded-full px-2.5 py-[3px] whitespace-nowrap transition-all duration-150 ${toneClass[tone]} ${className}`}
  >
    {children}
  </span>
));

Badge.displayName = "Badge";

export const LiveDot: React.FC<{ ping?: boolean; className?: string }> = ({ ping = true, className = "bg-brand-red" }) => (
  <span className="relative inline-flex h-2 w-2 shrink-0">
    {ping && <span className={`animate-live-ping absolute inline-flex h-full w-full rounded-full opacity-75 ${className}`} />}
    <span className={`relative inline-flex rounded-full h-2 w-2 ${className}`} />
  </span>
);

export const Skeleton: React.FC<{ className?: string }> = ({ className = "" }) => (
  <div className={`skeleton-shimmer rounded-xl ${className}`} aria-hidden="true" />
);

export interface SwitchProps {
  isChecked?: boolean;
  on?: boolean;
  label: string;
  isDisabled?: boolean;
  disabled?: boolean;
  handleToggle?: () => void;
  onToggle?: () => void;
}

export const Switch: React.FC<SwitchProps> = memo(
  ({ isChecked, on, label, isDisabled, disabled, handleToggle, onToggle }) => {
    const active = isChecked ?? on ?? false;
    const isControlDisabled = isDisabled ?? disabled ?? false;
    const toggleHandler = handleToggle ?? onToggle ?? (() => {});

    return (
      <button
        type="button"
        role="switch"
        aria-checked={active}
        aria-label={label}
        disabled={isControlDisabled}
        onClick={toggleHandler}
        className={`w-12 h-7 shrink-0 rounded-full p-[3px] flex items-center transition-colors duration-200 ease-out ${
          active ? "justify-end bg-navy-900" : "justify-start bg-[#C9CFD9]"
        } ${isControlDisabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer active:scale-95"}`}
      >
        <span className="w-[22px] h-[22px] rounded-full bg-white shadow-sm transition-all duration-200 ease-out" />
      </button>
    );
  }
);

Switch.displayName = "Switch";

export interface ToastProps {
  message: string | null;
  handleClose?: () => void;
  onClose?: () => void;
}

export const Toast: React.FC<ToastProps> = memo(({ message, handleClose, onClose }) => {
  if (!message) return null;
  const closeAction = handleClose ?? onClose ?? (() => {});

  return (
    <div
      role="status"
      className="fixed left-4 right-4 bottom-4 sm:right-auto sm:left-6 sm:bottom-6 z-[60] sm:max-w-[440px] bg-navy-900 text-white rounded-[14px] px-4 py-3 text-sm shadow-[0_18px_40px_-10px_rgba(15,27,61,0.55)] flex gap-3 items-center animate-slide-up backdrop-blur-md transition-all"
    >
      <span className="flex-1">{message}</span>
      <button
        type="button"
        onClick={closeAction}
        aria-label="إغلاق"
        className="min-w-[32px] min-h-[32px] rounded-lg bg-white/[0.12] flex items-center justify-center transition hover:bg-white/[0.2] active:scale-95 cursor-pointer"
      >
        ✕
      </button>
    </div>
  );
});

Toast.displayName = "Toast";

export interface PageProps {
  children: React.ReactNode;
  className?: string;
}

export const Page: React.FC<PageProps> = ({ children, className = "gap-4" }) => (
  <div className={`px-4 sm:px-6 lg:px-8 pt-5 sm:pt-7 pb-10 w-full min-w-0 flex flex-col animate-fade-in ${className}`}>{children}</div>
);

export interface PageTitleProps {
  children: React.ReactNode;
}

export const PageTitle: React.FC<PageTitleProps> = ({ children }) => (
  <h1 className="text-[26px] font-bold text-slate-900">{children}</h1>
);

/**
 * List + details layouts stack on narrow screens, putting the details panel below the list.
 * Attach the returned ref to the panel: when `selection` changes and the panel is off screen,
 * it scrolls into view. Does nothing on first render or when the panel is already visible.
 */
export function useRevealOnSelect<T extends HTMLElement>(selection: unknown) {
  const ref = useRef<T>(null);
  const first = useRef(true);
  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    const el = ref.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    if (r.top < 0 || r.top > window.innerHeight * 0.6) el.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [selection]);
  return ref;
}
