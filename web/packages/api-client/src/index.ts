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

const ARABIC_PROBLEM_MESSAGES: Record<string, string> = {
  "Pricing.NoBasePrice": "لم يتم تحديد سعر أساسي لهذا الملعب بعد.",
  "Pricing.CurrencyMismatch": "كل قواعد الأسعار يجب أن تكون بنفس العملة.",
  "PriceRule.Overlap": "يوجد تعارض مع قاعدة سعر أخرى نشطة في نفس الوقت والميعاد.",
  "PriceRule.NotFound": "قاعدة السعر غير موجودة.",
  "PriceRule.TooMany": "وصلت للحد الأقصى لقواعد الأسعار.",
  "Venue.NotFound": "المكان غير موجود.",
  "Pitch.NotFound": "الملعب غير موجود.",
};

/** Readable message for a failed call: the API puts its error code in `title` and the reason in `detail`. */
export function problemMessage(error: unknown, fallback = "حصل خطأ، حاول تاني"): string {
  if (error && typeof error === "object") {
    const problem = error as ProblemDetails;
    if (problem.title && ARABIC_PROBLEM_MESSAGES[problem.title]) {
      return ARABIC_PROBLEM_MESSAGES[problem.title]!;
    }
    if (problem.detail && (problem.detail.includes("has no base price") || problem.detail.includes("NoBasePrice"))) {
      return ARABIC_PROBLEM_MESSAGES["Pricing.NoBasePrice"]!;
    }
    const fieldErrors = problem.errors ? Object.values(problem.errors).flat() : [];
    return fieldErrors[0] ?? problem.detail ?? problem.title ?? fallback;
  }
  return fallback;
}
