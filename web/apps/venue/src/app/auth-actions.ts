"use server";

import { redirect } from "next/navigation";
import type { Schemas } from "@malabak/api-client";
import { anonymousApi, apiUrl, signIn, signOut } from "@malabak/bff/server";
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
  "Identity.EmailNotVerified": "لازم تفعّل الحساب الأول. افتح لينك التفعيل اللي وصلك على الموبايل.",
  "Identity.TwoFactorRequired": "اكتب الكود من تطبيق التحقق.",
  "Identity.InvalidTotpCode": "كود التحقق غلط.",
  "Identity.InvalidOrExpiredToken": "اللينك ده مش صالح أو انتهت صلاحيته.",
  "Identity.RegistrationFailed": "مقدرناش نعمل حساب بالبيانات دي.",
  "Identity.InvalidEmail": "اكتب إيميل صحيح.",
  "Identity.AccountExists": "فيه حساب بالرقم أو الإيميل ده بالفعل. سجّل دخول بيه واقبل الدعوة.",
  "Identity.WeakPassword": "كلمة السر ضعيفة: لازم ١٠ حروف على الأقل ومتكونش منتشرة.",
  "Staff.ContactMismatch": "اكتب نفس رقم الموبايل (أو الإيميل) اللي الدعوة وصلتلك عليه.",
  "Staff.InvalidContact": "اكتب رقم موبايل مصري صحيح.",
  "Staff.InvalidInvitation": "الدعوة مش صالحة أو انتهت. اطلب من صاحب الملعب يبعتها تاني.",
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

/**
 * Support impersonation (TASK-063): exchanges the single-use code an agent got from the admin console for a 15-minute
 * restricted session. There is no refresh token, so the session ends when that token does.
 */
export async function startImpersonation(code: string): Promise<ActionResult> {
  try {
    return await exchangeImpersonationCode(code);
  } catch {
    // Network or API outage: say so instead of leaving the support agent on a spinner.
    return { ok: false, message: "تعذّر الاتصال بالخادم. حاول تاني بعد شوية." };
  }
}

async function exchangeImpersonationCode(code: string): Promise<ActionResult> {
  const result = await anonymousApi<Schemas["ImpersonationTokenDto"]>("/api/v1/auth/impersonation/exchange", { code: code.trim() });
  if (!result.ok) return { ok: false, message: "الكود غلط أو انتهى أو اتستخدم قبل كده." };

  const token = result.data!;
  // The session carries the customer's real roles and name, read with the impersonation token itself.
  const auth = { headers: { authorization: `Bearer ${token.accessToken}` }, cache: "no-store" as const };
  const [permissions, profile] = await Promise.all([
    fetch(`${apiUrl()}/api/v1/me/permissions`, auth).then((r) => (r.ok ? (r.json() as Promise<Schemas["MyPermissionsResponse"]>) : null)),
    fetch(`${apiUrl()}/api/v1/me`, auth).then((r) => (r.ok ? (r.json() as Promise<Schemas["MyProfileDto"]>) : null)),
  ]);
  await signIn(
    bffConfig,
    { accessToken: token.accessToken, refreshToken: "impersonation-has-no-refresh", expiresIn: token.expiresIn },
    {
      id: token.userId,
      name: profile?.fullName ?? "حساب عميل",
      roles: permissions?.roles ?? [],
      impersonatedBy: token.impersonatorId,
      impersonationEndsAt: Date.parse(token.expiresAtUtc),
    },
  );
  return { ok: true };
}

export interface InvitationPreview {
  venueName: string;
  role: string;
  channel: "phone" | "email";
  maskedContact: string;
}

/** Venue, role and (masked) contact of an invitation, for an invitee who isn't signed in yet. */
export async function previewInvitation(token: string): Promise<{ ok: boolean; preview?: InvitationPreview; message?: string }> {
  const result = await anonymousApi<InvitationPreview>("/api/v1/venue-invitations/preview", { token });
  return result.ok ? { ok: true, preview: result.data! } : { ok: false, message: failure(result.problem).message };
}

/**
 * Invitee without an account: the API makes a staff account (never an owner one) and joins the venue in one step; then
 * they're signed in with the number they just used, so they land straight on the venue's dashboard.
 */
export async function registerFromInvitation(input: {
  token: string;
  fullName: string;
  phoneNumber: string;
  email?: string;
  password: string;
}): Promise<ActionResult> {
  const result = await anonymousApi("/api/v1/venue-invitations/register", {
    token: input.token,
    fullName: input.fullName.trim(),
    phoneNumber: input.phoneNumber.trim(),
    email: input.email?.trim() || null,
    password: input.password,
  });
  if (!result.ok) return failure(result.problem);
  return login({ email: input.phoneNumber.trim(), password: input.password });
}
