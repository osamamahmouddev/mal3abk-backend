"use client";

import React, { Suspense, useEffect, useState } from "react";
import Image from "next/image";
import { usePathname, useRouter } from "next/navigation";
import type { SessionUser } from "@malabak/bff/server";
import { Sidebar } from "./Sidebar";
import { SessionGate } from "./SessionGate";
import { useVenue } from "./Providers";
import { Toast } from "./ui";
import { RealtimeProvider, useVenueRealtime } from "@/lib/realtime";
import { logout } from "@/app/auth-actions";

export interface AppShellProps {
  children: React.ReactNode;
  user: SessionUser | null;
}

const PUBLIC_ROUTES = ["/login", "/register", "/verify-email", "/reset-password", "/accept-invitation", "/setup", "/impersonate"];

/** Owners land on the setup wizard until they have a venue. */
const NoVenueRedirect: React.FC = () => {
  const router = useRouter();
  const { venues, isLoading, loadFailed } = useVenue();
  useEffect(() => {
    // A failed request is not "no venues": sending the owner to the setup wizard would be wrong.
    if (!isLoading && !loadFailed && venues.length === 0) router.replace("/setup");
  }, [isLoading, loadFailed, venues.length, router]);
  return null;
};

const ShellFallback: React.FC = () => (
  <div className="flex-1 p-4 sm:p-8 flex flex-col gap-4 animate-fade-in">
    <div className="h-8 w-48 skeleton-shimmer rounded-lg" />
    <div className="h-64 w-full skeleton-shimmer rounded-2xl" />
  </div>
);

export const AppShell: React.FC<AppShellProps> = ({ children, user }) => {
  const pathname = usePathname();
  const { venueId } = useVenue();
  const [menuOpen, setMenuOpen] = useState(false);
  const isAuthOrSetupRoute = PUBLIC_ROUTES.some((route) => pathname?.startsWith(route));

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

  useEffect(() => {
    if (!user && !isAuthOrSetupRoute) {
      window.location.href = `/venue/login?next=${encodeURIComponent(pathname || "/")}`;
    }
  }, [user, isAuthOrSetupRoute, pathname]);

  if (isAuthOrSetupRoute) {
    return <main className="min-h-screen w-full">{children}</main>;
  }

  if (!user) {
    return (
      <main className="min-h-screen w-full flex items-center justify-center bg-[#F3F5F9]">
        <div className="w-8 h-8 rounded-full border-2 border-navy-900 border-t-transparent animate-spin" />
      </main>
    );
  }

  return (
    <SessionGate>
    <RealtimeProvider venueId={venueId}>
      <div className="lg:flex min-h-screen w-full bg-[#F3F5F9] text-[#0F172A] font-arabic antialiased" dir="rtl">
        <NoVenueRedirect />
        <LiveUpdates />
        {/* Desktop sidebar */}
        <div className="hidden lg:flex shrink-0">
          <Sidebar user={user} />
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
          <Image
            src="/venue/images/logo-horizontal-darkbg.png"
            alt="ملعبك"
            width={120}
            height={40}
            className="h-7 w-auto object-contain"
            priority
          />
          <span className="text-[11px] font-semibold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5 whitespace-nowrap">للملاعب</span>
        </header>

        {/* Mobile / tablet drawer */}
        {menuOpen && (
          <div className="lg:hidden fixed inset-0 z-50 animate-fade-in" role="dialog" aria-modal="true" aria-label="أقسام اللوحة">
            <button
              type="button"
              aria-label="إغلاق القائمة"
              onClick={() => setMenuOpen(false)}
              className="absolute inset-0 bg-[rgba(15,27,61,0.55)] backdrop-blur-xs transition-opacity"
            />
            <div className="absolute inset-y-0 right-0 max-w-[85vw] shadow-2xl animate-slide-in-right">
              <Sidebar user={user} onClose={() => setMenuOpen(false)} />
            </div>
          </div>
        )}

        <div className="flex-1 flex flex-col min-w-0">
          {user.impersonatedBy && <ImpersonationBanner endsAt={user.impersonationEndsAt} />}
          <Suspense fallback={<ShellFallback />}>{children}</Suspense>
        </div>
      </div>
    </RealtimeProvider>
    </SessionGate>
  );
};

const STATUS_TEXT: Record<string, string> = {
  PendingDeposit: "طلب حجز جديد",
  ProofSubmitted: "إيصال عربون جديد",
  Confirmed: "اتأكد الحجز",
  Cancelled: "اتلغى الحجز",
  Expired: "انتهت مهلة العربون",
};

/** Short live notice for booking changes at the selected venue (screen readers hear it through the toast's role=status). */
function LiveUpdates() {
  const [message, setMessage] = React.useState<string | null>(null);
  useVenueRealtime((u) => {
    const text = STATUS_TEXT[u.status];
    if (text) setMessage(`${text} — ${u.reference}`);
  });
  return <Toast message={message} onClose={() => setMessage(null)} />;
}

/**
 * Always visible while a Malabak support agent is signed in as this account (TASK-063). Sensitive actions (password,
 * phone, payment details) are refused by the API in this mode; the session ends on its own after 15 minutes.
 */
const ImpersonationBanner: React.FC<{ endsAt?: number }> = ({ endsAt }) => {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 15_000);
    return () => clearInterval(timer);
  }, []);
  const minutes = endsAt ? Math.max(0, Math.ceil((endsAt - now) / 60_000)) : null;
  return (
    <div role="alert" className="sticky top-0 z-30 bg-brand-red text-white px-4 py-2.5 text-sm font-semibold flex flex-wrap gap-2 items-center">
      <span className="flex-1">وضع الدعم: إنت داخل بصفة العميل. كل إجراء بيتسجل، وتغيير كلمة السر والرقم وبيانات الدفع مقفول.</span>
      {minutes !== null && <span>باقي {minutes} دقيقة</span>}
      <form action={logout}>
        <button type="submit" className="underline bg-transparent text-white">إنهاء الجلسة</button>
      </form>
    </div>
  );
};
