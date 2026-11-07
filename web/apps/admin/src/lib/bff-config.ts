import type { BffConfig } from "@malabak/bff/server";

export const bffConfig: BffConfig = {
  cookieName: "malabak-admin",
  sameSite: "strict",
  sessionTtlSeconds: 12 * 60 * 60,
  refreshPath: "/api/v1/admin/auth/refresh",
  logoutPath: "/api/v1/admin/auth/logout",
  loginPath: "/login",
  // Only what the admin console uses. The API still checks every permission; this list keeps the proxy from
  // becoming a general-purpose gateway with the admin's token.
  allowedPaths: [
    /^\/api\/v1\/admin\//,
    /^\/api\/v1\/auth\/step-up\/?$/,
    /^\/api\/v1\/me(\/permissions)?$/,
    /^\/api\/v1\/plans$/,
    /^\/api\/v1\/reference\//,
    /^\/api\/v1\/media\/[0-9a-f-]+\/[a-z]+$/,
  ],
};
