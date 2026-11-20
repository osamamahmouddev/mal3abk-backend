import type { Schemas } from "@malabak/api-client";
import type { Tone } from "@/components/ui";

export type BookingStatus = Schemas["BookingStatus"];

/** Booking statuses as the API sends them (numbers), with the label and badge colour the dashboard shows. */
export const BOOKING_STATUS: Record<BookingStatus, { key: string; label: string; tone: Tone }> = {
  1: { key: "Draft", label: "مسودة", tone: "wait" },
  2: { key: "PendingDeposit", label: "بانتظار العربون", tone: "wait" },
  3: { key: "ProofSubmitted", label: "إيصال مرفوع", tone: "warn" },
  4: { key: "UnderReview", label: "قيد المراجعة", tone: "warn" },
  5: { key: "Confirmed", label: "مؤكد", tone: "ok" },
  6: { key: "Rejected", label: "مرفوض", tone: "bad" },
  7: { key: "Expired", label: "انتهت المهلة", tone: "block" },
  8: { key: "Cancelled", label: "ملغي", tone: "block" },
  9: { key: "Completed", label: "مكتمل", tone: "ok" },
  10: { key: "NoShow", label: "لم يحضر", tone: "bad" },
};

/**
 * The label a booking shows. A manual booking the venue marked as taken without asking for a deposit is "tentative" (not
 * confirmed yet), not "waiting for the deposit".
 */
export const bookingStatusOf = (b: { status: BookingStatus; isManual?: boolean | null; depositAmount?: number | null }) =>
  b.status === 2 && b.isManual && !b.depositAmount ? { key: "Tentative", label: "محجوز مبدئي", tone: "wait" as Tone } : BOOKING_STATUS[b.status];

export const statusByKey = (key: string) =>
  (Object.entries(BOOKING_STATUS).find(([, v]) => v.key === key)?.[0] ?? null) as unknown as BookingStatus | null;

export const DEPOSIT_STATE: Record<string, string> = {
  Approved: "عربون معتمد",
  Received: "عربون معتمد",
  UnderReview: "إيصال مرفوع",
  AwaitingUpload: "لم يُرفع بعد",
  ManualAwaitingDeposit: "بانتظار العربون",
  PayAtVenue: "الدفع في الملعب",
  Cancelled: "ملغي",
  None: "—",
};

const CAIRO = "Africa/Cairo";
const digits = (s: string) => s.replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

function cairoPeriod(hour24: number): string {
  if (hour24 < 5 || hour24 >= 17) return "بليل";
  if (hour24 < 12) return "الصبح";
  return "الضهر";
}

/** "2026-11-05T17:00:00Z" → "٧:٠٠ بليل" in Cairo time. */
export function cairoTime(utc: string): string {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-US", { timeZone: CAIRO, hourCycle: "h23", hour: "2-digit", minute: "2-digit" })
      .formatToParts(new Date(utc))
      .map((p) => [p.type, p.value]),
  );
  const hour24 = Number(parts.hour);
  const hour12 = hour24 % 12 || 12;
  return `${digits(`${hour12}:${parts.minute}`)} ${cairoPeriod(hour24)}`;
}

/** A clear label for calendar rows, including late-night hours such as "١ بليل". */
export function cairoHourLabel(hour24: number): string {
  const normalized = ((hour24 % 24) + 24) % 24;
  return `${digits(String(normalized % 12 || 12))} ${cairoPeriod(normalized)}`;
}

/** "الخميس ٥ نوفمبر" in Cairo time. */
export function cairoDay(utc: string): string {
  return new Date(utc).toLocaleDateString("ar-EG", { timeZone: CAIRO, weekday: "long", day: "numeric", month: "long" });
}

/** "٥ نوفمبر، ٧:٠٠ بليل" in Cairo time. */
export function cairoDateTime(utc: string): string {
  const date = new Date(utc).toLocaleDateString("ar-EG", { timeZone: CAIRO, day: "numeric", month: "short" });
  return `${date}، ${cairoTime(utc)}`;
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

/** Cairo's offset from UTC in minutes at a given instant (+120 in winter, +180 in summer), independent of the browser's zone. */
function cairoOffsetMinutes(at: Date): number {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-US", { timeZone: CAIRO, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" })
      .formatToParts(at)
      .map((p) => [p.type, p.value]),
  );
  const wallClock = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute));
  return Math.round((wallClock - at.getTime()) / 60000);
}

