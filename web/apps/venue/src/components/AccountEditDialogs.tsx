"use client";

import React, { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { inputClass, muted, primaryBtn, secondaryBtn } from "@/components/ui";
import { api, unwrap } from "@/lib/api";
import { logout } from "@/app/auth-actions";

/** Which account detail is being edited from the settings page. */
export type AccountField = "name" | "email" | "phone";

const PASSWORD = 0;

function Dialog({ title, children, onClose }: { title: string; children: React.ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] backdrop-blur-sm flex items-center justify-center p-4" onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="bg-white rounded-[20px] p-6 w-full max-w-[420px] flex flex-col gap-3 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-[19px] font-bold">{title}</h2>
        {children}
      </div>
    </div>
  );
}

function ErrorLine({ message }: { message: string | null }) {
  if (!message) return null;
  return (
    <p role="alert" className="text-xs bg-[#FDECEA] text-[#A8261C] rounded-lg px-3 py-2">
      {message}
    </p>
  );
}

function CodeInput({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <input
      dir="ltr"
      inputMode="numeric"
      autoComplete="one-time-code"
      maxLength={6}
      aria-label={label}
      placeholder="123456"
      value={value}
      onChange={(e) => onChange(e.target.value.replace(/\D/g, ""))}
      className={`${inputClass} text-center tracking-[0.3em]`}
    />
  );
}

/** A password check first, like every sensitive change (SECURITY.md §5.5 step 0): returns a 5-minute token for one scope. */
async function stepUp(scope: "change_email" | "change_phone", password: string) {
  const r = await unwrap(api.POST("/api/v1/auth/step-up", { body: { scope, method: PASSWORD, secret: password } }));
  return r.stepUpToken;
}

export function NameDialog({ current, onClose, onDone }: { current: string; onClose: () => void; onDone: (message: string) => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(current);
  const [error, setError] = useState<string | null>(null);
  const clean = name.trim();

  const save = useMutation({
    mutationFn: () => unwrap(api.PATCH("/api/v1/me", { body: { fullName: clean } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["me"] });
      onDone("الاسم اتغيّر.");
    },
    onError: (e: Error) => setError(e.message),
  });

  return (
    <Dialog title="تعديل الاسم" onClose={onClose}>
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          if (clean.length >= 2 && !save.isPending) save.mutate();
        }}
      >
        <input autoFocus aria-label="الاسم" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} className={inputClass} />
        <ErrorLine message={error} />
        <div className="flex gap-2 justify-end">
          <button type="button" onClick={onClose} className={secondaryBtn}>
            رجوع
          </button>
          <button type="submit" disabled={clean.length < 2 || clean === current || save.isPending} className={primaryBtn}>
            {save.isPending ? "جاري…" : "حفظ"}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

/**
 * New sign-in email: password, then a code sent to the new address. Once confirmed every device is signed out (this one
 * too), so the owner signs in again with the new email.
 */
export function EmailDialog({ onClose }: { onClose: () => void }) {
  const [step, setStep] = useState<"start" | "code" | "done">("start");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);

  const start = useMutation({
    mutationFn: async () => {
      const token = await stepUp("change_email", password);
      await unwrap(api.POST("/api/v1/me/email-change/start", { body: { newEmail: email.trim(), stepUpToken: token } }));
    },
    onSuccess: () => {
      setError(null);
      setPassword("");
      setStep("code");
    },
    onError: (e: Error) => setError(e.message),
  });

  const confirm = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/me/email-change/confirm", { body: { code } })),
    onSuccess: () => setStep("done"),
    onError: (e: Error) => setError(e.message),
  });

  if (step === "done") return <SignInAgain title="الإيميل اتغيّر" body={`من دلوقتي هتدخل بـ ${email.trim()}. سجّل دخول تاني.`} />;

  return (
    <Dialog title="تغيير الإيميل" onClose={onClose}>
      {step === "start" ? (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (!start.isPending) start.mutate();
          }}
        >
          <p className={`text-[13px] ${muted} leading-[1.7]`}>هنبعت كود على الإيميل الجديد. الإيميل مش هيتغيّر غير لما تكتب الكود.</p>
          <input
            autoFocus
            type="email"
            dir="ltr"
            autoComplete="email"
            aria-label="الإيميل الجديد"
            placeholder="name@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={inputClass}
          />
          <input
            type="password"
            autoComplete="current-password"
            aria-label="كلمة السر الحالية"
            placeholder="كلمة السر الحالية للتأكيد"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={inputClass}
          />
          <ErrorLine message={error} />
          <div className="flex gap-2 justify-end">
            <button type="button" onClick={onClose} className={secondaryBtn}>
              رجوع
            </button>
            <button type="submit" disabled={!email.includes("@") || !password || start.isPending} className={primaryBtn}>
              {start.isPending ? "جاري…" : "ابعت الكود"}
            </button>
          </div>
        </form>
      ) : (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (code.length === 6 && !confirm.isPending) confirm.mutate();
          }}
        >
          <p className={`text-[13px] ${muted} leading-[1.7]`}>
            اكتب الكود اللي وصل على <span dir="ltr" className="font-bold text-navy-900">{email.trim()}</span> (صالح ١٥ دقيقة). بعد التأكيد هيتسجّل الخروج من كل الأجهزة.
          </p>
          <CodeInput label="كود الإيميل" value={code} onChange={setCode} />
          <ErrorLine message={error} />
          <div className="flex gap-2 justify-end">
            <button type="button" onClick={onClose} className={secondaryBtn}>
              إلغاء
            </button>
            <button type="submit" disabled={code.length !== 6 || confirm.isPending} className={primaryBtn}>
              {confirm.isPending ? "جاري…" : "تأكيد"}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}

