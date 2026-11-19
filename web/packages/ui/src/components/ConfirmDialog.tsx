"use client";

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";

export type ConfirmType = "destructive" | "warning" | "info";

export interface ConfirmOptions {
  title: string;
  message?: React.ReactNode;
  confirmLabel?: string;
  /** Label for cancel button, or null/empty to hide it (e.g. for alert-only notices). */
  cancelLabel?: string | null;
  /** Red destructive style (for deletions and irreversible actions). */
  destructive?: boolean;
  /** Explicit dialog type: destructive (red), warning (amber), or info (navy/blue). */
  type?: ConfirmType;
  /** Highlighted text (e.g. record name or count) displayed in a distinct badge. */
  itemHighlight?: string;
  /** Optional badge text at the top (e.g. "حذف نهائي", "تحذير أمني"). */
  badge?: string;
}

interface DialogState extends ConfirmOptions {
  resolve?: (value: boolean) => void;
  callback?: () => void;
}

export interface ConfirmDialogProps extends ConfirmOptions {
  isOpen?: boolean;
  onConfirm: () => void;
  onCancel?: () => void;
  onClose?: () => void;
}

/**
 * Animated, accessible, and branded confirmation modal dialog.
 * Replaces ugly browser `confirm()` and `alert()` boxes across Malabak.
 */
