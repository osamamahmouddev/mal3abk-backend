"use client";

import React, { useCallback, useEffect, useState } from "react";

export interface ConfirmOptions {
  title: string;
  message?: string;
  confirmLabel?: string;
  /** Red confirm button for actions that delete something. */
  destructive?: boolean;
}

/** A confirmation in the dashboard's own style (the browser's native confirm box looks out of place and cannot be styled). */
export function ConfirmDialog({
  title,
  message,
  confirmLabel = "تأكيد",
  destructive = false,
  onConfirm,
  onClose,
}: ConfirmOptions & { onConfirm: () => void; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50" onClick={onClose}>
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        aria-describedby={message ? "confirm-message" : undefined}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-[400px] bg-white rounded-[20px] p-6 flex flex-col gap-4 shadow-[0_24px_60px_-20px_rgba(15,27,61,0.45)]"
        dir="rtl"
      >
        <div className="flex items-start gap-3">
          {destructive && (
            <span aria-hidden="true" className="shrink-0 w-10 h-10 rounded-full bg-[#FDECEA] text-[#A8261C] flex items-center justify-center text-lg font-bold">
              !
            </span>
          )}
          <div className="flex flex-col gap-1">
            <h2 id="confirm-title" className="text-[18px] font-bold leading-snug">
              {title}
            </h2>
            {message && (
              <p id="confirm-message" className="text-sm text-[#5B6475] leading-6">
                {message}
              </p>
            )}
          </div>
        </div>
        <div className="flex gap-2 justify-end">
          <button type="button" onClick={onClose} className="min-h-[44px] px-5 rounded-xl bg-white border border-[#D9DEE8] text-sm font-bold">
            إلغاء
          </button>
          <button
            type="button"
            autoFocus
            onClick={() => {
              onClose();
              onConfirm();
            }}
            className={`min-h-[44px] px-5 rounded-xl text-white text-sm font-bold ${destructive ? "bg-[#C8372B] hover:bg-[#A8261C]" : "bg-navy-900 hover:bg-navy-800"}`}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * `const [confirm, confirmDialog] = useConfirm();` then `confirm({ title: "تمسح الملعب؟", destructive: true }, () => remove())`
 * and render `{confirmDialog}` once in the page.
 */
export function useConfirm(): [(options: ConfirmOptions, onConfirm: () => void) => void, React.ReactNode] {
  const [pending, setPending] = useState<{ options: ConfirmOptions; onConfirm: () => void } | null>(null);
  const ask = useCallback((options: ConfirmOptions, onConfirm: () => void) => setPending({ options, onConfirm }), []);
  const dialog = pending ? <ConfirmDialog {...pending.options} onConfirm={pending.onConfirm} onClose={() => setPending(null)} /> : null;
  return [ask, dialog];
}