/** Start of a Cairo business date as an ISO UTC instant (for range queries and slot times). */
export function cairoDayStartUtc(date: string): string {
  // The offset at local noon is the day's offset (Cairo changes clocks at midnight, never mid-day).
  const offset = cairoOffsetMinutes(new Date(`${date}T12:00:00Z`));
  return new Date(new Date(`${date}T00:00:00Z`).getTime() - offset * 60000).toISOString();
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

export type TimePeriod = "night" | "day";

/**
 * Computes minutes from local midnight of the business date.
 * Egyptian venues operate late nights after midnight (12 AM, 1 AM, 2 AM, 3 AM, 4 AM)
 * which calendar-wise belong to the next calendar day, but culturally belong to that night's match session.
 */
export function computeStartMinutes(hour12: number, minute: number, period: TimePeriod): number {
  let businessHour: number;
  if (period === "night") {
    // 12 midnight, 1 AM, 2 AM, 3 AM, 4 AM, 5 AM, 6 AM are late-night hours of this night's session
    if (hour12 === 12) {
      businessHour = 24; // 12:00 منتصف الليل (00:00 اليوم التالي)
    } else if (hour12 >= 1 && hour12 <= 6) {
      businessHour = 24 + hour12; // 1 AM -> 25, 2 AM -> 26, 3 AM -> 27, 4 AM -> 28
    } else {
      // 7 PM, 8 PM, 9 PM, 10 PM, 11 PM
      businessHour = 12 + hour12; // 7 PM -> 19, 8 PM -> 20, ..., 11 PM -> 23
    }
  } else {
    // "الصبح" (صباحاً / نهاراً)
    if (hour12 === 12) {
      businessHour = 12; // 12:00 الظهر
    } else if (hour12 >= 1 && hour12 <= 6) {
      businessHour = 12 + hour12; // 1:00 الظهر -> 13, 2:00 الظهر -> 14, 3:00 العصر -> 15, ..., 6:00 المغرب -> 18
    } else {
      // 7 AM, 8 AM, 9 AM, 10 AM, 11 AM
      businessHour = hour12; // 7 AM -> 7, 8 AM -> 8, ..., 11 AM -> 11
    }
  }
  return businessHour * 60 + minute;
}

/**
 * Parses minutes from the business date start back into 12-hour, minute, and period.
 */
export function parseStartMinutes(totalMinutes: number): { hour12: number; minute: number; period: TimePeriod } {
  const businessHour = Math.floor(totalMinutes / 60);
  const minute = totalMinutes % 60;

  if (businessHour >= 24) {
    const h = businessHour - 24;
    return {
      hour12: h === 0 ? 12 : h,
      minute,
      period: "night",
    };
  }
  if (businessHour >= 19) {
    return {
      hour12: businessHour - 12,
      minute,
      period: "night",
    };
  }
  if (businessHour >= 12) {
    return {
      hour12: businessHour === 12 ? 12 : businessHour - 12,
      minute,
      period: "day",
    };
  }
  if (businessHour >= 7) {
    return {
      hour12: businessHour,
      minute,
      period: "day",
    };
  }
  // If businessHour is 0..6 (00:00 to 06:00): in Egyptian venues, this represents late night ("بليل")
  return {
    hour12: businessHour === 0 ? 12 : businessHour,
    minute,
    period: "night",
  };
}

/**
 * Explains the selected business slot clearly so the user sees both the match session day and the actual calendar instant.
 * Example:
 * - 1 AM on Nov 25 evening: "سهرة الأربعاء ٢٥ نوفمبر — تقويمياً: فجر الخميس ٢٦ نوفمبر (١:٠٠ بليل)"
 * - 8 PM on Nov 25: "الأربعاء ٢٥ نوفمبر، ٨:٠٠ بليل"
 */
export function formatBusinessSlotSummary(date: string, startMinutes: number): string {
  const dayStartMs = new Date(cairoDayStartUtc(date)).getTime();
  const startUtc = new Date(dayStartMs + startMinutes * 60_000).toISOString();
  const baseDay = new Date(`${date}T12:00:00Z`).toLocaleDateString("ar-EG", { timeZone: "UTC", weekday: "long", day: "numeric", month: "long" });

  if (startMinutes >= 1440) {
    const actualNextDay = new Date(startUtc).toLocaleDateString("ar-EG", { timeZone: CAIRO, weekday: "long", day: "numeric", month: "long" });
    const timeStr = cairoTime(startUtc);
    return `سهرة ${baseDay} — (تقويمياً: فجر ${actualNextDay}، ${timeStr})`;
  }

  return `${baseDay}، ${cairoTime(startUtc)}`;
}
