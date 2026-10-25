"use client";

import React, { memo } from "react";
import Link from "next/link";
import { Bell, PlusCircle } from "lucide-react";

export interface HeaderProps {
  title?: string;
  subtitle?: string;
  actions?: React.ReactNode;
}

export const Header: React.FC<HeaderProps> = memo(({ title, subtitle, actions }) => {
  return (
    <header className="bg-white border-b border-slate-200 px-8 py-4 flex items-center justify-between sticky top-0 z-30 shadow-xs">
      <div className="flex items-center gap-4">
        <div>
          {title && <h1 className="text-2xl font-bold text-slate-900 leading-tight">{title}</h1>}
          {subtitle && <p className="text-xs text-slate-500 mt-0.5">{subtitle}</p>}
        </div>
      </div>

      <div className="flex items-center gap-3">
        {actions}

        <Link
          href="/reservations/manual"
          className="min-h-[40px] px-4 rounded-xl bg-brand-red hover:bg-brand-red-hover text-white font-bold text-sm flex items-center gap-2 shadow-sm transition"
        >
          <PlusCircle className="w-4 h-4" />
          <span>حجز يدوي</span>
        </Link>

        <div className="h-6 w-px bg-slate-200 mx-1" />

        <Link
          href="/notifications"
          className="relative p-2 rounded-xl text-slate-600 hover:text-slate-900 hover:bg-slate-100 transition"
          aria-label="الإشعارات"
        >
          <Bell className="w-5 h-5" />
          <span className="absolute top-1.5 right-1.5 w-2.5 h-2.5 rounded-full bg-brand-red ring-2 ring-white" />
        </Link>

        <div className="flex items-center gap-2.5 pr-2 mr-2 border-r border-slate-200">
          <div className="w-9 h-9 rounded-full bg-navy-900 text-white font-bold text-xs flex items-center justify-center">
            أح
          </div>
          <div className="flex flex-col text-right">
            <span className="text-xs font-bold text-slate-800">أحمد محمود</span>
            <span className="text-[10px] text-slate-500">مسؤول الحجوزات</span>
          </div>
        </div>
      </div>
    </header>
  );
});

Header.displayName = "Header";
