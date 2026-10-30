"use client";

import React, { Suspense, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { verifyEmail } from "@/app/auth-actions";
import { AuthCard, FormError, FormSuccess, authPrimary } from "@/components/AuthCard";

function VerifyEmail() {
  const token = useSearchParams().get("token");
  const [state, setState] = useState<"working" | "done" | "failed">("working");
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    if (!token) {
      setState("failed");
      setError("اللينك ناقص. افتحه من الإيميل تاني.");
      return;
    }
    verifyEmail(token).then((result) => {
      setState(result.ok ? "done" : "failed");
      if (!result.ok) setError(result.message);
    });
  }, [token]);

  return (
    <AuthCard title="تأكيد الإيميل">
      {state === "working" && <span className="text-sm text-[#5B6475]">جاري التأكيد…</span>}
      {state === "done" && <FormSuccess>✓ الإيميل اتأكد. تقدر تسجّل دخول دلوقتي.</FormSuccess>}
      <FormError message={error} />
      {state !== "working" && (
        <Link href="/login" className={`${authPrimary} flex items-center justify-center no-underline`}>
          تسجيل الدخول
        </Link>
      )}
    </AuthCard>
  );
}

export default function VerifyEmailPage() {
  return (
    <Suspense>
      <VerifyEmail />
    </Suspense>
  );
}
