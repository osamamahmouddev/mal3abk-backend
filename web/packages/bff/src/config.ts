export interface BffConfig {
  /** Session cookie name; gets the `__Host-` prefix in production. */
  cookieName: string;
  sameSite: "lax" | "strict";
  /** How long a signed-in browser session lasts without activity, in seconds. */
  sessionTtlSeconds: number;
  refreshPath: "/api/v1/auth/refresh" | "/api/v1/admin/auth/refresh";
  logoutPath: "/api/v1/auth/logout" | "/api/v1/admin/auth/logout";
  /** API paths the browser may reach through the proxy. Anything else is refused. */
  allowedPaths: RegExp[];
  loginPath: string;
}

export const CSRF_COOKIE = "malabak-csrf";
export const CSRF_HEADER = "x-csrf-token";

export const isProduction = process.env.NODE_ENV === "production";

export function apiUrl(): string {
  const url = process.env.API_URL || "http://mal3abk.runasp.net";
  return url.replace(/\/+$/, "");
}

export function sessionSecret(): string {
  const secret = process.env.SESSION_SECRET || "uDRBN3UrLVd55/toYJnznmfaADMcz34T6BSAR+kPwGM=";
  return secret;
}

export function sessionCookieName(config: BffConfig): string {
  return isProduction ? `__Host-${config.cookieName}` : config.cookieName;
}
