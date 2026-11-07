"use client";

import { createBrowserApi } from "@malabak/bff/client";
import { problemMessage } from "@malabak/api-client";

export const api = createBrowserApi("/login", "/admin");

/** An API failure with its HTTP status, so screens can tell "no permission" (403) from other errors. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
  ) {
    super(message);
  }
}

/** Unwraps an openapi-fetch result, throwing an {@link ApiError} with a readable message on failure (for TanStack Query). */
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) {
    const code = error && typeof error === "object" && "title" in error ? String((error as { title?: unknown }).title) : undefined;
    throw new ApiError(problemMessage(error), response.status, code);
  }
  return data as T;
}

/** Downloads a binary response (e.g. an invoice PDF) through the BFF and opens it. */
export async function openFile(path: string) {
  const response = await fetch(`/admin/bff${path}`, { credentials: "same-origin" });
  if (!response.ok) throw new ApiError("تعذّر تحميل الملف.", response.status);
  const url = URL.createObjectURL(await response.blob());
  window.open(url, "_blank", "noopener");
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
