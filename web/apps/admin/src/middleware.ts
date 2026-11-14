import { createAuthMiddleware } from "@malabak/bff/middleware";
import { bffConfig } from "@/lib/bff-config";

/** Pages reachable without signing in. Everything else under /admin needs a session. */
const PUBLIC_PATHS = ["/login", "/bff"];

export const middleware = createAuthMiddleware(bffConfig, PUBLIC_PATHS);

export const config = {
  // Skip Next.js internals and static files; the BFF proxy answers 401 itself.
  matcher: ["/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|webp|ico)$).*)"],
};
