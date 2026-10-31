"use server";

import { redirect } from "next/navigation";
import type { Schemas } from "@malabak/api-client";
import { anonymousApi, apiUrl, deleteSealedCookie, getSealedCookie, setSealedCookie, signIn, signOut } from "@malabak/bff/server";
import { bffConfig } from "@/lib/bff-config";

export interface ActionResult {
  ok: boolean;
  code?: string;
  message?: string;
}

const ENROLLMENT_COOKIE = "malabak-admin-enroll";

const messages: Record<string, string> = {
  "Identity.InvalidCredentials": "الإيميل أو كلمة السر غلط.",
  "Identity.InvalidTotpCode": "الكود غلط أو انتهى. جرّب الكود اللي ظاهر دلوقتي.",
  "Identity.LoginThrottled": "محاولات كتير غلط. استنى شوية وجرّب تاني.",
  "Identity.CaptchaRequired": "محاولات كتير غلط. استنى شوية وجرّب تاني.",
  "Identity.InvalidEnrollmentToken": "انتهت مهلة التفعيل. سجّل دخول تاني.",
};

function failure(problem: Record<string, unknown> | undefined): ActionResult {
  const code = String(problem?.title ?? "Error");
  return { ok: false, code, message: messages[code] ?? String(problem?.detail ?? "حصل خطأ، حاول تاني.") };
}

/**
 * Password, then the authenticator (or a recovery) code. An admin without TOTP gets an enrollment
 * ticket instead of a session; it stays in an encrypted httpOnly cookie, never in the browser.
 */
export async function adminLogin(input: { email: string; password: string; totpCode?: string; recoveryCode?: string }): Promise<ActionResult> {
  const result = await anonymousApi<Schemas["AdminLoginResponse"]>("/api/v1/admin/auth/login", {
    email: input.email,
    password: input.password,
    totpCode: input.totpCode || null,
    recoveryCode: input.recoveryCode || null,
    turnstileToken: null,
  });
  if (!result.ok) return failure(result.problem);

  const { tokens, enrollmentToken } = result.data!;
  if (enrollmentToken) {
    await setSealedCookie(ENROLLMENT_COOKIE, enrollmentToken, 10 * 60);
    return { ok: false, code: "EnrollmentRequired" };
  }
  if (!tokens) return { ok: false, code: "TotpRequired", message: "اكتب الكود من تطبيق التحقق." };

  const me = await fetch(`${apiUrl()}/api/v1/me`, { headers: { authorization: `Bearer ${tokens.accessToken}` }, cache: "no-store" });
  const profile = me.ok ? ((await me.json()) as Schemas["MyProfileDto"]) : null;
  await signIn(bffConfig, tokens, { id: profile?.userId ?? "", name: profile?.fullName ?? input.email, roles: [] });
  return { ok: true };
}

export async function startTotpEnrollment(): Promise<{ ok: boolean; secret?: string; provisioningUri?: string; message?: string }> {
  const enrollmentToken = await getSealedCookie(ENROLLMENT_COOKIE);
  if (!enrollmentToken) return { ok: false, message: messages["Identity.InvalidEnrollmentToken"] };
  const result = await anonymousApi<Schemas["TotpEnrollmentResponse"]>("/api/v1/admin/auth/totp/enroll", { enrollmentToken });
  return result.ok ? { ok: true, ...result.data! } : { ok: false, message: failure(result.problem).message };
}

export async function confirmTotpEnrollment(code: string): Promise<{ ok: boolean; recoveryCodes?: string[]; message?: string }> {
  const enrollmentToken = await getSealedCookie(ENROLLMENT_COOKIE);
  if (!enrollmentToken) return { ok: false, message: messages["Identity.InvalidEnrollmentToken"] };
  const result = await anonymousApi<Schemas["RecoveryCodesResponse"]>("/api/v1/admin/auth/totp/confirm", { enrollmentToken, code });
  if (!result.ok) return { ok: false, message: failure(result.problem).message };
  await deleteSealedCookie(ENROLLMENT_COOKIE);
  return { ok: true, recoveryCodes: result.data!.recoveryCodes };
}

export async function adminLogout() {
  await signOut(bffConfig);
  redirect("/login");
}
