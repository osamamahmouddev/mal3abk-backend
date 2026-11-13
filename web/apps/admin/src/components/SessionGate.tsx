"use client";

import React from "react";
import { useQuery } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";
import { adminLogout } from "@/app/auth-actions";

/**
 * Holds the signed-in console back until the API has accepted the session once.
 *
 * The browser cookie only proves a session was started; the tokens can have been revoked or lost
 * since (API restart, database reset, signed out elsewhere). Without this check the console shows
 * up empty and signs itself out a few seconds later. A rejected session is sent to sign-in right
 * away by the API client (401); an unreachable API shows a retry screen instead of empty data.
 */
export function SessionGate({ children }: { children: React.ReactNode }) {
  // Same key and function as the navigation query in the side menu, so the request is made once.
  const session = useQuery({
    queryKey: ["admin-navigation"],
    staleTime: 5 * 60_000,
    queryFn: () => unwrap(api.GET("/api/v1/admin/navigation")),
  });

  if (session.isSuccess) return <>{children}</>;

  if (session.isError) {
    return (
      <main className="min-h-screen w-full flex items-center justify-center p-6" dir="rtl">
        <div role="alert" className="max-w-sm w-full rounded-2xl bg-white border border-[#EDF0F5] p-6 text-center flex flex-col gap-4">
          <h1 className="text-lg font-bold">مقدرناش نوصل للخدمة</h1>
          <p className="text-sm text-[#5B6475]">اتأكد من الاتصال وجرّب تاني.</p>
          <div className="flex gap-2 justify-center">
            <button type="button" onClick={() => session.refetch()} className="min-h-[44px] px-[18px] rounded-xl bg-brand-red text-white text-sm font-bold">
              إعادة المحاولة
            </button>
            <form action={adminLogout}>
              <button type="submit" className="min-h-[44px] px-[18px] rounded-xl bg-white border border-[#D9DEE8] text-sm font-bold">
                خروج
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
}
