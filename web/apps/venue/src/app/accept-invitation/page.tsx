"use client";

import React, { Suspense, useState, useTransition } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { problemMessage } from "@malabak/api-client";
import { api } from "@/lib/api";
import { AuthCard, FormError, FormSuccess, authPrimary } from "@/components/AuthCard";

function AcceptInvitation() {
  const token = useSearchParams().get("token") ?? "";
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const accept = () =>
    startTransition(async () => {
      const { error: problem, response } = await api.POST("/api/v1/venue-invitations/accept", { body: { token } });
      if (response.ok) setDone(true);
      else setError(problemMessage(problem, "الدعوة مش صالحة أو انتهت."));
    });

  return (
    <AuthCard title="دعوة للانضمام لفريق الملعب" subtitle="لازم تكون مسجّل دخول بنفس الإيميل أو الموبايل اللي الدعوة اتبعتت عليه.">
      {done ? (
        <>
          <FormSuccess>✓ انضميت لفريق الملعب.</FormSuccess>
          <Link href="/overview" className={`${authPrimary} flex items-center justify-center no-underline`}>
            افتح اللوحة
          </Link>
        </>
      ) : (
        <>
          <FormError message={error} />
          <button type="button" onClick={accept} disabled={pending || !token} className={authPrimary}>
            {pending ? "جاري القبول…" : "قبول الدعوة"}
          </button>
        </>
      )}
    </AuthCard>
  );
}

export default function AcceptInvitationPage() {
  return (
    <Suspense>
      <AcceptInvitation />
    </Suspense>
  );
}
