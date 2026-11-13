import { createApiClient, type ApiClient } from "@malabak/api-client";
import { CSRF_COOKIE, CSRF_HEADER } from "./config";

function readCookie(name: string): string | undefined {
  if (typeof document === "undefined") return undefined;
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${name}=([^;]*)`));
  return match ? match[1] : undefined;
}

/**
 * Typed API client for the browser. Calls go to the app's own `/bff` proxy (never straight to the
 * API), carry the CSRF token on writes, and send the user to sign in when the session has ended.
 */
export function createBrowserApi(loginPath: string, basePath = ""): ApiClient {
  const normalizedBase = basePath ? `/${basePath.replace(/^\/+|\/+$/g, "")}` : "";
  const client = createApiClient({ baseUrl: `${normalizedBase}/bff` });
  const appCsrfName = normalizedBase ? `malabak-${normalizedBase.slice(1)}-csrf` : CSRF_COOKIE;
  client.use({
    onRequest({ request }) {
      if (request.method !== "GET" && request.method !== "HEAD") {
        const token = readCookie(appCsrfName) ?? readCookie(CSRF_COOKIE);
        if (token) request.headers.set(CSRF_HEADER, decodeURIComponent(token));
      }
      return request;
    },
    onResponse({ response }) {
      const fullLoginPath = `${normalizedBase}${loginPath.startsWith("/") ? loginPath : `/${loginPath}`}`;
      if (response.status === 401 && typeof window !== "undefined" && !window.location.pathname.startsWith(fullLoginPath)) {
        const next = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.assign(`${fullLoginPath}?next=${next}`);
      }
      return response;
    },
  });
  return client;
}
