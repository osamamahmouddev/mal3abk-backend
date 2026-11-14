"use client";

import React, { useEffect, useState, useTransition } from "react";
import Link from "next/link";
import { Eye, EyeOff } from "lucide-react";
import { registerOwner, resendVerification } from "@/app/auth-actions";
import { AuthCard, FormError, FormSuccess, authField, authPrimary } from "@/components/AuthCard";

export default function RegisterPage() {
  const [form, setForm] = useState({ fullName: "", email: "", phoneNumber: "", password: "" });
  const [showPassword, setShowPassword] = useState(false);
  const [done, setDone] = useState(false);
  const [resent, setResent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Carry ?next= (e.g. back to a staff invitation) through to sign-in; the login page only follows same-site paths.
  const [loginHref, setLoginHref] = useState("/login");
  useEffect(() => {
    const next = new URLSearchParams(window.location.search).get("next");
    if (next) setLoginHref(`/login?next=${encodeURIComponent(next)}`);
  }, []);
  const [pending, startTransition] = useTransition();

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    startTransition(async () => {
      const result = await registerOwner({ ...form, email: form.email.trim(), phoneNumber: form.phoneNumber.trim() });
      if (result.ok) setDone(true);
      else setError(result.message);
    });
  };

  if (done) {
    return (
      <AuthCard title="أكّد الإيميل" subtitle={`بعتنا لينك التأكيد على ${form.email}. افتحه وبعدها سجّل دخول وكمّل إعداد ملعبك.`}>
        {resent && <FormSuccess>✓ بعتنا اللينك تاني.</FormSuccess>}
        <button
          type="button"
          disabled={pending}
          onClick={() => startTransition(async () => setResent((await resendVerification(form.email)).ok))}
          className="min-h-[48px] rounded-[14px] border border-[#C9CFD9] text-navy-900 text-[15px] font-semibold"
        >
          ابعت اللينك تاني
        </button>
        <Link href={loginHref} className={`${authPrimary} flex items-center justify-center no-underline`}>
          رجوع للدخول
        </Link>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="سجّل ملعبك على ملعبك" subtitle="اعمل حساب المالك الأول، وبعدها تضيف بيانات الملعب والملاعب والمواعيد.">
      <form onSubmit={submit} className="flex flex-col gap-3.5">
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          الاسم بالكامل
          <input required autoComplete="name" value={form.fullName} onChange={set("fullName")} className={authField} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          الإيميل
          <input required type="email" dir="ltr" autoComplete="email" value={form.email} onChange={set("email")} className={`${authField} text-right`} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          الموبايل
          <input
            required
            type="tel"
            dir="ltr"
            autoComplete="tel"
            placeholder="01xxxxxxxxx"
            value={form.phoneNumber}
            onChange={set("phoneNumber")}
            className={`${authField} text-right`}
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          كلمة السر
          <div className="relative">
            <input
              required
              type={showPassword ? "text" : "password"}
              minLength={10}
              autoComplete="new-password"
              value={form.password}
              onChange={set("password")}
              className={`${authField} pl-11`}
            />
            <button
              type="button"
              aria-label={showPassword ? "إخفاء كلمة السر" : "إظهار كلمة السر"}
              onClick={() => setShowPassword((s) => !s)}
              className="absolute left-3 top-1/2 -translate-y-1/2 text-[#7B8494] hover:text-navy-900 transition-colors p-1 flex items-center justify-center"
            >
              {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
            </button>
          </div>
          <span className="text-xs font-normal text-[#5B6475]">١٠ حروف على الأقل.</span>
        </label>
        <FormError message={error} />
        <button type="submit" disabled={pending} className={authPrimary}>
          {pending ? "جاري التسجيل…" : "إنشاء الحساب"}
        </button>
        <Link href={loginHref} className="text-center text-sm font-semibold text-navy-900">
          عندك حساب؟ سجّل دخول
        </Link>
      </form>
    </AuthCard>
  );
}
