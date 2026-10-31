import type { Schemas } from "@malabak/api-client";
import type { Tone } from "@/components/ui";

export type BookingStatus = Schemas["BookingStatus"];

/** Booking statuses as the API sends them (numbers), with the label and badge colour the dashboard shows. */
export const BOOKING_STATUS: Record<BookingStatus, { key: string; label: string; tone: Tone }> = {
  1: { key: "Draft", label: "مسودة", tone: "wait" },
  2: { key: "PendingDeposit", label: "بانتظار العربون", tone: "wait" },
  3: { key: "ProofSubmitted", label: "إيصال مرفوع", tone: "warn" },
  4: { key: "UnderReview", label: "قيد المراجعة", tone: "warn" },
  5: { key: "Confirmed", label: "✓ مؤكد", tone: "ok" },
  6: { key: "Rejected", label: "مرفوض", tone: "bad" },
  7: { key: "Expired", label: "انتهت المهلة", tone: "block" },
  8: { key: "Cancelled", label: "ملغي", tone: "block" },
  9: { key: "Completed", label: "مكتمل", tone: "ok" },
  10: { key: "NoShow", label: "لم يحضر", tone: "bad" },
};

export const statusByKey = (key: string) =>
  (Object.entries(BOOKING_STATUS).find(([, v]) => v.key === key)?.[0] ?? null) as unknown as BookingStatus | null;

export const DEPOSIT_STATE: Record<string, string> = {
  Approved: "عربون معتمد",
  Received: "عربون معتمد",
  UnderReview: "إيصال مرفوع",
  AwaitingUpload: "لم يُرفع بعد",
  PayAtVenue: "الدفع في الملعب",
  Cancelled: "ملغي",
  None: "—",
};

const CAIRO = "Africa/Cairo";
const digits = (s: string) => s.replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

/** "2026-11-05T17:00:00Z" → "٧:٠٠ م" in Cairo time. */
export function cairoTime(utc: string): string {
  return new Date(utc).toLocaleTimeString("ar-EG", { timeZone: CAIRO, hour: "numeric", minute: "2-digit" });
}

/** "الخميس ٥ نوفمبر" in Cairo time. */
export function cairoDay(utc: string): string {
  return new Date(utc).toLocaleDateString("ar-EG", { timeZone: CAIRO, weekday: "long", day: "numeric", month: "long" });
}

/** "٥ نوفمبر، ٧:٠٠ م" in Cairo time. */
export function cairoDateTime(utc: string): string {
  return new Date(utc).toLocaleString("ar-EG", { timeZone: CAIRO, day: "numeric", month: "short", hour: "numeric", minute: "2-digit" });
}

/** Today's date in Cairo as yyyy-MM-dd (the API's DateOnly format). */
export function cairoToday(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: CAIRO, year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
}

/** Adds days to a yyyy-MM-dd date. */
export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Start of a Cairo business date as an ISO UTC instant (for range queries). */
export function cairoDayStartUtc(date: string): string {
  // Cairo is UTC+2 or UTC+3; find the offset at local noon and apply it to midnight.
  const noon = new Date(`${date}T12:00:00Z`);
  const local = new Date(noon.toLocaleString("en-US", { timeZone: CAIRO }));
  const offsetMinutes = Math.round((local.getTime() - noon.getTime()) / 60000);
  return new Date(new Date(`${date}T00:00:00Z`).getTime() - offsetMinutes * 60000).toISOString();
}

/** Money in Arabic digits: 1650 → "١٬٦٥٠ ج.م". */
export function money(amount: number, currency = "EGP"): string {
  const value = digits(Math.round(amount).toLocaleString("en-US")).replace(/,/g, "٬");
  return currency === "EGP" ? `${value} ج.م` : `${value} ${currency}`;
}

/** Minutes until a moment, in words: "١ س ٤٨ د" or "انتهت". */
export function timeLeft(utc: string | null | undefined, now = Date.now()): string {
  if (!utc) return "";
  const minutes = Math.floor((new Date(utc).getTime() - now) / 60000);
  if (minutes <= 0) return "انتهت المهلة";
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return digits(h > 0 ? `${h} س ${m} د` : `${m} د`);
}

export const percent = (value: number) => `${digits(String(Math.round(value)))}٪`;
