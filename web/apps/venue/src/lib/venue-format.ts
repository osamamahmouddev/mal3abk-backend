/** .NET DayOfWeek numbers (Sunday = 0), shown starting from Saturday as the Egyptian week does. */
export const DAY_ORDER = [6, 0, 1, 2, 3, 4, 5] as const;

export const DAY_NAMES: Record<number, string> = {
  0: "الأحد",
  1: "الإثنين",
  2: "الثلاثاء",
  3: "الأربعاء",
  4: "الخميس",
  5: "الجمعة",
  6: "السبت",
};

export interface DayHours {
  on: boolean;
  open: string;
  close: string;
  slot: number;
}

export const DEFAULT_WEEK: Record<number, DayHours> = Object.fromEntries(
  DAY_ORDER.map((d) => [d, { on: true, open: d === 5 ? "14:00" : "16:00", close: "02:00", slot: 60 }]),
) as Record<number, DayHours>;

/** Players per team a pitch can be set up for. */
export const CAPACITIES = [5, 6, 7, 8, 9, 11] as const;

export const METHOD_LABELS: Record<string, string> = {
  MobileWallet: "محفظة موبايل (فودافون كاش…)",
  InstaPay: "إنستاباي",
  BankTransfer: "تحويل بنكي",
};

/** "16:00" → "16:00:00" (the API's TimeOnly format). */
export const toApiTime = (hhmm: string) => (hhmm.length === 5 ? `${hhmm}:00` : hhmm);

/** "16:00:00" → "٤:٠٠ م" */
export function formatTime(value: string): string {
  const [h, m] = value.split(":").map(Number);
  const suffix = h < 12 ? "ص" : "م";
  const hour = h % 12 === 0 ? 12 : h % 12;
  return `${hour}:${String(m).padStart(2, "0")} ${suffix}`.replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);
}

export const APPROVAL_LABELS: Record<string, { label: string; tone: string }> = {
  Draft: { label: "مسودة", tone: "bg-[#EEF0F5] text-[#3B4352]" },
  Submitted: { label: "تحت المراجعة", tone: "bg-[#FEF3D6] text-[#7A4D00]" },
  NeedsInfo: { label: "مطلوب بيانات", tone: "bg-[#FEF3D6] text-[#7A4D00]" },
  Approved: { label: "معتمد من ملعبك", tone: "bg-[#E5F5EB] text-[#1B6E3E]" },
  Rejected: { label: "مرفوض", tone: "bg-[#FDECEA] text-[#A8261C]" },
  Suspended: { label: "موقوف", tone: "bg-[#FDECEA] text-[#A8261C]" },
};
