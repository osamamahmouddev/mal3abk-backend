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

/** Photos uploaded through the API are stored as "/api/v1/media/{id}/{variant}"; a photo can also be a full https:// link (demo data). */
export const isManagedMedia = (photo: string) => photo.startsWith("/api/v1/media/");

/** The id of an uploaded photo, for the gallery endpoints; null for a link to an image hosted elsewhere. */
export const mediaIdOf = (photo: string): string | null => (isManagedMedia(photo) ? (photo.split("/")[4] ?? null) : null);

/** Uploaded photos load through the dashboard's proxy; a full link is used as it is (it is not ours to proxy). */
export const mediaSrc = (path: string) => (/^https?:\/\//i.test(path) ? path : `/venue/bff${path}`);
