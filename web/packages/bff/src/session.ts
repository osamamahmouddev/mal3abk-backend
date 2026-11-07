import "server-only";
import { randomBytes } from "node:crypto";
import { getIronSession, type IronSession } from "iron-session";
import { cookies } from "next/headers";
import { CSRF_COOKIE, isProduction, sessionCookieName, sessionSecret, type BffConfig } from "./config";

export interface SessionUser {
  id: string;
  name: string;
  roles: string[];
  /** Set while a Malabak support agent is signed in as this user (TASK-063): the app shows a banner. */
  impersonatedBy?: string;
  /** Epoch ms when the impersonation token ends (no refresh: the session ends with it). */
  impersonationEndsAt?: number;
}

export interface SessionData {
  accessToken: string;
  refreshToken: string;
  /** Epoch ms after which the access token is treated as expired. */
  accessExpiresAt: number;
  user: SessionUser;
  csrf: string;
}

export interface Tokens {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export async function getSession(config: BffConfig): Promise<IronSession<SessionData>> {
  return getIronSession<SessionData>(await cookies(), {
    cookieName: sessionCookieName(config),
    password: sessionSecret(),
    ttl: config.sessionTtlSeconds,
    cookieOptions: { httpOnly: true, secure: isProduction, sameSite: config.sameSite, path: "/" },
  });
}

export function applyTokens(session: IronSession<SessionData>, tokens: Tokens) {
  session.accessToken = tokens.accessToken;
  session.refreshToken = tokens.refreshToken;
  // Refresh a little early so a request never reaches the API with a token about to expire.
  session.accessExpiresAt = Date.now() + Math.max(tokens.expiresIn - 30, 0) * 1000;
}

/** Starts a browser session after a successful sign-in. */
export async function startSession(config: BffConfig, tokens: Tokens, user: SessionUser) {
  const session = await getSession(config);
  applyTokens(session, tokens);
  session.user = user;
  session.csrf = randomBytes(32).toString("base64url");
  await session.save();

  // Double-submit token: readable by our scripts, sent back in X-CSRF-Token on every write.
  (await cookies()).set(CSRF_COOKIE, session.csrf, {
    httpOnly: false,
    secure: isProduction,
    sameSite: "strict",
    path: "/",
    maxAge: config.sessionTtlSeconds,
  });
}

export async function endSession(config: BffConfig) {
  const session = await getSession(config);
  session.destroy();
  (await cookies()).delete(CSRF_COOKIE);
}
