"use client";

import React, { Suspense, useState, useTransition } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { resetPassword } from "@/app/auth-actions";
import { AuthCard, FormError, FormSuccess, authField, authPrimary } from "@/components/AuthCard";

function ResetPassword() {
  const token = useSearchParams().get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
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
          <input
            required
            type="password"
            minLength={10}
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={authField}
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          اكتبها تاني
          <input
            required
            type="password"
            minLength={10}
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            className={authField}
          />
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
