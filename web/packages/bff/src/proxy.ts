import "server-only";
import { timingSafeEqual } from "node:crypto";
import { NextResponse, type NextRequest } from "next/server";
import { CSRF_HEADER, apiUrl, type BffConfig } from "./config";
import { endSession, getSession } from "./session";
import { callApi } from "./upstream";
import { clientIpHeaders } from "./client-ip";

const FORWARDED_REQUEST_HEADERS = ["content-type", "accept", "accept-language", "idempotency-key", "x-step-up-token"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "content-disposition", "cache-control", "retry-after", "location"];
const SAFE_METHODS = new Set(["GET", "HEAD"]);

function problem(status: number, title: string) {
  return NextResponse.json({ title, status }, { status, headers: { "content-type": "application/problem+json" } });
}

function sameValue(a: string, b: string) {
  const left = Buffer.from(a);
  const right = Buffer.from(b);
  return left.length === right.length && timingSafeEqual(left, right);
}

/**
 * Route handler for `app/bff/[...path]/route.ts`. The browser calls `/bff/api/v1/...` with its session
 * cookie; this forwards allow-listed paths to the API with the user's access token. Writes need a
 * same-origin request and the CSRF token (SECURITY.md §6).
 */
export function createProxy(config: BffConfig) {
  async function handle(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
    const { path } = await context.params;
    const apiPath = `/${path.map(encodeURIComponent).join("/")}`;
    if (!config.allowedPaths.some((pattern) => pattern.test(apiPath))) {
      return problem(404, "Bff.PathNotAllowed");
    }

    const session = await getSession(config);
    const isAnonymousAllowed = request.method === "GET" && apiPath.startsWith("/api/v1/reference/");

    if (!isAnonymousAllowed && (!session.refreshToken || !session.csrf)) {
      return problem(401, "Bff.NotSignedIn");
    }

    if (!SAFE_METHODS.has(request.method)) {
      const origin = request.headers.get("origin");
      if (!origin || origin !== request.nextUrl.origin) {
        return problem(403, "Bff.CrossOriginRequest");
      }
      const csrf = request.headers.get(CSRF_HEADER);
      if (!csrf || !session.csrf || !sameValue(csrf, session.csrf)) {
        return problem(403, "Bff.InvalidCsrfToken");
      }
    }

    const headers = new Headers();
    for (const name of FORWARDED_REQUEST_HEADERS) {
      const value = request.headers.get(name);
      if (value) headers.set(name, value);
    }

    for (const [name, value] of Object.entries(clientIpHeaders(request.headers))) headers.set(name, value);

    const body = SAFE_METHODS.has(request.method) ? undefined : await request.arrayBuffer();

    let upstream: Response;
    if (isAnonymousAllowed && (!session.refreshToken || !session.csrf)) {
      upstream = await fetch(`${apiUrl()}${apiPath}${request.nextUrl.search}`, {
        method: "GET",
        headers,
        cache: "no-store",
      });
    } else {
      upstream = await callApi(config, session, `${apiPath}${request.nextUrl.search}`, {
        method: request.method,
        headers,
        body: body && body.byteLength > 0 ? body : undefined,
      });
    }

    if (upstream.status === 401) {
      await endSession(config);
      return problem(401, "Bff.SessionExpired");
    }

    const responseHeaders = new Headers();
    for (const name of FORWARDED_RESPONSE_HEADERS) {
      const value = upstream.headers.get(name);
      if (value) responseHeaders.set(name, value);
    }
    return new NextResponse(upstream.status === 204 ? null : upstream.body, { status: upstream.status, headers: responseHeaders });
  }

  return { GET: handle, POST: handle, PUT: handle, PATCH: handle, DELETE: handle };
}
