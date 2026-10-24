import arMessages from "./messages/ar.json";
import enMessages from "./messages/en.json";

export const locales = ["ar", "en"] as const;
export type Locale = (typeof locales)[number];
export const defaultLocale: Locale = "ar";

export const messages = {
  ar: arMessages,
  en: enMessages,
} as const;
