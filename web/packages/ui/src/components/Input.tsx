import React from "react";

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  helperText?: string;
  startIcon?: React.ReactNode;
  endIcon?: React.ReactNode;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  (
    {
      label,
      error,
      helperText,
      startIcon,
      endIcon,
      id,
      disabled,
      className = "",
      ...props
    },
    ref
  ) => {
    const inputId = id ?? (label ? `input-${label.toLowerCase().replace(/\s+/g, "-")}` : undefined);
    const errorId = error && inputId ? `${inputId}-error` : undefined;
    const helperId = helperText && inputId ? `${inputId}-helper` : undefined;

    return (
      <div className="w-full text-start">
        {label && (
          <label
            htmlFor={inputId}
            className="block text-sm font-medium text-slate-700 dark:text-slate-200 mb-1.5"
          >
            {label}
            {props.required && <span className="text-[#D63B2F] ms-1">*</span>}
          </label>
        )}
        <div className="relative rounded-md shadow-sm">
          {startIcon && (
            <div className="pointer-events-none absolute inset-y-0 start-0 flex items-center ps-3 text-slate-400">
              {startIcon}
            </div>
          )}
          <input
            ref={ref}
            id={inputId}
            disabled={disabled}
            aria-invalid={Boolean(error)}
            aria-describedby={errorId ?? helperId}
            className={`block w-full rounded-md border text-sm transition-colors focus:outline-none focus:ring-2 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-500 dark:disabled:bg-slate-900 ${
              startIcon ? "ps-10" : "ps-3.5"
            } ${endIcon ? "pe-10" : "pe-3.5"} py-2.5 ${
              error
                ? "border-[#D63B2F] text-[#D63B2F] focus:border-[#D63B2F] focus:ring-[#D63B2F]/20"
                : "border-slate-300 text-slate-900 focus:border-[#0F1B3D] focus:ring-[#0F1B3D]/20 dark:border-slate-700 dark:text-white dark:bg-slate-900 dark:focus:border-[#D63B2F]"
            } ${className}`}
            {...props}
          />
          {endIcon && (
            <div className="pointer-events-none absolute inset-y-0 end-0 flex items-center pe-3 text-slate-400">
              {endIcon}
            </div>
          )}
        </div>
        {error && (
          <p id={errorId} className="mt-1.5 text-xs text-[#D63B2F] font-medium" role="alert">
            {error}
          </p>
        )}
        {!error && helperText && (
          <p id={helperId} className="mt-1.5 text-xs text-slate-500 dark:text-slate-400">
            {helperText}
          </p>
        )}
      </div>
    );
  }
);

Input.displayName = "Input";
