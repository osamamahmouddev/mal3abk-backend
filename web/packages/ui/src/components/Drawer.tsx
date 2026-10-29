import React, { useEffect } from "react";

export interface DrawerProps {
  isOpen: boolean;
  onClose: () => void;
  title?: string;
  placement?: "start" | "end";
  children: React.ReactNode;
  footer?: React.ReactNode;
  className?: string;
}

export const Drawer: React.FC<DrawerProps> = ({
  isOpen,
  onClose,
  title,
  placement = "end",
  children,
  footer,
  className = "",
}) => {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.body.style.overflow = "hidden";
      window.addEventListener("keydown", handleKeyDown);
    }

    return () => {
      document.body.style.overflow = "unset";
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const placementStyles =
    placement === "start"
      ? "inset-y-0 start-0 border-e"
      : "inset-y-0 end-0 border-s";

  return (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-50 overflow-hidden"
    >
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/40 backdrop-blur-sm transition-opacity"
        onClick={onClose}
        aria-hidden="true"
      />

      <div
        className={`fixed ${placementStyles} z-10 w-full max-w-md bg-white p-6 shadow-2xl dark:bg-slate-900 dark:border-slate-800 flex flex-col justify-between text-start ${className}`}
      >
        <div className="flex-1 overflow-y-auto">
          {title && (
            <div className="flex items-center justify-between pb-4 border-b border-slate-100 dark:border-slate-800 mb-4">
              <h3 className="text-lg font-semibold text-slate-900 dark:text-white">{title}</h3>
              <button
                onClick={onClose}
                aria-label="Close drawer"
                className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800 dark:hover:text-slate-200 transition-colors"
              >
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
          )}
          {children}
        </div>

        {footer && (
          <div className="pt-4 border-t border-slate-100 dark:border-slate-800 flex items-center justify-end gap-3 mt-4">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
};
