"use client";

import React, { Suspense, useState, useTransition } from "react";
import Image from "next/image";
import { useSearchParams } from "next/navigation";
import { QRCodeSVG } from "qrcode.react";
import { inputClass, muted, redShadow } from "@/components/ui";
import { adminLogin, confirmTotpEnrollment, startTotpEnrollment } from "@/app/auth-actions";

export default function AdminLoginPage() {
  return (
    <Suspense>
      <AdminLogin />
    </Suspense>
  );
}

type Step = "creds" | "otp" | "enroll" | "codes";

function AdminLogin() {
  const params = useSearchParams();
  const [step, setStep] = useState<Step>("creds");
  const [email, setEmail] = useState("");
  const [pass, setPass] = useState("");
  const [credErr, setCredErr] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const [codeErr, setCodeErr] = useState<string | null>(null);
  const [enrollment, setEnrollment] = useState<{ secret: string; provisioningUri: string } | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [pending, startTransition] = useTransition();

  const next = () => {
    const target = params.get("next");
    const fallback = "/admin/overview";
    const dest = target && target.startsWith("/") && !target.startsWith("//") ? target : fallback;
    window.location.assign(dest.startsWith("/admin") ? dest : `/admin${dest}`);
  };

  const submitCreds = (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !pass) {
      setCredErr("اكتب الإيميل وكلمة السر.");
      return;
    }
    // Password is checked together with the code on the next step; a wrong one shows there.
    setCredErr(null);
    startTransition(async () => {
      const result = await adminLogin({ email: email.trim(), password: pass });
      if (result.ok) return next();
      if (result.code === "EnrollmentRequired") {
        const started = await startTotpEnrollment();
        if (!started.ok) return setCredErr(started.message ?? null);
        setEnrollment({ secret: started.secret!, provisioningUri: started.provisioningUri! });
        setStep("enroll");
        return;
      }
      if (result.code === "Identity.TwoFactorRequired") {
        setCode("");
        setCodeErr(null);
        setStep("otp");
        return;
      }
      setCredErr(result.message ?? null);
    });
  };

  const submitCode = (e: React.FormEvent) => {
    e.preventDefault();
    const value = code.trim();
    if (!useRecovery && value.replace(/\D/g, "").length !== 6) {
      setCodeErr("الكود لازم يكون ٦ أرقام.");
      return;
    }
    startTransition(async () => {
      const result = await adminLogin(
        useRecovery ? { email: email.trim(), password: pass, recoveryCode: value } : { email: email.trim(), password: pass, totpCode: value },
      );
      if (result.ok) return next();
      setCodeErr(result.message ?? null);
    });
  };

  const submitEnrollment = (e: React.FormEvent) => {
    e.preventDefault();
    startTransition(async () => {
      const result = await confirmTotpEnrollment(code.trim());
      if (!result.ok) return setCodeErr(result.message ?? null);
      setRecoveryCodes(result.recoveryCodes ?? []);
      setCode("");
      setCodeErr(null);
      setStep("codes");
    });
  };

  const submitBtn = `min-h-[50px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`;

  return (
    <div
      dir="rtl"
      className="min-h-screen flex items-center justify-center px-4 py-8 bg-ink bg-[radial-gradient(circle_at_15%_20%,rgba(214,59,47,0.18),transparent_40%),radial-gradient(circle_at_85%_80%,rgba(245,182,43,0.12),transparent_40%)]"
    >
      <main className="w-full max-w-[440px] bg-white rounded-3xl p-8 flex flex-col gap-4 shadow-[0_30px_60px_-30px_rgba(0,0,0,0.6)]">
        <div className="flex items-center gap-2.5">
          <Image
            src="/images/logo-horizontal.png"
            alt="ملعبك"
            width={130}
            height={46}
            className="h-8 w-auto object-contain"
            priority
          />
          <span className="text-[11px] font-bold text-white bg-brand-red rounded-md px-2 py-0.5 whitespace-nowrap">الإدارة</span>
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
                placeholder="name@malabak.app"
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
            <button type="submit" disabled={pending} className={submitBtn}>
              {pending ? "جاري التحقق…" : "متابعة"}
            </button>
          </form>
        ) : step === "enroll" && enrollment ? (
          <form onSubmit={submitEnrollment} className="flex flex-col gap-3.5" noValidate>
            <div>
              <h1 className="text-[22px] font-bold">فعّل التحقق بخطوتين</h1>
              <p className={`mt-1 text-sm ${muted}`}>مطلوب لكل حسابات الإدارة. امسح الكود بتطبيق المصادقة (Google Authenticator أو غيره).</p>
            </div>
            <div className="flex justify-center">
              <QRCodeSVG value={enrollment.provisioningUri} size={168} className="border border-[#EDF0F5] rounded-xl p-2" />
            </div>
            <code dir="ltr" className="text-[13px] bg-[#F3F5F9] rounded-lg px-2 py-1.5 text-center break-all">
              {enrollment.secret}
            </code>
            <input
              aria-label="كود التحقق"
              dir="ltr"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              value={code}
              onChange={(e) => {
                setCode(e.target.value.replace(/\D/g, ""));
                setCodeErr(null);
              }}
              placeholder="••••••"
              className="min-h-[56px] border border-[#C9CFD9] rounded-xl px-3 text-2xl tracking-[10px] text-center focus:outline-none focus:ring-2 focus:ring-ink"
            />
            {codeErr && (
              <span role="alert" className="text-[13px] text-[#A8261C]">
                {codeErr}
              </span>
            )}
            <button type="submit" disabled={pending || code.length !== 6} className={submitBtn}>
              تفعيل
            </button>
          </form>
        ) : step === "codes" ? (
          <div className="flex flex-col gap-3.5">
            <div>
              <h1 className="text-[22px] font-bold">أكواد الاسترجاع</h1>
              <p className={`mt-1 text-sm ${muted}`}>
                احفظهم في مكان آمن. كل كود بيشتغل مرة واحدة لو موبايلك ضاع. مش هيظهروا تاني.
              </p>
            </div>
            <ul dir="ltr" className="grid grid-cols-2 gap-2 bg-[#F3F5F9] rounded-xl p-3 font-mono text-sm">
              {recoveryCodes.map((c) => (
                <li key={c} className="text-center">
                  {c}
                </li>
              ))}
            </ul>
            <button
              type="button"
              onClick={() => {
                setRecoveryCodes([]);
                setStep("otp");
              }}
              className={submitBtn}
            >
              حفظتهم — كمّل الدخول
            </button>
          </div>
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
              maxLength={useRecovery ? 20 : 6}
              value={code}
              onChange={(e) => {
                setCode(e.target.value);
                setCodeErr(null);
              }}
              placeholder={useRecovery ? "XXXX-XXXX" : "••••••"}
              aria-invalid={!!codeErr}
              className="min-h-[56px] border border-[#C9CFD9] rounded-xl px-3 text-2xl tracking-[10px] text-center focus:outline-none focus:ring-2 focus:ring-ink"
            />
            {codeErr && (
              <span role="alert" className="text-[13px] text-[#A8261C]">
                {codeErr}
              </span>
            )}
            <button type="submit" disabled={pending} className={submitBtn}>
              {pending ? "جاري التحقق…" : "تأكيد"}
            </button>
            <button
              type="button"
              onClick={() => {
                setUseRecovery(!useRecovery);
                setCode("");
                setCodeErr(null);
              }}
              className="min-h-[36px] bg-transparent text-ink text-[13px] underline"
            >
              {useRecovery ? "استخدم كود التطبيق" : "موبايلك مش معاك؟ استخدم كود استرجاع"}
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
