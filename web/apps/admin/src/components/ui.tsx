"use client";

import React, { useState } from "react";

export const toArabicDigits = (v: number | string) => String(v).replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

/** 1650 → "١٬٦٥٠ ج.م" */
export const money = (n: number) => `${toArabicDigits(Math.round(n).toLocaleString("en-US")).replace(/,/g, "٬")} ج.م`;

export const card =
  "bg-white border border-[#EDF0F5] shadow-[0_1px_2px_rgba(15,27,61,0.04),0_10px_24px_-14px_rgba(15,27,61,0.16)]";
export const redShadow = "shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)]";
export const muted = "text-[#5B6475]";
export const inputClass =
  "min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] bg-white text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-ink";

export const btnPrimary = `min-h-[44px] px-[18px] rounded-xl bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow} inline-flex items-center justify-center`;
export const btnDark = "min-h-[44px] px-[18px] rounded-xl bg-ink text-white text-sm font-bold inline-flex items-center justify-center";
export const btnOutline =
  "min-h-[44px] px-4 rounded-xl border border-[#C9CFD9] bg-white text-ink text-sm font-semibold hover:bg-slate-50 inline-flex items-center justify-center";
export const btnDanger =
  "min-h-[44px] px-4 rounded-xl border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-bold inline-flex items-center justify-center";
export const btnSmall =
  "min-h-[34px] px-3 rounded-[9px] border border-[#C9CFD9] bg-white text-ink text-xs font-semibold hover:bg-slate-50";

/** Selectable chip (filters, segmented choices). */
export const chip = (on: boolean, size = "min-h-[38px] px-3.5 rounded-[10px] text-[13px]") =>
  `${size} font-semibold border transition ${on ? "bg-ink text-white border-ink" : "bg-white text-slate-900 border-[#DCE0E8] hover:bg-slate-50"}`;

/** Table row; the selected row gets a tint and a red edge on the start side. */
export const rowCls = (on: boolean) =>
  `border-t border-[#EEF0F5] cursor-pointer ${on ? "bg-[#F3F5FB] shadow-[inset_-3px_0_0_#D63B2F]" : "hover:bg-[#FAFBFC]"}`;

export const thCls = "text-right px-2 py-3.5 font-semibold";

export type Tone ="ok" | "warn" | "bad" | "neutral" | "navy";
export const TONE: Record<Tone, string> = {
  ok: "bg-[#E5F5EB] text-[#1B6E3E]",
  warn: "bg-[#FEF3D6] text-[#7A4D00]",
  bad: "bg-[#FDECEA] text-[#A8261C]",
  neutral: "bg-[#EEF0F5] text-[#3B4352]",
  navy: "bg-ink text-white",
};

export function Pill({ tone, children, className = "" }: { tone: Tone; children: React.ReactNode; className?: string }) {
  return <span className={`inline-block text-xs font-bold rounded-full px-2.5 py-[3px] whitespace-nowrap ${TONE[tone]} ${className}`}>{children}</span>;
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
      className={`w-12 h-7 shrink-0 rounded-full p-[3px] flex transition-colors ${on ? "justify-end bg-ink" : "justify-start bg-[#C9CFD9]"} ${
        disabled ? "opacity-50 cursor-not-allowed" : ""
      }`}
    >
      <span className="w-[22px] h-[22px] rounded-full bg-white" />
    </button>
  );
}

export function Page({ children, className = "gap-4" }: { children: React.ReactNode; className?: string }) {
  return <div className={`px-8 pt-7 pb-10 w-full min-w-0 flex flex-col ${className}`}>{children}</div>;
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
          className={`min-h-[44px] px-4 bg-transparent text-sm -mb-px border-b-[3px] ${
            value === id ? "text-ink font-bold border-brand-red" : `${muted} font-medium border-transparent`
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}

export function Segmented<T extends string>({ items, value, onChange, label }: { items: [NoInfer<T>, string][]; value: T; onChange: (v: NoInfer<T>) => void; label: string }) {
  return (
    <div role="group" aria-label={label} className="flex bg-white border border-[#E4E7EC] rounded-[10px] p-[3px] gap-0.5">
      {items.map(([id, l]) => (
        <button
          key={id}
          type="button"
          aria-pressed={value === id}
          onClick={() => onChange(id)}
          className={`min-h-[34px] px-3.5 rounded-[7px] text-[13px] ${value === id ? "bg-ink text-white font-bold" : `bg-transparent ${muted}`}`}
        >
          {l}
        </button>
      ))}
    </div>
  );
}

export function SearchBox({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  return (
    <div className="flex-[1_1_260px] flex items-center gap-2 min-h-[44px] bg-white border border-[#DCE0E8] rounded-xl px-3">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5B6475" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
        <circle cx="11" cy="11" r="7" />
        <path d="M20 20l-4-4" />
      </svg>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label="بحث"
        className="flex-1 border-none outline-none text-sm bg-transparent text-slate-900"
      />
    </div>
  );
}

export function Toast({ message, onClose }: { message: string | null; onClose: () => void }) {
  if (!message) return null;
  return (
    <div
      role="status"
      className="fixed left-6 bottom-6 z-[60] max-w-[440px] bg-ink text-white rounded-[14px] px-4 py-3 text-sm shadow-[0_14px_34px_-12px_rgba(10,19,48,0.5)] flex gap-3 items-center"
    >
      <span className="flex-1">{message}</span>
      <button type="button" onClick={onClose} aria-label="إغلاق" className="min-w-[32px] min-h-[32px] rounded-lg bg-white/[0.12]">
        ✕
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
    <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] flex items-center justify-center p-4">
      <div role="dialog" aria-modal="true" aria-labelledby="reason-title" className="bg-white rounded-[20px] p-6 w-full max-w-[460px] flex flex-col gap-3">
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
          <button type="button" onClick={confirm} className="min-h-[44px] px-[18px] rounded-xl bg-brand-red text-white text-sm font-bold">
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
      <span className="text-left">{children}</span>
    </div>
  );
}
