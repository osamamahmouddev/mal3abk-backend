import "server-only";
import type { IronSession } from "iron-session";
import { apiUrl, type BffConfig } from "./config";
import { applyTokens, type SessionData, type Tokens } from "./session";

// Parallel requests from one browser carry the same refresh token. The API treats a reused refresh
// token as theft and revokes the session, so every caller in this process shares one refresh.
const inflight = new Map<string, Promise<RefreshOutcome>>();

/** "invalid": the API rejected the refresh token (session is over). "unavailable": try again later. */
type RefreshOutcome = Tokens | "invalid" | "unavailable";

async function refreshTokens(config: BffConfig, refreshToken: string): Promise<RefreshOutcome> {
  let pending = inflight.get(refreshToken);
  if (!pending) {
    pending = fetch(`${apiUrl()}${config.refreshPath}`, {
      method: "POST",
      headers: { "content-type": "application/json", accept: "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    })
      .then(async (response): Promise<RefreshOutcome> => {
        if (response.ok) {
          setTimeout(() => inflight.delete(refreshToken), 30_000);
          return (await response.json()) as Tokens;
        }
        inflight.delete(refreshToken);
        return response.status === 400 || response.status === 401 ? "invalid" : "unavailable";
      })
      .catch((): RefreshOutcome => {
        inflight.delete(refreshToken);
        return "unavailable";
      });
    inflight.set(refreshToken, pending);
  }
  return pending;
}

async function ensureFreshToken(config: BffConfig, session: IronSession<SessionData>, force: boolean): Promise<"ok" | "invalid" | "unavailable"> {
  if (!session.refreshToken) return "invalid";
  if (!force && session.accessToken && (session.accessExpiresAt ?? 0) > Date.now()) return "ok";

  const outcome = await refreshTokens(config, session.refreshToken);
  if (typeof outcome === "string") return outcome;
  applyTokens(session, outcome);
  await session.save();
  return "ok";
}

const unavailable = () =>
  new Response(JSON.stringify({ title: "Bff.ApiUnavailable", status: 503 }), {
    status: 503,
    headers: { "content-type": "application/problem+json", "retry-after": "5" },
  });

/**
 * Calls the API as the signed-in user, refreshing the access token when it has expired (or the API
 * rejects it once). Returns 401 only when the API rejected the session, so the caller signs the user
 * out; an unreachable API is a 503 and the session is kept.
 */
export async function callApi(
  config: BffConfig,
  session: IronSession<SessionData>,
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const fresh = await ensureFreshToken(config, session, false);
  if (fresh === "invalid") return new Response(null, { status: 401 });
  if (fresh === "unavailable") return unavailable();

  const send = () => {
    const headers = new Headers(init.headers);
    headers.set("authorization", `Bearer ${session.accessToken}`);
    return fetch(`${apiUrl()}${path}`, { ...init, headers, cache: "no-store" });
  };

  try {
    let response = await send();
    if (response.status === 401) {
      const retried = await ensureFreshToken(config, session, true);
      if (retried === "unavailable") return unavailable();
      if (retried === "ok") response = await send();
    }
    return response;
  } catch {
    return unavailable();
  }
}