/**
 * New phone (SECURITY.md §5): password, a code on the current number, then a code on the new one. Once done every device is
 * signed out, this one too.
 */
export function PhoneDialog({ current, onClose }: { current: string; onClose: () => void }) {
  const [step, setStep] = useState<"start" | "old" | "new" | "done">("start");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [requestId, setRequestId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const start = useMutation({
    mutationFn: async () => {
      const token = await stepUp("change_phone", password);
      return unwrap(api.POST("/api/v1/me/phone-change/start", { body: { newPhoneNumber: phone.trim(), stepUpToken: token } }));
    },
    onSuccess: (r) => {
      setRequestId(r.requestId);
      setError(null);
      setPassword("");
      setStep("old");
    },
    onError: (e: Error) => setError(e.message),
  });

  const verify = useMutation({
    mutationFn: async () => {
      const body = { requestId: requestId!, code };
      if (step === "old") await unwrap(api.POST("/api/v1/me/phone-change/verify-old", { body }));
      else await unwrap(api.POST("/api/v1/me/phone-change/verify-new", { body }));
    },
    onSuccess: () => {
      setCode("");
      setError(null);
      setStep(step === "old" ? "new" : "done");
    },
    onError: (e: Error) => setError(e.message),
  });

  if (step === "done") return <SignInAgain title="الموبايل اتغيّر" body="الرقم الجديد اتسجّل على حسابك. سجّل دخول تاني." />;

  return (
    <Dialog title="تغيير رقم الموبايل" onClose={onClose}>
      {step === "start" ? (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (!start.isPending) start.mutate();
          }}
        >
          <p className={`text-[13px] ${muted} leading-[1.7]`}>للأمان هنبعت كود على رقمك الحالي وبعدين كود على الرقم الجديد.</p>
          <input
            autoFocus
            dir="ltr"
            inputMode="tel"
            autoComplete="tel"
            aria-label="رقم الموبايل الجديد"
            placeholder="01xxxxxxxxx"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            className={inputClass}
          />
          <input
            type="password"
            autoComplete="current-password"
            aria-label="كلمة السر الحالية"
            placeholder="كلمة السر الحالية للتأكيد"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={inputClass}
          />
          <ErrorLine message={error} />
          <div className="flex gap-2 justify-end">
            <button type="button" onClick={onClose} className={secondaryBtn}>
              رجوع
            </button>
            <button type="submit" disabled={phone.replace(/\D/g, "").length < 10 || !password || start.isPending} className={primaryBtn}>
              {start.isPending ? "جاري…" : "ابعت الكود"}
            </button>
          </div>
        </form>
      ) : (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (code.length === 6 && !verify.isPending) verify.mutate();
          }}
        >
          <p className={`text-[13px] ${muted} leading-[1.7]`}>
            {step === "old" ? (
              <>
                اكتب الكود اللي وصل على رقمك الحالي <span dir="ltr" className="font-bold text-navy-900">{current}</span>.
              </>
            ) : (
              <>
                اكتب الكود اللي وصل على الرقم الجديد <span dir="ltr" className="font-bold text-navy-900">{phone.trim()}</span>. بعد التأكيد هيتسجّل الخروج من كل الأجهزة.
              </>
            )}
          </p>
          <CodeInput label={step === "old" ? "كود الرقم الحالي" : "كود الرقم الجديد"} value={code} onChange={setCode} />
          <ErrorLine message={error} />
          <div className="flex gap-2 justify-end">
            <button type="button" onClick={onClose} className={secondaryBtn}>
              إلغاء
            </button>
            <button type="submit" disabled={code.length !== 6 || verify.isPending} className={primaryBtn}>
              {verify.isPending ? "جاري…" : "تأكيد"}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}

/** Every session was revoked by the change, so the only way on is to sign in again. */
function SignInAgain({ title, body }: { title: string; body: string }) {
  return (
    <Dialog title={title} onClose={() => undefined}>
      <p className={`text-sm ${muted} leading-[1.7]`}>{body}</p>
      <form action={logout} className="flex justify-end">
        <button type="submit" className={primaryBtn}>
          تسجيل الدخول
        </button>
      </form>
    </Dialog>
  );
}
