"use client";

import React, { useState } from "react";
import { useRouter } from "next/navigation";
import { inputClass, muted, redShadow } from "@/components/ui";

export default function AdminLoginPage() {
  const router = useRouter();
  const [step, setStep] = useState<"creds" | "otp">("creds");
  const [email, setEmail] = useState("");
  const [pass, setPass] = useState("");
  const [credErr, setCredErr] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [codeErr, setCodeErr] = useState(false);

  const submitCreds = (e: React.FormEvent) => {
    e.preventDefault();
    if (!/@mal3abak\.com$/i.test(email.trim())) {
      setCredErr("الدخول بإيميل الشغل بس (@mal3abak.com).");
      return;
    }
    // TODO: verify credentials with the API; on failure show
    // "كلمة السر غلط. بعد ٥ محاولات الحساب بيتقفل ١٥ دقيقة."
    if (!pass) {
      setCredErr("اكتب كلمة السر.");
      return;
    }
    setCode("");
    setCodeErr(false);
    setStep("otp");
  };

  const submitCode = (e: React.FormEvent) => {
    e.preventDefault();
    // TODO: verify the TOTP code with the API.
    if (code.replace(/\D/g, "").length !== 6) {
      setCodeErr(true);
      return;
    }
    router.push("/overview");
  };

  const submitBtn = `min-h-[50px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold ${redShadow}`;

  return (
    <div
      dir="rtl"
      className="min-h-screen flex items-center justify-center px-4 py-8 bg-ink bg-[radial-gradient(circle_at_15%_20%,rgba(214,59,47,0.18),transparent_40%),radial-gradient(circle_at_85%_80%,rgba(245,182,43,0.12),transparent_40%)]"
    >
      <main className="w-full max-w-[440px] bg-white rounded-3xl p-8 flex flex-col gap-4 shadow-[0_30px_60px_-30px_rgba(0,0,0,0.6)]">
        <div className="flex items-center gap-2.5">
          <span className="text-[28px] font-bold text-ink">ملعبك</span>
          <span className="text-[11px] font-bold text-white bg-brand-red rounded-md px-2 py-0.5">الإدارة</span>
        </div>

        {step === "creds" ? (
          <form onSubmit={submitCreds} className="flex flex-col gap-3.5" noValidate>
            <div>
              <h1 className="text-[22px] font-bold">دخول فريق الإدارة</h1>
              <p className={`mt-1 text-sm ${muted}`}>للموظفين المصرّح لهم بس. كل دخول بيتسجل.</p>
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="ae" className="text-[13px] font-semibold">
                إيميل الشغل
              </label>
              <input
                id="ae"
                dir="ltr"
                type="email"
                autoComplete="username"
                value={email}
                onChange={(e) => {
                  setEmail(e.target.value);
                  setCredErr(null);
                }}
                placeholder="name@mal3abak.com"
                className={`${inputClass} text-right`}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="ap" className="text-[13px] font-semibold">
                كلمة السر
              </label>
              <input
                id="ap"
                type="password"
                autoComplete="current-password"
                value={pass}
                onChange={(e) => {
                  setPass(e.target.value);
                  setCredErr(null);
                }}
                className={inputClass}
              />
            </div>
            {credErr && (
              <div role="alert" className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-3 py-2.5 text-[13px]">
                {credErr}
              </div>
            )}
            <button type="submit" className={submitBtn}>
              متابعة
            </button>
          </form>
        ) : (
          <form onSubmit={submitCode} className="flex flex-col gap-3.5" noValidate>
            <div>
              <h1 className="text-[22px] font-bold">التحقق بخطوتين</h1>
              <p className={`mt-1 text-sm ${muted}`}>اكتب الكود اللي في تطبيق المصادقة على موبايلك.</p>
            </div>
            <input
              aria-label="كود التحقق"
              dir="ltr"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              value={code}
              onChange={(e) => {
                setCode(e.target.value);
                setCodeErr(false);
              }}
              placeholder="••••••"
              aria-invalid={codeErr}
              className="min-h-[56px] border border-[#C9CFD9] rounded-xl px-3 text-2xl tracking-[10px] text-center focus:outline-none focus:ring-2 focus:ring-ink"
            />
            {codeErr && (
              <span role="alert" className="text-[13px] text-[#A8261C]">
                الكود لازم يكون ٦ أرقام.
              </span>
            )}
            <button type="submit" className={submitBtn}>
              تأكيد
            </button>
            <button type="button" onClick={() => setStep("creds")} className="min-h-[40px] bg-transparent text-ink text-sm font-semibold">
              رجوع
            </button>
          </form>
        )}
      </main>
    </div>
  );
}
