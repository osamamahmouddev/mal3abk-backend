"use client";

import React, { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import type { UseQueryResult } from "@tanstack/react-query";
import { ApiError } from "@/lib/api";
import {
  btnOutline,
  card,
  inputClass,
  muted,
  thCls,
  toArabicDigits,
  EmptyState,
  type EmptyStateIcon,
  type EmptyStateProps,
} from "@/components/ui";
import { AlertCircle, CheckCircle2, ChevronLeft, X } from "lucide-react";

export { EmptyState, type EmptyStateIcon, type EmptyStateProps };

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
  emptyTitle,
  emptyDescription,
  emptyIcon,
  emptyAction,
  emptySecondaryAction,
  emptyBadge,
  emptyHint,
}: {
  query: UseQueryResult<T>;
  empty?: React.ReactNode | string;
  isEmpty?: (data: T) => boolean;
  children: (data: T) => React.ReactNode;
  emptyTitle?: string;
  emptyDescription?: string;
  emptyIcon?: EmptyStateIcon;
  emptyAction?: { label: string; onClick?: () => void; href?: string; variant?: "primary" | "outline" };
  emptySecondaryAction?: { label: string; onClick?: () => void; href?: string };
  emptyBadge?: string;
  emptyHint?: string;
}) {
  if (query.isLoading) return <div className={`${card} rounded-[18px] h-40 skeleton-shimmer`} aria-busy="true" />;
  if (query.error) return <ErrorState error={query.error} />;
  if (query.data === undefined) return null;
  if (isEmpty?.(query.data)) {
    if (React.isValidElement(empty)) {
      return empty;
    }

    const text = typeof empty === "string" ? empty : "مفيش بيانات.";

    let inferredIcon: EmptyStateIcon = emptyIcon ?? "inbox";
    let inferredTitle = emptyTitle ?? text;
    let inferredDesc = emptyDescription;
    let inferredBadge = emptyBadge;

    if (!emptyIcon) {
      if (text.includes("بحث") || text.includes("فلتر")) {
        inferredIcon = "search";
        inferredDesc = inferredDesc ?? "لم نتمكن من العثور على أي نتائج مطابقة لمعايير البحث الحالية. يمكنك تعديل الكلمات أو إعادة ضبط الفلاتر.";
      } else if (text.includes("مستنية") || text.includes("مراجعة") || text.includes("نزاعات")) {
        inferredIcon = "check";
        inferredBadge = inferredBadge ?? "مكتمل بالكامل";
        inferredDesc = inferredDesc ?? "كل شيء تحت السيطرة! لا توجد طلبات أو عناصر تحتاج لاتخاذ إجراء حالياً.";
      } else if (text.includes("ملاعب")) {
        inferredIcon = "stadium";
        inferredDesc = inferredDesc ?? "لا توجد ملاعب تطابق المعايير المحددة في النظام.";
      } else if (text.includes("حجوزات")) {
        inferredIcon = "calendar";
        inferredDesc = inferredDesc ?? "لا توجد حجوزات مسجلة تطابق الشروط المحددة.";
      } else if (text.includes("تذاكر")) {
        inferredIcon = "inbox";
        inferredDesc = inferredDesc ?? "صندوق تذاكر الدعم الفني فارغ تماماً ولا توجد شكاوى معلقة.";
      } else if (text.includes("فواتير") || text.includes("اشتراك") || text.includes("باقات")) {
        inferredIcon = "receipt";
        inferredDesc = inferredDesc ?? "لا توجد سجلات مالية أو اشتراكات تطابق الشروط الحالية.";
      } else if (text.includes("مستخدمين") || text.includes("فرق") || text.includes("حسابات") || text.includes("بلاغات")) {
        inferredIcon = "users";
        inferredDesc = inferredDesc ?? "لا توجد حسابات أو عناصر لعرضها في هذا القسم حالياً.";
      }
    }

    return (
      <EmptyState
        icon={inferredIcon}
        title={inferredTitle}
        description={inferredDesc}
        badge={inferredBadge}
        action={emptyAction}
        secondaryAction={emptySecondaryAction}
        hint={emptyHint}
      />
    );
  }
  return <>{children(query.data)}</>;
}

export function ErrorState({ error }: { error: unknown }) {
  const denied = error instanceof ApiError && error.status === 403;
  return (
    <div role="alert" className={`${card} rounded-[18px] p-8 text-center flex flex-col gap-1 animate-fade-in`}>
      <strong className="text-[15px]">{denied ? "مش عندك صلاحية للصفحة دي" : "حصل خطأ"}</strong>
      <span className={`text-sm ${muted}`}>
        {denied ? "دورك الحالي مايسمحش بالإجراء ده. لو محتاجه، كلّم مدير المنصة." : error instanceof Error ? error.message : "حاول تاني."}
      </span>
    </div>
  );
}

