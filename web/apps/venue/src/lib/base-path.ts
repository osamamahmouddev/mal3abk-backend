/**
 * Where this app is served. "/venue" when it shares one domain with the other apps (path routing),
 * "" when it has its own subdomain (venue.mal3abk.app). Set at build time from BASE_PATH (next.config.mjs).
 */
export const BASE_PATH = process.env.NEXT_PUBLIC_BASE_PATH ?? "";

/** Prefixes an app path with BASE_PATH, leaving paths that already carry it alone. */
export function withBase(path: string): string {
  const p = path.startsWith("/") ? path : `/${path}`;
  if (!BASE_PATH) return p;
  return p === BASE_PATH || p.startsWith(`${BASE_PATH}/`) ? p : `${BASE_PATH}${p}`;
}
