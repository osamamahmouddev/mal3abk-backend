"use server";

import { redirect } from "next/navigation";
import type { Schemas } from "@malabak/api-client";
import { anonymousApi, signIn, signOut } from "@malabak/bff/server";
import { bffConfig } from "@/lib/bff-config";

export interface ActionResult {
  ok: boolean;
  code?: string;
  message?: string;
}

const messages: Record<string, string> = {
  "Identity.InvalidCredentials": "الإيميل أو كلمة السر غلط.",
  "Identity.LoginThrottled": "محاولات كتير غلط. استنى شوية وجرّب تاني.",
  "Identity.CaptchaRequired": "كمّل التحقق الأمني الأول.",
  "Identity.EmailNotVerified": "لازم تأكد الإيميل الأول. افتح الرسالة اللي وصلتلك.",
  "Identity.TwoFactorRequired": "اكتب الكود من تطبيق التحقق.",
  "Identity.InvalidTotpCode": "كود التحقق غلط.",
  "Identity.InvalidOrExpiredToken": "اللينك ده مش صالح أو انتهت صلاحيته.",
  "Identity.RegistrationFailed": "مقدرناش نعمل حساب بالبيانات دي.",
  "Identity.InvalidEmail": "اكتب إيميل صحيح.",
};

function failure(problem: Record<string, unknown>): ActionResult {
  const code = String(problem.title ?? "Error");
  const errors = problem.errors as Record<string, string[]> | undefined;
  const fieldError = errors ? Object.values(errors).flat()[0] : undefined;
  return { ok: false, code, message: messages[code] ?? fieldError ?? String(problem.detail ?? "حصل خطأ، حاول تاني.") };
}

export async function login(input: { email: string; password: string; totpCode?: string; turnstileToken?: string }): Promise<ActionResult> {
  const result = await anonymousApi<Schemas["PasswordLoginResponse"]>("/api/v1/auth/login", {
    email: input.email,
    password: input.password,
    totpCode: input.totpCode || null,
    turnstileToken: input.turnstileToken || null,
  });
  if (!result.ok) return failure(result.problem);

  const data = result.data!;
  await signIn(bffConfig, data, { id: data.userId, name: data.fullName, roles: data.roles });
  return { ok: true };
}

export async function logout() {
  await signOut(bffConfig);
  redirect("/login");
}

export async function registerOwner(input: Schemas["RegisterVenueOwnerApiRequest"]): Promise<ActionResult> {
  const result = await anonymousApi("/api/v1/auth/register/venue-owner", input);
  return result.ok ? { ok: true } : failure(result.problem);
}

export async function verifyEmail(token: string): Promise<ActionResult> {
  const result = await anonymousApi("/api/v1/auth/email/verify", { token });
  return result.ok ? { ok: true } : failure(result.problem);
}

export async function resendVerification(email: string): Promise<ActionResult> {
  const result = await anonymousApi("/api/v1/auth/email/resend", { email });
  return result.ok ? { ok: true } : failure(result.problem);
}

export async function forgotPassword(email: string): Promise<ActionResult> {
  const result = await anonymousApi("/api/v1/auth/password/forgot", { email });
  return result.ok ? { ok: true } : failure(result.problem);
}

export async function resetPassword(token: string, newPassword: string): Promise<ActionResult> {
  const result = await anonymousApi("/api/v1/auth/password/reset", { token, newPassword });
  return result.ok ? { ok: true } : failure(result.problem);
}
