"use client";

import { createBrowserApi } from "@malabak/bff/client";
import { problemMessage } from "@malabak/api-client";
import { BASE_PATH } from "@/lib/base-path";

export const api = createBrowserApi("/login", BASE_PATH, "malabak-venue-csrf");

/** Unwraps an openapi-fetch result, throwing a readable message on failure (for TanStack Query). */
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) throw new Error(problemMessage(error));
  return data as T;
}

/** Downloads a binary response (e.g. an invoice PDF) through the BFF and opens it. */
export async function openFile(path: string) {
  const response = await fetch(`${BASE_PATH}/bff${path}`, { credentials: "same-origin" });
  if (!response.ok) throw new Error("تعذّر تحميل الملف.");
  const url = URL.createObjectURL(await response.blob());
  window.open(url, "_blank", "noopener");
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
