"use client";

import React, { Suspense, useState, useTransition } from "react";
import Image from "next/image";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { muted, redShadow } from "@/components/ui";
import { forgotPassword, login } from "@/app/auth-actions";

type View = "login" | "forgot";

const fieldClass =
  "min-h-[48px] rounded-xl px-3.5 text-[15px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900";

/** Only same-site paths, so `?next=` can't send the user to another site after sign-in. */
function safeNext(next: string | null): string {
  const fallback = "/venue/overview";
  const target = next && next.startsWith("/") && !next.startsWith("//") ? next : fallback;
  return target.startsWith("/venue") ? target : `/venue${target}`;
}

export default function LoginPage() {
  return (
    <Suspense>
      <LoginView />
    </Suspense>
  );
}

function LoginView() {
  const params = useSearchParams();
  const [view, setView] = useState<View>("login");
  const [user, setUser] = useState("");
  const [pass, setPass] = useState("");
  const [totp, setTotp] = useState("");
  const [needsTotp, setNeedsTotp] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [pending, startTransition] = useTransition();

  const handleLogin = (e: React.FormEvent) => {
    e.preventDefault();
    setErr(null);
    startTransition(async () => {
      const result = await login({ email: user.trim(), password: pass, totpCode: needsTotp ? totp.trim() : undefined });
      if (result.ok) {
        window.location.assign(safeNext(params.get("next")));
        return;
      }
      if (result.code === "Identity.TwoFactorRequired") {
        setNeedsTotp(true);
        return;
      }
      setErr(result.message);
    });
  };

  const handleForgot = () => {
    setErr(null);
    startTransition(async () => {
      const result = await forgotPassword(user.trim());
      if (result.ok) setSent(true);
      else setErr(result.message);
    });
  };

  return (
    <div dir="rtl" className="w-full min-h-screen bg-surface text-slate-900 flex flex-wrap">
      <section className="flex-[1_1_100%] lg:flex-[1_1_420px] min-w-0 relative isolate overflow-hidden bg-navy-900 text-white p-6 sm:p-12 flex flex-col justify-between gap-6 sm:gap-8 lg:min-h-screen">
        <svg
          aria-hidden="true"
          width="760"
          height="520"
          viewBox="0 0 760 520"
          fill="none"
          stroke="#FFFFFF"
          strokeOpacity="0.07"
          strokeWidth="3"
          className="absolute -bottom-[60px] -left-20 -z-10"
        >
          <rect x="10" y="10" width="740" height="500" rx="6" />
          <line x1="380" y1="10" x2="380" y2="510" />
          <circle cx="380" cy="260" r="80" />
          <rect x="10" y="140" width="120" height="240" />
          <rect x="630" y="140" width="120" height="240" />
          <rect x="10" y="200" width="45" height="120" />
          <rect x="705" y="200" width="45" height="120" />
        </svg>
        <div className="flex items-center gap-2.5">
          <Image
            src="/images/logo-horizontal-darkbg.svg"
            alt="ملعبك"
            width={140}
            height={50}
            className="h-10 w-auto object-contain"
            priority
          />
          <span className="text-xs font-bold text-navy-900 bg-brand-gold rounded-md px-2.5 py-[3px] whitespace-nowrap">للملاعب</span>
        </div>
        <div className="flex flex-col gap-[18px] max-w-[460px]">
          <h1 className="text-[28px] leading-[40px] sm:text-[38px] sm:leading-[54px] font-bold">كل حجوزات ملعبك في مكان واحد.</h1>
          <div className="hidden sm:flex flex-col gap-3 text-base text-[#D5DBE7]">
            <span>✓ تقويم واحد لكل الملاعب، من غير تعارض</span>
            <span>✓ مراجعة إيصالات العربون من اللوحة</span>
            <span>✓ تقارير الإشغال والإيراد أول بأول</span>
          </div>
        </div>
        <span className="text-[13px] text-[#AEB7CA]">محتاج مساعدة؟ كلّم فريق ملعبك على ٠١٠٠٠٠٠٠٠٠٠</span>
      </section>

      <section className="flex-[1_1_100%] lg:flex-[1_1_420px] min-w-0 px-4 py-8 sm:px-6 sm:py-12 flex items-center justify-center">
        <div className="w-full max-w-[420px] bg-white border border-[#EDF0F5] rounded-3xl shadow-[0_1px_2px_rgba(15,27,61,0.04),0_20px_40px_-24px_rgba(15,27,61,0.25)] p-6 sm:p-8 flex flex-col gap-[18px]">
          {view === "login" && (
            <form onSubmit={handleLogin} className="flex flex-col gap-4">
              <div className="flex flex-col gap-1">
                <h2 className="text-2xl font-bold">دخول لوحة الملعب</h2>
                <span className={`text-sm ${muted}`}>للمالك والموظفين المصرّح لهم</span>
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="em" className="text-sm font-semibold">
                  الإيميل
                </label>
                <input
                  id="em"
                  dir="ltr"
                  type="email"
                  required
                  autoComplete="username"
                  value={user}
                  onChange={(e) => setUser(e.target.value)}
                  className={`${fieldClass} border border-[#C9CFD9] text-right`}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <div className="flex justify-between">
                  <label htmlFor="pw" className="text-sm font-semibold">
                    كلمة السر
                  </label>
                  <button
                    type="button"
                    onClick={() => setView("forgot")}
                    className="bg-transparent p-0 min-h-[24px] text-navy-900 text-[13px] font-semibold underline"
                  >
                    نسيت كلمة السر؟
                  </button>
                </div>
                <input
                  id="pw"
                  type="password"
                  autoComplete="current-password"
                  value={pass}
                  required
                  onChange={(e) => {
                    setPass(e.target.value);
                    setErr(null);
                  }}
                  aria-invalid={!!err}
                  className={`${fieldClass} ${err ? "border-2 border-[#A8261C]" : "border border-[#C9CFD9]"}`}
                />
              </div>
              {needsTotp && (
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="totp" className="text-sm font-semibold">
                    كود تطبيق التحقق
                  </label>
                  <input
                    id="totp"
                    dir="ltr"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    maxLength={6}
                    autoFocus
                    value={totp}
                    onChange={(e) => setTotp(e.target.value.replace(/\D/g, ""))}
                    className={`${fieldClass} border border-[#C9CFD9] text-center tracking-[0.3em]`}
                  />
                </div>
              )}
              {err && (
                <span role="alert" className="text-[13px] text-[#A8261C]">
                  {err}
                </span>
              )}
              <button
                type="submit"
                disabled={pending}
                className={`min-h-[52px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-base font-bold ${redShadow}`}
              >
                {pending ? "جاري الدخول…" : "دخول"}
              </button>
              <div className="h-px bg-[#EEF1F5]" />
              <Link
                href="/register"
                className="min-h-[48px] rounded-[14px] border border-[#C9CFD9] text-navy-900 text-[15px] font-semibold flex items-center justify-center hover:bg-slate-50"
              >
                عندك ملعب؟ سجّله على ملعبك
              </Link>
            </form>
          )}

          {view === "forgot" && (
            <div className="flex flex-col gap-4">
              <h2 className="text-2xl font-bold">استرجاع كلمة السر</h2>
              <span className={`text-sm leading-[22px] ${muted}`}>هنبعتلك لينك على الإيميل تغيّر بيه كلمة السر.</span>
              <input
                dir="ltr"
                type="email"
                aria-label="الإيميل"
                value={user}
                onChange={(e) => setUser(e.target.value)}
                className={`${fieldClass} border border-[#C9CFD9] text-right`}
              />
              {sent && (
                <div role="status" className="bg-[#E5F5EB] text-[#155C33] rounded-xl px-3.5 py-2.5 text-[13px] font-semibold">
                  ✓ لو الإيميل مسجّل عندنا، هيوصلك لينك تغيير كلمة السر.
                </div>
              )}
              {err && (
                <span role="alert" className="text-[13px] text-[#A8261C]">
                  {err}
                </span>
              )}
              <button
                type="button"
                onClick={handleForgot}
                disabled={pending || !user.trim()}
                className="min-h-[52px] rounded-[14px] bg-navy-900 disabled:opacity-60 text-white text-base font-bold"
              >
                {sent ? "إعادة الإرسال" : "ابعت اللينك"}
              </button>
              <button
                type="button"
                onClick={() => {
                  setView("login");
                  setSent(false);
                }}
                className="min-h-[40px] bg-transparent text-navy-900 text-sm font-semibold"
              >
                رجوع للدخول
              </button>
            </div>
          )}

        </div>
      </section>
    </div>
  );
}
