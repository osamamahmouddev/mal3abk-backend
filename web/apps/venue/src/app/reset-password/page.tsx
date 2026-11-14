"use client";

import React, { Suspense, useState, useTransition } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Eye, EyeOff } from "lucide-react";
import { resetPassword } from "@/app/auth-actions";
import { AuthCard, FormError, FormSuccess, authField, authPrimary } from "@/components/AuthCard";

function ResetPassword() {
  const token = useSearchParams().get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (password !== confirm) {
      setError("كلمتين السر مش زي بعض.");
      return;
    }
    setError(null);
    startTransition(async () => {
      const result = await resetPassword(token, password);
      if (result.ok) setDone(true);
      else setError(result.message);
    });
  };

  if (done) {
    return (
      <AuthCard title="كلمة السر اتغيرت">
        <FormSuccess>✓ سجّل دخول بكلمة السر الجديدة. كل الأجهزة التانية اتسجّل خروجها.</FormSuccess>
        <Link href="/login" className={`${authPrimary} flex items-center justify-center no-underline`}>
          تسجيل الدخول
        </Link>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="كلمة سر جديدة">
      <form onSubmit={submit} className="flex flex-col gap-3.5">
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          كلمة السر الجديدة
          <div className="relative">
            <input
              required
              type={showPassword ? "text" : "password"}
              minLength={10}
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
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
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          اكتبها تاني
          <div className="relative">
            <input
              required
              type={showPassword ? "text" : "password"}
              minLength={10}
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              className={`${authField} pl-11`}
            />
          </div>
        </label>
        <FormError message={error} />
        <button type="submit" disabled={pending || !token} className={authPrimary}>
          {pending ? "جاري الحفظ…" : "حفظ"}
        </button>
      </form>
    </AuthCard>
  );
}

export default function ResetPasswordPage() {
  return (
    <Suspense>
      <ResetPassword />
    </Suspense>
  );
}
