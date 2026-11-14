"use client";

import React, { createContext, useCallback, useContext, useRef, useState } from "react";
import { problemMessage } from "@malabak/api-client";
import { btnOutline, btnPrimary, inputClass, muted } from "@/components/ui";
import { api } from "@/lib/api";

/** Scope the API requires for destructive admin actions (suspensions, role changes, dispute decisions…). */
export const ADMIN_DESTRUCTIVE = "admin_destructive";
const TOTP_METHOD = 1;
// The API issues 5-minute tokens; reuse a little less to avoid edge expiry.
const REUSE_MS = 4 * 60 * 1000;

type Headers = { "X-Step-Up-Token": string };
type Run = <T>(action: (headers: Headers) => Promise<T>) => Promise<T>;

const StepUpContext = createContext<Run | null>(null);

/**
 * Sensitive admin actions need a fresh authenticator code. `useStepUp()(headers => api.POST(..., { headers }))` asks for the
 * code once, keeps the short-lived token in memory only (never storage), and retries nothing on its own.
 */
export function StepUpProvider({ children }: { children: React.ReactNode }) {
  const cached = useRef<{ token: string; at: number } | null>(null);
  const [pending, setPending] = useState<{ resolve: (t: string) => void; reject: (e: Error) => void } | null>(null);
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const askToken = useCallback(
    () =>
      new Promise<string>((resolve, reject) => {
        if (cached.current && Date.now() - cached.current.at < REUSE_MS) {
          resolve(cached.current.token);
          return;
        }
        setCode("");
        setError(null);
        setPending({ resolve, reject });
      }),
    [],
  );

  const run: Run = useCallback(
    async (action) => {
      const token = await askToken();
      return action({ "X-Step-Up-Token": token });
    },
    [askToken],
  );

  const submit = async () => {
    if (!pending || !/^\d{6}$/.test(code.trim())) {
      setError("يرجى إدخال الرمز المكوّن من 6 أرقام من تطبيق المصادقة.");
      return;
    }
    setBusy(true);
    const { data, error: problem, response } = await api.POST("/api/v1/auth/step-up", {
      body: { scope: ADMIN_DESTRUCTIVE, method: TOTP_METHOD, secret: code.trim() },
    });
    setBusy(false);
    if (!response.ok || !data) {
      setError(problemMessage(problem) || "رمز التحقق غير صحيح، يرجى التأكد والمحاولة مجدداً.");
      return;
    }
    cached.current = { token: data.stepUpToken, at: Date.now() };
    pending.resolve(data.stepUpToken);
    setPending(null);
  };

  const cancel = () => {
    pending?.reject(new Error("تم إلغاء عملية التأكيد."));
    setPending(null);
  };

  return (
    <StepUpContext.Provider value={run}>
      {children}
      {pending && (
        <div className="fixed inset-0 z-[60] bg-[rgba(10,19,48,0.5)] backdrop-blur-xs flex items-center justify-center p-4">
          <div role="dialog" aria-modal="true" aria-labelledby="stepup-title" className="bg-white rounded-[22px] p-6 w-full max-w-[420px] flex flex-col gap-3 shadow-2xl animate-modal-in border border-[#E5E9F0]">
            <h2 id="stepup-title" className="text-[19px] font-bold text-slate-900">
              تأكيد الهوية الأمنية
            </h2>
            <p className={`text-sm ${muted} leading-[1.7]`}>
              هذا الإجراء يتطلب مصادقة أمنية مشددة. يرجى إدخال رمز التحقق (TOTP) الحالي المكون من 6 أرقام من تطبيق المصادقة لديك.
            </p>
            <input
              autoFocus
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              value={code}
              onChange={(e) => {
                setCode(e.target.value.replace(/\D/g, ""));
                setError(null);
              }}
              onKeyDown={(e) => e.key === "Enter" && submit()}
              aria-label="كود التحقق"
              aria-invalid={!!error}
              placeholder="••••••"
              className={`${inputClass} text-center tracking-[0.4em] font-mono text-lg font-bold`}
            />
            {error && (
              <div role="alert" className="rounded-xl px-3 py-2 bg-[#FDECEA] text-xs font-semibold text-[#A8261C] flex items-center gap-1.5 animate-fade-in">
                <span>⚠️</span>
                <span>{error}</span>
              </div>
            )}
            <div className="flex gap-2 justify-end pt-2 border-t border-[#EEF2F6]">
              <button type="button" onClick={cancel} className={btnOutline}>
                إلغاء
              </button>
              <button type="button" onClick={submit} disabled={busy || code.trim().length !== 6} className={btnPrimary}>
                {busy ? "جاري التحقق…" : "تأكيد"}
              </button>
            </div>
          </div>
        </div>
      )}
    </StepUpContext.Provider>
  );
}

export function useStepUp(): Run {
  const run = useContext(StepUpContext);
  if (!run) throw new Error("useStepUp must be used inside StepUpProvider");
  return run;
}
