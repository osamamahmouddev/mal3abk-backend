import createClient, { type Client, type ClientOptions } from "openapi-fetch";
import type { components, paths } from "./schema";

export type { components, paths };
export type Schemas = components["schemas"];
export type ApiClient = Client<paths>;

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
}

export function createApiClient(options: ClientOptions): ApiClient {
  return createClient<paths>(options);
}

/** Readable message for a failed call: the API puts its error code in `title` and the reason in `detail`. */
export function problemMessage(error: unknown, fallback = "حصل خطأ، حاول تاني"): string {
  if (error && typeof error === "object") {
    const problem = error as ProblemDetails;
    const fieldErrors = problem.errors ? Object.values(problem.errors).flat() : [];
    return fieldErrors[0] ?? problem.detail ?? problem.title ?? fallback;
  }
  return fallback;
}
