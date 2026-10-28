import arMessages from "./messages/ar.json";
import enMessages from "./messages/en.json";

export const locales = ["ar", "en", "ar-EG", "en-US"] as const;
export type Locale = (typeof locales)[number];
export const defaultLocale: Locale = "ar-EG";

export const messages = {
  ar: arMessages,
  en: enMessages,
  "ar-EG": arMessages,
  "en-US": enMessages,
} as const;

export function getMessages(locale: string) {
  if (locale === "ar" || locale === "ar-EG") {
    return arMessages;
  }
  return enMessages;
}
