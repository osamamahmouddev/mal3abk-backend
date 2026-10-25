"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { muted, redShadow } from "@/components/ui";

type View = "login" | "forgot" | "pending";

const fieldClass =
  "min-h-[48px] rounded-xl px-3.5 text-[15px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900";

export default function LoginPage() {
  const router = useRouter();
  const [view, setView] = useState<View>("login");
  const [user, setUser] = useState("");
  const [pass, setPass] = useState("");
  const [err, setErr] = useState(false);
  const [sent, setSent] = useState(false);

  const handleLogin = (e: React.FormEvent) => {
    e.preventDefault();
    // TODO: replace with the auth API call; on a bad password show `err`.
    if (!pass) {
      setErr(true);
      return;
    }
    router.push("/overview");
  };

  return (
    <div dir="rtl" className="w-full min-h-screen bg-surface text-slate-900 flex flex-wrap">
      <section className="flex-[1_1_420px] min-h-[320px] relative isolate overflow-hidden bg-navy-900 text-white p-12 flex flex-col justify-between gap-8">
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
          <span className="text-[32px] font-bold">ملعبك</span>
          <span className="text-xs font-bold text-navy-900 bg-brand-gold rounded-md px-2.5 py-[3px]">للملاعب</span>
        </div>
        <div className="flex flex-col gap-[18px] max-w-[460px]">
          <h1 className="text-[38px] leading-[54px] font-bold">كل حجوزات ملعبك في مكان واحد.</h1>
          <div className="flex flex-col gap-3 text-base text-[#D5DBE7]">
            <span>✓ تقويم واحد لكل الملاعب، من غير تعارض</span>
            <span>✓ مراجعة إيصالات العربون من اللوحة</span>
            <span>✓ تقارير الإشغال والإيراد أول بأول</span>
          </div>
        </div>
        <span className="text-[13px] text-[#AEB7CA]">محتاج مساعدة؟ كلّم فريق ملعبك على ٠١٠٠٠٠٠٠٠٠٠</span>
      </section>

      <section className="flex-[1_1_420px] px-6 py-12 flex items-center justify-center">
        <div className="w-full max-w-[420px] bg-white border border-[#EDF0F5] rounded-3xl shadow-[0_1px_2px_rgba(15,27,61,0.04),0_20px_40px_-24px_rgba(15,27,61,0.25)] p-8 flex flex-col gap-[18px]">
          {view === "login" && (
            <form onSubmit={handleLogin} className="flex flex-col gap-4">
              <div className="flex flex-col gap-1">
                <h2 className="text-2xl font-bold">دخول لوحة الملعب</h2>
                <span className={`text-sm ${muted}`}>للمالك والموظفين المصرّح لهم</span>
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="em" className="text-sm font-semibold">
                  الموبايل أو الإيميل
                </label>
                <input
                  id="em"
                  dir="ltr"
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
                  onChange={(e) => {
                    setPass(e.target.value);
                    setErr(false);
                  }}
                  aria-invalid={err}
                  className={`${fieldClass} ${err ? "border-2 border-[#A8261C]" : "border border-[#C9CFD9]"}`}
                />
                {err && (
                  <span role="alert" className="text-[13px] text-[#A8261C]">
                    كلمة السر غلط. فاضل محاولتين قبل ما الحساب يتقفل ١٥ دقيقة.
                  </span>
                )}
              </div>
              <button
                type="submit"
                className={`min-h-[52px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-base font-bold ${redShadow}`}
              >
                دخول
              </button>
              <div className="h-px bg-[#EEF1F5]" />
              <Link
                href="/setup"
                className="min-h-[48px] rounded-[14px] border border-[#C9CFD9] text-navy-900 text-[15px] font-semibold flex items-center justify-center hover:bg-slate-50"
              >
                عندك ملعب؟ سجّله على ملعبك
              </Link>
            </form>
          )}

          {view === "forgot" && (
            <div className="flex flex-col gap-4">
              <h2 className="text-2xl font-bold">استرجاع كلمة السر</h2>
              <span className={`text-sm leading-[22px] ${muted}`}>هنبعت كود على الموبايل المسجّل للحساب.</span>
              <input
                dir="ltr"
                aria-label="رقم الموبايل"
                value={user}
                onChange={(e) => setUser(e.target.value)}
                className={`${fieldClass} border border-[#C9CFD9] text-right`}
              />
              {sent && (
                <div role="status" className="bg-[#E5F5EB] text-[#155C33] rounded-xl px-3.5 py-2.5 text-[13px] font-semibold">
                  ✓ الكود اتبعت. صالح ١٠ دقايق.
                </div>
              )}
              <button type="button" onClick={() => setSent(true)} className="min-h-[52px] rounded-[14px] bg-navy-900 text-white text-base font-bold">
                {sent ? "إعادة الإرسال" : "ابعت الكود"}
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

          {view === "pending" && (
            <div className="flex flex-col gap-3.5 text-center items-center">
              <span className="w-16 h-16 rounded-full bg-[#FEF3D6] text-[#7A4D00] flex items-center justify-center">
                <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                  <circle cx="12" cy="12" r="9" />
                  <path d="M12 7v5l3 2" />
                </svg>
              </span>
              <h2 className="text-[22px] font-bold">ملعبك تحت المراجعة</h2>
              <span className={`text-sm leading-[23px] ${muted}`}>
                فريق ملعبك بيراجع المستندات (عادةً خلال يومين عمل). تقدر تكمّل إعداد الملاعب والمواعيد دلوقتي، بس الملعب مش هيظهر للاعبين غير بعد
                الموافقة.
              </span>
              <Link
                href="/setup"
                className="w-full min-h-[50px] rounded-[14px] bg-navy-900 text-white text-[15px] font-bold flex items-center justify-center"
              >
                كمّل الإعداد
              </Link>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
