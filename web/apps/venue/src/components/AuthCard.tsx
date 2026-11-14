import React from "react";
import Image from "next/image";
import Link from "next/link";
import { CircleCheck } from "lucide-react";

export const authField =
  "w-full min-h-[48px] rounded-xl px-3.5 text-[15px] text-slate-900 border border-[#C9CFD9] transition-all duration-150 ease-out focus:outline-none focus:ring-2 focus:ring-navy-900 focus:shadow-[0_0_0_3px_rgba(15,27,61,0.1)]";
export const authPrimary =
  "min-h-[52px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover active:scale-[0.98] transition-all duration-150 ease-out disabled:opacity-60 text-white text-base font-bold shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)] hover:shadow-lg cursor-pointer";

export function AuthCard({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  return (
    <div dir="rtl" className="min-h-screen bg-surface px-4 py-10 flex flex-col items-center justify-center gap-6 animate-fade-in">
      <Link href="/login" className="flex items-center gap-2.5 text-navy-900 no-underline transition-transform hover:scale-105">
        <Image
          src="/venue/images/logo-horizontal.png"
          alt="ملعبك"
          width={130}
          height={46}
          className="h-8 w-auto object-contain"
          priority
        />
        <span className="text-xs font-bold text-navy-900 bg-brand-gold rounded-md px-2.5 py-[3px] whitespace-nowrap">للملاعب</span>
      </Link>
      <div className="w-full max-w-[440px] bg-white border border-[#EDF0F5] rounded-3xl shadow-[0_1px_2px_rgba(15,27,61,0.04),0_20px_40px_-24px_rgba(15,27,61,0.25)] p-6 sm:p-8 flex flex-col gap-4 animate-fade-in-up">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-bold">{title}</h1>
          {subtitle && <span className="text-sm text-[#5B6475] leading-[22px]">{subtitle}</span>}
        </div>
        {children}
      </div>
    </div>
  );
}

export function FormError({ message }: { message: string | null }) {
  return message ? (
    <span role="alert" className="text-[13px] text-[#A8261C]">
      {message}
    </span>
  ) : null;
}

export function FormSuccess({ children }: { children: React.ReactNode }) {
  return (
    <div role="status" className="bg-[#E5F5EB] text-[#155C33] rounded-xl px-3.5 py-2.5 text-[13px] font-semibold leading-[20px] flex items-start gap-2">
      <CircleCheck className="size-4 shrink-0 mt-0.5" />
      <span>{children}</span>
    </div>
  );
}
