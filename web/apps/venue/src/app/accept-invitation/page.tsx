"use client";

import React, { Suspense, useEffect, useState, useTransition } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { problemMessage } from "@malabak/api-client";
import { api } from "@/lib/api";
import { useVenue } from "@/components/Providers";
import { AuthCard, FormError, FormSuccess, authPrimary } from "@/components/AuthCard";

const MESSAGES: Record<string, string> = {
  // The API deliberately gives one answer for expired, revoked or someone else's invitation.
  "Staff.InvalidInvitation": "الدعوة مش صالحة أو انتهت، أو متبعتة لإيميل/موبايل تاني. سجّل دخول بالحساب اللي الدعوة وصلتله أو اطلب دعوة جديدة.",
  "Staff.InvitationNotFound": "الدعوة مش صالحة أو اتلغت.",
};

function AcceptInvitation() {
  const params = useSearchParams();
  const { signedIn } = useVenue();
  const [token, setToken] = useState(() => params.get("token") ?? "");
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  // Signed in: keep the token in memory only and take it out of the address bar (history, screenshots, shared links).
  useEffect(() => {
    if (signedIn && params.get("token")) {
      setToken(params.get("token") ?? "");
      window.history.replaceState(null, "", window.location.pathname);
    }
  }, [signedIn, params]);

  const back = `/accept-invitation?token=${encodeURIComponent(token)}`;

  const accept = () =>
    startTransition(async () => {
      const { error: problem, response } = await api.POST("/api/v1/venue-invitations/accept", { body: { token } });
      if (response.ok) {
        setDone(true);
        return;
      }
      const code = (problem as { title?: string } | undefined)?.title ?? "";
      setError(MESSAGES[code] ?? problemMessage(problem, "الدعوة مش صالحة أو انتهت."));
    });

  if (!token) {
    return (
      <AuthCard title="دعوة للانضمام لفريق الملعب" subtitle="افتح اللينك اللي وصلك في الرسالة كامل.">
        <FormError message="اللينك ناقص — مفيش كود دعوة." />
      </AuthCard>
    );
  }

  if (!signedIn) {
    return (
      <AuthCard title="دعوة للانضمام لفريق الملعب" subtitle="سجّل دخول (أو اعمل حساب) بنفس الإيميل أو الموبايل اللي الدعوة وصلتله، وبعدها هترجع هنا تقبلها.">
        <Link href={`/login?next=${encodeURIComponent(back)}`} className={`${authPrimary} flex items-center justify-center no-underline`}>
          تسجيل الدخول
        </Link>
        <Link href={`/register?next=${encodeURIComponent(back)}`} className="min-h-[48px] rounded-xl border border-[#C9CFD9] text-navy-900 text-sm font-bold flex items-center justify-center no-underline">
          اعمل حساب جديد
        </Link>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="دعوة للانضمام لفريق الملعب" subtitle="لما تقبل هتشوف الملعب في اللوحة بالصلاحيات اللي صاحب الملعب اختارها.">
      {done ? (
        <>
          <FormSuccess>انضميت لفريق الملعب.</FormSuccess>
          <a href="/venue/overview" className={`${authPrimary} flex items-center justify-center no-underline`}>
            افتح اللوحة
          </a>
        </>
      ) : (
        <>
          <FormError message={error} />
          <button type="button" onClick={accept} disabled={pending} className={authPrimary}>
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
