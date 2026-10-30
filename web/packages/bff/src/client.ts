import { createApiClient, type ApiClient } from "@malabak/api-client";
import { CSRF_COOKIE, CSRF_HEADER } from "./config";

function readCookie(name: string): string | undefined {
  return document.cookie
    .split("; ")
    .find((part) => part.startsWith(`${name}=`))
    ?.slice(name.length + 1);
}

/**
 * Typed API client for the browser. Calls go to the app's own `/bff` proxy (never straight to the
 * API), carry the CSRF token on writes, and send the user to sign in when the session has ended.
 */
export function createBrowserApi(loginPath: string): ApiClient {
  const client = createApiClient({ baseUrl: "/bff" });
  client.use({
    onRequest({ request }) {
      if (request.method !== "GET" && request.method !== "HEAD") {
        const token = readCookie(CSRF_COOKIE);
        if (token) request.headers.set(CSRF_HEADER, decodeURIComponent(token));
      }
      return request;
    },
    onResponse({ response }) {
      if (response.status === 401 && typeof window !== "undefined" && !window.location.pathname.startsWith(loginPath)) {
        const next = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.assign(`${loginPath}?next=${next}`);
      }
      return response;
    },
  });
  return client;
}
