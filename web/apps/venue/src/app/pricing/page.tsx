"use client";

import { useConfirm } from "@/components/ConfirmDialog";
import React, { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Switch, Toast, card, chip, inputClass, muted, primaryBtn, secondaryBtn, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { cairoDayStartUtc, cairoToday, addDays, money } from "@/lib/booking-format";
import { P } from "@/lib/permissions";
import { DAY_NAMES, DAY_ORDER, formatTime, toApiTime } from "@/lib/venue-format";
import { Banknote, CalendarDays, ChevronDown, Clock, Hourglass, Lightbulb, Percent, Plus, Settings, Tag, X } from "lucide-react";
import { Pitch } from "@/components/icons";

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

const emptyDiscountDraft = (): Draft => ({
  kind: "Discount",
  adjustment: "Percentage",
  pitchId: "",
  name: "عرض خصم ترويجي",
  value: "15",
  days: [],
  startTime: "",
  endTime: "",
  startDate: cairoToday(),
  endDate: addDays(cairoToday(), 7),
  minDurationMinutes: "",
  isActive: true,
});

function getDiscountStatus(r: Rule): { label: string; tone: "ok" | "warn" | "block" } {
  if (!r.isActive) return { label: "متوقف مؤقتاً", tone: "block" };
  const today = cairoToday();
  if (r.endDate && r.endDate < today) return { label: "منتهي الصلاحية", tone: "block" };
  if (r.startDate && r.startDate > today) return { label: `يبدأ ${r.startDate}`, tone: "warn" };
  if (r.startDate && r.endDate) return { label: "ساري الآن", tone: "ok" };
  return { label: "ساري دائماً", tone: "ok" };
}

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
  if (r.startDate && r.endDate) parts.push(`${r.startDate} إلى ${r.endDate}`);
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
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["price-rules", venueId] }),
        queryClient.invalidateQueries({ queryKey: ["price-preview"] }),
      ]);
      setToast(d.id ? "اتحفظ التعديل. الأسعار الجديدة بتظهر في التطبيق فوراً." : "اتضافت القاعدة.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const [confirm, confirmDialog] = useConfirm();
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/price-rules/{ruleId}", { params: { path: { venueId: venueId!, ruleId: id } } })),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["price-rules", venueId] }),
        queryClient.invalidateQueries({ queryKey: ["price-preview"] }),
      ]);
      setToast("اتمسحت القاعدة.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const list = (rules.data ?? []).filter((r) => !pitchFilter || !r.pitchId || r.pitchId === pitchFilter);
  const discounts = list.filter((r) => r.kind === "Discount");
  const regularRules = list.filter((r) => r.kind !== "Discount");
  const standardKinds: KindName[] = ["Base", "Peak", "Weekend", "Holiday", "SpecialDate"];
  const grouped = standardKinds.map((k) => [k, regularRules.filter((r) => r.kind === k)] as const);
  const pitchName = (id?: string | null) => (id ? pitches.find((p) => p.id === id)?.name ?? "ملعب" : "كل الملاعب");
  const hasBase = (rules.data ?? []).some((r) => r.kind === "Base" && r.isActive);

  return (
    <Page className="gap-5">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_260px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">الأسعار</h1>
          <span className={`text-[13px] ${muted}`}>السعر بيتحسب على السيرفر لكل ربع ساعة: الأساسي، ثم الذروة/العطلة/الإجازة، ثم أفضل خصم واحد.</span>
        </div>
        <select aria-label="الملعب" value={pitchFilter} onChange={(e) => setPitchFilter(e.target.value)} className={`${inputClass} !w-auto min-w-[200px] max-w-[280px] cursor-pointer`}>
          <option value="">كل الملاعب</option>
          {pitches.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        {editable && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setDraft(emptyDiscountDraft())}
              className="min-h-[42px] px-4 rounded-xl bg-[#16A34A] hover:bg-[#15803D] active:scale-95 text-white font-bold text-sm shadow-xs transition-all flex items-center gap-1.5 cursor-pointer"
            >
              <Tag className="size-4" />
              <span>إضافة خصم / عرض مؤقت</span>
            </button>
            <button
              type="button"
              onClick={() => setDraft(emptyDraft(hasBase ? "Peak" : "Base"))}
              className={primaryBtn}
            >
              <Plus className="size-4" strokeWidth={2.5} />
              قاعدة سعر
            </button>
          </div>
        )}
      </div>

      {!hasBase && rules.isSuccess && (
        <p role="alert" className="bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-4 py-3 text-sm">
          لسه مفيش سعر أساسي — اللاعبين مش هيقدروا يحجزوا لحد ما تضيفه.
        </p>
      )}
      {rules.isError && <p className="bg-[#FDECEA] text-[#A8261C] rounded-xl px-4 py-3 text-sm">{(rules.error as Error).message}</p>}

      <div className="flex flex-wrap gap-4 items-start">
        <div className="flex-[999_1_560px] min-w-0 flex flex-col gap-4">
          {rules.isLoading && <div className={`${card} rounded-[18px] h-[160px] animate-pulse bg-[#F4F5F7]`} />}

          {/* قسم العروض والخصومات */}
          <section className={`${card} rounded-[18px] p-5 flex flex-col gap-3 border-emerald-100 bg-gradient-to-b from-[#F7FCF9] to-white`} aria-labelledby="discounts-title">
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-emerald-100 pb-3">
              <div className="flex items-center gap-2">
                <span className="grid place-items-center size-9 rounded-lg bg-emerald-100 text-emerald-700"><Tag className="size-[18px]" /></span>
                <h2 id="discounts-title" className="text-base font-bold text-emerald-950">
                  العروض والخصومات الترويجية
                </h2>
                {discounts.length > 0 && (
                  <span className="text-xs bg-emerald-100 text-emerald-800 font-bold px-2 py-0.5 rounded-full">
                    {toArabicDigits(discounts.length)}
                  </span>
                )}
              </div>
              {editable && (
                <button
                  type="button"
                  onClick={() => setDraft(emptyDiscountDraft())}
                  className="text-xs font-bold text-emerald-700 hover:text-emerald-800 hover:underline flex items-center gap-1 cursor-pointer"
                >
                  <Plus className="size-3.5" strokeWidth={2.5} />
                  إضافة عرض خصم
                </button>
              )}
            </div>

            {discounts.length > 0 ? (
              <div className="flex flex-col gap-2.5">
                {discounts.map((r) => {
                  const status = getDiscountStatus(r);
                  return (
                    <div
                      key={r.id}
                      className="flex flex-wrap items-center gap-3 p-3 bg-white border border-[#E8F2EC] rounded-xl shadow-xs transition-colors duration-150 hover:border-emerald-300"
                    >
                      <div className="flex-[1_1_220px] min-w-0 flex flex-col gap-1">
                        <div className="flex items-center gap-2">
                          <span className="font-bold text-navy-950">{r.name}</span>
                          <Badge tone={status.tone}>{status.label}</Badge>
                        </div>
                        <div className={`text-xs ${muted} flex flex-wrap gap-x-3 gap-y-1`}>
                          <span className="inline-flex items-center gap-1"><Pitch className="size-3.5 shrink-0" /> {pitchName(r.pitchId)}</span>
                          <span className="inline-flex items-center gap-1"><CalendarDays className="size-3.5 shrink-0" /> {r.startDate && r.endDate ? `${r.startDate} حتى ${r.endDate}` : r.startDate ? `من ${r.startDate}` : r.endDate ? `حتى ${r.endDate}` : "مستمر بدون تاريخ انتهاء"}</span>
                          {r.startTime && r.endTime && <span className="inline-flex items-center gap-1"><Clock className="size-3.5 shrink-0" /> {formatTime(r.startTime)} – {formatTime(r.endTime)}</span>}
                          {r.minDurationMinutes && <span className="inline-flex items-center gap-1"><Hourglass className="size-3.5 shrink-0" /> حجز {toArabicDigits(r.minDurationMinutes / 60)} س+</span>}
                        </div>
                      </div>
                      <div className="text-emerald-800 font-bold text-sm bg-emerald-50 border border-emerald-200 px-3 py-1 rounded-lg">
                        {describeValue(r)}
                      </div>
                      {editable && (
                        <div className="flex items-center gap-2">
                          <Switch
                            label={`تفعيل ${r.name}`}
                            isChecked={r.isActive}
                            handleToggle={() => save.mutate({ ...toDraft(r), isActive: !r.isActive })}
                          />
                          <button
                            type="button"
                            onClick={() => setDraft(toDraft(r))}
                            className="min-h-[34px] px-3 rounded-[9px] bg-[#EEF1F7] hover:bg-[#E3E7F0] active:scale-95 text-navy-900 text-xs font-semibold transition-all cursor-pointer"
                          >
                            تعديل
                          </button>
                          <button
                            type="button"
                            onClick={() =>
                              confirm(
                                {
                                  title: `تمسح عرض «${r.name}»؟`,
                                  message: "الخصم هيتشال من حساب أسعار الحجوزات القادمة.",
                                  confirmLabel: "مسح",
                                  destructive: true,
                                },
                                () => remove.mutate(r.id)
                              )
                            }
                            className="min-h-[34px] px-3 rounded-[9px] border border-[#F3C4BF] hover:bg-red-50 active:scale-95 text-[#A8261C] text-xs font-semibold transition-all cursor-pointer"
                          >
                            مسح
                          </button>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            ) : (
              <div className="p-4 bg-white/70 border border-dashed border-emerald-200 rounded-xl flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-full bg-emerald-100 flex items-center justify-center shrink-0 text-emerald-700">
                    <Lightbulb className="size-5" />
                  </div>
                  <div>
                    <h3 className="text-sm font-bold text-emerald-950">هل ترغب في تنشيط الحجوزات؟ أضف خصماً مؤقتاً</h3>
                    <p className={`text-xs ${muted} mt-0.5`}>
                      يمكنك تحديد نسبة خصم أو مبلغ ثابت على الساعة لفترة محددة (أسبوع، أسبوعين، أو شهر) لجذب اللاعبين في الأوقات الهادئة.
                    </p>
                  </div>
                </div>
                {editable && (
                  <button
                    type="button"
                    onClick={() => setDraft(emptyDiscountDraft())}
                    className="min-h-[36px] px-3.5 rounded-[10px] bg-emerald-600 hover:bg-emerald-700 active:scale-95 text-white font-semibold text-xs transition-all shadow-xs cursor-pointer shrink-0 inline-flex items-center gap-1.5"
                  >
                    <Plus className="size-3.5" strokeWidth={2.5} />
                    إضافة عرض خصم
                  </button>
                )}
              </div>
            )}
          </section>

          {/* القواعد الأساسية والإضافية */}
          {grouped
            .filter(([, items]) => items.length > 0)
            .map(([kind, items]) => (
              <section key={kind} className={`${card} rounded-[18px] p-4 flex flex-col gap-2`} aria-labelledby={`kind-${kind}`}>
                <h2 id={`kind-${kind}`} className="text-base font-bold">
                  {KINDS[kind].label}
                </h2>
                {items.map((r) => (
                  <div key={r.id} className="flex flex-wrap items-center gap-3 py-2 border-t border-[#EEF0F5] first:border-t-0 transition-colors duration-150 hover:bg-[#F9FAFC] rounded-lg px-2">
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
                        <button
                          type="button"
                          onClick={() => setDraft(toDraft(r))}
                          className="min-h-[36px] px-3 rounded-[9px] bg-[#EEF1F7] hover:bg-[#E3E7F0] active:scale-95 text-navy-900 text-xs font-semibold transition-all cursor-pointer"
                        >
                          تعديل
                        </button>
                        <button
                          type="button"
                          onClick={() => confirm({ title: `تمسح «${r.name}»؟`, message: "القاعدة هتتشال من حساب الأسعار.", confirmLabel: "مسح", destructive: true }, () => remove.mutate(r.id))}
                          className="min-h-[36px] px-3 rounded-[9px] border border-[#F3C4BF] hover:bg-red-50 active:scale-95 text-[#A8261C] text-xs font-semibold transition-all cursor-pointer"
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

        {venueId && pitches.length > 0 && <PricePreview venueId={venueId} pitches={pitches} rulesVersion={rules.dataUpdatedAt} />}
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
      {confirmDialog}
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
  const isDiscount = draft.kind === "Discount";
  const value = Number(draft.value);
  const error = useMemo(() => {
    if (!draft.name.trim()) return isDiscount ? "اكتب اسم العرض / الخصم" : "اكتب اسم للقاعدة";
    if (!draft.value || Number.isNaN(value)) return isDiscount ? "اكتب قيمة الخصم" : "اكتب القيمة";
    if (isPercent && isDiscount && (value < 1 || value > 100)) return "نسبة الخصم من ١٪ لـ ١٠٠٪";
    if (isPercent && !isDiscount && (value === 0 || value < -90 || value > 300)) return "النسبة من -٩٠٪ لـ +٣٠٠٪ ومش صفر";
    if (!isPercent && value <= 0) return "القيمة لازم تكون أكبر من صفر";
    if (draft.kind === "SpecialDate" && (!draft.startDate || !draft.endDate)) return "حدد تاريخ البداية والنهاية";
    if (draft.startDate && draft.endDate && draft.endDate < draft.startDate) return "تاريخ النهاية لازم يكون بعد أو نفس تاريخ البداية";
    if ((draft.startTime && !draft.endTime) || (!draft.startTime && draft.endTime)) return "حدد بداية ونهاية الساعات";
    return null;
  }, [draft, value, isPercent, isDiscount]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] backdrop-blur-xs animate-fade-in flex items-center justify-center p-4 z-50">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="rule-title"
        className="w-full max-w-[580px] max-h-[92vh] overflow-y-auto bg-white rounded-[20px] p-6 flex flex-col gap-4 animate-modal-in shadow-2xl"
      >
        <div className="flex items-center justify-between gap-2 border-b border-gray-100 pb-3">
          <div>
            <h2 id="rule-title" className="text-[20px] font-bold text-navy-950 flex items-center gap-2">
              {isDiscount ? <span className="inline-flex items-center gap-2"><Tag className="size-5 text-emerald-700" /> {draft.id ? "تعديل عرض الخصم" : "إضافة خصم ترويجي على الساعة"}</span> : (draft.id ? "تعديل قاعدة سعر" : "قاعدة سعر جديدة")}
            </h2>
            <p className={`text-[13px] ${muted} mt-0.5`}>
              {isDiscount
                ? "حدد قيمة الخصم والمدة الزمنية (من تاريخ إلى تاريخ) لجذب حجوزات جديدة على ملاعبك."
                : KINDS[draft.kind].hint}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="w-8 h-8 rounded-full bg-gray-100 hover:bg-gray-200 text-gray-600 flex items-center justify-center transition-colors cursor-pointer"
            aria-label="إغلاق"
          >
            <X className="size-4" />
          </button>
        </div>

        {!draft.id && (
          <div className="flex flex-wrap gap-1.5" role="group" aria-label="نوع القاعدة">
            {(Object.keys(KINDS) as KindName[]).map((k) => (
              <button
                key={k}
                type="button"
                aria-pressed={draft.kind === k}
                onClick={() => onChange(k === "Discount" ? emptyDiscountDraft() : emptyDraft(k))}
                className={chip(draft.kind === k, "min-h-[36px] px-3 rounded-[10px] text-[13px]")}
              >
                {k === "Discount" && <Tag className="size-3.5" />}
                {k === "Discount" ? "خصم وعرض مؤقت" : KINDS[k].label}
              </button>
            ))}
          </div>
        )}

        {isDiscount ? (
          /* نموذج مخصص وسهل جداً للخصومات المؤقتة */
          <div className="flex flex-col gap-3.5">
            {/* نوع الخصم: نسبة مئوية أو مبلغ ثابت */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-bold text-navy-900">طريقة احتساب الخصم</label>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => set({ adjustment: "Percentage" })}
                  className={`py-2.5 px-3 rounded-xl border text-xs font-bold transition-all flex items-center justify-center gap-1.5 cursor-pointer ${
                    isPercent
                      ? "bg-emerald-700 text-white border-emerald-700 shadow-xs"
                      : "bg-[#F8FAFC] text-navy-800 border-gray-200 hover:bg-gray-100"
                  }`}
                >
                  <Percent className="size-4" />
                  <span>نسبة مئوية (مثلاً خصم ٢٠٪)</span>
                </button>
                <button
                  type="button"
                  onClick={() => set({ adjustment: "FixedAmountOff" })}
                  className={`py-2.5 px-3 rounded-xl border text-xs font-bold transition-all flex items-center justify-center gap-1.5 cursor-pointer ${
                    !isPercent
                      ? "bg-emerald-700 text-white border-emerald-700 shadow-xs"
                      : "bg-[#F8FAFC] text-navy-800 border-gray-200 hover:bg-gray-100"
                  }`}
                >
                  <Banknote className="size-4" />
                  <span>مبلغ خصم ثابت (ج.م)</span>
                </button>
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-bold text-navy-900">
                اسم العرض / الخصم
                <input
                  value={draft.name}
                  maxLength={100}
                  placeholder="مثال: خصم الأسبوع، عرض الصيف، تخفيض الصباح"
                  onChange={(e) => set({ name: e.target.value })}
                  className={inputClass}
                />
              </label>

              <label className="flex flex-col gap-1 text-xs font-bold text-navy-900">
                {isPercent ? "نسبة الخصم (٪)" : "قيمة الخصم من الحجز (ج.م)"}
                <div className="relative flex items-center">
                  <input
                    type="number"
                    inputMode="decimal"
                    value={draft.value}
                    placeholder={isPercent ? "مثلاً: 20" : "مثلاً: 50"}
                    onChange={(e) => set({ value: e.target.value })}
                    className={`${inputClass} w-full pl-8`}
                  />
                  <span className="absolute left-3 text-xs text-gray-400 font-bold pointer-events-none">
                    {isPercent ? "٪" : "ج.م"}
                  </span>
                </div>
              </label>

              <label className="flex flex-col gap-1 text-xs font-bold text-navy-900 sm:col-span-2">
                الملعب المطبق عليه الخصم
                <select value={draft.pitchId} onChange={(e) => set({ pitchId: e.target.value })} className={inputClass}>
                  <option value="">كل الملاعب في النادي</option>
                  {pitches.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            {/* صندوق تحديد المدة والفترة الزمنية للخصم */}
            <div className="flex flex-col gap-2 p-3.5 bg-emerald-50/50 border border-emerald-200/70 rounded-xl">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-emerald-950 flex items-center gap-1.5">
                  <CalendarDays className="size-3.5 shrink-0" />
                  <span>مدة سريان الخصم (الفترة الزمنية)</span>
                </span>
                <span className="text-[11px] text-emerald-800">اختيار سريع:</span>
              </div>
              <div className="flex flex-wrap gap-1.5">
                <button
                  type="button"
                  onClick={() => set({ startDate: cairoToday(), endDate: addDays(cairoToday(), 7) })}
                  className="px-2.5 py-1 text-xs rounded-lg bg-white border border-emerald-200 hover:bg-emerald-100 hover:text-emerald-900 text-emerald-800 font-medium transition-colors cursor-pointer"
                >
                  أسبوع (+٧ أيام)
                </button>
                <button
                  type="button"
                  onClick={() => set({ startDate: cairoToday(), endDate: addDays(cairoToday(), 14) })}
                  className="px-2.5 py-1 text-xs rounded-lg bg-white border border-emerald-200 hover:bg-emerald-100 hover:text-emerald-900 text-emerald-800 font-medium transition-colors cursor-pointer"
                >
                  أسبوعين (+١٤ يوم)
                </button>
                <button
                  type="button"
                  onClick={() => set({ startDate: cairoToday(), endDate: addDays(cairoToday(), 30) })}
                  className="px-2.5 py-1 text-xs rounded-lg bg-white border border-emerald-200 hover:bg-emerald-100 hover:text-emerald-900 text-emerald-800 font-medium transition-colors cursor-pointer"
                >
                  شهر (+٣٠ يوم)
                </button>
                <button
                  type="button"
                  onClick={() => set({ startDate: "", endDate: "" })}
                  className="px-2.5 py-1 text-xs rounded-lg bg-white border border-gray-300 hover:bg-gray-100 text-gray-700 font-medium transition-colors cursor-pointer"
                >
                  مستمر دائماً
                </button>
              </div>
              <div className="grid grid-cols-2 gap-2 mt-1">
                <label className="flex flex-col gap-1 text-xs font-semibold text-gray-700">
                  تاريخ بداية العرض
                  <input
                    type="date"
                    value={draft.startDate}
                    onChange={(e) => set({ startDate: e.target.value })}
                    className={inputClass}
                  />
                </label>
                <label className="flex flex-col gap-1 text-xs font-semibold text-gray-700">
                  تاريخ نهاية العرض
                  <input
                    type="date"
                    value={draft.endDate}
                    min={draft.startDate}
                    onChange={(e) => set({ endDate: e.target.value })}
                    className={inputClass}
                  />
                </label>
              </div>
            </div>

            {/* خيارات متقدمة إضافية للخصم */}
            <details className="group border border-gray-200 rounded-xl p-3 bg-gray-50/50">
              <summary className="text-xs font-bold text-gray-700 cursor-pointer flex items-center justify-between">
                <span className="inline-flex items-center gap-1.5"><Settings className="size-3.5" /> شروط إضافية اختيارية (ساعات معينة، أيام محددة، حد أدنى للحجز)</span>
                <ChevronDown className="size-4 text-gray-400 group-open:rotate-180 transition-transform" />
              </summary>
              <div className="flex flex-col gap-3 pt-3 mt-2 border-t border-gray-200">
                <div className="grid grid-cols-2 gap-2">
                  <label className="flex flex-col gap-1 text-xs font-semibold text-gray-700">
                    من الساعة (اختياري)
                    <input
                      type="time"
                      step={900}
                      value={draft.startTime}
                      onChange={(e) => set({ startTime: e.target.value })}
                      className={inputClass}
                    />
                  </label>
                  <label className="flex flex-col gap-1 text-xs font-semibold text-gray-700">
                    لحد الساعة (اختياري)
                    <input
                      type="time"
                      step={900}
                      value={draft.endTime}
                      onChange={(e) => set({ endTime: e.target.value })}
                      className={inputClass}
                    />
                  </label>
                </div>

                <label className="flex flex-col gap-1 text-xs font-semibold text-gray-700">
                  يطبق عند حجز مدته على الأقل (دقيقة)
                  <input
                    type="number"
                    min={15}
                    step={15}
                    placeholder="فاضي = أي مدة (مثلاً 120 لساعتين فأكثر)"
                    value={draft.minDurationMinutes}
                    onChange={(e) => set({ minDurationMinutes: e.target.value })}
                    className={inputClass}
                  />
                </label>

                <fieldset className="flex flex-col gap-1.5">
                  <legend className="text-xs font-semibold text-gray-700 mb-1">أيام تطبيق الخصم (فاضي = كل الأيام):</legend>
                  <div className="flex flex-wrap gap-1.5">
                    {DAY_ORDER.map((d) => (
                      <button
                        key={d}
                        type="button"
                        aria-pressed={draft.days.includes(d)}
                        onClick={() =>
                          set({
                            days: draft.days.includes(d) ? draft.days.filter((x) => x !== d) : [...draft.days, d],
                          })
                        }
                        className={chip(draft.days.includes(d), "min-h-[32px] px-2.5 rounded-lg text-xs")}
                      >
                        {DAY_NAMES[d]}
                      </button>
                    ))}
                  </div>
                </fieldset>
              </div>
            </details>
          </div>
        ) : (
          /* النموذج القياسي لباقي قواعد الأسعار (الأساسي، الذروة، الإجازات، نهاية الأسبوع، تاريخ خاص) */
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
            {draft.kind === "SpecialDate" && (
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
          </div>
        )}

        {!isDiscount && draft.kind !== "Base" && draft.kind !== "Holiday" && (
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
          <p role="alert" className="text-sm text-[#A8261C] font-semibold bg-red-50 p-2.5 rounded-lg border border-red-200">
            {error}
          </p>
        )}
        <div className="flex gap-2 pt-2 border-t border-gray-100">
          <button
            type="button"
            disabled={!!error || busy}
            onClick={onSave}
            className={`${isDiscount ? "min-h-[44px] px-[18px] rounded-xl bg-emerald-600 hover:bg-emerald-700 active:scale-[0.98] text-white text-sm font-bold shadow-xs transition-all flex items-center justify-center gap-2 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed" : primaryBtn} disabled:opacity-50`}
          >
            {busy ? "بيتحفظ…" : isDiscount ? "حفظ عرض الخصم" : "حفظ"}
          </button>
          <button type="button" onClick={onClose} className={secondaryBtn}>
            إلغاء
          </button>
        </div>
      </div>
    </div>
  );
}

function PricePreview({
  venueId,
  pitches,
  rulesVersion,
}: {
  venueId: string;
  pitches: Schemas["PitchDto"][];
  rulesVersion?: number;
}) {
  const [pitchId, setPitchId] = useState(pitches[0]?.id ?? "");
  const [date, setDate] = useState(cairoToday());
  const [time, setTime] = useState("20:00");
  const [hours, setHours] = useState(1);

  useEffect(() => {
    if (!pitchId && pitches[0]?.id) {
      setPitchId(pitches[0].id);
    }
  }, [pitchId, pitches]);

  const [h, m] = time.split(":").map(Number);
  const startUtc = new Date(new Date(cairoDayStartUtc(date)).getTime() + (h * 60 + m) * 60_000).toISOString();
  const endUtc = new Date(new Date(startUtc).getTime() + hours * 3_600_000).toISOString();

  const preview = useQuery({
    queryKey: ["price-preview", venueId, pitchId, startUtc, endUtc, rulesVersion],
    enabled: !!pitchId && !!venueId,
    retry: false,
    queryFn: () => unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/price-rules/preview", { params: { path: { venueId } }, body: { pitchId, startUtc, endUtc } })),
  });
  const q = preview.data?.current;

  const errorMessage = preview.error
    ? ((preview.error as Error).message.includes("has no base price") || (preview.error as Error).message.includes("Pricing.NoBasePrice")
        ? "لم يتم تحديد سعر أساسي لهذا الملعب بعد."
        : (preview.error as Error).message)
    : null;

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
      {preview.isError && <span className="text-sm text-[#A8261C]">{errorMessage}</span>}
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
