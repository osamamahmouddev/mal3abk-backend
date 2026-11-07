"use client";

import React, { useEffect, useState } from "react";
import type { UseQueryResult } from "@tanstack/react-query";
import { ApiError } from "@/lib/api";
import { btnOutline, card, inputClass, muted, thCls, toArabicDigits } from "@/components/ui";

/** "7 نوفمبر 2026، 10:30" in Cairo time. */
export function fmtDateTime(iso?: string | null) {
  if (!iso) return "—";
  return new Intl.DateTimeFormat("ar-EG", { dateStyle: "medium", timeStyle: "short", timeZone: "Africa/Cairo" }).format(new Date(iso));
}

export function fmtDate(iso?: string | null) {
  if (!iso) return "—";
  return new Intl.DateTimeFormat("ar-EG", { dateStyle: "medium", timeZone: "Africa/Cairo" }).format(new Date(iso));
}

export function num(n?: number | null) {
  return toArabicDigits(Math.round(n ?? 0).toLocaleString("en-US")).replace(/,/g, "٬");
}

/** Loading, permission-denied, error and empty states for a query; renders children only with data. */
export function QueryState<T>({
  query,
  empty,
  isEmpty,
  children,
}: {
  query: UseQueryResult<T>;
  empty?: string;
  isEmpty?: (data: T) => boolean;
  children: (data: T) => React.ReactNode;
}) {
  if (query.isLoading) return <div className={`${card} rounded-[18px] h-40 animate-pulse`} aria-busy="true" />;
  if (query.error) return <ErrorState error={query.error} />;
  if (query.data === undefined) return null;
  if (isEmpty?.(query.data)) return <div className={`${card} rounded-[18px] p-10 text-center text-sm ${muted}`}>{empty ?? "مفيش بيانات."}</div>;
  return <>{children(query.data)}</>;
}

export function ErrorState({ error }: { error: unknown }) {
  const denied = error instanceof ApiError && error.status === 403;
  return (
    <div role="alert" className={`${card} rounded-[18px] p-8 text-center flex flex-col gap-1`}>
      <strong className="text-[15px]">{denied ? "مش عندك صلاحية للصفحة دي" : "حصل خطأ"}</strong>
      <span className={`text-sm ${muted}`}>
        {denied ? "دورك الحالي مايسمحش بالإجراء ده. لو محتاجه، كلّم مدير المنصة." : error instanceof Error ? error.message : "حاول تاني."}
      </span>
    </div>
  );
}

/** Simple, accessible table: column headers + rows; clicking a row selects it. */
export function Table<T>({
  rows,
  columns,
  rowKey,
  onSelect,
  selected,
  caption,
}: {
  rows: T[];
  columns: { title: string; cell: (row: T) => React.ReactNode; className?: string }[];
  rowKey: (row: T) => string;
  onSelect?: (row: T) => void;
  selected?: string | null;
  caption: string;
}) {
  return (
    <div className={`${card} rounded-[18px] overflow-x-auto`}>
      <table className="w-full text-sm min-w-[640px]">
        <caption className="sr-only">{caption}</caption>
        <thead className="bg-[#F6F7FA] text-[#5B6475]">
          <tr>
            {columns.map((c) => (
              <th key={c.title} scope="col" className={`${thCls} ${c.className ?? ""}`}>
                {c.title}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const key = rowKey(row);
            const on = key === selected;
            return (
              <tr
                key={key}
                onClick={onSelect ? () => onSelect(row) : undefined}
                onKeyDown={onSelect ? (e) => (e.key === "Enter" || e.key === " ") && onSelect(row) : undefined}
                tabIndex={onSelect ? 0 : undefined}
                aria-selected={onSelect ? on : undefined}
                className={`border-t border-[#EEF0F5] ${onSelect ? "cursor-pointer hover:bg-[#F6F7FA]" : ""} ${on ? "bg-[#EEF0F5]" : ""}`}
              >
                {columns.map((c) => (
                  <td key={c.title} className={`px-2 py-3 ${c.className ?? ""}`}>
                    {c.cell(row)}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/** Side panel for details; full screen on phones. Closes on Escape. */
export function Drawer({ title, open, onClose, children }: { title: string; open: boolean; onClose: () => void; children: React.ReactNode }) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label={title}>
      <button type="button" aria-label="إغلاق" onClick={onClose} className="absolute inset-0 bg-[rgba(10,19,48,0.45)]" />
      <div className="absolute inset-y-0 left-0 w-full sm:w-[520px] bg-white shadow-2xl overflow-y-auto p-5 flex flex-col gap-4" dir="rtl">
        <div className="flex items-center gap-2">
          <h2 className="text-[19px] font-bold flex-1">{title}</h2>
          <button type="button" onClick={onClose} className={btnOutline}>
            إغلاق
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-[15px] font-bold">{title}</h3>
      {children}
    </section>
  );
}

export function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1 text-[13px] font-semibold">
      {label}
      {children}
    </label>
  );
}

export function TextInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`${inputClass} ${props.className ?? ""}`} />;
}

export function Select({ value, onChange, options, label }: { value: string; onChange: (v: string) => void; options: [string, string][]; label: string }) {
  return (
    <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} className={inputClass}>
      {options.map(([v, t]) => (
        <option key={v} value={v}>
          {t}
        </option>
      ))}
    </select>
  );
}

/** Page-level status line for the result of the last action. */
export function useNotice() {
  const [notice, setNotice] = useState<{ text: string; bad?: boolean } | null>(null);
  const node = notice ? (
    <div role="status" className={`rounded-xl px-4 py-3 text-sm font-semibold ${notice.bad ? "bg-[#FDECEA] text-[#A8261C]" : "bg-[#EEF0F5] text-[#1F2A44]"}`}>
      {notice.text}
    </div>
  ) : null;
  return {
    node,
    ok: (text: string) => setNotice({ text }),
    fail: (e: unknown) => setNotice({ text: e instanceof Error ? e.message : String(e), bad: true }),
    clear: () => setNotice(null),
  };
}

export function Pager({ page, pageSize, total, onChange }: { page: number; pageSize: number; total: number; onChange: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages <= 1) return null;
  return (
    <nav aria-label="الصفحات" className="flex items-center gap-2 justify-center text-sm">
      <button type="button" className={btnOutline} disabled={page <= 1} onClick={() => onChange(page - 1)}>
        السابق
      </button>
      <span>
        {toArabicDigits(page)} / {toArabicDigits(pages)}
      </span>
      <button type="button" className={btnOutline} disabled={page >= pages} onClick={() => onChange(page + 1)}>
        التالي
      </button>
    </nav>
  );
}
