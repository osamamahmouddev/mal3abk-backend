"use client";

import { problemMessage, type Schemas } from "@malabak/api-client";
import { api, unwrap } from "./api";

/** Two-step upload: get a single-use upload ticket, then PUT the image bytes to it. Returns the media id. */
export async function uploadImage(file: File, purpose: Schemas["MediaPurpose"]): Promise<string> {
  const ticket = await unwrap(api.POST("/api/v1/media/uploads", { body: { purpose } }));
  if (file.size > ticket.maxBytes) throw new Error("الصورة أكبر من ٨ ميجا.");

  const csrf = document.cookie
    .split("; ")
    .find((c) => c.startsWith("malabak-csrf="))
    ?.slice("malabak-csrf=".length);
  const response = await fetch(`/venue/bff${ticket.uploadUrl}`, {
    method: "PUT",
    headers: { "content-type": file.type || "application/octet-stream", "x-csrf-token": decodeURIComponent(csrf ?? "") },
    body: file,
  });
  if (!response.ok) throw new Error(problemMessage(await response.json().catch(() => null), "الصورة مترفعتش. جرّب صورة JPG أو PNG."));
  return ticket.mediaId;
}

/** Media paths from the API ("/api/v1/media/...") load through the dashboard's proxy. */
export const mediaSrc = (path: string) => `/venue/bff${path}`;
