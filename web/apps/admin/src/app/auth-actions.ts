"use server";

import { redirect } from "next/navigation";
import type { Schemas } from "@malabak/api-client";
import { anonymousApi, apiUrl, deleteSealedCookie, getSealedCookie, setSealedCookie, signIn, signOut, userApi } from "@malabak/bff/server";
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
  "Identity.InvalidEnrollmentToken": "انتهت مهلة التفعيل. ابدأ من الأول.",
  "Identity.TwoFactorAlreadyEnabled": "التحقق بخطوتين متفعّل بالفعل.",
  "Identity.TwoFactorNotEnabled": "التحقق بخطوتين مقفول بالفعل.",
};

function failure(problem: Record<string, unknown> | undefined): ActionResult {
  const code = String(problem?.title ?? "Error");
  return { ok: false, code, message: messages[code] ?? String(problem?.detail ?? "حصل خطأ، حاول تاني.") };
}

/**
 * Password, then the authenticator (or a recovery) code when the admin turned two-step verification on.
 * Without it the password signs in directly (two-step is optional for admins; see SECURITY.md §3.9).
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

/** Whether the signed-in admin has two-step verification on, and how many recovery codes are left. */
export async function twoFactorStatus(): Promise<{ ok: boolean; enabled?: boolean; recoveryCodesLeft?: number; message?: string }> {
  const response = await userApi(bffConfig, "/api/v1/admin/auth/totp/status");
  if (!response.ok) return { ok: false, message: failure(await response.json().catch(() => undefined)).message };
  const data = (await response.json()) as { enabled: boolean; recoveryCodesLeft: number };
  return { ok: true, enabled: data.enabled, recoveryCodesLeft: data.recoveryCodesLeft };
}

/**
 * Turning two-step verification on from a signed-in session: the API hands a short-lived setup ticket that, like the
 * sign-in one, stays in an encrypted httpOnly cookie; then the usual enroll step returns the secret for the QR code.
 */
export async function beginTotpSetup(): Promise<{ ok: boolean; secret?: string; provisioningUri?: string; message?: string }> {
  const response = await userApi(bffConfig, "/api/v1/admin/auth/totp/setup", { method: "POST" });
  if (!response.ok) return { ok: false, message: failure(await response.json().catch(() => undefined)).message };
  const { enrollmentToken } = (await response.json()) as { enrollmentToken: string };
  await setSealedCookie(ENROLLMENT_COOKIE, enrollmentToken, 10 * 60);
  return startTotpEnrollment();
}

/** Turns two-step verification off; the API needs a current authenticator code or an unused recovery code. */
export async function disableTotp(input: { totpCode?: string; recoveryCode?: string }): Promise<ActionResult> {
  const response = await userApi(bffConfig, "/api/v1/admin/auth/totp/disable", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ totpCode: input.totpCode || null, recoveryCode: input.recoveryCode || null }),
  });
  return response.ok ? { ok: true } : failure(await response.json().catch(() => undefined));
}