/** Simple, accessible table: column headers + rows; transforms into responsive cards on mobile. */
export function Table<T>({
  rows,
  columns,
  rowKey,
  onSelect,
  selected,
  caption,
  cardView,
}: {
  rows: T[];
  columns: { title: string; cell: (row: T) => React.ReactNode; className?: string }[];
  rowKey: (row: T) => string;
  onSelect?: (row: T) => void;
  selected?: string | null;
  caption: string;
  cardView?: (row: T, isSelected: boolean) => React.ReactNode;
}) {
  return (
    <>
      {/* Mobile Card Layout (hidden on md and larger) */}
      <div className="md:hidden flex flex-col gap-3">
        {rows.map((row) => {
          const key = rowKey(row);
          const on = key === selected;

          if (cardView) {
            return (
              <div
                key={key}
                onClick={onSelect ? () => onSelect(row) : undefined}
                className={onSelect ? "cursor-pointer" : undefined}
              >
                {cardView(row, on)}
              </div>
            );
          }

          const primaryCol = columns[0];
          const badgeCol = columns.find(
            (c) =>
              c.title.includes("حالة") ||
              c.title.includes("النزاع") ||
              c.title.includes("الأولوية") ||
              c.title.includes("الدور"),
          ) ?? (columns.length > 1 ? columns[columns.length - 1] : undefined);

          const secondaryCols = columns.filter((c) => c !== primaryCol && c !== badgeCol);

          return (
            <div
              key={key}
              onClick={onSelect ? () => onSelect(row) : undefined}
              onKeyDown={onSelect ? (e) => (e.key === "Enter" || e.key === " ") && onSelect(row) : undefined}
              tabIndex={onSelect ? 0 : undefined}
              aria-selected={onSelect ? on : undefined}
              className={`${card} rounded-2xl p-4 flex flex-col gap-3 border transition-all ${
                onSelect ? "cursor-pointer active:scale-[0.99]" : ""
              } ${on ? "border-brand-red ring-2 ring-brand-red/20 bg-[#FDFDFE]" : "border-[#E2E6EF] hover:border-slate-300"}`}
            >
              {/* Header: Primary title and Badge/Status */}
              <div className="flex items-start justify-between gap-2 pb-2.5 border-b border-[#F0F2F6]">
                <div className="flex flex-col gap-0.5 min-w-0">
                  <span className="text-[11px] font-semibold text-[#5B6475]">{primaryCol?.title}</span>
                  <div className="text-sm font-bold text-slate-900 truncate">{primaryCol?.cell(row)}</div>
                </div>
                {badgeCol && (
                  <div className="flex flex-col items-end gap-0.5 shrink-0">
                    <span className="text-[11px] font-semibold text-[#5B6475]">{badgeCol.title}</span>
                    <div>{badgeCol.cell(row)}</div>
                  </div>
                )}
              </div>

              {/* Grid of other columns */}
              {secondaryCols.length > 0 && (
                <div className="grid grid-cols-2 gap-2 text-xs">
                  {secondaryCols.map((c) => (
                    <div key={c.title} className="flex flex-col gap-0.5">
                      <span className={muted}>{c.title}</span>
                      <div className="font-semibold text-slate-800">{c.cell(row)}</div>
                    </div>
                  ))}
                </div>
              )}

              {/* Action indicator if selectable */}
              {onSelect && (
                <div className="flex items-center justify-between text-xs text-brand-red font-semibold pt-2 border-t border-dashed border-[#EEF0F5]">
                  <span>عرض التفاصيل</span>
                  <ChevronLeft className="size-3.5" />
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Desktop Table View */}
      <div className="hidden md:block overflow-x-auto rounded-[18px] border border-[#EDF0F5] bg-white shadow-[0_1px_2px_rgba(15,27,61,0.04),0_10px_24px_-14px_rgba(15,27,61,0.16)]">
        <table className="w-full text-sm">
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
                  className={`border-t border-[#EEF0F5] transition-all duration-150 ease-out ${
                    onSelect ? "cursor-pointer hover:bg-[#F6F7FA]" : ""
                  } ${on ? "bg-[#EEF0F5]" : ""}`}
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
    </>
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
    <div className="fixed inset-0 z-50 animate-fade-in" role="dialog" aria-modal="true" aria-label={title}>
      <button
        type="button"
        aria-label="إغلاق"
        onClick={onClose}
        className="absolute inset-0 bg-[rgba(10,19,48,0.45)] backdrop-blur-xs transition-opacity duration-200 cursor-pointer"
      />
      <div className="absolute inset-y-0 left-0 w-full sm:w-[520px] bg-white shadow-2xl overflow-y-auto p-5 flex flex-col gap-4 animate-slide-in-left" dir="rtl">
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

export function Select({
  value,
  onChange,
  options,
  label,
  className,
}: {
  value: string;
  onChange: (v: string) => void;
  options: [string, string][];
  label: string;
  className?: string;
}) {
  return (
    <select
      aria-label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={`min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] bg-white text-xs font-semibold text-slate-800 hover:border-slate-400 focus:outline-none focus:ring-2 focus:ring-ink focus:border-ink transition-all cursor-pointer ${className ?? "w-full"}`}
    >
      {options.map(([v, t]) => (
        <option key={v} value={v}>
          {t}
        </option>
      ))}
    </select>
  );
}

/**
 * How a list starts: a table on a computer, cards on a phone (a narrow screen cannot fit the columns). The page's toggle
 * still switches between them. Starts as a table and moves to cards after mount, so server and client render the same.
 */
export function useListView() {
  const [view, setView] = useState<"cards" | "table">("table");
  useEffect(() => {
    if (window.matchMedia("(max-width: 767px)").matches) setView("cards");
  }, []);
  return [view, setView] as const;
}

function sanitizeNoticeError(e: unknown): string {
  const raw = e instanceof Error ? e.message : String(e ?? "");
  if (!raw || raw === "[object Object]") return "حدث خطأ غير متوقع، يرجى المحاولة لاحقاً.";
  if (raw.includes("max requests limit exceeded") || raw.includes("quota")) {
    return "تم الوصول للحد الأقصى لخدمة الكاش (Upstash Redis). برجاء المحاولة لاحقاً أو مراجعة استهلاك الكاش.";
  }
  if (raw.includes("Failed to fetch") || raw.includes("NetworkError") || raw.includes("ECONNREFUSED") || raw.includes("Network request failed")) {
    return "تعذر الاتصال بالخادم. يرجى التأكد من اتصال الإنترنت والمحاولة مجدداً.";
  }
  if (raw.includes("Unauthorized") || raw.includes("401")) {
    return "انتهت صلاحية الجلسة، برجاء تسجيل الدخول مجدداً.";
  }
  if (raw.includes("Forbidden") || raw.includes("403")) {
    return "ليس لديك صلاحية كافية لتنفيذ هذا الإجراء.";
  }
  if (raw.includes("DbUpdateConcurrencyException") || raw.includes("concurrency")) {
    return "حدث تعارض أثناء الحفظ بسبب تعديل البيانات بواسطة مستخدم آخر. يرجى تحديث الصفحة.";
  }
  if (raw.includes("Internal Server Error") || raw.includes("500")) {
    return "حدث خطأ داخلي في الخادم أثناء معالجة الطلب. يرجى المحاولة بعد قليل.";
  }
  return raw;
}

/** Page-level animated snackbar/toast for the result of the last action. */
export function useNotice() {
  const [mounted, setMounted] = useState(false);
  const [notice, setNotice] = useState<{ text: string; bad?: boolean } | null>(null);

  useEffect(() => {
    setMounted(true);
  }, []);

  useEffect(() => {
    if (!notice) return;
    const timer = setTimeout(() => {
      setNotice(null);
    }, notice.bad ? 7000 : 4500);
    return () => clearTimeout(timer);
  }, [notice]);

  const clear = () => setNotice(null);

  const node =
    mounted && notice
      ? createPortal(
          <div
            role="status"
            aria-live="polite"
            className="fixed top-6 left-1/2 -translate-x-1/2 z-[9999] pointer-events-none w-full max-w-[480px] px-4"
          >
            <div
              className={`pointer-events-auto flex items-start gap-3 p-4 rounded-2xl shadow-[0_12px_36px_rgba(0,0,0,0.18)] border backdrop-blur-md transition-all duration-300 animate-fade-in-down ${
                notice.bad
                  ? "bg-white/95 border-red-200 text-slate-900 ring-1 ring-red-500/15"
                  : "bg-white/95 border-emerald-200 text-slate-900 ring-1 ring-emerald-500/15"
              }`}
            >
              <div
                className={`p-2 rounded-xl shrink-0 ${
                  notice.bad ? "bg-red-50 text-red-600" : "bg-emerald-50 text-emerald-600"
                }`}
              >
                {notice.bad ? <AlertCircle className="size-5" /> : <CheckCircle2 className="size-5" />}
              </div>
              <div className="flex-1 min-w-0 pt-0.5">
                <p className={`text-xs font-bold ${notice.bad ? "text-red-700" : "text-emerald-700"}`}>
                  {notice.bad ? "تنبيه / فشل الإجراء" : "تمت العملية بنجاح"}
                </p>
                <p className="text-sm font-semibold text-slate-800 leading-snug mt-0.5 break-words">
                  {notice.text}
                </p>
              </div>
              <button
                type="button"
                onClick={clear}
                className="p-1 rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-100 transition-colors shrink-0"
                aria-label="إغلاق التنبيه"
              >
                <X className="size-4" />
              </button>
            </div>
          </div>,
          document.body,
        )
      : null;

  return {
    node,
    ok: (text: string) => setNotice({ text, bad: false }),
    fail: (e: unknown) => setNotice({ text: sanitizeNoticeError(e), bad: true }),
    clear,
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
      <span className="font-semibold text-slate-700">
        {toArabicDigits(page)} / {toArabicDigits(pages)}
      </span>
      <button type="button" className={btnOutline} disabled={page >= pages} onClick={() => onChange(page + 1)}>
        التالي
      </button>
    </nav>
  );
}
