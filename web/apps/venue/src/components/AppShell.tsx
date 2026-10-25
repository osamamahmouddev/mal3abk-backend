"use client";

import React, { Suspense, useEffect, useState } from "react";
import { usePathname } from "next/navigation";
import { Sidebar } from "./Sidebar";

export interface AppShellProps {
  children: React.ReactNode;
}

const ShellFallback: React.FC = () => (
  <div className="flex-1 p-4 sm:p-8 animate-pulse flex flex-col gap-4">
    <div className="h-8 w-48 bg-slate-200 rounded-lg" />
    <div className="h-64 w-full bg-slate-200 rounded-2xl" />
  </div>
);

export const AppShell: React.FC<AppShellProps> = ({ children }) => {
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const isAuthOrSetupRoute = pathname?.startsWith("/login") || pathname?.startsWith("/setup");

  // Close the mobile drawer whenever the route changes.
  useEffect(() => setMenuOpen(false), [pathname]);

  // While the drawer is open: lock page scroll and close on Escape.
  useEffect(() => {
    if (!menuOpen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setMenuOpen(false);
    window.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = prev;
      window.removeEventListener("keydown", onKey);
    };
  }, [menuOpen]);

  if (isAuthOrSetupRoute) {
    return <main className="min-h-screen w-full">{children}</main>;
  }

  return (
    <div className="lg:flex min-h-screen w-full bg-[#F3F5F9] text-[#0F172A] font-arabic antialiased" dir="rtl">
      {/* Desktop sidebar */}
      <div className="hidden lg:flex">
        <Sidebar />
      </div>

      {/* Mobile / tablet top bar */}
      <header className="lg:hidden sticky top-0 z-40 h-14 px-4 bg-navy-900 text-white flex items-center gap-3">
        <button
          type="button"
          onClick={() => setMenuOpen(true)}
          aria-label="فتح القائمة"
          aria-expanded={menuOpen}
          aria-controls="venue-nav-drawer"
          className="w-10 h-10 -ms-1 rounded-[10px] flex items-center justify-center hover:bg-white/[0.08]"
        >
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <path d="M4 7h16M4 12h16M4 17h16" />
          </svg>
        </button>
        <span className="text-xl font-bold">ملعبك</span>
        <span className="text-[11px] font-semibold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5">للملاعب</span>
      </header>

      {/* Mobile / tablet drawer */}
      {menuOpen && (
        <div className="lg:hidden fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label="أقسام اللوحة">
          <button type="button" aria-label="إغلاق القائمة" onClick={() => setMenuOpen(false)} className="absolute inset-0 bg-[rgba(15,27,61,0.55)]" />
          <div className="absolute inset-y-0 right-0 max-w-[85vw] shadow-2xl">
            <Sidebar onClose={() => setMenuOpen(false)} />
          </div>
        </div>
      )}

      <div className="flex-1 flex flex-col min-w-0">
        <Suspense fallback={<ShellFallback />}>{children}</Suspense>
      </div>
    </div>
  );
};
