"use client";

import { createBrowserApi } from "@malabak/bff/client";
import { problemMessage } from "@malabak/api-client";

export const api = createBrowserApi("/login", "/venue");

/** Unwraps an openapi-fetch result, throwing a readable message on failure (for TanStack Query). */
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) throw new Error(problemMessage(error));
  return data as T;
}
