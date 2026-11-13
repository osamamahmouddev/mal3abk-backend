"use client";

import React, { useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import type { SessionUser } from "@malabak/bff/server";
import { adminLogout } from "@/app/auth-actions";
import { api, unwrap } from "@/lib/api";
import { SessionGate } from "./SessionGate";

/** Admin app sections, keyed by the navigation keys the API returns for the signed-in staff member's permissions. */
const SECTIONS: Record<string, { title: string; href: string }[]> = {
  dashboard: [{ title: "نظرة عامة", href: "/overview" }],
  users: [{ title: "المستخدمين", href: "/users" }],
  venues: [{ title: "الملاعب", href: "/venues" }],
  approvals: [{ title: "موافقات الملاعب", href: "/approvals" }],
  bookings: [{ title: "الحجوزات", href: "/bookings" }],
  disputes: [{ title: "نزاعات الإيصالات", href: "/disputes" }],
  moderation: [{ title: "الفرق والمجتمع", href: "/teams" }],
  support: [{ title: "تذاكر الدعم", href: "/support" }],
  plans: [{ title: "باقات الاشتراك", href: "/plans" }],
  subscriptions: [{ title: "الاشتراكات والفواتير", href: "/billing" }],
  invoices: [{ title: "الإيرادات والتقارير", href: "/reports" }],
  featured: [{ title: "ملاعب مميزة", href: "/featured" }],
  settings: [{ title: "إعدادات المنصة", href: "/settings" }, { title: "المناطق", href: "/areas" }],
  accounts: [{ title: "حسابات الفريق", href: "/accounts" }],
  audit: [{ title: "سجل التدقيق", href: "/audit" }],
};

function Brand({ height = "h-8" }: { height?: string; size?: string }) {
  return (
    <div className="flex items-center gap-2">
      <Image
        src="/admin/images/logo-horizontal-darkbg.png"
        alt="ملعبك"
        width={140}
        height={44}
        className={`${height} w-auto object-contain`}
        priority
      />
      <span className="text-[11px] font-bold text-white bg-brand-red rounded-md px-2 py-0.5 whitespace-nowrap">الإدارة</span>
    </div>
  );
}

const toArabicDigits = (n: number) => String(n).replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

function SideNav({ pathname, user, onClose }: { pathname: string; user: SessionUser; onClose?: () => void }) {
  const navigation = useQuery({
    queryKey: ["admin-navigation"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/navigation")),
    staleTime: 5 * 60_000,
  });
  const keys = new Set((navigation.data ?? []).map((n) => n.key));
  const items = Object.entries(SECTIONS)
    .filter(([key]) => keys.has(key))
    .flatMap(([, list]) => list)
    .filter((item, i, all) => all.findIndex((x) => x.href === item.href) === i);

  const pending = useQuery({
    queryKey: ["approvals"],
    enabled: keys.has("approvals"),
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/pending-approval", { params: { query: { page: 1, pageSize: 50 } } })),
    refetchInterval: 60_000,
  });
  const figures = useQuery({
    queryKey: ["dashboard-badges"],
    enabled: keys.has("dashboard"),
    queryFn: () => unwrap(api.GET("/api/v1/admin/dashboard", {})),
    refetchInterval: 120_000,
  });
  const badge: Record<string, number> = {
    "/approvals": pending.data?.length ?? 0,
    "/disputes": figures.data?.figures["disputes.open"] ?? 0,
    "/support": figures.data?.figures["support.open"] ?? 0,
  };
  const isActive = (href: string) => (href === "/overview" ? pathname === "/" || pathname === "/overview" : pathname.startsWith(href));

  return (
    <aside
      id={onClose ? "admin-nav-drawer" : undefined}
      className={`w-[248px] shrink-0 bg-ink text-white px-4 py-6 flex flex-col gap-5 ${onClose ? "h-full overflow-y-auto" : "min-h-screen"}`}
    >
      <div className="flex items-center gap-2.5 px-2">
        <Brand />
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            aria-label="إغلاق القائمة"
            className="ms-auto w-10 h-10 rounded-[10px] bg-white/[0.08] flex items-center justify-center"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        )}
      </div>
      <nav aria-label="أقسام الإدارة" className="flex flex-col gap-0.5">
        {navigation.isLoading && <div className="h-40 rounded-[9px] bg-white/[0.06] animate-pulse" />}
        {items.map((n) => {
          const active = isActive(n.href);
          return (
            <Link
              key={n.href}
              href={n.href}
              aria-current={active ? "page" : undefined}
              className={`min-h-[40px] px-3 rounded-[9px] flex items-center gap-2 text-sm ${
                active ? "bg-white text-ink font-bold hover:text-ink" : "text-[#D5DBE7] font-medium hover:bg-white/[0.08] hover:text-white"
              }`}
            >
              <span className="flex-1">{n.title}</span>
              {(badge[n.href] ?? 0) > 0 && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center">
                  {toArabicDigits(badge[n.href])}
                </span>
              )}
            </Link>
          );
        })}
      </nav>
      <div className="mt-auto pt-3 px-2 border-t border-white/[0.12] text-xs text-[#AEB7CA] flex justify-between items-center gap-2">
        <span>{user.name}</span>
        <form action={adminLogout}>
          <button type="submit" className="text-[#AEB7CA] hover:text-white underline min-h-[32px] bg-transparent">
            خروج
          </button>
        </form>
      </div>
    </aside>
  );
}

export function AdminShell({ children, user }: { children: React.ReactNode; user: SessionUser | null }) {
  const pathname = usePathname() ?? "/";
  const [menuOpen, setMenuOpen] = useState(false);

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
    if (!user && !pathname.startsWith("/login")) {
      window.location.href = `/admin/login?next=${encodeURIComponent(pathname)}`;
    }
  }, [user, pathname]);

  if (pathname.startsWith("/login") || !user) return <>{children}</>;

  return (
    <SessionGate>
    <div className="lg:flex min-h-screen w-full" dir="rtl">
      {/* Desktop sidebar */}
      <div className="hidden lg:flex">
        <SideNav pathname={pathname} user={user} />
      </div>

      {/* Mobile / tablet top bar */}
      <header className="lg:hidden sticky top-0 z-40 h-14 px-4 bg-ink text-white flex items-center gap-3">
        <button
          type="button"
          onClick={() => setMenuOpen(true)}
          aria-label="فتح القائمة"
          aria-expanded={menuOpen}
          aria-controls="admin-nav-drawer"
          className="w-10 h-10 -ms-1 rounded-[10px] flex items-center justify-center hover:bg-white/[0.08]"
        >
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <path d="M4 7h16M4 12h16M4 17h16" />
          </svg>
        </button>
        <Brand height="h-7" />
      </header>

      {/* Mobile / tablet drawer */}
      {menuOpen && (
        <div className="lg:hidden fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label="أقسام الإدارة">
          <button type="button" aria-label="إغلاق القائمة" onClick={() => setMenuOpen(false)} className="absolute inset-0 bg-[rgba(10,19,48,0.55)]" />
          <div className="absolute inset-y-0 right-0 max-w-[85vw] shadow-2xl">
            <SideNav pathname={pathname} user={user} onClose={() => setMenuOpen(false)} />
          </div>
        </div>
      )}

      <main className="flex-1 min-w-0">{children}</main>
    </div>
    </SessionGate>
  );
}
