import { NextResponse, type NextRequest } from "next/server";
import { sessionCookieName, type BffConfig } from "./config";

/**
 * Redirects to sign-in when there is no session cookie. This is only a UX guard: the cookie is
 * verified when it is used, and the API authorizes every call.
 */
export function createAuthMiddleware(config: BffConfig, publicPaths: string[]) {
  return function middleware(request: NextRequest) {
    const { pathname, search } = request.nextUrl;
    if (publicPaths.some((path) => pathname === path || pathname.startsWith(`${path}/`))) {
      return NextResponse.next();
    }
    if (!request.cookies.has(sessionCookieName(config))) {
      const url = request.nextUrl.clone();
      url.pathname = config.loginPath;
      url.searchParams.set("next", pathname + search);
      return NextResponse.redirect(url);
    }
    return NextResponse.next();
  };
}
