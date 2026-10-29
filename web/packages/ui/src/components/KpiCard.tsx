import React from "react";
import { Card, CardContent } from "./Card";

export interface KpiCardProps {
  title: string;
  value: string | number;
  change?: string;
  trend?: "up" | "down" | "neutral";
  trendLabel?: string;
  icon?: React.ReactNode;
  className?: string;
}

export const KpiCard: React.FC<KpiCardProps> = ({
  title,
  value,
  change,
  trend = "neutral",
  trendLabel,
  icon,
  className = "",
}) => {
  const getTrendStyle = () => {
    switch (trend) {
      case "up":
        return "text-emerald-600 bg-emerald-50 dark:bg-emerald-950/40 dark:text-emerald-400";
      case "down":
        return "text-[#D63B2F] bg-[#D63B2F]/10 dark:bg-red-950/40 dark:text-red-400";
      case "neutral":
      default:
        return "text-slate-600 bg-slate-100 dark:bg-slate-800 dark:text-slate-300";
    }
  };

  return (
    <Card className={`overflow-hidden ${className}`}>
      <CardContent className="p-6">
        <div className="flex items-center justify-between">
          <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{title}</p>
          {icon && (
            <div className="w-10 h-10 rounded-lg bg-slate-100 dark:bg-slate-800 flex items-center justify-center text-slate-600 dark:text-slate-300 flex-shrink-0">
              {icon}
            </div>
          )}
        </div>
        <div className="mt-4 flex items-baseline gap-2">
          <span className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
            {value}
          </span>
          {change && (
            <span
              className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${getTrendStyle()}`}
            >
              {trend === "up" && "↑ "}
              {trend === "down" && "↓ "}
              {change}
            </span>
          )}
        </div>
        {trendLabel && (
          <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">{trendLabel}</p>
        )}
      </CardContent>
    </Card>
  );
};
