"use client";

import React, { Suspense, useState, useTransition } from "react";
import Image from "next/image";
import { useSearchParams } from "next/navigation";
import { QRCodeSVG } from "qrcode.react";
import { Check, Copy, Download } from "lucide-react";
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
  const [showPassword, setShowPassword] = useState(false);
  const [credErr, setCredErr] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const [codeErr, setCodeErr] = useState<string | null>(null);
  const [enrollment, setEnrollment] = useState<{ secret: string; provisioningUri: string } | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [enrolledCode, setEnrolledCode] = useState("");
  const [copied, setCopied] = useState(false);
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

  const handleCodeChange = (raw: string) => {
    setCodeErr(null);
    if (useRecovery) {
      setCode(raw);
      return;
    }
    const digits = raw.replace(/\D/g, "").slice(0, 6);
    if (digits.length > 3) {
      setCode(`${digits.slice(0, 3)} ${digits.slice(3)}`);
    } else {
      setCode(digits);
    }
  };

  const submitCode = (e: React.FormEvent) => {
    e.preventDefault();
    const cleanDigits = code.replace(/\D/g, "");
    if (!useRecovery && cleanDigits.length !== 6) {
      setCodeErr("الكود لازم يكون ٦ أرقام.");
      return;
    }
    const value = useRecovery ? code.trim() : cleanDigits;
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
    const cleanDigits = code.replace(/\D/g, "");
    startTransition(async () => {
      const result = await confirmTotpEnrollment(cleanDigits);
      if (!result.ok) return setCodeErr(result.message ?? null);
      setRecoveryCodes(result.recoveryCodes ?? []);
      setEnrolledCode(cleanDigits);
      setCode("");
      setCodeErr(null);
      setStep("codes");
    });
  };

  const handleCopyCodes = async () => {
    try {
      const text = recoveryCodes.map((c) => c.toLowerCase()).join("\n");
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Fallback
    }
  };

  const handleDownloadCodes = () => {
    const lines = [
      "====================================================",
      "ملعبك (Malabak) - رموز استرجاع حساب الإدارة",
      `تاريخ الإنشاء: ${new Date().toLocaleDateString("ar-EG", { dateStyle: "full" })}`,
      `البريد الإلكتروني: ${email.trim()}`,
      "====================================================",
      "",
      "تنبيه أمني مهم:",
      "- احتفظ بهذه الرموز في مكان آمن ومشفر.",
      "- كل رمز يُستخدم لمرة واحدة فقط كبديل لرمز تطبيق المصادقة (TOTP).",
      "- لا تشارك هذه الرموز مع أي شخص تحت أي ظرف.",
      "",
      "قائمة رموز الاسترجاع:",
      ...recoveryCodes.map((c, i) => `${String(i + 1).padStart(2, "0")}. ${c.toLowerCase()}`),
      "",
      "====================================================",
    ];
    const blob = new Blob([lines.join("\n")], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `malabak-recovery-codes-${Date.now()}.txt`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const handleContinueFromCodes = () => {
    startTransition(async () => {
      if (enrolledCode && pass && email.trim()) {
        const result = await adminLogin({
          email: email.trim(),
          password: pass,
          totpCode: enrolledCode,
        });
        if (result.ok) return next();
      }
      setRecoveryCodes([]);
      setCode("");
      setCodeErr(null);
      setStep("otp");
    });
  };

  const submitBtn = `min-h-[50px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`;

  return (
    <div
      dir="rtl"
      className="min-h-screen flex items-center justify-center px-4 py-8 bg-ink bg-[radial-gradient(circle_at_15%_20%,rgba(214,59,47,0.18),transparent_40%),radial-gradient(circle_at_85%_80%,rgba(245,182,43,0.12),transparent_40%)]"
    >
      <main className="w-full max-w-[440px] bg-white rounded-3xl p-8 flex flex-col gap-4 shadow-[0_30px_60px_-30px_rgba(0,0,0,0.6)] animate-fade-in-up">
        <div className="flex items-center gap-2.5">
          <Image
            src="/admin/images/logo-horizontal.png"
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
              <div className="relative w-full">
                <input
                  id="ap"
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  value={pass}
                  onChange={(e) => {
                    setPass(e.target.value);
                    setCredErr(null);
                  }}
                  className={`${inputClass} w-full pl-11`}
                />
                <button
                  type="button"
                  aria-label={showPassword ? "إخفاء كلمة السر" : "إظهار كلمة السر"}
                  onClick={() => setShowPassword((s) => !s)}
                  className="absolute left-3 top-1/2 -translate-y-1/2 text-[#7B8494] hover:text-ink transition-colors p-1 flex items-center justify-center cursor-pointer"
                >
                  {showPassword ? (
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                      <path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49" />
                      <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242" />
                      <path d="M17.479 17.499A10.75 10.75 0 0 1 2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.18-5.148" />
                      <line x1="2" x2="22" y1="2" y2="22" />
                    </svg>
                  ) : (
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                      <path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  )}
                </button>
              </div>
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
              maxLength={7}
              value={code}
              onChange={(e) => handleCodeChange(e.target.value)}
              placeholder="000 000"
              className="min-h-[56px] border border-[#C9CFD9] rounded-xl px-3 text-2xl font-mono tracking-[6px] text-center focus:outline-none focus:ring-2 focus:ring-ink"
            />
            {codeErr && (
              <span role="alert" className="text-[13px] text-[#A8261C]">
                {codeErr}
              </span>
            )}
            <button type="submit" disabled={pending || code.replace(/\D/g, "").length !== 6} className={submitBtn}>
              تفعيل
            </button>
          </form>
        ) : step === "codes" ? (
          <div className="flex flex-col gap-4">
            <div>
              <h1 className="text-[22px] font-bold text-ink">رموز استرجاع الحساب</h1>
              <p className={`mt-1 text-sm ${muted}`}>
                احفظ هذه الرموز في مكان آمن. كل كود بيشتغل مرة واحدة لو موبايلك ضاع. مش هيظهروا تاني.
              </p>
            </div>

            <div className="bg-[#EFF5F1] border border-[#D5E3DB] rounded-[20px] p-4 sm:p-5 shadow-sm">
              <div dir="ltr" className="flex flex-col">
                {Array.from({ length: Math.ceil(recoveryCodes.length / 2) }).map((_, rowIndex) => {
                  const left = recoveryCodes[rowIndex * 2];
                  const right = recoveryCodes[rowIndex * 2 + 1];
                  const isLastRow = rowIndex === Math.ceil(recoveryCodes.length / 2) - 1;

                  return (
                    <div
                      key={rowIndex}
                      className={`grid grid-cols-2 py-2.5 text-center font-mono text-[14px] font-medium text-[#1E293B] tracking-wider ${
                        !isLastRow ? "border-b border-dashed border-[#CBDDD2]" : ""
                      }`}
                    >
                      <span className="select-all hover:text-[#0B6A3E] transition-colors">{left?.toLowerCase()}</span>
                      <span className="select-all hover:text-[#0B6A3E] transition-colors">{right ? right.toLowerCase() : ""}</span>
                    </div>
                  );
                })}
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <button
                type="button"
                onClick={handleCopyCodes}
                dir="ltr"
                className="flex items-center justify-center gap-2 py-2.5 px-3 rounded-full border border-[#D5E3DB] bg-white hover:bg-[#F2F7F4] text-[#1E293B] text-[13px] font-bold transition-all shadow-sm active:scale-[0.98] cursor-pointer"
              >
                {copied ? (
                  <>
                    <span className="text-[#0B6A3E]">تم النسخ!</span>
                    <Check className="w-4 h-4 text-[#0B6A3E]" />
                  </>
                ) : (
                  <>
                    <span>نسخ الرموز</span>
                    <Copy className="w-4 h-4 text-[#1E293B]" />
                  </>
                )}
              </button>

              <button
                type="button"
                onClick={handleDownloadCodes}
                dir="ltr"
                className="flex items-center justify-center gap-2 py-2.5 px-3 rounded-full border border-[#D5E3DB] bg-white hover:bg-[#F2F7F4] text-[#1E293B] text-[13px] font-bold transition-all shadow-sm active:scale-[0.98] cursor-pointer"
              >
                <span>تنزيل كملف</span>
                <Download className="w-4 h-4 text-[#1E293B]" />
              </button>
            </div>

            <button
              type="button"
              disabled={pending}
              onClick={handleContinueFromCodes}
              className="w-full min-h-[50px] rounded-full bg-[#0B6A3E] hover:bg-[#085531] disabled:opacity-60 text-white text-[15px] font-bold transition-all shadow-sm active:scale-[0.99] flex items-center justify-center cursor-pointer mt-1"
            >
              {pending ? "جاري المتابعة…" : "حفظتها، متابعة"}
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
              inputMode={useRecovery ? "text" : "numeric"}
              autoComplete="one-time-code"
              maxLength={useRecovery ? 20 : 7}
              value={code}
              onChange={(e) => handleCodeChange(e.target.value)}
              placeholder={useRecovery ? "xxxxx-xxxxx" : "000 000"}
              aria-invalid={!!codeErr}
              className={`min-h-[56px] border border-[#C9CFD9] rounded-xl px-3 text-2xl font-mono text-center focus:outline-none focus:ring-2 focus:ring-ink ${
                useRecovery ? "tracking-[2px]" : "tracking-[6px]"
              }`}
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
