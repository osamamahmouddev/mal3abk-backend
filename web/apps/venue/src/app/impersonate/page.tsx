"use client";

import React, { useState } from "react";
import { card, inputClass, muted, primaryBtn } from "@/components/ui";
import { startImpersonation } from "@/app/auth-actions";
import { withBase } from "@/lib/base-path";

/** Entry point for support agents: paste the 60-second code from the admin console (TASK-063). */
export default function ImpersonatePage() {
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await startImpersonation(code);
      if (result.ok) {
        window.location.assign(withBase("/overview"));
        return;
      }
      setError(result.message ?? "حصل خطأ.");
    } catch {
      setError("تعذّر الاتصال بالخادم. حاول تاني بعد شوية.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="min-h-screen flex items-center justify-center p-4 bg-[#F4F5F7]" dir="rtl">
      <form onSubmit={submit} className={`${card} rounded-[20px] p-6 w-full max-w-[440px] flex flex-col gap-3`}>
        <h1 className="text-[20px] font-bold">دخول فريق الدعم</h1>
        <p className={`text-sm ${muted} leading-[1.7]`}>
          الصق الكود اللي طلع من لوحة الإدارة. صالح ٦٠ ثانية ولمرة واحدة. الجلسة ١٥ دقيقة، والعميل بيوصله إشعار، ومفيش تغيير كلمة سر أو رقم أو بيانات دفع.
        </p>
        <label htmlFor="imp-code" className="text-[13px] font-semibold">الكود</label>
        <input id="imp-code" dir="ltr" autoFocus autoComplete="off" value={code} onChange={(e) => setCode(e.target.value)} className={`${inputClass} font-inter`} />
        {error && <span role="alert" className="text-xs text-[#A8261C]">{error}</span>}
        <button type="submit" className={primaryBtn} disabled={busy || code.trim().length < 20}>
          {busy ? "جاري الدخول…" : "دخول"}
        </button>
      </form>
    </main>
  );
}
