"use client";

import React, { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { X } from "lucide-react";

export const toArabicDigits = (v: number | string) => String(v).replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

/** 1650 → "١٬٦٥٠ ج.م" */
export const money = (n: number) => `${toArabicDigits(Math.round(n).toLocaleString("en-US")).replace(/,/g, "٬")} ج.م`;

export const card =
  "bg-white border border-[#EDF0F5] shadow-[0_1px_2px_rgba(15,27,61,0.04),0_10px_24px_-14px_rgba(15,27,61,0.16)] transition-all duration-200 ease-out";
export const redShadow = "shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)]";
export const muted = "text-[#5B6475]";
export const inputClass =
  "w-full min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] bg-white text-sm text-slate-900 transition-all duration-150 ease-out focus:outline-none focus:ring-2 focus:ring-ink focus:shadow-[0_0_0_3px_rgba(10,19,48,0.1)]";

export const btnPrimary = `min-h-[44px] px-[18px] rounded-xl bg-brand-red hover:bg-brand-red-hover active:scale-[0.98] text-white text-sm font-bold ${redShadow} transition-all duration-150 ease-out inline-flex items-center justify-center cursor-pointer hover:shadow-lg disabled:cursor-not-allowed disabled:active:scale-100`;
export const btnDark =
  "min-h-[44px] px-[18px] rounded-xl bg-ink hover:bg-[#152147] active:scale-[0.98] text-white text-sm font-bold transition-all duration-150 ease-out inline-flex items-center justify-center cursor-pointer hover:shadow-md disabled:cursor-not-allowed disabled:active:scale-100";
export const btnOutline =
  "min-h-[44px] px-4 rounded-xl border border-[#C9CFD9] bg-white text-ink text-sm font-semibold hover:bg-slate-50 hover:border-slate-400 active:scale-[0.98] transition-all duration-150 ease-out inline-flex items-center justify-center cursor-pointer disabled:cursor-not-allowed disabled:active:scale-100";
export const btnDanger =
  "min-h-[44px] px-4 rounded-xl border border-[#F3C4BF] bg-white text-[#A8261C] hover:bg-red-50/50 hover:border-red-300 active:scale-[0.98] text-sm font-bold transition-all duration-150 ease-out inline-flex items-center justify-center cursor-pointer disabled:cursor-not-allowed disabled:active:scale-100";
export const btnSmall =
  "min-h-[34px] px-3 rounded-[9px] inline-flex items-center justify-center gap-1.5 border border-[#C9CFD9] bg-white text-ink text-xs font-semibold hover:bg-slate-50 active:scale-[0.98] transition-all duration-150 ease-out cursor-pointer disabled:cursor-not-allowed disabled:active:scale-100";

/** Selectable chip (filters, segmented choices). */
export const chip = (on: boolean, size = "min-h-[38px] px-3.5 rounded-[10px] text-[13px]") =>
  `${size} inline-flex items-center justify-center gap-1.5 font-semibold border transition-all duration-150 ease-out active:scale-[0.97] cursor-pointer ${
    on ? "bg-ink text-white border-ink shadow-xs" : "bg-white text-slate-900 border-[#DCE0E8] hover:bg-slate-50 hover:border-slate-300"
  }`;

/** Table row; the selected row gets a tint and a red edge on the start side. */
export const rowCls = (on: boolean) =>
  `border-t border-[#EEF0F5] cursor-pointer transition-all duration-150 ease-out ${
    on ? "bg-[#F3F5FB] shadow-[inset_-3px_0_0_#D63B2F]" : "hover:bg-[#FAFBFC]"
  }`;

export const thCls = "text-right px-2 py-3.5 font-semibold";

export type Tone = "ok" | "warn" | "bad" | "neutral" | "navy";
export const TONE: Record<Tone, string> = {
  ok: "bg-[#E5F5EB] text-[#1B6E3E]",
  warn: "bg-[#FEF3D6] text-[#7A4D00]",
  bad: "bg-[#FDECEA] text-[#A8261C]",
  neutral: "bg-[#EEF0F5] text-[#3B4352]",
  navy: "bg-ink text-white",
};

export function Pill({ tone, children, className = "" }: { tone: Tone; children: React.ReactNode; className?: string }) {
  return (
    <span className={`inline-block text-xs font-bold rounded-full px-2.5 py-[3px] whitespace-nowrap transition-all duration-150 ${TONE[tone]} ${className}`}>
      {children}
    </span>
  );
}

export function LiveDot({ ping = true, className = "bg-brand-red" }: { ping?: boolean; className?: string }) {
  return (
    <span className="relative inline-flex h-2 w-2 shrink-0">
      {ping && <span className={`animate-live-ping absolute inline-flex h-full w-full rounded-full opacity-75 ${className}`} />}
      <span className={`relative inline-flex rounded-full h-2 w-2 ${className}`} />
    </span>
  );
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`skeleton-shimmer rounded-xl ${className}`} aria-hidden="true" />;
}

