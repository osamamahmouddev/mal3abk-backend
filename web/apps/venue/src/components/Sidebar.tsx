"use client";

import React, { memo } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import type { SessionUser } from "@malabak/bff/server";
import { logout } from "@/app/auth-actions";
import { api, unwrap } from "@/lib/api";
import { useVenue } from "./Providers";
import { toArabicDigits } from "./ui";

export interface NavItem {
  title: string;
  href: string;
  badge?: string;
}

const NAV_ITEMS: readonly NavItem[] = [
  { title: "نظرة عامة", href: "/overview" },
  { title: "تقويم الحجوزات", href: "/calendar" },
  { title: "الحجوزات", href: "/reservations" },
  { title: "الملاعب", href: "/pitches" },
  { title: "المواعيد", href: "/schedule" },
  { title: "الأسعار", href: "/pricing" },
  { title: "سياسات الحجز", href: "/policies" },
  { title: "العملاء", href: "/customers" },
  { title: "التقارير", href: "/reports" },
  { title: "الاشتراك والفواتير", href: "/subscription" },
  { title: "الإشعارات", href: "/notifications" },
  { title: "الموظفين والصلاحيات", href: "/staff" },
  { title: "الإعدادات", href: "/settings" },
] as const;

export interface SidebarProps {
  user: SessionUser;
  /** Rendered inside the mobile drawer: adds a close button and fills the drawer height. */
  onClose?: () => void;
}

export const Sidebar: React.FC<SidebarProps> = memo(({ user, onClose }) => {
  const pathname = usePathname() ?? "/";
  const { venue, venues, selectVenue } = useVenue();
  const unread = useQuery({
    queryKey: ["notifications", "unread"],
    queryFn: () => unwrap(api.GET("/api/v1/me/notifications/unread-count")),
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
      className={`w-[248px] shrink-0 bg-navy-900 text-white px-4 py-6 flex flex-col gap-6 ${
        onClose ? "h-full overflow-y-auto" : "min-h-screen"
      }`}
    >
      <div className="flex items-center gap-2.5 px-2">
        <span className="text-[26px] font-bold tracking-[-0.5px]">ملعبك</span>
        <span className="text-[11px] font-semibold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5">
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

      <div className="bg-white/[0.08] rounded-xl px-3 py-2.5 flex flex-col gap-0.5">
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

      <nav aria-label="أقسام اللوحة" className="flex flex-col gap-0.5">
        {NAV_ITEMS.map((item) => {
          const isItemActive = checkIsActive(item.href);
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-current={isItemActive ? "page" : undefined}
              className={`min-h-[40px] px-3 rounded-[10px] flex items-center gap-2 text-sm no-underline transition ${
                isItemActive
                  ? "bg-white text-navy-900 font-bold"
                  : "text-[#D5DBE7] font-medium hover:bg-white/[0.08]"
              }`}
            >
              <span className="flex-1">{item.title}</span>
              {item.href === "/notifications" && unreadCount > 0 && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center">
                  {toArabicDigits(unreadCount)}
                </span>
              )}
              {item.badge && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center">
                  {item.badge}
                </span>
              )}
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto border-t border-white/[0.12] pt-4 px-2 flex flex-col gap-2">
        <span className="text-[13px] font-semibold">{user.name}</span>
        <form action={logout}>
          <button type="submit" className="min-h-[36px] px-3 rounded-[10px] bg-white/[0.08] hover:bg-white/[0.14] text-[13px] font-semibold">
            تسجيل الخروج
          </button>
        </form>
      </div>
    </aside>
  );
});

Sidebar.displayName = "Sidebar";
