import type { BffConfig } from "@malabak/bff/server";

export const bffConfig: BffConfig = {
  cookieName: "malabak-venue",
  sameSite: "lax",
  sessionTtlSeconds: 7 * 24 * 60 * 60,
  refreshPath: "/api/v1/auth/refresh",
  logoutPath: "/api/v1/auth/logout",
  loginPath: "/login",
  allowedPaths: [
    /^\/api\/v1\/venue-manager\//,
    /^\/api\/v1\/venue-manager\/venues$/,
    /^\/api\/v1\/me(\/|$)/,
    /^\/api\/v1\/reference\//,
    /^\/api\/v1\/media\//,
    /^\/api\/v1\/auth\/(password\/change|2fa\/totp\/(enroll|confirm)|step-up(\/otp)?)$/,
    /^\/api\/v1\/venue-invitations\/accept$/,
  ],
};
