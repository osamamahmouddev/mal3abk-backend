import React, { useEffect } from "react";

export type ToastType = "success" | "error" | "warning" | "info";

export interface ToastProps {
  id: string;
  type: ToastType;
  title: string;
  message?: string;
  durationMs?: number;
  onDismiss: (id: string) => void;
  className?: string;
}

export const Toast: React.FC<ToastProps> = ({
  id,
  type,
  title,
  message,
  durationMs = 5000,
  onDismiss,
  className = "",
}) => {
  useEffect(() => {
    if (durationMs <= 0) return;
    const timer = setTimeout(() => {
      onDismiss(id);
    }, durationMs);

    return () => clearTimeout(timer);
  }, [id, durationMs, onDismiss]);

  const getStyleAndIcon = () => {
    switch (type) {
      case "success":
        return {
          containerClass: "border-emerald-200 bg-emerald-50 text-emerald-900 dark:bg-emerald-950/60 dark:border-emerald-800 dark:text-emerald-200",
          icon: (
            <svg className="w-5 h-5 text-emerald-600 dark:text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          ),
        };
      case "error":
        return {
          containerClass: "border-rose-200 bg-rose-50 text-rose-900 dark:bg-rose-950/60 dark:border-rose-800 dark:text-rose-200",
          icon: (
            <svg className="w-5 h-5 text-[#EB4F42]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          ),
        };
      case "warning":
        return {
          containerClass: "border-amber-200 bg-amber-50 text-amber-900 dark:bg-amber-950/60 dark:border-amber-800 dark:text-amber-200",
          icon: (
            <svg className="w-5 h-5 text-amber-600 dark:text-amber-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
          ),
        };
      case "info":
      default:
        return {
          containerClass: "border-blue-200 bg-blue-50 text-blue-900 dark:bg-blue-950/60 dark:border-blue-800 dark:text-blue-200",
          icon: (
            <svg className="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          ),
        };
    }
  };

  const { containerClass, icon } = getStyleAndIcon();

  return (
    <div
      role="alert"
      className={`flex items-start gap-3 w-full max-w-sm rounded-lg border p-4 shadow-lg transition-all text-start ${containerClass} ${className}`}
    >
      <div className="flex-shrink-0 mt-0.5">{icon}</div>
      <div className="flex-1">
        <h4 className="text-sm font-semibold">{title}</h4>
        {message && <p className="mt-1 text-xs opacity-90">{message}</p>}
      </div>
      <button
        onClick={() => onDismiss(id)}
        aria-label="Dismiss notification"
        className="flex-shrink-0 text-current opacity-60 hover:opacity-100 transition-opacity p-1 rounded"
      >
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
    </div>
  );
};
