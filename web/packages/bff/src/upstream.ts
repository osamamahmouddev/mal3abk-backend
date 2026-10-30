import "server-only";
import type { IronSession } from "iron-session";
import { apiUrl, type BffConfig } from "./config";
import { applyTokens, type SessionData, type Tokens } from "./session";

// Parallel requests from one browser carry the same refresh token. The API treats a reused refresh
// token as theft and revokes the session, so every caller in this process shares one refresh.
const inflight = new Map<string, Promise<Tokens | null>>();

async function refreshTokens(config: BffConfig, refreshToken: string): Promise<Tokens | null> {
  let pending = inflight.get(refreshToken);
  if (!pending) {
    pending = fetch(`${apiUrl()}${config.refreshPath}`, {
      method: "POST",
      headers: { "content-type": "application/json", accept: "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    })
      .then(async (response) => (response.ok ? ((await response.json()) as Tokens) : null))
      .catch(() => null);
    inflight.set(refreshToken, pending);
    setTimeout(() => inflight.delete(refreshToken), 60_000);
  }
  return pending;
}

async function ensureFreshToken(config: BffConfig, session: IronSession<SessionData>, force: boolean): Promise<boolean> {
  if (!session.refreshToken) return false;
  if (!force && session.accessToken && (session.accessExpiresAt ?? 0) > Date.now()) return true;

  const tokens = await refreshTokens(config, session.refreshToken);
  if (!tokens) return false;
  applyTokens(session, tokens);
  await session.save();
  return true;
}

/**
 * Calls the API as the signed-in user, refreshing the access token when it has expired (or the API
 * rejects it once). Returns 401 when the session can't be refreshed; the caller signs the user out.
 */
export async function callApi(
  config: BffConfig,
  session: IronSession<SessionData>,
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  if (!(await ensureFreshToken(config, session, false))) {
    return new Response(null, { status: 401 });
  }

  const send = () => {
    const headers = new Headers(init.headers);
    headers.set("authorization", `Bearer ${session.accessToken}`);
    return fetch(`${apiUrl()}${path}`, { ...init, headers, cache: "no-store" });
  };

  let response = await send();
  if (response.status === 401 && (await ensureFreshToken(config, session, true))) {
    response = await send();
  }
  return response;
}
