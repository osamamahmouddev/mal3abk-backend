"use client";

import React, { Suspense, useEffect, useState, useTransition } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { problemMessage } from "@malabak/api-client";
import { api } from "@/lib/api";
import { useVenue } from "@/components/Providers";
import { AuthCard, FormError, FormSuccess, authField, authPrimary } from "@/components/AuthCard";
import { previewInvitation, registerFromInvitation, type InvitationPreview } from "@/app/auth-actions";
import { Eye, EyeOff, UserPlus } from "lucide-react";

const ROLE_AR: Record<string, string> = { Manager: "مدير", Receptionist: "استقبال", Accountant: "محاسب" };

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

  if (!signedIn) return <JoinAsNewStaff token={token} loginHref={`/login?next=${encodeURIComponent(back)}`} />;

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

/**
 * Invitee who isn't signed in: shows what they're joining and lets them make a staff account right here (an owner sign-up
 * would send them to set up a venue of their own). People who already have an account sign in and accept instead.
 */
function JoinAsNewStaff({ token, loginHref }: { token: string; loginHref: string }) {
  const [preview, setPreview] = useState<InvitationPreview | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [form, setForm] = useState({ fullName: "", phoneNumber: "", email: "", password: "" });
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  useEffect(() => {
    previewInvitation(token).then((r) => (r.ok && r.preview ? setPreview(r.preview) : setPreviewError(r.message ?? "الدعوة مش صالحة أو انتهت.")));
  }, [token]);

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    startTransition(async () => {
      const result = await registerFromInvitation({ token, ...form });
      if (result.ok) window.location.href = "/venue/overview";
      else setError(result.message ?? "حصل خطأ، حاول تاني.");
    });
  };

  if (previewError) {
    return (
      <AuthCard title="دعوة للانضمام لفريق الملعب">
        <FormError message={previewError} />
      </AuthCard>
    );
  }

  if (!preview) {
    return (
      <AuthCard title="دعوة للانضمام لفريق الملعب">
        <div className="h-24 rounded-xl skeleton-shimmer" aria-hidden="true" />
      </AuthCard>
    );
  }

  const byEmail = preview.channel === "email";
  return (
    <AuthCard
      title={`انضم لفريق ${preview.venueName}`}
      subtitle={`صاحب الملعب دعاك تشتغل معاه كـ ${ROLE_AR[preview.role] ?? preview.role}. الدعوة وصلت على ${byEmail ? "الإيميل" : "الموبايل"} ${preview.maskedContact}.`}
    >
      <form onSubmit={submit} className="flex flex-col gap-3.5">
        <span className="flex items-center gap-2 text-[15px] font-bold">
          <UserPlus className="size-[18px] text-brand-red" />
          اعمل حساب موظف
        </span>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          اسمك بالكامل
          <input required autoComplete="name" value={form.fullName} onChange={set("fullName")} className={authField} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          {byEmail ? "رقم موبايلك" : "رقم الموبايل اللي الدعوة وصلتلك عليه"}
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
          <span className="text-xs font-normal text-[#5B6475]">هتدخل بيه على اللوحة بعد كده.</span>
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          {byEmail ? "الإيميل اللي الدعوة وصلتلك عليه" : "الإيميل (اختياري)"}
          <input
            required={byEmail}
            type="email"
            dir="ltr"
            autoComplete="email"
            value={form.email}
            onChange={set("email")}
            className={`${authField} text-right`}
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          كلمة السر
          <div className="relative w-full">
            <input
              required
              type={showPassword ? "text" : "password"}
              minLength={10}
              autoComplete="new-password"
              value={form.password}
              onChange={set("password")}
              className={`${authField} pl-11 w-full`}
            />
            <button
              type="button"
              aria-label={showPassword ? "إخفاء كلمة السر" : "إظهار كلمة السر"}
              onClick={() => setShowPassword((v) => !v)}
              className="absolute left-3 top-1/2 -translate-y-1/2 text-[#7B8494] hover:text-navy-900 transition-colors p-1 flex items-center justify-center"
            >
              {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
            </button>
          </div>
          <span className="text-xs font-normal text-[#5B6475]">١٠ حروف على الأقل.</span>
        </label>
        <FormError message={error} />
        <button type="submit" disabled={pending} className={authPrimary}>
          {pending ? "جاري إنشاء الحساب…" : "إنشاء الحساب والانضمام"}
        </button>
        <div className="flex items-center gap-3 text-xs text-[#8A93A3]" aria-hidden="true">
          <span className="flex-1 h-px bg-[#E4E7EC]" />
          أو
          <span className="flex-1 h-px bg-[#E4E7EC]" />
        </div>
        <Link
          href={loginHref}
          className="min-h-[48px] rounded-xl border border-[#C9CFD9] text-navy-900 text-sm font-bold flex items-center justify-center no-underline hover:bg-slate-50 transition-colors"
        >
          عندي حساب بالفعل، سجّل دخول
        </Link>
      </form>
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
