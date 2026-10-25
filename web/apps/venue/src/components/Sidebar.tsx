"use client";

import React, { memo } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";

export interface NavItem {
  title: string;
  href: string;
  badge?: string;
}

const NAV_ITEMS: readonly NavItem[] = [
  { title: "نظرة عامة", href: "/overview" },
  { title: "تقويم الحجوزات", href: "/calendar" },
  { title: "الحجوزات", href: "/reservations", badge: "٥" },
  { title: "الملاعب", href: "/pitches" },
  { title: "المواعيد", href: "/schedule" },
  { title: "الأسعار", href: "/pricing" },
  { title: "سياسات الحجز", href: "/policies" },
  { title: "العملاء", href: "/customers" },
  { title: "التقارير", href: "/reports" },
  { title: "الاشتراك والفواتير", href: "/subscription" },
  { title: "الإشعارات", href: "/notifications", badge: "٣" },
  { title: "الموظفين والصلاحيات", href: "/staff" },
  { title: "الإعدادات", href: "/settings" },
] as const;

export const Sidebar: React.FC = memo(() => {
  const pathname = usePathname() ?? "/";

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
    <aside className="w-[248px] shrink-0 min-h-screen bg-navy-900 text-white px-4 py-6 flex flex-col gap-6">
      <div className="flex items-center gap-2.5 px-2">
        <span className="text-[26px] font-bold tracking-[-0.5px]">ملعبك</span>
        <span className="text-[11px] font-semibold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5">
          للملاعب
        </span>
      </div>

      <div className="bg-white/[0.08] rounded-xl px-3 py-2.5 flex flex-col gap-0.5">
        <span className="text-[11px] text-[#AEB7CA]">الملعب الحالي</span>
        <span className="text-sm font-bold">ملعب النجوم — مدينة نصر</span>
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
              {item.badge && (
                <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center">
                  {item.badge}
                </span>
              )}
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto border-t border-white/[0.12] pt-4 px-2 flex flex-col gap-1">
        <span className="text-xs text-[#AEB7CA]">باقة Growth · تتجدد ١٤ أغسطس</span>
        <span className="text-[13px] font-semibold">٣ من ٥ ملاعب مستخدمة</span>
      </div>
    </aside>
  );
});

Sidebar.displayName = "Sidebar";