export function Switch({ on, label, onToggle, disabled }: { on: boolean; label: string; onToggle: () => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      aria-label={label}
      disabled={disabled}
      onClick={onToggle}
      className={`w-12 h-7 shrink-0 rounded-full p-[3px] flex items-center transition-colors duration-200 ease-out ${
        on ? "justify-end bg-ink" : "justify-start bg-[#C9CFD9]"
      } ${disabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer active:scale-95"}`}
    >
      <span className="w-[22px] h-[22px] rounded-full bg-white shadow-sm transition-all duration-200 ease-out" />
    </button>
  );
}

export function Page({ children, className = "gap-4" }: { children: React.ReactNode; className?: string }) {
  return <div className={`px-4 sm:px-6 lg:px-8 pt-5 sm:pt-7 pb-10 w-full min-w-0 flex flex-col animate-fade-in ${className}`}>{children}</div>;
}

export function PageHeader({ title, sub, children }: { title: string; sub?: React.ReactNode; children?: React.ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="flex-[1_1_280px] flex flex-col gap-0.5">
        <h1 className="text-[26px] font-bold">{title}</h1>
        {sub && <span className={`text-sm ${muted}`}>{sub}</span>}
      </div>
      {children}
    </div>
  );
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: [NoInfer<T>, string][]; value: T; onChange: (v: NoInfer<T>) => void }) {
  return (
    <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8] flex-wrap">
      {tabs.map(([id, label]) => (
        <button
          key={id}
          type="button"
          role="tab"
          aria-selected={value === id}
          onClick={() => onChange(id)}
          className={`min-h-[44px] px-4 bg-transparent text-sm -mb-px border-b-[3px] transition-all duration-150 ease-out cursor-pointer ${
            value === id ? "text-ink font-bold border-brand-red" : `${muted} font-medium border-transparent hover:text-ink`
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}

export function Segmented<T extends string>({
  items,
  value,
  onChange,
  label,
}: {
  items: [NoInfer<T>, string][];
  value: T;
  onChange: (v: NoInfer<T>) => void;
  label: string;
}) {
  return (
    <div role="group" aria-label={label} className="flex bg-white border border-[#E4E7EC] rounded-[10px] p-[3px] gap-0.5">
      {items.map(([id, l]) => (
        <button
          key={id}
          type="button"
          aria-pressed={value === id}
          onClick={() => onChange(id)}
          className={`min-h-[34px] px-3.5 rounded-[7px] text-[13px] transition-all duration-150 ease-out active:scale-95 cursor-pointer ${
            value === id ? "bg-ink text-white font-bold shadow-xs" : `bg-transparent ${muted} hover:text-ink`
          }`}
        >
          {l}
        </button>
      ))}
    </div>
  );
}

export function SearchBox({
  value,
  onChange,
  placeholder,
  className = "",
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
  className?: string;
}) {
  return (
    <div
      className={`w-full h-11 min-h-[44px] max-h-[44px] shrink-0 min-w-0 flex items-center gap-2 bg-white border border-[#DCE0E8] rounded-xl px-3 transition-all duration-150 ease-out focus-within:border-ink focus-within:ring-2 focus-within:ring-ink focus-within:shadow-[0_0_0_3px_rgba(10,19,48,0.08)] ${className}`}
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5B6475" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
        <circle cx="11" cy="11" r="7" />
        <path d="M20 20l-4-4" />
      </svg>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label="بحث"
        className="flex-1 min-w-0 border-none outline-none text-sm bg-transparent text-slate-900 placeholder:text-slate-400"
      />
    </div>
  );
}

export function Toast({ message, onClose }: { message: string | null; onClose: () => void }) {
  if (!message) return null;
  return (
    <div
      role="status"
      className="fixed left-4 right-4 bottom-4 sm:right-auto sm:left-6 sm:bottom-6 z-[60] sm:max-w-[440px] bg-ink text-white rounded-[14px] px-4 py-3 text-sm shadow-[0_18px_40px_-10px_rgba(10,19,48,0.55)] flex gap-3 items-center animate-slide-up backdrop-blur-md transition-all"
    >
      <span className="flex-1">{message}</span>
      <button
        type="button"
        onClick={onClose}
        aria-label="إغلاق"
        className="min-w-[32px] min-h-[32px] rounded-lg bg-white/[0.12] flex items-center justify-center transition hover:bg-white/[0.2] active:scale-95 cursor-pointer"
      >
        <X className="size-4" />
      </button>
    </div>
  );
}

export interface ReasonRequest {
  title: string;
  body: string;
  confirmLabel: string;
  onConfirm: (reason: string) => void;
}

/**
 * Every sensitive admin action needs a written reason (≥ 5 chars) that goes to the audit log.
 * Render once per page and open it with `setRequest({...})`.
 */
export function ReasonModal({ request, onClose }: { request: ReasonRequest | null; onClose: () => void }) {
  const [reason, setReason] = useState("");
  const [err, setErr] = useState(false);
  if (!request) return null;

  const close = () => {
    setReason("");
    setErr(false);
    onClose();
  };
  const confirm = () => {
    const r = reason.trim();
    if (r.length < 5) {
      setErr(true);
      return;
    }
    request.onConfirm(r);
    close();
  };

  return (
    <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] backdrop-blur-sm animate-fade-in flex items-center justify-center p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="reason-title"
        className="bg-white rounded-[20px] p-6 w-full max-w-[460px] flex flex-col gap-3 animate-modal-in shadow-2xl"
      >
        <h2 id="reason-title" className="text-[19px] font-bold">
          {request.title}
        </h2>
        <p className={`text-sm ${muted} leading-[1.7]`}>{request.body}</p>
        <label htmlFor="reason-input" className="text-[13px] font-semibold">
          السبب (بيتسجل في سجل التدقيق)
        </label>
        <input
          id="reason-input"
          autoFocus
          value={reason}
          onChange={(e) => {
            setReason(e.target.value);
            setErr(false);
          }}
          placeholder="اكتب السبب بوضوح"
          aria-invalid={err}
          className={inputClass}
        />
        {err && (
          <span role="alert" className="text-xs text-[#A8261C]">
            لازم تكتب سبب (٥ حروف على الأقل).
          </span>
        )}
        <div className="flex gap-2 justify-end">
          <button type="button" onClick={close} className={btnOutline}>
            رجوع
          </button>
          <button type="button" onClick={confirm} className={btnPrimary}>
            {request.confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

/** Two-column label/value row used in detail panels. */
export function KV({ k, children }: { k: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-3 text-sm">
      <span className={muted}>{k}</span>
      <span className="text-left font-medium">{children}</span>
    </div>
  );
}

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

export type EmptyStateIcon = "check" | "search" | "inbox" | "stadium" | "users" | "shield" | "receipt" | "sparkles" | "calendar";

export interface EmptyStateProps {
  icon?: EmptyStateIcon | React.ReactNode;
  badge?: string;
  title: string;
  description?: string;
  action?: {
    label: string;
    onClick?: () => void;
    href?: string;
    variant?: "primary" | "outline";
  };
  secondaryAction?: {
    label: string;
    onClick?: () => void;
    href?: string;
  };
  hint?: string;
  className?: string;
}

export function EmptyState({
  icon = "inbox",
  badge,
  title,
  description,
  action,
  secondaryAction,
  hint,
  className = "",
}: EmptyStateProps) {
  let iconElement: React.ReactNode;
  let iconToneCls = "bg-[#EEF2F6] text-[#475569] border-[#D8DFE9]";

  if (React.isValidElement(icon)) {
    iconElement = icon;
  } else {
    switch (icon) {
      case "check":
        iconToneCls = "bg-emerald-50 text-emerald-600 border-emerald-200/80 shadow-[0_8px_20px_-6px_rgba(16,185,129,0.25)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
            <polyline points="22 4 12 14.01 9 11.01" />
          </svg>
        );
        break;
      case "shield":
        iconToneCls = "bg-emerald-50 text-emerald-600 border-emerald-200/80 shadow-[0_8px_20px_-6px_rgba(16,185,129,0.25)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
            <polyline points="9 12 11 14 15 10" />
          </svg>
        );
        break;
      case "search":
        iconToneCls = "bg-amber-50 text-amber-600 border-amber-200/80 shadow-[0_8px_20px_-6px_rgba(245,158,11,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <circle cx="11" cy="11" r="8" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
            <line x1="11" y1="8" x2="11" y2="8.01" strokeWidth="3" />
          </svg>
        );
        break;
      case "stadium":
        iconToneCls = "bg-[#EAF7EE] text-[#1B6E3E] border-[#CFE8D7] shadow-[0_8px_20px_-6px_rgba(27,110,62,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <rect x="2" y="4" width="20" height="16" rx="3" />
            <line x1="12" y1="4" x2="12" y2="20" />
            <circle cx="12" cy="12" r="3.5" />
            <path d="M2 9a3 3 0 0 1 3-3" />
            <path d="M2 15a3 3 0 0 0 3 3" />
            <path d="M22 9a3 3 0 0 0-3-3" />
            <path d="M22 15a3 3 0 0 1-3 3" />
          </svg>
        );
        break;
      case "users":
        iconToneCls = "bg-rose-50 text-brand-red border-rose-200/80 shadow-[0_8px_20px_-6px_rgba(214,59,47,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
            <circle cx="9" cy="7" r="4" />
            <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
            <path d="M16 3.13a4 4 0 0 1 0 7.75" />
          </svg>
        );
        break;
      case "receipt":
        iconToneCls = "bg-blue-50 text-blue-600 border-blue-200/80 shadow-[0_8px_20px_-6px_rgba(37,99,235,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1-2-1z" />
            <line x1="8" y1="7" x2="16" y2="7" />
            <line x1="8" y1="11" x2="16" y2="11" />
            <line x1="8" y1="15" x2="12" y2="15" />
          </svg>
        );
        break;
      case "calendar":
        iconToneCls = "bg-violet-50 text-violet-600 border-violet-200/80 shadow-[0_8px_20px_-6px_rgba(139,92,246,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <rect x="3" y="4" width="18" height="18" rx="2" />
            <line x1="16" y1="2" x2="16" y2="6" />
            <line x1="8" y1="2" x2="8" y2="6" />
            <line x1="3" y1="10" x2="21" y2="10" />
          </svg>
        );
        break;
      case "sparkles":
        iconToneCls = "bg-amber-50 text-amber-500 border-amber-200 shadow-[0_8px_20px_-6px_rgba(245,158,11,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M12 3l1.912 5.886a2 2 0 0 0 1.202 1.202L21 12l-5.886 1.912a2 2 0 0 0-1.202 1.202L12 21l-1.912-5.886a2 2 0 0 0-1.202-1.202L3 12l5.886-1.912a2 2 0 0 0 1.202-1.202L12 3z" />
          </svg>
        );
        break;
      case "inbox":
      default:
        iconToneCls = "bg-indigo-50 text-indigo-600 border-indigo-200/80 shadow-[0_8px_20px_-6px_rgba(99,102,241,0.22)]";
        iconElement = (
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <polyline points="22 12 16 12 14 15 10 15 8 12 2 12" />
            <path d="M5.45 5.11L2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" />
          </svg>
        );
        break;
    }
  }

  return (
    <div className={`${card} rounded-[22px] p-8 sm:p-14 text-center flex flex-col items-center justify-center relative overflow-hidden animate-fade-in ${className}`}>
      {/* Decorative ambient background */}
      <div className="absolute inset-0 pointer-events-none opacity-40 bg-[radial-gradient(circle_at_top,_var(--tw-gradient-stops))] from-slate-100/90 via-transparent to-transparent" />
      <div className="absolute -top-24 -right-24 w-48 h-48 rounded-full bg-slate-50/50 pointer-events-none blur-2xl" />
      <div className="absolute -bottom-24 -left-24 w-48 h-48 rounded-full bg-slate-50/50 pointer-events-none blur-2xl" />

      {/* Badge */}
      {badge && (
        <span className="relative z-10 mb-3 inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-[#F0F2F7] text-[#1F2A44] border border-[#E1E5ED] shadow-xs">
          {badge}
        </span>
      )}

      {/* Layered Icon Circle with glowing halo */}
      <div className="relative z-10 mb-4 group">
        <div className="relative flex items-center justify-center">
          <div className="absolute -inset-2 rounded-3xl bg-slate-100/60 blur-sm -z-10 group-hover:bg-slate-200/50 transition-all duration-300" />
          <div className={`w-20 h-20 rounded-2xl border flex items-center justify-center transition-all duration-300 group-hover:scale-105 group-hover:rotate-1 ${iconToneCls}`}>
            {iconElement}
          </div>
        </div>
      </div>

      {/* Title */}
      <h3 className="relative z-10 text-lg sm:text-xl font-bold text-slate-900 mb-2">{title}</h3>

      {/* Description */}
      {description && (
        <p className={`relative z-10 text-sm ${muted} max-w-md mx-auto leading-relaxed mb-6`}>
          {description}
        </p>
      )}

      {/* Actions */}
      {(action || secondaryAction) && (
        <div className="relative z-10 flex flex-wrap items-center justify-center gap-3">
          {action && (
            action.href ? (
              <Link href={action.href} className={action.variant === "outline" ? btnOutline : btnPrimary}>
                {action.label}
              </Link>
            ) : (
              <button type="button" onClick={action.onClick} className={action.variant === "outline" ? btnOutline : btnPrimary}>
                {action.label}
              </button>
            )
          )}
          {secondaryAction && (
            secondaryAction.href ? (
              <Link href={secondaryAction.href} className={btnOutline}>
                {secondaryAction.label}
              </Link>
            ) : (
              <button type="button" onClick={secondaryAction.onClick} className={btnOutline}>
                {secondaryAction.label}
              </button>
            )
          )}
        </div>
      )}

      {/* Hint strip */}
      {hint && (
        <div className="relative z-10 mt-6 pt-4 border-t border-[#EEF0F5] w-full max-w-md text-xs text-[#5B6475] flex items-center justify-center gap-1.5">
          <span>{hint}</span>
        </div>
      )}
    </div>
  );
}
