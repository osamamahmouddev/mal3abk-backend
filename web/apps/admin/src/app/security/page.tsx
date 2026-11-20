"use client";

import React, { useEffect, useState, useTransition } from "react";
import { QRCodeSVG } from "qrcode.react";
import { Check, Copy, Download, KeyRound, Loader2, ShieldCheck, ShieldOff, TriangleAlert } from "lucide-react";
import { Page, PageHeader, Pill, btnDanger, btnOutline, btnPrimary, card, inputClass, muted, toArabicDigits } from "@/components/ui";
import { beginTotpSetup, confirmTotpEnrollment, disableTotp, twoFactorStatus } from "@/app/auth-actions";

type Status = { enabled: boolean; recoveryCodesLeft: number };
type Step = "idle" | "setup" | "codes" | "disable";

const digits = (v: string) => v.replace(/\D/g, "").slice(0, 6);

/** The signed-in admin's own sign-in security: two-step verification is optional and off until they turn it on. */
export default function AccountSecurityPage() {
  const [status, setStatus] = useState<Status | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [step, setStep] = useState<Step>("idle");
  const [setup, setSetup] = useState<{ secret: string; provisioningUri: string } | null>(null);
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const [codes, setCodes] = useState<string[]>([]);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const load = () =>
    twoFactorStatus().then((r) => {
      if (r.ok) setStatus({ enabled: !!r.enabled, recoveryCodesLeft: r.recoveryCodesLeft ?? 0 });
      else setLoadError(r.message ?? "مقدرناش نجيب حالة الحساب.");
    });

  useEffect(() => {
    load();
  }, []);

  const reset = (next: Step = "idle") => {
    setStep(next);
    setCode("");
    setError(null);
    setUseRecovery(false);
  };

  const start = () =>
    startTransition(async () => {
      setNotice(null);
      const r = await beginTotpSetup();
      if (!r.ok || !r.secret || !r.provisioningUri) return setError(r.message ?? "مقدرناش نبدأ التفعيل.");
      setSetup({ secret: r.secret, provisioningUri: r.provisioningUri });
      reset("setup");
    });

  const confirm = (e: React.FormEvent) => {
    e.preventDefault();
    startTransition(async () => {
      const r = await confirmTotpEnrollment(code);
      if (!r.ok) return setError(r.message ?? "الكود غلط.");
      setCodes(r.recoveryCodes ?? []);
      setSetup(null);
      reset("codes");
      await load();
    });
  };

  const turnOff = (e: React.FormEvent) => {
    e.preventDefault();
    startTransition(async () => {
      const r = await disableTotp(useRecovery ? { recoveryCode: code.trim() } : { totpCode: code });
      if (!r.ok) return setError(r.message ?? "الكود غلط.");
      reset();
      setNotice("اتقفل التحقق بخطوتين. الدخول دلوقتي بالإيميل وكلمة السر بس.");
      await load();
    });
  };

  const copyCodes = async () => {
    try {
      await navigator.clipboard.writeText(codes.map((c) => c.toLowerCase()).join("\n"));
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard blocked: the codes stay on screen to copy by hand.
    }
  };

  const downloadCodes = () => {
    const text = ["ملعبك - رموز استرجاع حساب الإدارة", "كل رمز بيشتغل مرة واحدة بدل كود تطبيق المصادقة.", "", ...codes.map((c, i) => `${i + 1}. ${c.toLowerCase()}`)].join("\n");
    const url = URL.createObjectURL(new Blob([text], { type: "text/plain;charset=utf-8" }));
    const a = Object.assign(document.createElement("a"), { href: url, download: "malabak-recovery-codes.txt" });
    a.click();
    URL.revokeObjectURL(url);
  };

  const codeInput = (
    <input
      autoFocus
      dir="ltr"
      inputMode={useRecovery ? "text" : "numeric"}
      autoComplete="one-time-code"
      aria-label={useRecovery ? "كود الاسترجاع" : "كود التحقق"}
      placeholder={useRecovery ? "xxxxx-xxxxx" : "000000"}
      value={code}
      onChange={(e) => {
        setCode(useRecovery ? e.target.value : digits(e.target.value));
        setError(null);
      }}
      className={`${inputClass} text-center font-mono text-xl tracking-[0.3em]`}
    />
  );

  const errorBox = error && (
    <div role="alert" className="rounded-xl px-3 py-2 bg-[#FDECEA] text-[13px] font-semibold text-[#A8261C] flex items-center gap-1.5 animate-fade-in">
      <TriangleAlert className="size-4 shrink-0" />
      {error}
    </div>
  );

  return (
    <Page className="gap-4 max-w-[720px]">
      <PageHeader title="أمان الحساب" sub="إعدادات دخولك إنت بس، مش إعدادات المنصة." />

      {loadError && <div className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-4 py-3 text-sm">{loadError}</div>}
      {!status && !loadError && <div className="h-40 rounded-[20px] skeleton-shimmer" />}

      {status && (
        <section className={`${card} rounded-[20px] p-5 flex flex-col gap-4 animate-fade-in-up`}>
          <div className="flex items-start gap-3.5">
            <span className={`grid place-items-center size-12 rounded-2xl shrink-0 ${status.enabled ? "bg-[#E5F5EB] text-[#1B6E3E]" : "bg-[#EEF0F5] text-[#5B6475]"}`}>
              {status.enabled ? <ShieldCheck className="size-6" /> : <ShieldOff className="size-6" />}
            </span>
            <div className="flex-1 flex flex-col gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="text-[17px] font-bold">التحقق بخطوتين</h2>
                <Pill tone={status.enabled ? "ok" : "neutral"}>{status.enabled ? "مفعّل" : "مقفول"}</Pill>
              </div>
              <p className={`text-sm ${muted} leading-relaxed`}>
                {status.enabled
                  ? `عند الدخول هنطلب الكود من تطبيق المصادقة بعد كلمة السر. باقي ${toArabicDigits(status.recoveryCodesLeft)} من رموز الاسترجاع.`
                  : "بتدخل بالإيميل وكلمة السر بس. لو حد عرف كلمة السر يقدر يدخل لوحة الإدارة، فبننصحك تفعّله."}
              </p>
            </div>
          </div>

          {notice && (
            <div role="status" className="rounded-xl px-3.5 py-2.5 bg-[#E5F5EB] text-[#1B6E3E] text-[13px] font-semibold animate-fade-in">
              {notice}
            </div>
          )}

          {step === "idle" &&
            (status.enabled ? (
              <button type="button" onClick={() => reset("disable")} className={`${btnDanger} self-start`}>
                <ShieldOff className="size-4" />
                إلغاء التحقق بخطوتين
              </button>
            ) : (
              <div className="flex flex-col gap-2">
                <button type="button" onClick={start} disabled={pending} className={`${btnPrimary} self-start`}>
                  {pending ? <Loader2 className="size-4 animate-spin" /> : <ShieldCheck className="size-4" />}
                  تفعيل التحقق بخطوتين
                </button>
                {errorBox}
              </div>
            ))}

          {step === "setup" && setup && (
            <form onSubmit={confirm} className="flex flex-col gap-3.5 border-t border-[#EEF0F5] pt-4 animate-fade-in" noValidate>
              <ol className={`text-sm ${muted} list-decimal ps-5 flex flex-col gap-1`}>
                <li>افتح تطبيق المصادقة (Google Authenticator أو Microsoft Authenticator).</li>
                <li>امسح الكود، أو اكتب المفتاح اللي تحته يدوي.</li>
                <li>اكتب الـ ٦ أرقام اللي ظاهرين في التطبيق.</li>
              </ol>
              <div className="flex flex-col items-center gap-2">
                <QRCodeSVG value={setup.provisioningUri} size={176} className="border border-[#EDF0F5] rounded-xl p-2 bg-white" />
                <code dir="ltr" className="text-[13px] bg-[#F3F5F9] rounded-lg px-2.5 py-1.5 break-all select-all">
                  {setup.secret}
                </code>
              </div>
              {codeInput}
              {errorBox}
              <div className="flex gap-2">
                <button type="submit" disabled={pending || code.length !== 6} className={btnPrimary}>
                  {pending ? <Loader2 className="size-4 animate-spin" /> : <Check className="size-4" />}
                  تفعيل
                </button>
                <button type="button" onClick={() => reset()} className={btnOutline}>
                  إلغاء
                </button>
              </div>
            </form>
          )}

          {step === "codes" && (
            <div className="flex flex-col gap-3 border-t border-[#EEF0F5] pt-4 animate-fade-in">
              <div className="flex items-start gap-2 rounded-xl bg-[#FEF3D6] text-[#7A4D00] px-3.5 py-2.5 text-[13px] font-semibold">
                <KeyRound className="size-4 shrink-0 mt-0.5" />
                احفظ رموز الاسترجاع دي في مكان آمن. كل رمز بيشتغل مرة واحدة لو موبايلك ضاع، ومش هيظهروا تاني.
              </div>
              <div dir="ltr" className="grid grid-cols-2 gap-x-6 gap-y-2 rounded-2xl bg-[#F6F8FB] border border-[#E4E7EC] p-4 font-mono text-[14px] text-center">
                {codes.map((c) => (
                  <span key={c} className="select-all">
                    {c.toLowerCase()}
                  </span>
                ))}
              </div>
              <div className="flex flex-wrap gap-2">
                <button type="button" onClick={copyCodes} className={btnOutline}>
                  {copied ? <Check className="size-4 text-[#1B6E3E]" /> : <Copy className="size-4" />}
                  {copied ? "اتنسخت" : "نسخ الرموز"}
                </button>
                <button type="button" onClick={downloadCodes} className={btnOutline}>
                  <Download className="size-4" />
                  تنزيل كملف
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setCodes([]);
                    reset();
                  }}
                  className={btnPrimary}
                >
                  حفظتهم
                </button>
              </div>
            </div>
          )}

          {step === "disable" && (
            <form onSubmit={turnOff} className="flex flex-col gap-3 border-t border-[#EEF0F5] pt-4 animate-fade-in" noValidate>
              <p className={`text-sm ${muted}`}>
                {useRecovery ? "اكتب واحد من رموز الاسترجاع اللي حفظتهم." : "عشان نتأكد إنه إنت، اكتب الكود اللي ظاهر دلوقتي في تطبيق المصادقة."}
              </p>
              {codeInput}
              {errorBox}
              <button
                type="button"
                onClick={() => {
                  setUseRecovery(!useRecovery);
                  setCode("");
                  setError(null);
                }}
                className="self-start text-[13px] font-semibold text-navy-900 underline"
              >
                {useRecovery ? "استخدم كود التطبيق" : "موبايلك مش معاك؟ استخدم كود استرجاع"}
              </button>
              <div className="flex gap-2">
                <button type="submit" disabled={pending || (useRecovery ? code.trim().length < 10 : code.length !== 6)} className={btnDanger}>
                  {pending ? <Loader2 className="size-4 animate-spin" /> : <ShieldOff className="size-4" />}
                  إلغاء التحقق بخطوتين
                </button>
                <button type="button" onClick={() => reset()} className={btnOutline}>
                  رجوع
                </button>
              </div>
            </form>
          )}
        </section>
      )}
    </Page>
  );
}
