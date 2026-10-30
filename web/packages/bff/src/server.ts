import "server-only";
import { headers } from "next/headers";
import { apiUrl, type BffConfig } from "./config";
import { clientIpHeaders } from "./client-ip";
import { endSession, getSession, startSession, type SessionUser, type Tokens } from "./session";
import { callApi } from "./upstream";

export type { BffConfig } from "./config";
export type { SessionUser, Tokens } from "./session";
export { createProxy } from "./proxy";
export { startSession, getSession };

/** Calls an anonymous API endpoint (sign-in, password reset, ...) from a server action. */
export interface AnonymousResult<T> {
  ok: boolean;
  data?: T;
  problem?: Record<string, unknown>;
}

export async function anonymousApi<T>(path: string, body: unknown): Promise<AnonymousResult<T>> {
  const response = await fetch(`${apiUrl()}${path}`, {
    method: "POST",
    headers: { "content-type": "application/json", accept: "application/json", ...clientIpHeaders(await headers()) },
    body: JSON.stringify(body),
    cache: "no-store",
  });
  if (response.ok) {
    const text = await response.text();
    return { ok: true, data: (text ? JSON.parse(text) : undefined) as T };
  }
  const problem = await response.json().catch(() => ({ status: response.status }));
  return { ok: false, problem: { status: response.status, ...problem } };
}

/** Calls the API as the signed-in user from a server action or route handler. */
export async function userApi(config: BffConfig, path: string, init?: RequestInit): Promise<Response> {
  return callApi(config, await getSession(config), path, init);
}

export async function currentUser(config: BffConfig): Promise<SessionUser | null> {
  const session = await getSession(config);
  return session.refreshToken && session.user ? session.user : null;
}

export async function signIn(config: BffConfig, tokens: Tokens, user: SessionUser) {
  await startSession(config, tokens, user);
}

/** Revokes the refresh token at the API (best effort) and clears the browser session. */
export async function signOut(config: BffConfig) {
  const session = await getSession(config);
  if (session.refreshToken) {
    await callApi(config, session, config.logoutPath, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ refreshToken: session.refreshToken }),
    }).catch(() => undefined);
  }
  await endSession(config);
}
