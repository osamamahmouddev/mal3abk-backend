import type { BffConfig } from "@malabak/bff/server";

export const bffConfig: BffConfig = {
  cookieName: "malabak-admin",
  sameSite: "strict",
  sessionTtlSeconds: 12 * 60 * 60,
  refreshPath: "/api/v1/admin/auth/refresh",
  logoutPath: "/api/v1/admin/auth/logout",
  loginPath: "/login",
  allowedPaths: [/^\/api\/v1\/admin\/(venues|sessions)(\/|$)/, /^\/api\/v1\/me$/, /^\/api\/v1\/reference\//, /^\/api\/v1\/media\/[0-9a-f-]+\/[a-z]+$/],
};
