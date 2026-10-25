"use client";

import React, { Suspense } from "react";
import { usePathname } from "next/navigation";
import { Sidebar } from "./Sidebar";

export interface AppShellProps {
  children: React.ReactNode;
}

const ShellFallback: React.FC = () => (
  <div className="flex-1 p-8 animate-pulse flex flex-col gap-4">
    <div className="h-8 w-48 bg-slate-200 rounded-lg" />
    <div className="h-64 w-full bg-slate-200 rounded-2xl" />
  </div>
);

export const AppShell: React.FC<AppShellProps> = ({ children }) => {
  const pathname = usePathname();
  const isAuthOrSetupRoute = pathname?.startsWith("/login") || pathname?.startsWith("/setup");

  if (isAuthOrSetupRoute) {
    return <main className="min-h-screen w-full">{children}</main>;
  }

  return (
    <div
      className="flex min-h-screen w-full bg-[#F3F5F9] text-[#0F172A] font-arabic antialiased"
      dir="rtl"
    >
      <Sidebar />
      <div className="flex-1 flex flex-col min-w-0 overflow-y-auto">
        <Suspense fallback={<ShellFallback />}>{children}</Suspense>
      </div>
    </div>
  );
};