export function ConfirmDialog({
  isOpen = true,
  title,
  message,
  confirmLabel,
  cancelLabel,
  destructive = false,
  type,
  itemHighlight,
  badge,
  onConfirm,
  onCancel,
  onClose,
}: ConfirmDialogProps) {
  const showCancel = cancelLabel !== null && cancelLabel !== "";
  const resolvedCancelLabel = cancelLabel ?? "إلغاء";
  const handleDismiss = onCancel ?? onClose ?? (() => {});
  const confirmButtonRef = useRef<HTMLButtonElement>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  const resolvedType: ConfirmType = type ?? (destructive ? "destructive" : "info");
  const isDestructive = resolvedType === "destructive";
  const isWarning = resolvedType === "warning";

  const resolvedConfirmLabel =
    confirmLabel ?? (isDestructive ? "نعم، احذف" : isWarning ? "تأكيد" : "متابعة");

  // Handle keyboard navigation: Escape to cancel
  useEffect(() => {
    if (!isOpen) return;

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        handleDismiss();
      }
    };

    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", onKeyDown);

    // Focus cancel button for destructive actions to avoid accidental enter; focus confirm otherwise
    const timer = setTimeout(() => {
      if (isDestructive && showCancel) {
        cancelButtonRef.current?.focus();
      } else {
        confirmButtonRef.current?.focus();
      }
    }, 50);

    return () => {
      document.body.style.overflow = "unset";
      window.removeEventListener("keydown", onKeyDown);
      clearTimeout(timer);
    };
  }, [isOpen, handleDismiss, isDestructive]);

  if (!isOpen) return null;

  return (
    <div
      role="presentation"
      className="fixed inset-0 z-[100] flex items-center justify-center p-4 sm:p-6 overflow-y-auto cursor-pointer"
      onClick={handleDismiss}
    >
      {/* Backdrop with elegant blur */}
      <div
        className="fixed inset-0 bg-[#0A1330]/60 backdrop-blur-sm animate-fade-in transition-opacity"
        aria-hidden="true"
      />

      {/* Modal Dialog Card */}
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-modal-title"
        aria-describedby={message ? "confirm-modal-message" : undefined}
        onClick={(e) => e.stopPropagation()}
        className="relative z-10 w-full max-w-[430px] rounded-[22px] bg-white p-6 shadow-[0_24px_60px_-15px_rgba(10,19,48,0.35),0_0_0_1px_rgba(15,27,61,0.06)] animate-modal-in cursor-default border border-[#EDF0F5] overflow-hidden"
        dir="rtl"
      >
        <div className="flex items-start gap-3.5">
          {/* Status Icon */}
          <div
            className={`shrink-0 w-11 h-11 rounded-xl flex items-center justify-center ${
              isDestructive
                ? "bg-red-50 text-[#D63B2F] border border-red-100"
                : isWarning
                ? "bg-amber-50 text-amber-600 border border-amber-100"
                : "bg-slate-100 text-[#0A1330] border border-slate-200"
            }`}
            aria-hidden="true"
          >
            {isDestructive ? (
              // Standard Lucide Trash2 icon
              <svg
                className="w-5 h-5"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="M3 6h18" />
                <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" />
                <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" />
                <line x1="10" x2="10" y1="11" y2="17" />
                <line x1="14" x2="14" y1="11" y2="17" />
              </svg>
            ) : isWarning ? (
              // Alert triangle icon
              <svg
                className="w-5 h-5"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
                <line x1="12" x2="12" y1="9" y2="13" />
                <line x1="12" x2="12.01" y1="17" y2="17" />
              </svg>
            ) : (
              // Info circle icon
              <svg
                className="w-5 h-5"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <circle cx="12" cy="12" r="10" />
                <line x1="12" x2="12" y1="16" y2="12" />
                <line x1="12" x2="12.01" y1="8" y2="8" />
              </svg>
            )}
          </div>

          {/* Texts */}
          <div className="flex-1 min-w-0 pt-0.5">
            {badge && (
              <span
                className={`inline-block text-[11px] font-bold px-2.5 py-0.5 rounded-full mb-1.5 ${
                  isDestructive
                    ? "bg-red-50 text-red-700 border border-red-200/60"
                    : isWarning
                    ? "bg-amber-50 text-amber-800 border border-amber-200/60"
                    : "bg-slate-100 text-slate-700 border border-slate-200"
                }`}
              >
                {badge}
              </span>
            )}
            <h3
              id="confirm-modal-title"
              className="text-[17px] sm:text-[18px] font-bold text-slate-900 leading-snug"
            >
              {title}
            </h3>

            {message && (
              <div
                id="confirm-modal-message"
                className="mt-1.5 text-sm text-[#5B6475] leading-relaxed"
              >
                {message}
              </div>
            )}

            {itemHighlight && (
              <div className="mt-3 p-2.5 rounded-xl bg-slate-50 border border-slate-200/70 text-xs text-slate-800 font-semibold flex items-center gap-2">
                <span className="w-1.5 h-1.5 rounded-full bg-slate-400 shrink-0" />
                <span className="truncate">{itemHighlight}</span>
              </div>
            )}
          </div>
        </div>

        {/* Buttons */}
        <div className="flex items-center justify-end gap-2.5 mt-6 pt-4 border-t border-[#EEF0F5]">
          {showCancel && (
            <button
              ref={cancelButtonRef}
              type="button"
              onClick={handleDismiss}
              className="min-h-[42px] px-5 rounded-xl bg-white border border-[#D9DEE8] hover:bg-slate-50 hover:border-slate-300 text-slate-700 text-sm font-bold active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              {resolvedCancelLabel}
            </button>
          )}

          <button
            ref={confirmButtonRef}
            type="button"
            onClick={onConfirm}
            className={`min-h-[42px] px-6 rounded-xl text-white text-sm font-bold active:scale-[0.98] transition-all duration-150 cursor-pointer flex items-center justify-center ${
              isDestructive
                ? "bg-[#D63B2F] hover:bg-[#B82E23] shadow-[0_4px_14px_rgba(214,59,47,0.3)]"
                : isWarning
                ? "bg-amber-600 hover:bg-amber-700 shadow-[0_4px_14px_rgba(217,119,6,0.25)]"
                : "bg-[#0A1330] hover:bg-[#152147] shadow-[0_4px_14px_rgba(10,19,48,0.25)]"
            }`}
          >
            {resolvedConfirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

// ==============================================================================
// Confirm Provider & Hooks
// ==============================================================================

type ConfirmFn = {
  (options: ConfirmOptions): Promise<boolean>;
  (options: ConfirmOptions, onConfirm: () => void): void;
};

interface ConfirmContextValue {
  confirm: ConfirmFn;
}

const ConfirmContext = createContext<ConfirmContextValue | null>(null);

/**
 * Root Provider that enables `useConfirm()` anywhere across the app
 * without needing to manually render `{confirmDialog}` in each page.
 */
export function ConfirmProvider({ children }: { children: React.ReactNode }) {
  const [dialogState, setDialogState] = useState<DialogState | null>(null);

  const confirm = useCallback(
    ((options: ConfirmOptions, callback?: () => void) => {
      if (callback) {
        setDialogState({
          ...options,
          callback,
        });
        return;
      }

      return new Promise<boolean>((resolve) => {
        setDialogState({
          ...options,
          resolve,
        });
      });
    }) as ConfirmFn,
    []
  );

  const handleConfirm = useCallback(() => {
    if (dialogState?.callback) {
      dialogState.callback();
    }
    if (dialogState?.resolve) {
      dialogState.resolve(true);
    }
    setDialogState(null);
  }, [dialogState]);

  const handleCancel = useCallback(() => {
    if (dialogState?.resolve) {
      dialogState.resolve(false);
    }
    setDialogState(null);
  }, [dialogState]);

  return (
    <ConfirmContext.Provider value={{ confirm }}>
      {children}
      {dialogState && (
        <ConfirmDialog
          isOpen={true}
          {...dialogState}
          onConfirm={handleConfirm}
          onCancel={handleCancel}
        />
      )}
    </ConfirmContext.Provider>
  );
}

/**
 * Universal confirmation hook.
 *
 * Supports BOTH patterns:
 * 1. Global / Promise style (inside <ConfirmProvider>):
 *    `const confirm = useConfirm();`
 *    `const ok = await confirm({ title: "حذف الملعب؟", destructive: true });`
 *    `if (ok) deletePitch();`
 *
 * 2. Callback style:
 *    `confirm({ title: "حذف الملعب؟", destructive: true }, () => deletePitch());`
 *
 * 3. Tuple style for backward compatibility:
 *    `const [confirm, confirmDialog] = useConfirm();`
 */
export function useConfirm(): ConfirmFn & [(options: ConfirmOptions, onConfirm: () => void) => void, React.ReactNode] {
  const context = useContext(ConfirmContext);
  const [localState, setLocalState] = useState<DialogState | null>(null);

  const ask = useCallback(
    ((options: ConfirmOptions, callback?: () => void) => {
      // If inside ConfirmProvider, prefer global context
      if (context) {
        return context.confirm(options, callback as any);
      }

      // Fallback to local state if no provider
      if (callback) {
        setLocalState({ ...options, callback });
        return;
      }

      return new Promise<boolean>((resolve) => {
        setLocalState({ ...options, resolve });
      });
    }) as ConfirmFn,
    [context]
  );

  const localDialog = localState ? (
    <ConfirmDialog
      isOpen={true}
      {...localState}
      onConfirm={() => {
        localState.callback?.();
        localState.resolve?.(true);
        setLocalState(null);
      }}
      onCancel={() => {
        localState.resolve?.(false);
        setLocalState(null);
      }}
    />
  ) : null;

  // Make `ask` act as both a callable function and an iterable tuple [ask, dialog]
  const callableAndTuple = Object.assign(
    ((opts: ConfirmOptions, cb?: () => void) => ask(opts, cb as any)) as ConfirmFn,
    {
      0: ask,
      1: context ? null : localDialog,
      [Symbol.iterator]: function* () {
        yield ask;
        yield context ? null : localDialog;
      },
    }
  );

  return callableAndTuple as any;
}
