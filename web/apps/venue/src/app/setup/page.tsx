"use client";

import React, { useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Switch, card, chip, muted, redShadow, toArabicDigits } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { logout } from "@/app/auth-actions";
import { api, unwrap } from "@/lib/api";
import { useReference } from "@/lib/reference";
import { CAPACITIES, DAY_ORDER, DAY_NAMES, DEFAULT_WEEK, METHOD_LABELS, toApiTime, type DayHours } from "@/lib/venue-format";
import { AddAreaInline } from "@/components/AddAreaInline";
import { PaymentMethodForm } from "@/components/PaymentMethodForm";

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const field = "min-h-[46px] border border-[#C9CFD9] rounded-xl px-3 text-[15px] text-slate-900 bg-white focus:outline-none focus:ring-2 focus:ring-navy-900";
const setupChip = (on: boolean) => chip(on, "min-h-[40px] px-3.5 rounded-xl text-sm");

interface VenueForm {
  name: string;
  city: string;
  area: string;
  phoneNumber: string;
  address: string;
  description: string;
  facilities: string[];
}

export default function SetupPage() {
  const queryClient = useQueryClient();
  const { venue } = useVenue();
  const reference = useReference();
  const [step, setStep] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<VenueForm>({ name: "", city: "", area: "", phoneNumber: "", address: "", description: "", facilities: [] });
  const [loadedVenue, setLoadedVenue] = useState<string | null>(null);

  // Resume an unfinished setup with the owner's existing venue.
  useEffect(() => {
    if (venue && loadedVenue !== venue.id) {
      setLoadedVenue(venue.id);
      setForm({
        name: venue.name,
        city: venue.city,
        area: venue.area,
        phoneNumber: venue.phoneNumber,
        address: venue.address,
        description: venue.description ?? "",
        facilities: venue.facilities,
      });
    }
  }, [venue, loadedVenue]);

  const venueId = venue?.id ?? null;
  const governorate = reference.governorates.find((g) => g.nameAr === form.city);

  const saveVenue = useMutation({
    mutationFn: async () => {
      const body = {
        name: form.name.trim(),
        city: form.city,
        area: form.area,
        phoneNumber: form.phoneNumber.trim(),
        address: form.address.trim(),
        description: form.description.trim() || null,
        facilities: form.facilities,
        photos: venue?.photos ?? [],
      };
      return venueId
        ? unwrap(api.PUT("/api/v1/venue-manager/venues/{id}", { params: { path: { id: venueId } }, body }))
        : unwrap(api.POST("/api/v1/venue-manager/venues", { body }));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["venues"] });
      setError(null);
      setStep(2);
    },
    onError: (e: Error) => setError(e.message),
  });

  const venueValid = form.name.trim().length >= 3 && form.city && form.area && form.phoneNumber.trim() && form.address.trim();

  const steps: [string, string][] = [
    ["بيانات المكان", "الاسم والعنوان والمرافق"],
    ["الملاعب", `${toArabicDigits(venue?.pitches.length ?? 0)} ملاعب`],
    ["مواعيد الشغل", "المواعيد الأسبوعية"],
    ["الدفع والعربون", "طرق الاستلام"],
    ["المراجعة", venue?.approvalStatus === "Draft" || !venue ? "إرسال للمراجعة" : "اتبعت"],
  ];

  return (
    <div dir="rtl" className="w-full min-h-screen bg-surface text-slate-900 flex flex-col">
      <header className="bg-white border-b border-[#EDF0F5] px-4 sm:px-8 py-4 flex items-center gap-x-4 gap-y-2 flex-wrap">
        <Image
          src="/venue/images/logo-horizontal.png"
          alt="ملعبك"
          width={120}
          height={44}
          className="h-8 w-auto object-contain"
          priority
        />
        <span className="text-[11px] font-bold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5 whitespace-nowrap">للملاعب</span>
        <span className="flex-1" />
        {venueId && (
          <Link href="/overview" className="text-sm font-semibold text-navy-900 underline">
            اللوحة
          </Link>
        )}
        <form action={logout}>
          <button type="submit" className="text-sm font-semibold text-navy-900 underline hover:text-brand-red">
            خروج
          </button>
        </form>
      </header>

      <div className="flex-1 w-full max-w-[1180px] mx-auto px-4 sm:px-6 pt-5 sm:pt-8 pb-10 flex flex-wrap gap-4 sm:gap-6 items-start">
        <nav aria-label="خطوات الإعداد" className={`flex-[1_1_240px] ${card} rounded-[18px] p-4 flex flex-col gap-1`}>
          {steps.map(([label, hint], i) => {
            const n = i + 1;
            const done = n < step;
            const cur = n === step;
            return (
              <button
                key={label}
                type="button"
                aria-current={cur ? "step" : undefined}
                disabled={n > 1 && !venueId}
                onClick={() => setStep(n)}
                className={`min-h-[60px] px-2.5 py-2 rounded-xl flex items-center gap-3 text-slate-900 disabled:opacity-50 ${cur ? "bg-[#EEF1F7]" : "bg-transparent"}`}
              >
                <span
                  className={`w-[30px] h-[30px] shrink-0 rounded-full flex items-center justify-center text-[13px] font-bold ${
                    done ? "bg-[#1B6E3E] text-white" : cur ? "bg-navy-900 text-white" : "bg-[#EEF0F5] text-[#3B4352]"
                  }`}
                >
                  {done ? "✓" : toArabicDigits(n)}
                </span>
                <span className="flex-1 text-right flex flex-col">
                  <span className="text-sm font-bold">{label}</span>
                  <span className={`text-xs ${muted}`}>{hint}</span>
                </span>
              </button>
            );
          })}
        </nav>

        <main className={`flex-[999_1_560px] min-w-0 ${card} rounded-[20px] p-5 sm:p-7 flex flex-col gap-5`}>
          {step === 1 && (
            <div className="flex flex-col gap-[18px]">
              <h1 className="text-2xl font-bold">بيانات المكان</h1>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <label className="flex flex-col gap-1.5 sm:col-span-2 text-sm font-semibold">
                  <span>
                    اسم المكان <span className="text-[#A8261C]">*</span>
                  </span>
                  <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} className={field} />
                </label>
                <label className="flex flex-col gap-1.5 text-sm font-semibold">
                  المحافظة
                  <select value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value, area: "" })} className={field}>
                    <option value="">اختار</option>
                    {reference.governorates.map((g) => (
                      <option key={g.code} value={g.nameAr}>
                        {g.nameAr}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="flex flex-col gap-1.5 text-sm font-semibold">
                  المنطقة
                  {governorate && governorate.areas.length > 0 ? (
                    <select value={form.area} onChange={(e) => setForm({ ...form, area: e.target.value })} className={field}>
                      <option value="">اختار</option>
                      {governorate.areas.map((a) => (
                        <option key={a.code} value={a.nameAr}>
                          {a.nameAr}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <input value={form.area} onChange={(e) => setForm({ ...form, area: e.target.value })} className={field} />
                  )}
                </label>
                <div className="sm:col-span-2 flex flex-col">
                  <AddAreaInline
                    governorateCode={governorate?.code}
                    inputClassName={field}
                    onAdded={(nameAr) => setForm((f) => ({ ...f, area: nameAr }))}
                  />
                </div>
                <label className="flex flex-col gap-1.5 text-sm font-semibold">
                  رقم التواصل للاعبين
                  <input
                    dir="ltr"
                    type="tel"
                    value={form.phoneNumber}
                    onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })}
                    className={`${field} text-right`}
                  />
                </label>
                <label className="flex flex-col gap-1.5 text-sm font-semibold sm:col-span-2">
                  العنوان بالتفصيل
                  <input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} className={field} />
                </label>
                <label className="flex flex-col gap-1.5 text-sm font-semibold sm:col-span-2">
                  وصف قصير (اختياري)
                  <textarea
                    rows={3}
                    value={form.description}
                    onChange={(e) => setForm({ ...form, description: e.target.value })}
                    className={`${field} py-2.5`}
                  />
                </label>
              </div>
              <div className="flex flex-col gap-2">
                <span className="text-sm font-semibold">المرافق</span>
                <div className="flex flex-wrap gap-2">
                  {reference.facilities.map((f) => {
                    const on = form.facilities.includes(f.code);
                    return (
                      <button
                        key={f.code}
                        type="button"
                        aria-pressed={on}
                        onClick={() =>
                          setForm({ ...form, facilities: on ? form.facilities.filter((c) => c !== f.code) : [...form.facilities, f.code] })
                        }
                        className={setupChip(on)}
                      >
                        {on ? "✓ " : ""}
                        {f.nameAr}
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>
          )}

          {step === 2 && venueId && <PitchesStep venueId={venueId} />}
          {step === 3 && venueId && <ScheduleStep venueId={venueId} onSaved={() => setStep(4)} />}
          {step === 4 && venueId && <PaymentStep venueId={venueId} />}
          {step === 5 && venueId && <SubmitStep venueId={venueId} />}

          {error && (
            <span role="alert" className="text-[13px] text-[#A8261C]">
              {error}
            </span>
          )}

          <div className="flex gap-2.5 border-t border-[#EEF1F5] pt-[18px]">
            {step > 1 && (
              <button
                type="button"
                onClick={() => setStep(step - 1)}
                className="min-h-[48px] px-[22px] rounded-[14px] border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold"
              >
                رجوع
              </button>
            )}
            <span className="flex-1" />
            {step === 1 && (
              <button
                type="button"
                disabled={!venueValid || saveVenue.isPending}
                onClick={() => saveVenue.mutate()}
                className={`min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`}
              >
                {saveVenue.isPending ? "جاري الحفظ…" : "حفظ والتالي"}
              </button>
            )}
            {(step === 2 || step === 4) && (
              <button
                type="button"
                onClick={() => setStep(step + 1)}
                className={`min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold ${redShadow}`}
              >
                التالي
              </button>
            )}
          </div>
        </main>
      </div>
    </div>
  );
}

function PitchesStep({ venueId }: { venueId: string }) {
  const queryClient = useQueryClient();
  const { surfaces } = useReference();
  const [draft, setDraft] = useState({ name: "", turfType: "ArtificialTurf", capacity: 5 });
  const [error, setError] = useState<string | null>(null);

  const pitches = useQuery({
    queryKey: ["pitches", venueId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/pitches", { params: { path: { venueId } } })),
  });

  const refresh = () => Promise.all([pitches.refetch(), queryClient.invalidateQueries({ queryKey: ["venues"] })]);

  const add = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/pitches", {
          params: { path: { venueId } },
          body: { name: draft.name.trim(), turfType: draft.turfType, capacity: draft.capacity, photos: [] },
        }),
      ),
    onSuccess: async () => {
      setDraft({ ...draft, name: "" });
      setError(null);
      await refresh();
    },
    onError: (e: Error) => setError(e.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/v1/venue-manager/pitches/{id}", { params: { path: { id } } })),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold">الملاعب</h1>
      <span className={`text-sm ${muted}`}>كل ملعب ليه مواعيده وسعره. تقدر تعدّل التفاصيل بعدين من صفحة الملاعب.</span>
      {(pitches.data ?? []).map((p) => (
        <div key={p.id} className="border border-[#EDF0F5] rounded-2xl px-4 py-3.5 flex items-center gap-3.5">
          <span className="w-14 h-14 shrink-0 rounded-xl" style={{ background: pitchPattern }} />
          <span className="flex-1 flex flex-col gap-0.5">
            <span className="text-[15px] font-bold">{p.name}</span>
            <span className={`text-[13px] ${muted}`}>
              {toArabicDigits(p.capacity)} لاعب · {p.surfaceArabicDisplayName}
            </span>
          </span>
          <button
            type="button"
            aria-label={`حذف ${p.name}`}
            disabled={remove.isPending}
            onClick={() => remove.mutate(p.id)}
            className="min-w-[40px] min-h-[40px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C]"
          >
            ✕
          </button>
        </div>
      ))}
      <div className="border-2 border-dashed border-[#9AA3B2] rounded-2xl p-4 grid grid-cols-1 sm:grid-cols-[1fr_1fr_120px_auto] gap-3 items-end">
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          اسم الملعب
          <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="ملعب ١" className={field} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          الأرضية
          <select value={draft.turfType} onChange={(e) => setDraft({ ...draft, turfType: e.target.value })} className={field}>
            {surfaces.map((s) => (
              <option key={s.code} value={s.code}>
                {s.nameAr}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          لاعب/فريق
          <select value={draft.capacity} onChange={(e) => setDraft({ ...draft, capacity: Number(e.target.value) })} className={field}>
            {CAPACITIES.map((c) => (
              <option key={c} value={c}>
                {toArabicDigits(c)}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          disabled={!draft.name.trim() || add.isPending}
          onClick={() => add.mutate()}
          className="min-h-[46px] px-5 rounded-xl bg-navy-900 disabled:opacity-60 text-white text-sm font-bold"
        >
          + إضافة
        </button>
      </div>
      {error && (
        <span role="alert" className="text-[13px] text-[#A8261C]">
          {error}
        </span>
      )}
    </div>
  );
}

function ScheduleStep({ venueId, onSaved }: { venueId: string; onSaved: () => void }) {
  const [week, setWeek] = useState<Record<number, DayHours>>(DEFAULT_WEEK);
  const [error, setError] = useState<string | null>(null);

  const existing = useQuery({
    queryKey: ["schedules", venueId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/schedules", { params: { path: { venueId } } })),
  });

  useEffect(() => {
    if (existing.data && existing.data.length > 0) {
      const next = { ...DEFAULT_WEEK };
      for (const day of DAY_ORDER) next[day] = { ...next[day], on: false };
      for (const s of existing.data) {
        next[s.dayOfWeek] = { on: s.isActive, open: s.openTime.slice(0, 5), close: s.closeTime.slice(0, 5), slot: s.slotDurationMinutes };
      }
      setWeek(next);
    }
  }, [existing.data]);

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
    onSuccess: () => {
      setError(null);
      onSaved();
    },
    onError: (e: Error) => setError(e.message),
  });

  return (
    <div className="flex flex-col gap-3.5">
      <h1 className="text-2xl font-bold">مواعيد الشغل</h1>
      <span className={`text-sm ${muted}`}>المواعيد بعد نص الليل بتتحسب على نفس يوم التشغيل. تقدر تعدّل لكل ملعب بعدين من صفحة المواعيد.</span>
      {DAY_ORDER.map((d) => {
        const day = week[d];
        return (
          <div key={d} className="flex flex-wrap items-center gap-3 py-2.5 border-b border-[#EEF1F5]">
            <span className="w-[90px] text-sm font-bold">{DAY_NAMES[d]}</span>
            <Switch on={day.on} label={`تشغيل يوم ${DAY_NAMES[d]}`} onToggle={() => setWeek({ ...week, [d]: { ...day, on: !day.on } })} />
            {day.on ? (
              <span className="flex items-center gap-2 text-sm">
                <input
                  type="time"
                  aria-label={`فتح ${DAY_NAMES[d]}`}
                  value={day.open}
                  onChange={(e) => setWeek({ ...week, [d]: { ...day, open: e.target.value } })}
                  className={`${field} min-h-[40px]`}
                />
                <span>–</span>
                <input
                  type="time"
                  aria-label={`قفل ${DAY_NAMES[d]}`}
                  value={day.close}
                  onChange={(e) => setWeek({ ...week, [d]: { ...day, close: e.target.value } })}
                  className={`${field} min-h-[40px]`}
                />
              </span>
            ) : (
              <span className="flex-1 text-sm text-[#333B4A]">مقفول</span>
            )}
          </div>
        );
      })}
      {error && (
        <span role="alert" className="text-[13px] text-[#A8261C]">
          {error}
        </span>
      )}
      <button
        type="button"
        disabled={save.isPending}
        onClick={() => save.mutate()}
        className={`self-start min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`}
      >
        {save.isPending ? "جاري الحفظ…" : "حفظ المواعيد والتالي"}
      </button>
    </div>
  );
}

function PaymentStep({ venueId }: { venueId: string }) {
  const methods = useQuery({
    queryKey: ["payment-methods", venueId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/payment-methods", { params: { path: { venueId } } })),
  });
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold">الدفع والعربون</h1>
      <span className={`text-sm ${muted}`}>
        ضيف طريقة واحدة على الأقل يحوّل عليها اللاعب العربون. ملعبك مش بيستلم فلوس؛ اللاعب بيحوّل لك مباشرة وإنت بتراجع الإيصال.
      </span>
      {(methods.data ?? []).map((m) => (
        <div key={m.id} className="border border-[#EDF0F5] rounded-[14px] px-3.5 py-3 flex items-center gap-3">
          <span className="flex-1 flex flex-col">
            <span className="text-sm font-bold">{METHOD_LABELS[m.methodType] ?? m.methodType}</span>
            <span dir="ltr" className={`text-right text-[13px] ${muted}`}>
              {m.accountDetailsMasked} · {m.recipientName}
            </span>
          </span>
        </div>
      ))}
      <PaymentMethodForm venueId={venueId} onAdded={() => methods.refetch()} />
    </div>
  );
}

function SubmitStep({ venueId }: { venueId: string }) {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState("");
  const [error, setError] = useState<string | null>(null);
  const approval = useQuery({
    queryKey: ["approval", venueId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/approval", { params: { path: { venueId } } })),
  });
  const submit = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/approval/submit", {
          params: { path: { venueId } },
          body: { message: message.trim() || null },
        }),
      ),
    onSuccess: async () => {
      setError(null);
      await Promise.all([approval.refetch(), queryClient.invalidateQueries({ queryKey: ["venues"] })]);
    },
    onError: (e: Error) => setError(e.message),
  });

  const status = approval.data?.approvalStatus;
  const canSubmit = status === "Draft" || status === "NeedsInfo" || status === "Rejected";
  const lastAdminNote = approval.data?.history.filter((h) => h.message && h.action !== "Submitted").at(-1)?.message;

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold">الإرسال للمراجعة</h1>
      <span className={`text-sm ${muted}`}>فريق ملعبك بيراجع بيانات الملعب قبل ما يظهر للاعبين (عادةً خلال يومين عمل).</span>
      {status === "NeedsInfo" && lastAdminNote && (
        <div className="bg-[#FEF3D6] text-[#7A4D00] rounded-[14px] px-4 py-3 text-sm leading-[22px]">مطلوب منك: {lastAdminNote}</div>
      )}
      {status === "Rejected" && lastAdminNote && (
        <div className="bg-[#FDECEA] text-[#A8261C] rounded-[14px] px-4 py-3 text-sm leading-[22px]">سبب الرفض: {lastAdminNote}</div>
      )}
      {canSubmit ? (
        <>
          <label className="flex flex-col gap-1.5 text-sm font-semibold">
            رسالة لفريق المراجعة (اختياري)
            <textarea rows={3} value={message} onChange={(e) => setMessage(e.target.value)} className={`${field} py-2.5`} />
          </label>
          {error && (
            <span role="alert" className="text-[13px] text-[#A8261C]">
              {error}
            </span>
          )}
          <button
            type="button"
            disabled={submit.isPending}
            onClick={() => submit.mutate()}
            className={`min-h-[50px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold ${redShadow}`}
          >
            {submit.isPending ? "جاري الإرسال…" : "إرسال للمراجعة"}
          </button>
        </>
      ) : (
        <>
          <div role="status" className="bg-[#E5F5EB] text-[#155C33] rounded-[14px] px-4 py-3.5 text-sm leading-[22px] font-semibold">
            {status === "Approved"
              ? "✓ الملعب اتوافق عليه."
              : "✓ اتبعت للمراجعة. تقدر تستخدم اللوحة دلوقتي وتجهّز الحجوزات، والملعب هيظهر للاعبين بعد الموافقة."}
          </div>
          <Link href="/overview" className="min-h-[50px] rounded-[14px] bg-navy-900 text-white text-[15px] font-bold flex items-center justify-center">
            افتح لوحة الملعب
          </Link>
        </>
      )}
    </div>
  );
}
