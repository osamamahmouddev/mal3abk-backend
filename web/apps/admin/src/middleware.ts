import { createAuthMiddleware } from "@malabak/bff/middleware";
import { bffConfig } from "@/lib/bff-config";

export const middleware = createAuthMiddleware(bffConfig, ["/login", "/bff"]);

export const config = {
  matcher: ["/((?!_next/|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|webp|ico)$).*)"],
};
