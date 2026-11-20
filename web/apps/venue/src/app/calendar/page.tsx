"use client";

import React, { Suspense, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Toast, Tone, card, inputClass, muted, redShadow, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { ManualBookingForm, type ManualBookingDefaults } from "@/components/ManualBookingForm";
import { api, unwrap } from "@/lib/api";
import { addDays, cairoDayStartUtc, cairoHourLabel, cairoTime, cairoToday } from "@/lib/booking-format";
import { P } from "@/lib/permissions";
import { ChevronLeft, ChevronRight, Plus, X } from "lucide-react";

type View = "day" | "week" | "month";
type Slot = Schemas["CalendarSlotDto"];
type Kind = "ok" | "tent" | "rev" | "wait" | "blk";

const KIND: Record<Kind, { label: string; tone: Tone; box: string }> = {
  ok: { label: "مؤكد", tone: "ok", box: "bg-navy-900 text-white border border-navy-900" },
  tent: { label: "محجوز مبدئي", tone: "wait", box: "bg-[#EEF1F7] text-navy-900 border-2 border-dashed border-navy-900" },
  rev: { label: "قيد المراجعة", tone: "warn", box: "bg-[#FEF3D6] text-[#6B4400] border-2 border-[#D9A21B]" },
  wait: { label: "بانتظار العربون", tone: "wait", box: "bg-white text-slate-900 border-2 border-dashed border-[#8A94AA]" },
  blk: { label: "محظور", tone: "block", box: "bg-[repeating-linear-gradient(135deg,#DCE0E8_0_5px,#EEF0F5_5px_10px)] text-[#3B4352] border border-[#C9CFD9]" },
};

/** A manual booking the venue marked as taken but has not confirmed yet is "tentative", not "waiting for a deposit". */
const kindOf = ({ status, isTentative }: Pick<Slot, "status" | "isTentative">): Kind =>
  status === "Blocked"
    ? "blk"
    : isTentative
      ? "tent"
      : status === "PendingDeposit"
        ? "wait"
        : status === "ProofSubmitted" || status === "UnderReview"
          ? "rev"
          : "ok";

/** Grid hours: 08:00 to 02:00 the next morning (most venues play late). */
const FIRST_HOUR = 8;
const LAST_HOUR = 26;
const ROW = 52;
const DAY_SHORT = ["أحد", "إثنين", "ثلاثاء", "أربعاء", "خميس", "جمعة", "سبت"];

const hourLabel = cairoHourLabel;
const weekStart = (date: string) => addDays(date, -((new Date(`${date}T12:00:00Z`).getUTCDay() + 1) % 7));
const monthStart = (date: string) => `${date.slice(0, 7)}-01`;
const monthEnd = (date: string) => {
  const d = new Date(`${monthStart(date)}T12:00:00Z`);
  d.setUTCMonth(d.getUTCMonth() + 1, 0);
  return d.toISOString().slice(0, 10);
};
const dayLabel = (date: string) => new Date(`${date}T12:00:00Z`).toLocaleDateString("ar-EG", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" });

/** Minutes from the business date's local midnight. */
const minutesFrom = (date: string, utc: string) => (new Date(utc).getTime() - new Date(cairoDayStartUtc(date)).getTime()) / 60_000;

function autoHours(slots: Slot[]) {
  if (slots.length === 0) return { firstHour: FIRST_HOUR, lastHour: LAST_HOUR };

  const ranges = slots.map((slot) => {
    const localDate = new Date(slot.startUtc).toLocaleDateString("en-CA", { timeZone: "Africa/Cairo" });
    const startHour = minutesFrom(localDate, slot.startUtc) / 60;
    const duration = (new Date(slot.endUtc).getTime() - new Date(slot.startUtc).getTime()) / 3_600_000;
    return { startHour, endHour: startHour + duration };
  });
  let firstHour = Math.max(0, Math.floor(Math.min(...ranges.map((r) => r.startHour))) - 1);
  let lastHour = Math.min(LAST_HOUR, Math.ceil(Math.max(...ranges.map((r) => r.endHour))) + 1);

  // Keep a useful amount of empty space for creating the next booking.
  if (lastHour - firstHour < 6) {
    lastHour = Math.min(LAST_HOUR, firstHour + 6);
    firstHour = Math.max(0, lastHour - 6);
  }

  return { firstHour, lastHour };
}

export default function CalendarPage() {
  return (
    <Suspense>
      <Calendar />
    </Suspense>
  );
}

function Calendar() {
  const router = useRouter();
  const params = useSearchParams();
  const { venue, venueId, can } = useVenue();
  const [view, setView] = useState<View>(() => (["day", "week", "month"].includes(params.get("view") ?? "") ? (params.get("view") as View) : "week"));
  const [date, setDate] = useState(() => params.get("date") ?? cairoToday());
  const pitches = useMemo(() => venue?.pitches.filter((p) => p.isActive) ?? [], [venue]);
  const [weekPitch, setWeekPitch] = useState<string>("");
  const [selected, setSelected] = useState<Slot | null>(null);
  const [empty, setEmpty] = useState<ManualBookingDefaults | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  useEffect(() => {
    if (!weekPitch && pitches[0]) setWeekPitch(pitches[0].id);
  }, [pitches, weekPitch]);

  const switchTo = (v: View, d = date) => {
    setView(v);
    setDate(d);
    router.replace(`/calendar?view=${v}&date=${d}`);
  };

  const range = useMemo(() => {
    if (view === "day") return { from: date, days: 2 };
    if (view === "week") return { from: weekStart(date), days: 8 };
    const first = monthStart(date);
    return { from: first, days: Number(monthEnd(date).slice(8)) + 1 };
  }, [view, date]);

  const slots = useQuery({
    queryKey: ["calendar", venueId, range.from, range.days, view === "week" ? weekPitch : ""],
    enabled: !!venueId,
    refetchInterval: 60_000,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/venue-manager/venues/{venueId}/calendar", {
          params: {
            path: { venueId: venueId! },
            query: {
              rangeStartUtc: cairoDayStartUtc(range.from),
              rangeEndUtc: cairoDayStartUtc(addDays(range.from, range.days)),
              pitchId: view === "week" && weekPitch ? weekPitch : undefined,
            },
          },
        }),
      ),
  });

  const all = slots.data ?? [];
  const visibleHours = useMemo(() => autoHours(all), [all]);
  // Existing bookings must remain visible even if their pitch was later paused.
  // The active-pitches list is still used for creating new bookings and the week selector.
  const dayPitches = useMemo(() => venue?.pitches ?? [], [venue]);
  const pitchName = (id: string) => pitches.find((p) => p.id === id)?.name ?? "ملعب";
  const step = view === "day" ? 1 : view === "week" ? 7 : 30;
  const title =
    view === "day"
      ? dayLabel(date)
      : view === "week"
        ? `${dayLabel(weekStart(date))} – ${dayLabel(addDays(weekStart(date), 6))}`
        : new Date(`${date}T12:00:00Z`).toLocaleDateString("ar-EG", { month: "long", year: "numeric", timeZone: "UTC" });

  return (
    <Page className="gap-4 lg:!px-7 lg:!pt-6 !pb-8">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-[24px] font-bold flex-[1_1_200px]">التقويم</h1>
        <ViewSwitch view={view} onChange={(v) => switchTo(v)} />
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            aria-label="السابق"
            onClick={() => switchTo(view, view === "month" ? addDays(monthStart(date), -1) : addDays(date, -step))}
            className="w-10 h-10 rounded-[10px] border border-[#EDF0F5] bg-white hover:bg-slate-50 hover:border-slate-300 active:scale-95 transition-all cursor-pointer flex items-center justify-center"
          >
            <ChevronRight className="size-5" />
          </button>
          <span className="min-w-[180px] text-center text-sm font-bold" aria-live="polite">
            {title}
          </span>
          <button
            type="button"
            aria-label="التالي"
            onClick={() => switchTo(view, view === "month" ? addDays(monthEnd(date), 1) : addDays(date, step))}
            className="w-10 h-10 rounded-[10px] border border-[#EDF0F5] bg-white hover:bg-slate-50 hover:border-slate-300 active:scale-95 transition-all cursor-pointer flex items-center justify-center"
          >
            <ChevronLeft className="size-5" />
          </button>
          <button
            type="button"
            onClick={() => switchTo(view, cairoToday())}
            className="min-h-[40px] px-3 rounded-[10px] border border-[#EDF0F5] bg-white hover:bg-slate-50 hover:border-slate-300 active:scale-95 transition-all text-[13px] font-semibold cursor-pointer"
          >
            النهارده
          </button>
        </div>
        {view === "week" && (
          <select aria-label="الملعب" value={weekPitch} onChange={(e) => setWeekPitch(e.target.value)} className={`${inputClass} !w-auto min-w-[200px] max-w-[280px] min-h-[40px] cursor-pointer`}>
            {pitches.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        )}
        {can(P.bookingsApprove) && (
          <button
            type="button"
            onClick={() => setEmpty({ date, startMinutes: 20 * 60, pitchId: pitches[0]?.id })}
            className={`btn-press min-h-[40px] px-4 rounded-[10px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold inline-flex items-center gap-2 transition-colors ${redShadow}`}
          >
            <Plus className="size-4" strokeWidth={2.5} />
            حجز يدوي
          </button>
        )}
      </div>

      <Legend />
      {slots.isError && (
        <div role="alert" className="bg-[#FDECEA] text-[#A8261C] rounded-[14px] px-4 py-3 text-sm">
          {(slots.error as Error).message}
        </div>
      )}

      {view === "day" && (
        <Grid
          columns={dayPitches.map((p) => ({ key: p.id, label: p.name, date, pitchId: p.id }))}
          slots={all}
          firstHour={visibleHours.firstHour}
          lastHour={visibleHours.lastHour}
          busy={slots.isFetching}
          onSlot={setSelected}
          onEmpty={(c, minutes) => setEmpty({ date: c.date, pitchId: c.pitchId, startMinutes: minutes })}
        />
      )}
      {view === "week" && (
        <Grid
          columns={Array.from({ length: 7 }, (_, i) => {
            const d = addDays(weekStart(date), i);
            return { key: d, label: `${DAY_SHORT[new Date(`${d}T12:00:00Z`).getUTCDay()]} ${toArabicDigits(Number(d.slice(8)))}`, date: d, pitchId: weekPitch };
          })}
          slots={all}
          firstHour={visibleHours.firstHour}
          lastHour={visibleHours.lastHour}
          busy={slots.isFetching}
          onSlot={setSelected}
          onEmpty={(c, minutes) => setEmpty({ date: c.date, pitchId: c.pitchId, startMinutes: minutes })}
        />
      )}
      {view === "month" && <MonthGrid date={date} slots={all} onOpenDay={(d) => switchTo("day", d)} />}

      {selected && venueId && (
        <SlotDrawer
          slot={selected}
          venueId={venueId}
          pitchName={pitchName}
          canUnblock={can(P.blockedSlotsManage)}
          canDecide={can(P.bookingsApprove)}
          onClose={() => setSelected(null)}
          onDone={(m) => {
            setSelected(null);
            setToast(m);
          }}
        />
      )}
      {empty && venueId && (
        <EmptySlotDialog
          venueId={venueId}
          defaults={empty}
          canBook={can(P.bookingsApprove)}
          canBlock={can(P.blockedSlotsManage)}
          onClose={() => setEmpty(null)}
          onDone={(m) => {
            setEmpty(null);
            setToast(m);
          }}
        />
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function ViewSwitch({ view, onChange }: { view: View; onChange: (v: View) => void }) {
  return (
    <div role="group" aria-label="طريقة العرض" className="flex bg-white border border-[#EDF0F5] rounded-xl p-[3px] gap-0.5">
      {(
        [
          ["day", "يوم"],
          ["week", "أسبوع"],
          ["month", "شهر"],
        ] as const
      ).map(([id, label]) => (
        <button
          key={id}
          type="button"
          aria-pressed={view === id}
          onClick={() => onChange(id)}
          className={`min-h-[36px] px-3.5 rounded-[9px] text-[13px] transition-all duration-150 ease-spring ${view === id ? "bg-navy-900 text-white font-bold shadow-sm" : "text-[#3B4352] hover:bg-slate-100"}`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}

function Legend() {
  return (
    <div className="flex flex-wrap gap-3 text-xs" aria-label="دليل الألوان">
      {(Object.keys(KIND) as Kind[]).map((k) => (
        <span key={k} className="flex items-center gap-1.5">
          <span className={`w-5 h-3.5 rounded ${KIND[k].box}`} aria-hidden="true" />
          {KIND[k].label}
        </span>
      ))}
    </div>
  );
}

interface Column {
  key: string;
  label: string;
  date: string;
  pitchId: string;
}

/** Time grid: one column per pitch (day view) or per day (week view); bookings are placed by their real start and length. */
function Grid({
  columns,
  slots,
  firstHour,
  lastHour,
  busy,
  onSlot,
  onEmpty,
}: {
  columns: Column[];
  slots: Slot[];
  firstHour: number;
  lastHour: number;
  busy: boolean;
  onSlot: (s: Slot) => void;
  onEmpty: (c: Column, minutes: number) => void;
}) {
  if (columns.length === 0) {
    return <p className={`${card} rounded-[18px] p-8 text-center text-sm ${muted}`}>ضيف ملعب نشط عشان يظهر التقويم.</p>;
  }
  const hours = Array.from({ length: lastHour - firstHour }, (_, i) => firstHour + i);
  const gridHeight = hours.length * ROW;
  return (
    <section aria-label="جدول المواعيد" aria-busy={busy} className={`${card} rounded-[18px] overflow-x-auto`} tabIndex={0}>
      <div className="grid min-w-[640px]" style={{ gridTemplateColumns: `64px repeat(${columns.length}, minmax(120px, 1fr))` }}>
        <div className="sticky top-0 bg-white border-b border-[#EDF0F5]" />
        {columns.map((c) => (
          <div key={c.key} className="sticky top-0 bg-white border-b border-[#EDF0F5] px-2 py-2.5 text-center text-[13px] font-bold">
            {c.label}
          </div>
        ))}
        <div className="relative" style={{ height: gridHeight }}>
          {hours.map((h, i) => (
            <span key={h} className={`absolute right-2 text-[11px] ${muted}`} style={{ top: i * ROW + 4 }}>
              {hourLabel(h)}
            </span>
          ))}
        </div>
        {columns.map((c) => {
          const mine = slots.filter((s) => s.pitchId === c.pitchId).filter((s) => {
            const start = minutesFrom(c.date, s.startUtc);
            return start >= firstHour * 60 - 60 && start < lastHour * 60;
          });
          return (
            <div key={c.key} className="relative border-r border-[#EDF0F5]" style={{ height: gridHeight }}>
              {hours.map((h, i) => (
                <button
                  key={h}
                  type="button"
                  aria-label={`${c.label} ${hourLabel(h)}: ميعاد فاضي`}
                  onClick={() => onEmpty(c, h * 60)}
                  className="absolute inset-x-0 border-t border-[#F1F3F7] hover:bg-[#F8F9FB] focus:bg-[#EEF1F7] focus:outline-none"
                  style={{ top: i * ROW, height: ROW }}
                />
              ))}
              {mine.map((s) => {
                const start = Math.max(minutesFrom(c.date, s.startUtc), firstHour * 60);
                const end = Math.min(minutesFrom(c.date, s.endUtc), lastHour * 60);
                const kind = kindOf(s);
                return (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => onSlot(s)}
                    aria-label={`${KIND[kind].label}: ${s.customerName ?? s.reason ?? ""} ${cairoTime(s.startUtc)}`}
                    className={`absolute inset-x-1 rounded-lg px-2 py-1 text-right text-xs overflow-hidden ${KIND[kind].box}`}
                    style={{ top: ((start - firstHour * 60) / 60) * ROW + 2, height: Math.max(22, ((end - start) / 60) * ROW - 4) }}
                  >
                    <span className="block font-bold truncate">{kind === "blk" ? s.reason || "محظور" : s.customerName || (s.isManual ? "حجز يدوي" : "—")}</span>
                    <span className="block opacity-80">
                      {cairoTime(s.startUtc)} – {cairoTime(s.endUtc)}
                    </span>
                  </button>
                );
              })}
            </div>
          );
        })}
      </div>
    </section>
  );
}

function MonthGrid({ date, slots, onOpenDay }: { date: string; slots: Slot[]; onOpenDay: (d: string) => void }) {
  const first = monthStart(date);
  const lead = (new Date(`${first}T12:00:00Z`).getUTCDay() + 1) % 7;
  const days = Number(monthEnd(date).slice(8));
  const today = cairoToday();
  const byDay = (d: string) => slots.filter((s) => s.status !== "Blocked" && new Date(s.startUtc).toLocaleDateString("en-CA", { timeZone: "Africa/Cairo" }) === d);

  return (
    <section aria-label="أيام الشهر" className={`${card} rounded-[18px] p-3.5 flex flex-col gap-2`}>
      <div className={`grid grid-cols-7 gap-1.5 text-center text-xs ${muted}`} aria-hidden="true">
        {[6, 0, 1, 2, 3, 4, 5].map((d) => (
          <span key={d}>{DAY_SHORT[d]}</span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-1.5">
        {Array.from({ length: lead }, (_, i) => (
          <span key={`lead-${i}`} />
        ))}
        {Array.from({ length: days }, (_, i) => {
          const d = addDays(first, i);
          const list = byDay(d);
          const review = list.filter((s) => kindOf(s) === "rev").length;
          return (
            <button
              key={d}
              type="button"
              onClick={() => onOpenDay(d)}
              aria-label={`${dayLabel(d)}: ${list.length} حجز${review ? `، ${review} للمراجعة` : ""}`}
              className={`min-h-[78px] rounded-xl border p-2 flex flex-col items-start gap-1 text-right hover:bg-[#F8F9FB] ${d === today ? "border-brand-red" : "border-[#EDF0F5]"}`}
            >
              <span className="text-sm font-bold">{toArabicDigits(i + 1)}</span>
              {list.length > 0 && <span className="text-xs">{toArabicDigits(list.length)} حجز</span>}
              {review > 0 && <Badge tone="warn">{toArabicDigits(review)} مراجعة</Badge>}
            </button>
          );
        })}
      </div>
    </section>
  );
}

function SlotDrawer({
  slot,
  venueId,
  pitchName,
  canUnblock,
  canDecide,
  onClose,
  onDone,
}: {
  slot: Slot;
  venueId: string;
  pitchName: (id: string) => string;
  canUnblock: boolean;
  canDecide: boolean;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const kind = kindOf(slot);
  const unblock = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/blocked-slots/{id}", { params: { path: { venueId, id: slot.id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["calendar"] });
      onDone("اتشال الحظر والميعاد بقى متاح للحجز.");
    },
    onError: (e: Error) => onDone(e.message),
  });
  // A manual booking that is not confirmed yet (tentative, or waiting for its deposit) is settled here by the venue.
  const pendingManual = slot.isManual && slot.status === "PendingDeposit";
  const [cancelling, setCancelling] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const settled = async (message: string) => {
    await queryClient.invalidateQueries({ predicate: (q) => ["calendar", "dashboard", "manual-booking-calendar"].includes(String(q.queryKey[0])) || String(q.queryKey[0]).startsWith("booking") });
    onDone(message);
  };
  const confirm = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/{id}/confirm-deposit", { params: { path: { venueId, id: slot.id } }, body: {} })),
    onSuccess: () => settled("اتثبت الحجز."),
    onError: (e: Error) => onDone(e.message),
  });
  const cancel = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/bookings/{id}/cancel", { params: { path: { venueId, id: slot.id } }, body: { reason: cancelReason.trim() } })),
    onSuccess: () => settled("اتلغى الحجز والميعاد رجع متاح في التطبيق."),
    onError: (e: Error) => onDone(e.message),
  });

  return (
    <Dialog title={kind === "blk" ? "ميعاد محظور" : slot.customerName || (slot.isManual ? "حجز يدوي" : "تفاصيل الحجز")} onClose={onClose}>
      <Badge tone={KIND[kind].tone} className="self-start">
        {KIND[kind].label}
      </Badge>
      <dl className="flex flex-col gap-2 text-sm">
        <div className="flex justify-between">
          <dt className={muted}>الملعب</dt>
          <dd>{pitchName(slot.pitchId)}</dd>
        </div>
        <div className="flex justify-between">
          <dt className={muted}>الميعاد</dt>
          <dd>
            {cairoTime(slot.startUtc)} – {cairoTime(slot.endUtc)}
          </dd>
        </div>
        {slot.bookingReference && (
          <div className="flex justify-between">
            <dt className={muted}>رقم الحجز</dt>
            <dd dir="ltr">{slot.bookingReference}</dd>
          </div>
        )}
        {slot.reason && (
          <div className="flex justify-between">
            <dt className={muted}>السبب</dt>
            <dd>{slot.reason}</dd>
          </div>
        )}
        {kind !== "blk" && (
          <div className="flex justify-between">
            <dt className={muted}>العميل</dt>
            <dd>{slot.customerName || "من غير اسم"}</dd>
          </div>
        )}
        {slot.customerPhone && (
          <div className="flex justify-between">
            <dt className={muted}>الموبايل</dt>
            <dd dir="ltr">
              <a href={`tel:${slot.customerPhone}`} className="underline underline-offset-2">
                {slot.customerPhone}
              </a>
            </dd>
          </div>
        )}
        {slot.isManual && (
          <p className={`text-xs ${muted}`}>
            {kind === "tent"
              ? "حجز يدوي لسه مش متثبت: الميعاد مقفول في التطبيق لحد ما تثبّته أو تلغيه، ولو ماتثبتش بيتفتح وقت الماتش."
              : "حجز يدوي — الدفع في الملعب."}
          </p>
        )}
      </dl>
      <div className="flex flex-wrap gap-2">
        {kind === "rev" && (
          <p className={`basis-full text-xs ${muted}`}>إيصال العربون عند فريق ملعبك للمراجعة، والحجز هيتأكد أول ما يتراجع.</p>
        )}
        {kind !== "blk" && (
          <Link href="/reservations" className="min-h-[44px] px-4 rounded-xl border border-[#C9CFD9] text-navy-900 text-sm font-semibold flex items-center">
            فتح في الحجوزات
          </Link>
        )}
        {pendingManual && canDecide && !cancelling && (
          <>
            <button
              type="button"
              onClick={() => confirm.mutate()}
              disabled={confirm.isPending}
              className="min-h-[44px] px-4 rounded-xl bg-navy-900 text-white text-sm font-bold"
            >
              {confirm.isPending ? "جاري…" : "ثبّت الحجز"}
            </button>
            <button
              type="button"
              onClick={() => setCancelling(true)}
              className="min-h-[44px] px-4 rounded-xl border border-[#F3C4BF] text-[#A8261C] text-sm font-bold"
            >
              إلغاء الحجز
            </button>
          </>
        )}
        {pendingManual && canDecide && cancelling && (
          <form
            className="basis-full flex flex-col gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              if (cancelReason.trim()) cancel.mutate();
            }}
          >
            <label className="flex flex-col gap-1 text-sm font-semibold">
              سبب الإلغاء
              <input value={cancelReason} maxLength={500} onChange={(e) => setCancelReason(e.target.value)} className={inputClass} placeholder="مثلاً العميل اعتذر" />
            </label>
            <div className="flex gap-2">
              <button type="submit" disabled={!cancelReason.trim() || cancel.isPending} className="min-h-[44px] px-4 rounded-xl bg-[#A8261C] text-white text-sm font-bold disabled:opacity-50">
                {cancel.isPending ? "جاري…" : "تأكيد الإلغاء"}
              </button>
              <button type="button" onClick={() => setCancelling(false)} className="min-h-[44px] px-4 rounded-xl border border-[#C9CFD9] text-sm font-semibold">
                رجوع
              </button>
            </div>
          </form>
        )}
        {kind === "blk" && canUnblock && (
          <button type="button" onClick={() => unblock.mutate()} disabled={unblock.isPending} className="min-h-[44px] px-4 rounded-xl border border-[#F3C4BF] text-[#A8261C] text-sm font-bold">
            {unblock.isPending ? "جاري…" : "إلغاء الحظر"}
          </button>
        )}
      </div>
    </Dialog>
  );
}

function EmptySlotDialog({
  venueId,
  defaults,
  canBook,
  canBlock,
  onClose,
  onDone,
}: {
  venueId: string;
  defaults: ManualBookingDefaults;
  canBook: boolean;
  canBlock: boolean;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<"book" | "block">(canBook ? "book" : "block");
  const [reason, setReason] = useState("");
  const [hours, setHours] = useState(1);
  const startUtc = new Date(new Date(cairoDayStartUtc(defaults.date ?? cairoToday())).getTime() + (defaults.startMinutes ?? 0) * 60_000).toISOString();

  const block = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/blocked-slots", {
          params: { path: { venueId } },
          body: { pitchId: defaults.pitchId!, startUtc, endUtc: new Date(new Date(startUtc).getTime() + hours * 3_600_000).toISOString(), reason: reason.trim() },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["calendar"] });
      onDone("اتحظر الميعاد ومش هيظهر للاعبين.");
    },
    onError: (e: Error) => onDone(e.message),
  });

  if (!canBook && !canBlock) {
    return (
      <Dialog title="ميعاد فاضي" onClose={onClose}>
        <p className={`text-sm ${muted}`}>دورك مايسمحش بالحجز اليدوي أو الحظر.</p>
      </Dialog>
    );
  }

  return (
    <Dialog title={`ميعاد فاضي — ${cairoTime(startUtc)}`} onClose={onClose} wide>
      {canBook && canBlock && (
        <div role="tablist" className="flex gap-1.5">
          {(
            [
              ["book", "حجز يدوي"],
              ["block", "حظر الميعاد"],
            ] as const
          ).map(([id, label]) => (
            <button
              key={id}
              type="button"
              role="tab"
              aria-selected={mode === id}
              onClick={() => setMode(id)}
              className={`min-h-[40px] px-4 rounded-xl text-sm ${mode === id ? "bg-navy-900 text-white font-bold" : "bg-[#F4F5F7]"}`}
            >
              {label}
            </button>
          ))}
        </div>
      )}
      {mode === "book" ? (
        <ManualBookingForm defaults={defaults} onDone={onDone} onCancel={onClose} />
      ) : (
        <div className="flex flex-col gap-3">
          <label className="flex flex-col gap-1 text-sm font-semibold">
            السبب (بيظهر للموظفين بس)
            <input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} placeholder="صيانة النجيلة" className={inputClass} />
          </label>
          <label className="flex flex-col gap-1 text-sm font-semibold">
            المدة بالساعات
            <input type="number" min={1} max={24} value={hours} onChange={(e) => setHours(Math.min(24, Math.max(1, Number(e.target.value) || 1)))} className={inputClass} />
          </label>
          <button
            type="button"
            disabled={!reason.trim() || block.isPending || !defaults.pitchId}
            onClick={() => block.mutate()}
            className="min-h-[46px] rounded-xl bg-navy-900 text-white text-sm font-bold disabled:opacity-50"
          >
            {block.isPending ? "جاري…" : "حظر الميعاد"}
          </button>
        </div>
      )}
    </Dialog>
  );
}

function Dialog({ title, onClose, wide = false, children }: { title: string; onClose: () => void; wide?: boolean; children: React.ReactNode }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div
      className="fixed inset-0 bg-[rgba(15,27,61,0.55)] backdrop-blur-xs animate-fade-in flex items-center justify-center p-4 z-50 cursor-pointer"
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="dialog-title"
        className={`w-full ${wide ? "max-w-[640px]" : "max-w-[440px]"} max-h-[90vh] overflow-y-auto bg-white rounded-[20px] p-6 flex flex-col gap-3 animate-modal-in shadow-2xl cursor-default`}
      >
        <div className="flex justify-between items-center gap-2">
          <h2 id="dialog-title" className="text-[19px] font-bold">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="إغلاق"
            className="min-w-[36px] min-h-[36px] rounded-[10px] bg-surface hover:bg-slate-200 active:scale-95 transition-all cursor-pointer flex items-center justify-center"
          >
            <X className="size-4" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}
