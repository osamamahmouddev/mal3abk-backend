import React from "react";

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "secondary" | "outline" | "danger" | "ghost";
  size?: "sm" | "md" | "lg";
  isLoading?: boolean;
  startIcon?: React.ReactNode;
  endIcon?: React.ReactNode;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      children,
      variant = "primary",
      size = "md",
      isLoading = false,
      startIcon,
      endIcon,
      disabled,
      className = "",
      ...props
    },
    ref
  ) => {
    const baseStyles =
      "inline-flex items-center justify-center font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50 select-none rounded-md";

    const variantStyles = {
      primary:
        "bg-[#071832] text-white hover:bg-[#0c2344] active:bg-[#040e1e] focus-visible:ring-[#071832] dark:bg-[#EB4F42] dark:hover:bg-[#ee7168]",
      secondary:
        "bg-slate-100 text-slate-900 hover:bg-slate-200 active:bg-slate-300 focus-visible:ring-slate-400 dark:bg-slate-800 dark:text-slate-100",
      outline:
        "border border-slate-300 bg-transparent text-slate-700 hover:bg-slate-50 focus-visible:ring-slate-400 dark:border-slate-700 dark:text-slate-200 dark:hover:bg-slate-800",
      danger:
        "bg-[#EB4F42] text-white hover:bg-[#bc3f35] active:bg-[#8d2f28] focus-visible:ring-[#EB4F42]",
      ghost:
        "bg-transparent text-slate-700 hover:bg-slate-100 focus-visible:ring-slate-400 dark:text-slate-200 dark:hover:bg-slate-800",
    };

    const sizeStyles = {
      sm: "h-8 px-3 text-xs gap-1.5",
      md: "h-10 px-4 text-sm gap-2",
      lg: "h-12 px-6 text-base gap-2.5",
    };

    return (
      <button
        ref={ref}
        disabled={disabled || isLoading}
        className={`${baseStyles} ${variantStyles[variant]} ${sizeStyles[size]} ${className}`}
        {...props}
      >
        {isLoading ? (
          <svg
            className="animate-spin h-4 w-4 text-current"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
            aria-hidden="true"
          >
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path
              className="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
            />
          </svg>
        ) : (
          startIcon && <span className="flex-shrink-0">{startIcon}</span>
        )}
        <span>{children}</span>
        {!isLoading && endIcon && <span className="flex-shrink-0">{endIcon}</span>}
      </button>
    );
  }
);

Button.displayName = "Button";
