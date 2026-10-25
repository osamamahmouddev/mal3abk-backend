"use client";

import React from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";

const NAV: { title: string; href: string; badge?: string }[] = [
  { title: "نظرة عامة", href: "/overview" },
  { title: "المستخدمين", href: "/users" },
  { title: "الملاعب", href: "/venues" },
  { title: "موافقات الملاعب", href: "/approvals", badge: "٤" },
  { title: "الحجوزات", href: "/bookings" },
  { title: "إيصالات ونزاعات", href: "/disputes", badge: "٣" },
  { title: "الفرق والمجتمع", href: "/teams" },
  { title: "باقات الاشتراك", href: "/plans" },
  { title: "الاشتراكات والفواتير", href: "/billing" },
  { title: "ملاعب مميزة", href: "/featured" },
  { title: "التقارير", href: "/reports" },
  { title: "تذاكر الدعم", href: "/support", badge: "٧" },
  { title: "إعدادات المنصة", href: "/settings" },
  { title: "سجل التدقيق", href: "/audit" },
];

export function AdminShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname() ?? "/";
  if (pathname.startsWith("/login")) return <>{children}</>;

  const isActive = (href: string) => (href === "/overview" ? pathname === "/" || pathname === "/overview" : pathname.startsWith(href));

  return (
    <div className="flex min-h-screen w-full" dir="rtl">
      <aside className="w-[248px] shrink-0 min-h-screen bg-ink text-white px-4 py-6 flex flex-col gap-5">
        <div className="flex items-center gap-2.5 px-2">
          <span className="text-[26px] font-bold tracking-[-0.5px]">ملعبك</span>
          <span className="text-[11px] font-bold text-white bg-brand-red rounded-md px-2 py-0.5">الإدارة</span>
        </div>
        <nav aria-label="أقسام الإدارة" className="flex flex-col gap-0.5">
          {NAV.map((n) => {
            const active = isActive(n.href);
            return (
              <Link
                key={n.href}
                href={n.href}
                aria-current={active ? "page" : undefined}
                className={`min-h-[38px] px-3 rounded-[9px] flex items-center gap-2 text-sm ${
                  active ? "bg-white text-ink font-bold hover:text-ink" : "text-[#D5DBE7] font-medium hover:bg-white/[0.08] hover:text-white"
                }`}
              >
                <span className="flex-1">{n.title}</span>
                {n.badge && (
                  <span className="min-w-[20px] h-5 px-1.5 rounded-[10px] bg-brand-red text-white text-[11px] font-bold flex items-center justify-center">
                    {n.badge}
                  </span>
                )}
              </Link>
            );
          })}
        </nav>
        <div className="mt-auto pt-3 px-2 border-t border-white/[0.12] text-xs text-[#AEB7CA] flex justify-between items-center">
          <span>سارة منصور · مشرفة عمليات</span>
          <Link href="/login" className="text-[#AEB7CA] hover:text-white underline">
            خروج
          </Link>
        </div>
      </aside>
      <main className="flex-1 min-w-0">{children}</main>
    </div>
  );
}
