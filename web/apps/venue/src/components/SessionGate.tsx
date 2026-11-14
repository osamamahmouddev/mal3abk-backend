"use client";

import React from "react";
import { useQuery } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";
import { logout } from "@/app/auth-actions";
import { RefreshCw, Wrench } from "lucide-react";

/**
 * Holds the signed-in screens back until the API has accepted the session once.
 *
 * The browser cookie only proves a session was started; the access and refresh tokens can have been
 * revoked or lost since (API restart, database reset, signed out elsewhere). Without this check the
 * dashboard shows up empty and signs itself out a few seconds later. A rejected session is sent to
 * sign-in right away by the API client (401); an unreachable API shows a retry screen instead of
 * empty data.
 */
export const SessionGate: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  // Same key and function as the permissions query in Providers, so the request is made once.
  const session = useQuery({
    queryKey: ["my-permissions"],
    staleTime: 5 * 60_000,
    queryFn: () => unwrap(api.GET("/api/v1/me/permissions")),
  });

  if (session.isSuccess) return <>{children}</>;

  if (session.isError) {
    const errorMsg = session.error instanceof Error ? session.error.message : String(session.error ?? "");
    const isMaintenance =
      errorMsg.includes("صيانة") ||
      errorMsg.includes("Maintenance") ||
      errorMsg.includes("Platform.Maintenance");

    if (isMaintenance) {
      return (
        <main className="min-h-screen w-full flex items-center justify-center p-6 bg-[#F8FAFC]" dir="rtl">
          <div role="alert" className="max-w-md w-full rounded-3xl bg-white border border-[#E2E8F0] p-8 text-center flex flex-col items-center gap-4 shadow-xl">
            <div className="w-16 h-16 rounded-2xl bg-amber-50 text-amber-600 flex items-center justify-center shadow-xs">
              <Wrench className="size-8" strokeWidth={1.75} />
            </div>
            <h1 className="text-xl font-black text-navy-900">المنصة في وضع الصيانة</h1>
            <p className="text-sm text-gray-600 leading-relaxed">
              {errorMsg.includes("صيانة") ? errorMsg : "نعمل حالياً على تحسين وتحديث الخدمة، وسنعود للعمل قريباً جداً. شكراً لتفهمكم وصبركم."}
            </p>
            <div className="flex gap-2 justify-center w-full mt-2">
              <button
                type="button"
                onClick={() => session.refetch()}
                className="min-h-[44px] px-6 rounded-xl bg-navy-900 hover:bg-slate-800 text-white text-sm font-bold cursor-pointer transition-all duration-150 inline-flex items-center gap-2"
              >
                <RefreshCw className="size-4" />
                إعادة المحاولة الآن
              </button>
            </div>
          </div>
        </main>
      );
    }

    return (
      <main className="min-h-screen w-full flex items-center justify-center p-6" dir="rtl">
        <div role="alert" className="max-w-sm w-full rounded-2xl bg-white border border-[#EDF0F5] p-6 text-center flex flex-col gap-4">
          <h1 className="text-lg font-bold">مقدرناش نوصل للخدمة</h1>
          <p className="text-sm text-[#5B6475]">اتأكد من الاتصال بالإنترنت وجرّب تاني. بياناتك محفوظة.</p>
          <div className="flex gap-2 justify-center">
            <button
              type="button"
              onClick={() => session.refetch()}
              className="min-h-[44px] px-[18px] rounded-xl bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold"
            >
              إعادة المحاولة
            </button>
            <form action={logout}>
              <button type="submit" className="min-h-[44px] px-[18px] rounded-xl bg-white border border-[#D9DEE8] text-sm font-bold">
                تسجيل الخروج
              </button>
            </form>
          </div>
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen w-full flex items-center justify-center" dir="rtl" aria-busy="true">
      <div role="status" className="flex flex-col items-center gap-3 text-sm text-[#5B6475]">
        <span className="h-8 w-8 rounded-full border-4 border-[#E4E7EC] border-t-brand-red animate-spin" aria-hidden="true" />
        جاري التحقق من الجلسة…
      </div>
    </main>
  );
};
