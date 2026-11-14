"use client";

import { VENUE_SURFACE } from "@/lib/notification-surface";
import React, { memo, useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import type { SessionUser } from "@malabak/bff/server";
import { logout } from "@/app/auth-actions";
import { api, unwrap } from "@/lib/api";
import { useVenue } from "./Providers";
import { P } from "@/lib/permissions";
import { toArabicDigits } from "./ui";
import { LogOut } from "lucide-react";

export interface NavItem {
  title: string;
  href: string;
  /** Venue permission needed to see the page; the API enforces it, this only hides links the role cannot use. */
  permission?: string;
  badge?: string;
}

const NAV_ITEMS: readonly NavItem[] = [
  { title: "نظرة عامة", href: "/overview" },
  { title: "تقويم الحجوزات", href: "/calendar" },
  { title: "الحجوزات", href: "/reservations" },
  { title: "الملاعب", href: "/pitches" },
  { title: "المواعيد", href: "/schedule" },
  { title: "الأسعار", href: "/pricing", permission: P.venueView },
  { title: "سياسات الحجز", href: "/policies" },
  { title: "العملاء", href: "/customers", permission: P.customersView },
  { title: "التقارير", href: "/reports", permission: P.reportsView },
  { title: "الاشتراك والفواتير", href: "/subscription" },
  { title: "الإشعارات", href: "/notifications" },
  { title: "الموظفين والصلاحيات", href: "/staff", permission: P.staffManage },
  { title: "الإعدادات", href: "/settings" },
] as const;

export interface SidebarProps {
  user: SessionUser;
  /** Rendered inside the mobile drawer: adds a close button and fills the drawer height. */
  onClose?: () => void;
}

export const Sidebar: React.FC<SidebarProps> = memo(({ user, onClose }) => {
  const router = useRouter();
  const pathname = usePathname() ?? "/";
  const [pendingHref, setPendingHref] = useState<string | null>(null);
  const { venue, venues, selectVenue, can } = useVenue();

  // Reset pending state on route change
  useEffect(() => {
    setPendingHref(null);
  }, [pathname]);

  // Eagerly prefetch all sidebar routes on mount to ensure snappy zero-lag navigation
  useEffect(() => {
    NAV_ITEMS.forEach((item) => {
      router.prefetch(item.href);
    });
  }, [router]);

  const unread = useQuery({
    queryKey: ["notifications", "unread"],
    queryFn: () => unwrap(api.GET("/api/v1/me/notifications/unread-count", { params: { query: { surface: VENUE_SURFACE } } })),
    refetchInterval: 60_000,
  });
  const unreadCount = unread.data?.unreadCount ?? 0;

  const checkIsActive = (href: string): boolean => {
    if (href === "/overview") {
      return pathname === "/" || pathname === "/overview";
    }
    if (href === "/calendar") {
      return pathname.startsWith("/calendar") || pathname.startsWith("/reservations/manual");
    }
    if (href === "/reservations") {
      return pathname.startsWith("/reservations") && !pathname.startsWith("/reservations/manual");
    }
    return pathname.startsWith(href);
  };

  return (
    <aside
      id={onClose ? "venue-nav-drawer" : undefined}
      className={`w-[248px] shrink-0 bg-navy-900 text-white px-3.5 py-4 flex flex-col gap-3 ${
        onClose ? "h-full overflow-y-auto" : "h-screen sticky top-0"
      }`}
    >
      <div className="flex items-center gap-2 px-2 shrink-0">
        <Image
          src="/venue/images/logo-horizontal-darkbg.png"
          alt="ملعبك"
          width={130}
          height={44}
          className="h-8 w-auto object-contain"
          priority
        />
        <span className="text-[11px] font-semibold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5 whitespace-nowrap">
          للملاعب
        </span>
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

      <div className="bg-white/[0.08] rounded-xl px-3 py-2 flex flex-col gap-0.5 shrink-0">
        <span className="text-[11px] text-[#AEB7CA]">الملعب الحالي</span>
        {venues.length > 1 ? (
          <select
            aria-label="الملعب الحالي"
            value={venue?.id ?? ""}
            onChange={(e) => selectVenue(e.target.value)}
            className="bg-transparent text-sm font-bold text-white focus:outline-none"
          >
            {venues.map((v) => (
              <option key={v.id} value={v.id} className="text-slate-900">
                {v.name} — {v.area}
              </option>
            ))}
          </select>
        ) : (
          <span className="text-sm font-bold">{venue ? `${venue.name} — ${venue.area}` : "—"}</span>
        )}
      </div>

      <nav aria-label="أقسام اللوحة" className="flex-1 overflow-y-auto flex flex-col gap-0.5 pe-1 py-0.5">
        {NAV_ITEMS.filter((item) => !item.permission || !venue || can(item.permission)).map((item) => {
          const isItemActive = checkIsActive(item.href);
          const isPending = pendingHref === item.href && !isItemActive;
          return (
            <Link
              key={item.href}
              href={item.href}
              prefetch={true}
              aria-current={isItemActive ? "page" : undefined}
              onClick={() => {
                if (!isItemActive) {
                  setPendingHref(item.href);
                  if (typeof window !== "undefined") {
                    window.dispatchEvent(new Event("malabak-nav-start"));
                  }
                }
                onClose?.();
              }}
              className={`min-h-[40px] px-3 rounded-[10px] flex items-center gap-2 text-sm no-underline cursor-pointer transition-all duration-150 ease-out active:scale-[0.98] ${
                isItemActive
                  ? "bg-white text-navy-900 font-bold shadow-xs -translate-x-0.5"
                  : isPending
                    ? "bg-white/20 text-white font-bold shadow-xs -translate-x-0.5 ring-1 ring-white/30"
                    : "text-[#D5DBE7] font-medium hover:bg-white/[0.08] hover:text-white hover:-translate-x-0.5"
              }`}
            >
              <span className="flex-1">{item.title}</span>
              {isPending && (
                <div className="w-3.5 h-3.5 rounded-full border-2 border-white/60 border-t-white animate-spin shrink-0" />
              )}
              {item.href === "/notifications" && unreadCount > 0 && !isPending && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center animate-scale-in">
                  {toArabicDigits(unreadCount)}
                </span>
              )}
              {item.badge && !isPending && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center animate-scale-in">
                  {item.badge}
                </span>
              )}
            </Link>
          );
        })}
      </nav>

      {/* User profile & Logout footer */}
      <div className="shrink-0 border-t border-white/[0.12] pt-3 flex flex-col gap-2.5">
        <div className="flex items-center gap-2.5 px-1">
          <div className="w-8 h-8 rounded-full bg-brand-gold text-navy-900 flex items-center justify-center font-bold text-xs shrink-0 shadow-sm">
            {user.name.trim().charAt(0) || "م"}
          </div>
          <div className="flex-1 min-w-0 flex flex-col">
            <span className="text-[13px] font-bold text-white truncate" title={user.name}>
              {user.name}
            </span>
            <span className="text-[11px] text-[#AEB7CA] truncate">
              {user.roles.includes("VenueStaff") ? "موظف مصرح" : "مالك الملعب"}
            </span>
          </div>
        </div>
        <form action={logout}>
          <button
            type="submit"
            className="w-full min-h-[36px] px-3 rounded-xl bg-white/[0.08] hover:bg-[#A8261C] active:scale-[0.98] text-xs font-semibold text-[#D5DBE7] hover:text-white transition-all duration-150 flex items-center justify-center gap-2 cursor-pointer"
          >
            <LogOut className="w-4 h-4" />
            <span>تسجيل الخروج</span>
          </button>
        </form>
      </div>
    </aside>
  );
});

Sidebar.displayName = "Sidebar";
