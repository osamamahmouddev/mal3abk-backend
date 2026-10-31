"use client";

import React, { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Switch, Toast, card, chip, inputClass, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { cairoDayStartUtc, cairoToday, money } from "@/lib/booking-format";
import { P } from "@/lib/permissions";
import { DAY_NAMES, DAY_ORDER, formatTime, toApiTime } from "@/lib/venue-format";

type Rule = Schemas["PriceRuleDto"];
type Request = Schemas["PriceRuleApiRequest"];
type KindName = "Base" | "Peak" | "Weekend" | "Holiday" | "SpecialDate" | "Discount";
type AdjustmentName = "FixedHourlyRate" | "Percentage" | "FixedAmountOff";

const KINDS: Record<KindName, { id: Request["kind"]; label: string; hint: string }> = {
  Base: { id: 0, label: "السعر الأساسي", hint: "سعر الساعة العادي. ممكن تحدد سعر مختلف لكل ملعب." },
  Peak: { id: 1, label: "ساعات الذروة", hint: "سعر مختلف في ساعات معينة، مثلاً من ٦ م لـ ١٢ ص." },
  Weekend: { id: 2, label: "عطلة نهاية الأسبوع", hint: "سعر الخميس والجمعة (أو الأيام اللي تختارها)." },
  Holiday: { id: 3, label: "الإجازات الرسمية", hint: "بيتطبق تلقائياً في الأعياد والإجازات الرسمية في مصر." },
  SpecialDate: { id: 4, label: "تاريخ خاص", hint: "سعر لفترة محددة، زي رمضان أو بطولة." },
  Discount: { id: 5, label: "خصم", hint: "خصم على الحجز — مثلاً ٢٠٪ للحجز ساعتين أو أكتر. بيتطبق أفضل خصم واحد بس." },
};

const ADJUSTMENTS: Record<AdjustmentName, { id: Request["adjustment"]; label: string }> = {
  FixedHourlyRate: { id: 0, label: "سعر ساعة ثابت" },
  Percentage: { id: 1, label: "نسبة ٪" },
  FixedAmountOff: { id: 2, label: "مبلغ خصم ثابت" },
};

const ALLOWED: Record<KindName, AdjustmentName[]> = {
  Base: ["FixedHourlyRate"],
  Peak: ["FixedHourlyRate", "Percentage"],
  Weekend: ["FixedHourlyRate", "Percentage"],
  Holiday: ["FixedHourlyRate", "Percentage"],
  SpecialDate: ["FixedHourlyRate", "Percentage"],
  Discount: ["Percentage", "FixedAmountOff"],
};

const DAY_KEYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"] as const;

interface Draft {
  id?: string;
  kind: KindName;
  adjustment: AdjustmentName;
  pitchId: string;
  name: string;
  value: string;
  days: number[];
  startTime: string;
  endTime: string;
  startDate: string;
  endDate: string;
  minDurationMinutes: string;
  isActive: boolean;
}

const emptyDraft = (kind: KindName = "Base"): Draft => ({
  kind,
  adjustment: ALLOWED[kind][0],
  pitchId: "",
  name: KINDS[kind].label,
  value: "",
  days: kind === "Weekend" ? [4, 5] : [],
  startTime: kind === "Peak" ? "18:00" : "",
  endTime: kind === "Peak" ? "00:00" : "",
  startDate: "",
  endDate: "",
  minDurationMinutes: "",
  isActive: true,
});

function toDraft(r: Rule): Draft {
  const kind = r.kind as KindName;
  return {
    id: r.id,
    kind,
    adjustment: r.adjustment as AdjustmentName,
    pitchId: r.pitchId ?? "",
    name: r.name,
    value: String((r.adjustment === "Percentage" ? r.percent : r.amount) ?? ""),
    days: (r.days ?? []).map((d) => DAY_KEYS.indexOf(d as (typeof DAY_KEYS)[number])).filter((d) => d >= 0),
    startTime: r.startTime?.slice(0, 5) ?? "",
    endTime: r.endTime?.slice(0, 5) ?? "",
    startDate: r.startDate ?? "",
    endDate: r.endDate ?? "",
    minDurationMinutes: r.minDurationMinutes ? String(r.minDurationMinutes) : "",
    isActive: r.isActive,
  };
}

function toRequest(d: Draft): Request {
  const value = Number(d.value);
  const conditional = d.kind !== "Base";
  return {
    kind: KINDS[d.kind].id,
    adjustment: ADJUSTMENTS[d.adjustment].id,
    pitchId: d.pitchId || null,
    name: d.name.trim(),
    amount: d.adjustment === "Percentage" ? null : value,
    percent: d.adjustment === "Percentage" ? value : null,
    days: conditional && d.days.length ? (d.days as Request["days"]) : null,
    startTime: conditional && d.startTime ? toApiTime(d.startTime) : null,
    endTime: conditional && d.endTime ? toApiTime(d.endTime) : null,
    startDate: conditional && d.startDate ? d.startDate : null,
    endDate: conditional && d.endDate ? d.endDate : null,
    minDurationMinutes: d.kind === "Discount" && d.minDurationMinutes ? Number(d.minDurationMinutes) : null,
    isActive: d.isActive,
  };
}

function describeValue(r: Rule): string {
  if (r.adjustment === "Percentage") {
    const p = r.percent ?? 0;
    return r.kind === "Discount" ? `خصم ${toArabicDigits(p)}٪` : `${p > 0 ? "+" : ""}${toArabicDigits(p)}٪ على السعر`;
  }
  if (r.adjustment === "FixedAmountOff") return `خصم ${money(r.amount ?? 0, r.currency)}`;
  return `${money(r.amount ?? 0, r.currency)} / ساعة`;
}

function describeWhen(r: Rule): string {
  const parts: string[] = [];
  if (r.days?.length) parts.push(r.days.map((d) => DAY_NAMES[DAY_KEYS.indexOf(d as (typeof DAY_KEYS)[number])]).join("، "));
  if (r.startTime && r.endTime) parts.push(`${formatTime(r.startTime)} – ${formatTime(r.endTime)}`);
  if (r.startDate && r.endDate) parts.push(`${r.startDate} → ${r.endDate}`);
  if (r.minDurationMinutes) parts.push(`لحجز ${toArabicDigits(r.minDurationMinutes / 60)} ساعة أو أكتر`);
  if (r.kind === "Holiday") parts.push("الإجازات الرسمية");
  return parts.join(" · ") || "كل الأوقات";
}

export default function PricingPage() {
  const queryClient = useQueryClient();
  const { venue, venueId, can } = useVenue();
  const pitches = venue?.pitches ?? [];
  const [pitchFilter, setPitchFilter] = useState("");
  const [draft, setDraft] = useState<Draft | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const editable = can(P.venueEdit);

  const rules = useQuery({
    queryKey: ["price-rules", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/price-rules", { params: { path: { venueId: venueId! } } })),
  });

  const save = useMutation({
    mutationFn: (d: Draft) =>
      d.id
        ? unwrap(api.PUT("/api/v1/venue-manager/venues/{venueId}/price-rules/{ruleId}", { params: { path: { venueId: venueId!, ruleId: d.id } }, body: toRequest(d) }))
        : unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/price-rules", { params: { path: { venueId: venueId! } }, body: toRequest(d) })),
    onSuccess: async (_, d) => {
      setDraft(null);
      await queryClient.invalidateQueries({ queryKey: ["price-rules", venueId] });
      setToast(d.id ? "اتحفظ التعديل. الأسعار الجديدة بتظهر في التطبيق فوراً." : "اتضافت القاعدة.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/price-rules/{ruleId}", { params: { path: { venueId: venueId!, ruleId: id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["price-rules", venueId] });
      setToast("اتمسحت القاعدة.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const list = (rules.data ?? []).filter((r) => !pitchFilter || !r.pitchId || r.pitchId === pitchFilter);
  const grouped = (Object.keys(KINDS) as KindName[]).map((k) => [k, list.filter((r) => r.kind === k)] as const);
  const pitchName = (id?: string | null) => (id ? pitches.find((p) => p.id === id)?.name ?? "ملعب" : "كل الملاعب");
  const hasBase = (rules.data ?? []).some((r) => r.kind === "Base" && r.isActive);

  return (
    <Page className="gap-5">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_260px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">الأسعار</h1>
          <span className={`text-[13px] ${muted}`}>السعر بيتحسب على السيرفر لكل ربع ساعة: الأساسي، ثم الذروة/العطلة/الإجازة، ثم أفضل خصم واحد.</span>
        </div>
        <select aria-label="الملعب" value={pitchFilter} onChange={(e) => setPitchFilter(e.target.value)} className={inputClass}>
          <option value="">كل الملاعب</option>
          {pitches.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        {editable && (
          <button type="button" onClick={() => setDraft(emptyDraft(hasBase ? "Peak" : "Base"))} className={primaryBtn}>
            + قاعدة سعر
          </button>
        )}
      </div>

      {!hasBase && rules.isSuccess && (
        <p role="alert" className="bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-4 py-3 text-sm">
          لسه مفيش سعر أساسي — اللاعبين مش هيقدروا يحجزوا لحد ما تضيفه.
        </p>
      )}
      {rules.isError && <p className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-4 py-3 text-sm">{(rules.error as Error).message}</p>}

      <div className="flex flex-wrap gap-4 items-start">
        <div className="flex-[999_1_560px] min-w-0 flex flex-col gap-3">
          {rules.isLoading && <div className={`${card} rounded-[18px] h-[160px] animate-pulse bg-[#F4F5F7]`} />}
          {grouped
            .filter(([, items]) => items.length > 0)
            .map(([kind, items]) => (
              <section key={kind} className={`${card} rounded-[18px] p-4 flex flex-col gap-2`} aria-labelledby={`kind-${kind}`}>
                <h2 id={`kind-${kind}`} className="text-base font-bold">
                  {KINDS[kind].label}
                </h2>
                {items.map((r) => (
                  <div key={r.id} className="flex flex-wrap items-center gap-3 py-2 border-t border-[#EEF0F5] first:border-t-0">
                    <div className="flex-[1_1_220px] min-w-0 flex flex-col">
                      <span className="font-semibold">{r.name}</span>
                      <span className={`text-xs ${muted}`}>
                        {pitchName(r.pitchId)} · {describeWhen(r)}
                      </span>
                    </div>
                    <Badge tone={r.isActive ? "info" : "block"}>{describeValue(r)}</Badge>
                    {editable && (
                      <span className="flex items-center gap-2">
                        <Switch label={`تفعيل ${r.name}`} isChecked={r.isActive} handleToggle={() => save.mutate({ ...toDraft(r), isActive: !r.isActive })} />
                        <button type="button" onClick={() => setDraft(toDraft(r))} className="min-h-[36px] px-3 rounded-[9px] bg-[#EEF1F7] text-navy-900 text-xs font-semibold">
                          تعديل
                        </button>
                        <button
                          type="button"
                          onClick={() => window.confirm(`تمسح «${r.name}»؟`) && remove.mutate(r.id)}
                          className="min-h-[36px] px-3 rounded-[9px] border border-[#F3C4BF] text-[#A8261C] text-xs font-semibold"
                        >
                          مسح
                        </button>
                      </span>
                    )}
                  </div>
                ))}
              </section>
            ))}
          {rules.isSuccess && list.length === 0 && <p className={`${card} rounded-[18px] p-8 text-center text-sm ${muted}`}>مفيش قواعد أسعار لسه.</p>}
        </div>

        {venueId && pitches.length > 0 && <PricePreview venueId={venueId} pitches={pitches} />}
      </div>

      {draft && (
        <RuleEditor
          draft={draft}
          pitches={pitches}
          busy={save.isPending}
          onChange={setDraft}
          onSave={() => save.mutate(draft)}
          onClose={() => setDraft(null)}
        />
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function RuleEditor({
  draft,
  pitches,
  busy,
  onChange,
  onSave,
  onClose,
}: {
  draft: Draft;
  pitches: Schemas["PitchDto"][];
  busy: boolean;
  onChange: (d: Draft) => void;
  onSave: () => void;
  onClose: () => void;
}) {
  const set = (patch: Partial<Draft>) => onChange({ ...draft, ...patch });
  const isPercent = draft.adjustment === "Percentage";
  const value = Number(draft.value);
  const error = useMemo(() => {
    if (!draft.name.trim()) return "اكتب اسم للقاعدة";
    if (!draft.value || Number.isNaN(value)) return "اكتب القيمة";
    if (isPercent && draft.kind === "Discount" && (value < 1 || value > 100)) return "نسبة الخصم من ١ لـ ١٠٠٪";
    if (isPercent && draft.kind !== "Discount" && (value === 0 || value < -90 || value > 300)) return "النسبة من -٩٠٪ لـ +٣٠٠٪ ومش صفر";
    if (!isPercent && value <= 0) return "القيمة لازم تكون أكبر من صفر";
    if (draft.kind === "SpecialDate" && (!draft.startDate || !draft.endDate)) return "حدد تاريخ البداية والنهاية";
    if ((draft.startTime && !draft.endTime) || (!draft.startTime && draft.endTime)) return "حدد بداية ونهاية الساعات";
    return null;
  }, [draft, value, isPercent]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
      <div role="dialog" aria-modal="true" aria-labelledby="rule-title" className="w-full max-w-[560px] max-h-[90vh] overflow-y-auto bg-white rounded-[20px] p-6 flex flex-col gap-3">
        <h2 id="rule-title" className="text-[19px] font-bold">
          {draft.id ? "تعديل قاعدة سعر" : "قاعدة سعر جديدة"}
        </h2>
        {!draft.id && (
          <div className="flex flex-wrap gap-1.5" role="group" aria-label="نوع القاعدة">
            {(Object.keys(KINDS) as KindName[]).map((k) => (
              <button key={k} type="button" aria-pressed={draft.kind === k} onClick={() => onChange(emptyDraft(k))} className={chip(draft.kind === k, "min-h-[38px] px-3 rounded-[10px] text-[13px]")}>
                {KINDS[k].label}
              </button>
            ))}
          </div>
        )}
        <p className={`text-[13px] ${muted}`}>{KINDS[draft.kind].hint}</p>

        <div className="grid gap-3 sm:grid-cols-2">
          <label className="flex flex-col gap-1 text-sm font-semibold">
            الاسم
            <input value={draft.name} maxLength={100} onChange={(e) => set({ name: e.target.value })} className={inputClass} />
          </label>
          <label className="flex flex-col gap-1 text-sm font-semibold">
            الملعب
            <select value={draft.pitchId} onChange={(e) => set({ pitchId: e.target.value })} className={inputClass}>
              <option value="">كل الملاعب</option>
              {pitches.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
          {ALLOWED[draft.kind].length > 1 && (
            <label className="flex flex-col gap-1 text-sm font-semibold">
              طريقة الحساب
              <select value={draft.adjustment} onChange={(e) => set({ adjustment: e.target.value as AdjustmentName })} className={inputClass}>
                {ALLOWED[draft.kind].map((a) => (
                  <option key={a} value={a}>
                    {ADJUSTMENTS[a].label}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="flex flex-col gap-1 text-sm font-semibold">
            {isPercent ? "النسبة ٪" : draft.adjustment === "FixedAmountOff" ? "مبلغ الخصم (ج.م)" : "سعر الساعة (ج.م)"}
            <input type="number" inputMode="decimal" value={draft.value} onChange={(e) => set({ value: e.target.value })} className={inputClass} />
          </label>
          {draft.kind !== "Base" && draft.kind !== "Holiday" && (
            <>
              <label className="flex flex-col gap-1 text-sm font-semibold">
                من الساعة
                <input type="time" step={900} value={draft.startTime} onChange={(e) => set({ startTime: e.target.value })} className={inputClass} />
              </label>
              <label className="flex flex-col gap-1 text-sm font-semibold">
                لحد الساعة
                <input type="time" step={900} value={draft.endTime} onChange={(e) => set({ endTime: e.target.value })} className={inputClass} />
              </label>
            </>
          )}
          {(draft.kind === "SpecialDate" || draft.kind === "Discount") && (
            <>
              <label className="flex flex-col gap-1 text-sm font-semibold">
                من تاريخ
                <input type="date" value={draft.startDate} onChange={(e) => set({ startDate: e.target.value })} className={inputClass} />
              </label>
              <label className="flex flex-col gap-1 text-sm font-semibold">
                لحد تاريخ
                <input type="date" value={draft.endDate} min={draft.startDate} onChange={(e) => set({ endDate: e.target.value })} className={inputClass} />
              </label>
            </>
          )}
          {draft.kind === "Discount" && (
            <label className="flex flex-col gap-1 text-sm font-semibold">
              لحجز مدته على الأقل (دقيقة)
              <input type="number" min={15} step={15} value={draft.minDurationMinutes} onChange={(e) => set({ minDurationMinutes: e.target.value })} className={inputClass} />
            </label>
          )}
        </div>

        {draft.kind !== "Base" && draft.kind !== "Holiday" && (
          <fieldset className="flex flex-col gap-1.5">
            <legend className="text-sm font-semibold mb-1">الأيام (فاضي = كل الأيام)</legend>
            <div className="flex flex-wrap gap-1.5">
              {DAY_ORDER.map((d) => (
                <button
                  key={d}
                  type="button"
                  aria-pressed={draft.days.includes(d)}
                  onClick={() => set({ days: draft.days.includes(d) ? draft.days.filter((x) => x !== d) : [...draft.days, d] })}
                  className={chip(draft.days.includes(d), "min-h-[36px] px-3 rounded-[9px] text-[13px]")}
                >
                  {DAY_NAMES[d]}
                </button>
              ))}
            </div>
          </fieldset>
        )}

        {error && (
          <p role="alert" className="text-sm text-[#A8261C]">
            {error}
          </p>
        )}
        <div className="flex gap-2">
          <button type="button" disabled={!!error || busy} onClick={onSave} className={`${primaryBtn} disabled:opacity-50`}>
            {busy ? "بيتحفظ…" : "حفظ"}
          </button>
          <button type="button" onClick={onClose} className={secondaryBtn}>
            رجوع
          </button>
        </div>
      </div>
    </div>
  );
}

function PricePreview({ venueId, pitches }: { venueId: string; pitches: Schemas["PitchDto"][] }) {
  const [pitchId, setPitchId] = useState(pitches[0]?.id ?? "");
  const [date, setDate] = useState(cairoToday());
  const [time, setTime] = useState("20:00");
  const [hours, setHours] = useState(1);
  const [h, m] = time.split(":").map(Number);
  const startUtc = new Date(new Date(cairoDayStartUtc(date)).getTime() + (h * 60 + m) * 60_000).toISOString();
  const endUtc = new Date(new Date(startUtc).getTime() + hours * 3_600_000).toISOString();

  const preview = useQuery({
    queryKey: ["price-preview", venueId, pitchId, startUtc, endUtc],
    enabled: !!pitchId,
    retry: false,
    queryFn: () => unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/price-rules/preview", { params: { path: { venueId } }, body: { pitchId, startUtc, endUtc } })),
  });
  const q = preview.data?.current;

  return (
    <aside aria-label="جرّب السعر" className={`flex-[1_1_300px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-3`}>
      <h2 className="text-base font-bold">جرّب السعر</h2>
      <div className="grid grid-cols-2 gap-2">
        <select aria-label="الملعب" value={pitchId} onChange={(e) => setPitchId(e.target.value)} className={`${inputClass} col-span-2`}>
          {pitches.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <input aria-label="اليوم" type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputClass} />
        <input aria-label="الساعة" type="time" step={900} value={time} onChange={(e) => setTime(e.target.value)} className={inputClass} />
        <select aria-label="المدة" value={hours} onChange={(e) => setHours(Number(e.target.value))} className={`${inputClass} col-span-2`}>
          {[1, 1.5, 2, 3].map((x) => (
            <option key={x} value={x}>
              {toArabicDigits(x)} ساعة
            </option>
          ))}
        </select>
      </div>
      {preview.isLoading && <span className={`text-sm ${muted}`}>بيحسب…</span>}
      {preview.isError && <span className="text-sm text-[#A8261C]">{(preview.error as Error).message}</span>}
      {q && (
        <div className="flex flex-col gap-1.5 text-sm" aria-live="polite">
          <div className="flex justify-between">
            <span className={muted}>قبل الخصم</span>
            <span>{money(q.subtotal, q.currency)}</span>
          </div>
          {q.discount > 0 && (
            <div className="flex justify-between text-[#1B6E3E]">
              <span>{q.discountRule ?? "خصم"}</span>
              <span>− {money(q.discount, q.currency)}</span>
            </div>
          )}
          <div className="flex justify-between text-lg font-bold border-t border-[#EEF0F5] pt-1.5">
            <span>الإجمالي</span>
            <span>{money(q.total, q.currency)}</span>
          </div>
          <div className="flex justify-between">
            <span className={muted}>العربون</span>
            <span>{money(q.deposit, q.currency)}</span>
          </div>
        </div>
      )}
    </aside>
  );
}
