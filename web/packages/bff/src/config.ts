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

const DEV_API_URL = "http://localhost:5000";
// Development only: a fixed, non-secret key so local sessions survive restarts. Never used in production.
const DEV_SESSION_SECRET = "development-only-session-secret-not-for-production";

/** Backend API base URL. Production must set API_URL; staging hosts like runasp.net or siteasp.net are permitted over http. */
export function apiUrl(): string {
  const url = process.env.API_URL?.trim();
  if (!url) {
    if (isProduction) throw new Error("API_URL is not configured.");
    return DEV_API_URL;
  }
  if (isProduction && !url.startsWith("https://") && !url.includes("runasp.net") && !url.includes("siteasp.net") && !url.includes("localhost")) {
    throw new Error("API_URL must use https in production.");
  }
  return url.replace(/\/+$/, "");
}

/** Key that encrypts the session cookie (iron-session). Production must set SESSION_SECRET (32+ characters). */
export function sessionSecret(): string {
  const secret = process.env.SESSION_SECRET || process.env.IRON_SESSION_SECRET;
  if (!secret) {
    if (isProduction) throw new Error("SESSION_SECRET is not configured.");
    return DEV_SESSION_SECRET;
  }
  if (secret.length < 32) throw new Error("SESSION_SECRET must be at least 32 characters.");
  return secret;
}

export function sessionCookieName(config: BffConfig): string {
  return isProduction ? `__Host-${config.cookieName}` : config.cookieName;
}
