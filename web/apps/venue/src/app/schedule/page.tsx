"use client";

import React, { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { problemMessage, type Schemas } from "@malabak/api-client";
import { Badge, Page, Switch, Toast, card, chip, inputClass, muted, redShadow, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { DAY_NAMES, DAY_ORDER, DEFAULT_WEEK, formatTime, toApiTime, type DayHours } from "@/lib/venue-format";
import { X } from "lucide-react";
import { TimeSelect, hoursSentence } from "@/components/TimeSelect";

type Tab = "week" | "closures" | "rules";
type Affected = Schemas["UpcomingBookingRef"][];

const KIND_LABELS: Record<string, string> = { Closure: "قفل اليوم كله", SpecialHours: "مواعيد خاصة", Break: "استراحة" };
const smallChip = (on: boolean) => chip(on, "min-h-[38px] px-3.5 rounded-[10px] text-[13px]");
const timeSelect = `${inputClass} !w-auto min-h-[40px] min-w-[150px]`;

/** A schedule change that would orphan confirmed bookings comes back as 409 with the affected bookings. */
async function applyWithImpact(
  send: (confirm: boolean) => Promise<{ data?: unknown; error?: unknown; response: Response }>,
  confirm: boolean,
): Promise<{ applied: boolean; affected: Affected }> {
  const { error, response } = await send(confirm);
  if (response.ok) return { applied: true, affected: [] };
  if (response.status === 409) return { applied: false, affected: (error as Schemas["ScheduleChangeOutcome"]).affectedBookings };
  throw new Error(problemMessage(error));
}

function formatBooking(b: Schemas["UpcomingBookingRef"]) {
  const start = new Date(b.startUtc);
  return `${b.reference} — ${start.toLocaleString("ar-EG", { weekday: "long", day: "numeric", month: "long", hour: "numeric", minute: "2-digit", timeZone: "Africa/Cairo" })}`;
}

export default function SchedulePage() {
  const { venue, venueId } = useVenue();
  const queryClient = useQueryClient();
  const [pitchId, setPitchId] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>("week");
  const [toast, setToast] = useState<string | null>(null);
  const [affected, setAffected] = useState<{ list: Affected; retry: () => void } | null>(null);

  const special = useQuery({
    queryKey: ["special-schedules", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/special-schedules", { params: { path: { venueId: venueId! } } })),
  });
  const refreshSpecial = () => queryClient.invalidateQueries({ queryKey: ["special-schedules", venueId] });

  const pitches = venue?.pitches ?? [];
  const pitchName = (id?: string | null) => (id ? pitches.find((p) => p.id === id)?.name ?? "ملعب" : "كل الملاعب");

  const handleOutcome = (outcome: Awaited<ReturnType<typeof applyWithImpact>>, done: string, retry: () => void) => {
    if (outcome.applied) {
      setAffected(null);
      setToast(done);
      void refreshSpecial();
    } else {
      setAffected({ list: outcome.affected, retry });
    }
  };

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">المواعيد وساعات التشغيل</h1>
        <div role="tablist" aria-label="الملعب" className="flex gap-1.5 flex-wrap">
          <button type="button" role="tab" aria-selected={pitchId === null} onClick={() => setPitchId(null)} className={smallChip(pitchId === null)}>
            كل الملاعب
          </button>
          {pitches.map((p) => (
            <button key={p.id} type="button" role="tab" aria-selected={pitchId === p.id} onClick={() => setPitchId(p.id)} className={smallChip(pitchId === p.id)}>
              {p.name}
            </button>
          ))}
        </div>
      </div>

      <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8]">
        {(
          [
            ["week", "الجدول الأسبوعي"],
            ["closures", "أيام مقفولة وأجازات"],
            ["rules", "قواعد الحجز"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setTab(id)}
            className={`min-h-[44px] px-4 bg-transparent text-sm -mb-px border-b-[3px] ${
              tab === id ? "text-navy-900 font-bold border-brand-red" : `${muted} font-medium border-transparent`
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {affected && (
        <div role="alert" className="bg-[#FEF3D6] text-[#7A4D00] rounded-[14px] px-4 py-3 flex flex-col gap-2">
          <span className="text-sm font-bold">
            التعديل ده بيخبط في {toArabicDigits(affected.list.length)} حجز مأكد. الحجوزات دي هتفضل زي ما هي — لازم تكلّم أصحابها لو عايز تلغيها.
          </span>
          <ul className="text-[13px] list-disc ps-5">
            {affected.list.map((b) => (
              <li key={b.bookingId}>{formatBooking(b)}</li>
            ))}
          </ul>
          <div className="flex gap-2">
            <button type="button" onClick={affected.retry} className="min-h-[38px] px-4 rounded-[10px] bg-[#7A4D00] text-white text-[13px] font-bold">
              كمّل التعديل
            </button>
            <button type="button" onClick={() => setAffected(null)} className="min-h-[38px] px-4 rounded-[10px] border border-[#7A4D00] text-[13px] font-semibold">
              إلغاء
            </button>
          </div>
        </div>
      )}

      {venueId && tab === "week" &&
        (pitchId === null ? (
          <VenueWeek venueId={venueId} onSaved={(m) => setToast(m)} onAffected={(list, retry) => setAffected({ list, retry })} />
        ) : (
          <PitchWeek
            key={pitchId}
            venueId={venueId}
            pitchId={pitchId}
            hours={(special.data?.pitchHours ?? []).filter((h) => h.pitchId === pitchId)}
            onOutcome={handleOutcome}
          />
        ))}

      {venueId && tab === "closures" && (
        <Closures
          venueId={venueId}
          pitchId={pitchId}
          exceptions={special.data?.exceptions ?? []}
          pitchName={pitchName}
          onOutcome={handleOutcome}
          onDeleted={() => {
            setToast("الاستثناء اتشال.");
            void refreshSpecial();
          }}
          onError={setToast}
        />
      )}

      {venueId && tab === "rules" && (
        <Rules
          key={pitchId ?? "venue"}
          venueId={venueId}
          pitchId={pitchId}
          rules={special.data?.rules ?? []}
          onSaved={() => {
            setToast(`اتحفظت قواعد الحجز لـ${pitchName(pitchId)}.`);
            void refreshSpecial();
          }}
          onError={setToast}
        />
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function DayRow({ day, value, onChange }: { day: number; value: DayHours; onChange: (v: DayHours) => void }) {
  const overnight = value.close <= value.open;
  return (
    <div className="flex flex-wrap items-center gap-3 py-3 border-b border-[#EEF0F5] last:border-b-0">
      <span className="w-[84px] text-[15px] font-bold">{DAY_NAMES[day]}</span>
      <Switch on={value.on} label={`فتح يوم ${DAY_NAMES[day]}`} onToggle={() => onChange({ ...value, on: !value.on })} />
      {value.on ? (
        <div className="flex items-center gap-x-3 gap-y-2 flex-[1_1_360px] min-w-0 flex-wrap">
          <label className="flex items-center gap-2 text-[13px]">
            <span className={muted}>من</span>
            <TimeSelect ariaLabel={`فتح ${DAY_NAMES[day]}`} value={value.open} onChange={(open) => onChange({ ...value, open })} className={timeSelect} />
          </label>
          <label className="flex items-center gap-2 text-[13px]">
            <span className={muted}>لحد</span>
            <TimeSelect ariaLabel={`قفل ${DAY_NAMES[day]}`} value={value.close} after={value.open} onChange={(close) => onChange({ ...value, close })} className={timeSelect} />
          </label>
          {overnight && <Badge tone="wait">{value.close === value.open ? "٢٤ ساعة" : "بيعدّي نص الليل"}</Badge>}
          <span className={`basis-full text-xs ${muted}`}>{hoursSentence(value.open, value.close)}</span>
        </div>
      ) : (
        <span className="text-sm text-[#8A94AA]">مقفول</span>
      )}
    </div>
  );
}

function VenueWeek({
  venueId,
  onSaved,
  onAffected,
}: {
  venueId: string;
  onSaved: (message: string) => void;
  onAffected: (list: Affected, retry: () => void) => void;
}) {
  const [week, setWeek] = useState<Record<number, DayHours>>(DEFAULT_WEEK);
  const schedules = useQuery({
    queryKey: ["schedules", venueId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/schedules", { params: { path: { venueId } } })),
  });

  useEffect(() => {
    if (!schedules.data) return;
    const next = Object.fromEntries(DAY_ORDER.map((d) => [d, { ...DEFAULT_WEEK[d], on: false }])) as Record<number, DayHours>;
    for (const s of schedules.data) {
      next[s.dayOfWeek] = { on: s.isActive, open: s.openTime.slice(0, 5), close: s.closeTime.slice(0, 5), slot: s.slotDurationMinutes };
    }
    setWeek(next);
  }, [schedules.data]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/venue-manager/venues/{venueId}/schedules", {
          params: { path: { venueId } },
          body: {
            schedules: DAY_ORDER.filter((d) => week[d].on).map((d) => ({
              dayOfWeek: d as Schemas["DayOfWeek"],
              openTime: toApiTime(week[d].open),
              closeTime: toApiTime(week[d].close),
              slotDurationMinutes: week[d].slot,
              isActive: true,
            })),
          },
        }),
      ),
    onSuccess: () => onSaved("اتحفظ الجدول الأسبوعي. اللاعبين بيشوفوا المواعيد الجديدة فوراً."),
    onError: (e: Error) => onSaved(e.message),
  });

  // Ask the API which future bookings the new hours would leave outside the schedule, then save.
  const preview = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/schedules/impact", {
          params: { path: { venueId } },
          body: {
            days: DAY_ORDER.map((d) => ({
              dayOfWeek: d as Schemas["DayOfWeek"],
              openTime: toApiTime(week[d].open),
              closeTime: toApiTime(week[d].close),
              isActive: week[d].on,
            })),
          },
        }),
      ),
    onSuccess: (impact) => (impact.affectedBookings.length > 0 ? onAffected(impact.affectedBookings, () => save.mutate()) : save.mutate()),
    onError: (e: Error) => onSaved(e.message),
  });

  return (
    <div className="flex flex-col gap-4">
      <section className={`${card} rounded-[20px] px-5 py-2`}>
        {DAY_ORDER.map((d) => (
          <DayRow key={d} day={d} value={week[d]} onChange={(v) => setWeek({ ...week, [d]: v })} />
        ))}
      </section>
      <button
        type="button"
        disabled={preview.isPending || save.isPending}
        onClick={() => preview.mutate()}
        className={`self-start min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`}
      >
        {save.isPending || preview.isPending ? "جاري الحفظ…" : "حفظ الجدول"}
      </button>
    </div>
  );
}

function PitchWeek({
  venueId,
  pitchId,
  hours,
  onOutcome,
}: {
  venueId: string;
  pitchId: string;
  hours: Schemas["PitchWeeklyHoursDto"][];
  onOutcome: (outcome: Awaited<ReturnType<typeof applyWithImpact>>, done: string, retry: () => void) => void;
}) {
  const initial = useMemo(() => {
    const week = { ...DEFAULT_WEEK };
    for (const h of hours) week[h.dayOfWeek] = { on: !h.isClosed, open: h.openTime.slice(0, 5), close: h.closeTime.slice(0, 5), slot: 60 };
    return week;
  }, [hours]);
  const [week, setWeek] = useState(initial);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setWeek(initial), [initial]);

  const send = (confirm: boolean) =>
    applyWithImpact(
      (c) =>
        api.PUT("/api/v1/venue-manager/venues/{venueId}/pitches/{pitchId}/weekly-hours", {
          params: { path: { venueId, pitchId } },
          body: {
            confirm: c,
            days: DAY_ORDER.map((d) => ({
              dayOfWeek: d as Schemas["DayOfWeek"],
              openTime: toApiTime(week[d].open),
              closeTime: toApiTime(week[d].close),
              isClosed: !week[d].on,
            })),
          },
        }),
      confirm,
    )
      .then((outcome) => onOutcome(outcome, "اتحفظت مواعيد الملعب.", () => void send(true)))
      .catch((e: Error) => setError(e.message));

  return (
    <div className="flex flex-col gap-4">
      <span className={`text-[13px] ${muted}`}>
        {hours.length > 0 ? "الملعب ده ليه مواعيد خاصة بيه بدل مواعيد الفرع." : "الملعب ماشي على مواعيد الفرع. لو حفظت هنا، هيبقى ليه جدول لوحده."}
      </span>
      <section className={`${card} rounded-[20px] px-5 py-2`}>
        {DAY_ORDER.map((d) => (
          <DayRow key={d} day={d} value={week[d]} onChange={(v) => setWeek({ ...week, [d]: v })} />
        ))}
      </section>
      {error && <span className="text-[13px] text-[#A8261C]">{error}</span>}
      <button
        type="button"
        onClick={() => void send(false)}
        className={`self-start min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold ${redShadow}`}
      >
        حفظ مواعيد الملعب
      </button>
    </div>
  );
}

function Closures({
  venueId,
  pitchId,
  exceptions,
  pitchName,
  onOutcome,
  onDeleted,
  onError,
}: {
  venueId: string;
  pitchId: string | null;
  exceptions: Schemas["ScheduleOverrideDto"][];
  pitchName: (id?: string | null) => string;
  onOutcome: (outcome: Awaited<ReturnType<typeof applyWithImpact>>, done: string, retry: () => void) => void;
  onDeleted: () => void;
  onError: (message: string) => void;
}) {
  const today = new Date().toISOString().slice(0, 10);
  const [form, setForm] = useState({ startDate: today, endDate: "", kind: 0, fromTime: "15:00", toTime: "16:00", reason: "", allPitches: true });
  const year = new Date().getFullYear();

  const add = (confirm: boolean) =>
    applyWithImpact(
      (c) =>
        api.POST("/api/v1/venue-manager/venues/{venueId}/schedule-overrides", {
          params: { path: { venueId } },
          body: {
            pitchId: form.allPitches ? null : pitchId,
            startDate: form.startDate,
            endDate: form.endDate || null,
            kind: form.kind as Schemas["ScheduleOverrideKind"],
            fromTime: form.kind === 0 ? null : toApiTime(form.fromTime),
            toTime: form.kind === 0 ? null : toApiTime(form.toTime),
            reason: form.reason.trim() || KIND_LABELS[["Closure", "SpecialHours", "Break"][form.kind]],
            confirm: c,
          },
        }),
      confirm,
    )
      .then((outcome) => {
        onOutcome(outcome, "اتضاف الاستثناء.", () => void add(true));
        if (outcome.applied) setForm({ ...form, reason: "" });
      })
      .catch((e: Error) => onError(e.message));

  const holidays = (confirm: boolean) =>
    applyWithImpact(
      (c) =>
        api.POST("/api/v1/venue-manager/venues/{venueId}/schedule-overrides/holidays", {
          params: { path: { venueId } },
          body: { year, pitchId: form.allPitches ? null : pitchId, confirm: c },
        }),
      confirm,
    )
      .then((outcome) => onOutcome(outcome, `اتقفلت الأجازات الرسمية لسنة ${toArabicDigits(year)}.`, () => void holidays(true)))
      .catch((e: Error) => onError(e.message));

  const remove = useMutation({
    mutationFn: (overrideId: string) =>
      unwrap(
        api.DELETE("/api/v1/venue-manager/venues/{venueId}/schedule-overrides/{overrideId}", {
          params: { path: { venueId, overrideId }, query: { confirm: true } },
        }),
      ),
    onSuccess: onDeleted,
    onError: (e: Error) => onError(e.message),
  });

  return (
    <div className="flex flex-wrap gap-4 items-start">
      <section className={`flex-[999_1_420px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
        <h2 className="text-base font-bold">الاستثناءات الجاية</h2>
        {exceptions.length === 0 && <span className={`text-sm ${muted}`}>مفيش أيام مقفولة أو مواعيد خاصة.</span>}
        {exceptions.map((x) => (
          <div key={x.id} className="flex items-center gap-3 border border-[#EDF0F5] rounded-[14px] px-3.5 py-3">
            <span className="flex-1 flex flex-col gap-0.5">
              <span className="text-sm font-bold">{x.reason}</span>
              <span className={`text-xs ${muted}`}>
                {new Date(x.startDate).toLocaleDateString("ar-EG", { day: "numeric", month: "long" })}
                {x.endDate !== x.startDate && ` – ${new Date(x.endDate).toLocaleDateString("ar-EG", { day: "numeric", month: "long" })}`} ·{" "}
                {KIND_LABELS[x.kind] ?? x.kind}
                {x.fromTime && x.toTime && ` ${formatTime(x.fromTime)} – ${formatTime(x.toTime)}`} · {pitchName(x.pitchId)}
              </span>
            </span>
            <button
              type="button"
              aria-label={`شيل ${x.reason}`}
              disabled={remove.isPending}
              onClick={() => remove.mutate(x.id)}
              className="min-w-[40px] min-h-[40px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] grid place-items-center"
            >
              <X className="size-4" />
            </button>
          </div>
        ))}
        <button
          type="button"
          onClick={() => void holidays(false)}
          className="self-start min-h-[40px] px-4 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
        >
          اقفل الأجازات الرسمية لسنة {toArabicDigits(year)}
        </button>
      </section>

      <section className={`flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
        <h2 className="text-base font-bold">إضافة استثناء</h2>
        <div className="flex gap-1.5 flex-wrap">
          {["Closure", "SpecialHours", "Break"].map((k, i) => (
            <button key={k} type="button" aria-pressed={form.kind === i} onClick={() => setForm({ ...form, kind: i })} className={smallChip(form.kind === i)}>
              {KIND_LABELS[k]}
            </button>
          ))}
        </div>
        <div className="grid grid-cols-2 gap-2.5">
          <label className="flex flex-col gap-1 text-[13px] font-semibold">
            من يوم
            <input type="date" value={form.startDate} min={today} onChange={(e) => setForm({ ...form, startDate: e.target.value })} className={inputClass} />
          </label>
          <label className="flex flex-col gap-1 text-[13px] font-semibold">
            لحد يوم (اختياري)
            <input type="date" value={form.endDate} min={form.startDate} onChange={(e) => setForm({ ...form, endDate: e.target.value })} className={inputClass} />
          </label>
        </div>
        {form.kind !== 0 && (
          <div className="flex gap-2.5 items-center text-[13px]">
            <TimeSelect ariaLabel="من الساعة" value={form.fromTime} onChange={(fromTime) => setForm({ ...form, fromTime })} className={timeSelect} />
            <span>–</span>
            <TimeSelect ariaLabel="لحد الساعة" value={form.toTime} after={form.fromTime} onChange={(toTime) => setForm({ ...form, toTime })} className={timeSelect} />
          </div>
        )}
        <label className="flex flex-col gap-1 text-[13px] font-semibold">
          السبب
          <input value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} placeholder="صيانة النجيل" className={inputClass} />
        </label>
        {pitchId && (
          <label className="flex items-center gap-2 text-[13px]">
            <input type="checkbox" checked={form.allPitches} onChange={(e) => setForm({ ...form, allPitches: e.target.checked })} />
            على كل الملاعب (مش {pitchName(pitchId)} بس)
          </label>
        )}
        <button
          type="button"
          onClick={() => void add(false)}
          className={`min-h-[46px] rounded-[12px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow}`}
        >
          إضافة
        </button>
      </section>
    </div>
  );
}

function Rules({
  venueId,
  pitchId,
  rules,
  onSaved,
  onError,
}: {
  venueId: string;
  pitchId: string | null;
  rules: Schemas["BookingRulesDto"][];
  onSaved: () => void;
  onError: (message: string) => void;
}) {
  const current = rules.find((r) => (r.pitchId ?? null) === pitchId) ?? rules.find((r) => !r.pitchId);
  const [form, setForm] = useState({
    minDurationMinutes: current?.minDurationMinutes ?? 60,
    maxDurationMinutes: current?.maxDurationMinutes ?? 120,
    incrementMinutes: current?.incrementMinutes ?? 30,
    minNoticeMinutes: current?.minNoticeMinutes ?? 60,
    maxAdvanceDays: current?.maxAdvanceDays ?? 14,
  });

  const save = useMutation({
    mutationFn: () =>
      unwrap(api.PUT("/api/v1/venue-manager/venues/{venueId}/booking-rules", { params: { path: { venueId } }, body: { pitchId, ...form } })),
    onSuccess: onSaved,
    onError: (e: Error) => onError(e.message),
  });

  const option = (key: keyof typeof form, label: string, values: [number, string][]) => (
    <section className={`${card} rounded-[18px] p-[18px] flex flex-col gap-2.5`}>
      <h2 className="text-base font-bold">{label}</h2>
      <div className="flex gap-1.5 flex-wrap">
        {values.map(([value, text]) => (
          <button key={value} type="button" aria-pressed={form[key] === value} onClick={() => setForm({ ...form, [key]: value })} className={smallChip(form[key] === value)}>
            {text}
          </button>
        ))}
      </div>
    </section>
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 grid-cols-[repeat(auto-fit,minmax(min(260px,100%),1fr))]">
        {option("minDurationMinutes", "أقل مدة حجز", [
          [60, "ساعة"],
          [90, "ساعة ونص"],
          [120, "ساعتين"],
        ])}
        {option("maxDurationMinutes", "أطول مدة حجز", [
          [60, "ساعة"],
          [120, "ساعتين"],
          [180, "٣ ساعات"],
        ])}
        {option("incrementMinutes", "الحجز بيزيد كل", [
          [30, "نص ساعة"],
          [60, "ساعة"],
        ])}
        {option("minNoticeMinutes", "أقل مهلة للحجز", [
          [0, "فوراً"],
          [60, "ساعة"],
          [180, "٣ ساعات"],
          [1440, "يوم"],
        ])}
        {option("maxAdvanceDays", "أقصى مدة حجز مقدماً", [
          [7, "أسبوع"],
          [14, "أسبوعين"],
          [30, "شهر"],
        ])}
      </div>
      <button
        type="button"
        disabled={save.isPending}
        onClick={() => save.mutate()}
        className={`self-start min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`}
      >
        {save.isPending ? "جاري الحفظ…" : "حفظ القواعد"}
      </button>
    </div>
  );
}
