"use client";

import React, { useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import type { SessionUser } from "@malabak/bff/server";
import { adminLogout } from "@/app/auth-actions";
import { api, unwrap } from "@/lib/api";
import { SessionGate } from "./SessionGate";
import { TopProgressBar } from "./TopProgressBar";
import { Pitch } from "./icons";
import { BadgeCheck, ChartColumn, ClipboardList, HandCoins, Handshake, Hourglass, LayoutDashboard, LifeBuoy, LogOut, type LucideIcon, MapPin, Menu, ReceiptText, Scale, ScrollText, Settings, ShieldCheck, Sparkles, Trophy, UserCog, Users, X } from "lucide-react";

/** Admin app sections, keyed by the navigation keys the API returns for the signed-in staff member's permissions. */
const SECTIONS: Record<string, { title: string; href: string; icon: LucideIcon | typeof Pitch }[]> = {
  dashboard: [{ title: "نظرة عامة", href: "/overview", icon: LayoutDashboard }],
  users: [
    { title: "المستخدمين", href: "/users", icon: Users },
    { title: "قائمة الانتظار", href: "/waitlist", icon: Hourglass },
  ],
  venues: [{ title: "الملاعب", href: "/venues", icon: Pitch }],
  approvals: [{ title: "موافقات الملاعب", href: "/approvals", icon: BadgeCheck }],
  crm: [{ title: "متابعة الملاعب", href: "/crm", icon: Handshake }],
  bookings: [{ title: "الحجوزات", href: "/bookings", icon: ClipboardList }],
  deposits: [{ title: "مراجعة العرابين", href: "/deposits", icon: ReceiptText }],
  payouts: [{ title: "مستحقات الملاعب", href: "/payouts", icon: HandCoins }],
  disputes: [{ title: "نزاعات الإيصالات", href: "/disputes", icon: Scale }],
  moderation: [{ title: "الفرق والمجتمع", href: "/teams", icon: Trophy }],
  support: [{ title: "تذاكر الدعم", href: "/support", icon: LifeBuoy }],
  invoices: [{ title: "الإيرادات والتقارير", href: "/reports", icon: ChartColumn }],
  featured: [{ title: "ملاعب مميزة", href: "/featured", icon: Sparkles }],
  settings: [{ title: "إعدادات المنصة", href: "/settings", icon: Settings }, { title: "المناطق", href: "/areas", icon: MapPin }],
  accounts: [{ title: "حسابات الفريق", href: "/accounts", icon: UserCog }],
  audit: [{ title: "سجل التدقيق", href: "/audit", icon: ScrollText }],
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
  const router = useRouter();
  const [pendingHref, setPendingHref] = useState<string | null>(null);

  useEffect(() => {
    setPendingHref(null);
  }, [pathname]);

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

  // Eagerly prefetch routes so clicking tabs is instantaneous
  useEffect(() => {
    items.forEach((n) => {
      router.prefetch(n.href);
    });
  }, [items, router]);

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
  const deposits = useQuery({
    queryKey: ["admin-deposit-reviews"],
    enabled: keys.has("deposits"),
    queryFn: () => unwrap(api.GET("/api/v1/admin/bookings/deposit-reviews", { params: { query: { page: 1, pageSize: 50 } } })),
    refetchInterval: 60_000,
  });
  const badge: Record<string, number> = {
    "/approvals": pending.data?.length ?? 0,
    "/deposits": deposits.data?.total ?? 0,
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
            className="ms-auto w-10 h-10 rounded-[10px] bg-white/[0.08] hover:bg-white/[0.14] transition-colors flex items-center justify-center"
          >
            <X className="size-5" />
          </button>
        )}
      </div>
      <nav aria-label="أقسام الإدارة" className="flex flex-col gap-0.5">
        {navigation.isLoading && <div className="h-40 rounded-[9px] bg-white/[0.06] animate-pulse" />}
        {items.map((n) => {
          const active = isActive(n.href);
          const isPending = pendingHref === n.href && !active;
          return (
            <Link
              key={n.href}
              href={n.href}
              prefetch={true}
              aria-current={active ? "page" : undefined}
              onClick={() => {
                if (!active) {
                  setPendingHref(n.href);
                  if (typeof window !== "undefined") {
                    window.dispatchEvent(new Event("malabak-nav-start"));
                  }
                }
                onClose?.();
              }}
              className={`group min-h-[40px] px-3 rounded-[9px] flex items-center gap-2.5 text-sm cursor-pointer transition-all duration-150 ease-out active:scale-[0.98] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-gold/70 ${
                active
                  ? "bg-white text-ink font-bold hover:text-ink shadow-xs"
                  : isPending
                    ? "bg-white/[0.16] text-white font-bold ring-1 ring-white/25"
                    : "text-[#D5DBE7] font-medium hover:bg-white/[0.08] hover:text-white"
              }`}
            >
              <n.icon
                className={`size-[18px] shrink-0 transition-colors duration-150 ${active ? "text-brand-red" : "text-[#AEB7CA] group-hover:text-white"}`}
                strokeWidth={active ? 2.25 : 1.75}
              />
              <span className="flex-1">{n.title}</span>
              {isPending && (
                <div className="w-3.5 h-3.5 rounded-full border-2 border-white/60 border-t-white animate-spin shrink-0" />
              )}
              {(badge[n.href] ?? 0) > 0 && !isPending && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center animate-scale-in">
                  {toArabicDigits(badge[n.href])}
                </span>
              )}
            </Link>
          );
        })}
      </nav>
      <div className="mt-auto pt-3 border-t border-white/[0.12] flex flex-col gap-2.5">
        <div className="flex items-center gap-2.5 px-1">
          <div className="w-8 h-8 rounded-full bg-brand-red text-white flex items-center justify-center font-bold text-xs shrink-0 shadow-sm">
            {user.name.trim().charAt(0) || "م"}
          </div>
          <span className="flex-1 min-w-0 text-[13px] font-bold text-white truncate" title={user.name}>
            {user.name}
          </span>
        </div>
        <Link
          href="/security"
          onClick={() => onClose?.()}
          aria-current={pathname.startsWith("/security") ? "page" : undefined}
          className={`w-full min-h-[36px] px-3 rounded-xl text-xs font-semibold flex items-center justify-center gap-2 transition-all duration-150 ${
            pathname.startsWith("/security") ? "bg-white text-ink" : "bg-white/[0.08] text-[#D5DBE7] hover:bg-white/[0.14] hover:text-white"
          }`}
        >
          <ShieldCheck className="size-4" />
          أمان الحساب
        </Link>
        <form action={adminLogout}>
          <button
            type="submit"
            className="w-full min-h-[36px] px-3 rounded-xl bg-white/[0.08] hover:bg-[#A8261C] active:scale-[0.98] text-xs font-semibold text-[#D5DBE7] hover:text-white transition-all duration-150 flex items-center justify-center gap-2 cursor-pointer"
          >
            <LogOut className="size-4" />
            تسجيل الخروج
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

  if (pathname.startsWith("/login")) return <>{children}</>;

  if (!user) {
    return (
      <div className="min-h-screen w-full flex items-center justify-center bg-[#F3F5F9]">
        <div className="w-8 h-8 rounded-full border-2 border-slate-900 border-t-transparent animate-spin" />
      </div>
    );
  }

  return (
    <SessionGate>
      <TopProgressBar />
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
          <Menu className="size-[22px]" />
        </button>
        <Brand height="h-7" />
      </header>

      {/* Mobile / tablet drawer */}
      {menuOpen && (
        <div className="lg:hidden fixed inset-0 z-50 animate-fade-in" role="dialog" aria-modal="true" aria-label="أقسام الإدارة">
          <button
            type="button"
            aria-label="إغلاق القائمة"
            onClick={() => setMenuOpen(false)}
            className="absolute inset-0 bg-[rgba(10,19,48,0.55)] backdrop-blur-xs transition-opacity"
          />
          <div className="absolute inset-y-0 right-0 max-w-[85vw] shadow-2xl animate-slide-in-right">
            <SideNav pathname={pathname} user={user} onClose={() => setMenuOpen(false)} />
          </div>
        </div>
      )}

      <main className="flex-1 min-w-0">{children}</main>
    </div>
    </SessionGate>
  );
}
