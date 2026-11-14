"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, btnOutline, btnPrimary, btnSmall, card, money, muted } from "@/components/ui";
import { Drawer, Field, QueryState, TextInput, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Plan = Schemas["PlanDto"];
type Form = Schemas["PlanApiRequest"];

const FEATURES: [string, string][] = [
  ["reports.advanced", "تقارير متقدمة"],
  ["reports.csv_export", "تصدير CSV"],
  ["venues.multiple", "أكتر من ملعب"],
  ["support.priority", "دعم أولوية"],
];
const STATUS_TONE = { Draft: "neutral", Active: "ok", Archived: "bad" } as const;
const STATUS_MAP = { Draft: "مسودة", Active: "نشطة", Archived: "مؤرشفة" } as const;
const STATUS_VALUE = { Draft: 0, Active: 1, Archived: 2 } as const;

const empty: Form = { code: "", name: "", description: "", monthlyPrice: 0, annualPrice: 0, maxVenues: 1, maxPitches: 3, maxStaffPerVenue: 3, features: [], trialDays: 14, graceDays: 7, isPublic: true, sortOrder: 0 };

export default function PlansPage() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [editing, setEditing] = useState<{ id: string | null; form: Form } | null>(null);

  const plans = useQuery({ queryKey: ["admin-plans"], queryFn: () => unwrap(api.GET("/api/v1/admin/plans")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin-plans"] });

  const save = useMutation({
    mutationFn: ({ id, form }: { id: string | null; form: Form }) =>
      id ? unwrap(api.PUT("/api/v1/admin/plans/{planId}", { params: { path: { planId: id } }, body: form })) : unwrap(api.POST("/api/v1/admin/plans", { body: form })),
    onSuccess: async () => { notice.ok("اتحفظت الباقة."); setEditing(null); await refresh(); },
    onError: notice.fail,
  });
  const status = useMutation({
    mutationFn: ({ id, to }: { id: string; to: keyof typeof STATUS_VALUE }) =>
      unwrap(api.POST("/api/v1/admin/plans/{planId}/status", { params: { path: { planId: id } }, body: { status: STATUS_VALUE[to] } })),
    onSuccess: async () => { notice.ok("اتغيرت حالة الباقة."); await refresh(); },
    onError: notice.fail,
  });
  const makeDefault = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/v1/admin/plans/{planId}/default", { params: { path: { planId: id } } })),
    onSuccess: async () => { notice.ok("بقت الباقة الافتراضية للملاك اللي من غير اشتراك."); await refresh(); },
    onError: notice.fail,
  });

  return (
    <Page>
      <PageHeader title="باقات الاشتراك" sub="الحدود الفاضية = بلا حدود. الباقة الافتراضية بتتطبق على أي مالك مالوش اشتراك.">
        <button type="button" className={btnPrimary} onClick={() => setEditing({ id: null, form: empty })}>
          باقة جديدة
        </button>
      </PageHeader>
      {notice.node}
      <QueryState
        query={plans}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد باقات اشتراك مضافة"
        emptyDescription="لم يتم إنشاء أي باقات اشتراك لأصحاب الملاعب حتى الآن. يمكنك البدء بإضافة الباقة الأولى الآن وتحديد مزاياها وأسعارها."
        emptyIcon="receipt"
        emptyAction={{
          label: "+ إضافة باقة جديدة الآن",
          onClick: () => setEditing({ id: null, form: empty }),
          variant: "primary",
        }}
      >
        {(list) => (
          <div className="grid md:grid-cols-2 xl:grid-cols-3 gap-3">
            {list.map((p) => (
              <article key={p.id} className={`${card} rounded-[18px] p-4 flex flex-col gap-2`}>
                <div className="flex items-center gap-2">
                  <strong className="text-[17px] flex-1">{p.name}</strong>
                  {p.isDefault && <Pill tone="navy">افتراضية</Pill>}
                  <Pill tone={STATUS_TONE[p.status as keyof typeof STATUS_TONE] ?? "neutral"}>{STATUS_MAP[p.status as keyof typeof STATUS_MAP] ?? p.status}</Pill>
                </div>
                <span dir="ltr" className={`text-xs font-inter text-right ${muted}`}>{p.code}</span>
                <span className="text-sm">{money(p.monthlyPrice)} / شهر · {money(p.annualPrice)} / سنة</span>
                <span className={`text-[13px] ${muted}`}>
                  ملاعب: {limit(p.maxVenues)} · ملاعب فرعية: {limit(p.maxPitches)} · موظفين لكل ملعب: {limit(p.maxStaffPerVenue)}
                </span>
                <span className={`text-[13px] ${muted}`}>تجربة {num(p.trialDays)} يوم · سماح {num(p.graceDays)} يوم · {num(p.subscribers)} مشترك</span>
                <div className="flex flex-wrap gap-2 mt-auto pt-2">
                  <button type="button" className={btnSmall} onClick={() => setEditing({ id: p.id, form: toForm(p) })}>تعديل</button>
                  {p.status !== "Active" && <button type="button" className={btnSmall} onClick={() => status.mutate({ id: p.id, to: "Active" })}>تفعيل</button>}
                  {p.status === "Active" && !p.isDefault && <button type="button" className={btnSmall} onClick={() => makeDefault.mutate(p.id)}>اجعلها افتراضية</button>}
                  {p.status !== "Archived" && !p.isDefault && <button type="button" className={btnSmall} onClick={() => status.mutate({ id: p.id, to: "Archived" })}>أرشفة</button>}
                </div>
              </article>
            ))}
          </div>
        )}
      </QueryState>
      {editing && <PlanForm initial={editing.form} isNew={!editing.id} busy={save.isPending} onCancel={() => setEditing(null)} onSave={(form) => save.mutate({ id: editing.id, form })} />}
    </Page>
  );
}

const limit = (v?: number | null) => (v === null || v === undefined ? "بلا حدود" : num(v));

function toForm(p: Plan): Form {
  return {
    code: p.code, name: p.name, description: p.description ?? "", monthlyPrice: p.monthlyPrice, annualPrice: p.annualPrice,
    maxVenues: p.maxVenues ?? null, maxPitches: p.maxPitches ?? null, maxStaffPerVenue: p.maxStaffPerVenue ?? null,
    features: p.features, trialDays: p.trialDays, graceDays: p.graceDays, isPublic: p.isPublic, sortOrder: p.sortOrder, currency: p.currency,
  };
}

function PlanForm({ initial, isNew, busy, onCancel, onSave }: { initial: Form; isNew: boolean; busy: boolean; onCancel: () => void; onSave: (f: Form) => void }) {
  const [f, setF] = useState<Form>(initial);
  const set = <K extends keyof Form>(k: K, v: Form[K]) => setF((x) => ({ ...x, [k]: v }));
  const optional = (v: string) => (v.trim() === "" ? null : Math.max(0, Number(v)));
  const valid = f.code.trim() && f.name.trim() && f.monthlyPrice >= 0 && f.annualPrice >= 0;

  return (
    <Drawer title={isNew ? "باقة جديدة" : "تعديل الباقة"} open onClose={onCancel}>
      <Field label="الكود (إنجليزي، مش بيتغير بعد الاشتراك)">
        <TextInput dir="ltr" value={f.code} onChange={(e) => set("code", e.target.value.toLowerCase())} />
      </Field>
      <Field label="الاسم">
        <TextInput value={f.name} onChange={(e) => set("name", e.target.value)} />
      </Field>
      <Field label="الوصف">
        <TextInput value={f.description ?? ""} onChange={(e) => set("description", e.target.value)} />
      </Field>
      <div className="grid grid-cols-2 gap-2">
        <Field label="السعر الشهري"><TextInput inputMode="decimal" value={String(f.monthlyPrice)} onChange={(e) => set("monthlyPrice", Number(e.target.value) || 0)} /></Field>
        <Field label="السعر السنوي"><TextInput inputMode="decimal" value={String(f.annualPrice)} onChange={(e) => set("annualPrice", Number(e.target.value) || 0)} /></Field>
        <Field label="أقصى ملاعب (فاضي = بلا حدود)"><TextInput inputMode="numeric" value={f.maxVenues ?? ""} onChange={(e) => set("maxVenues", optional(e.target.value))} /></Field>
        <Field label="أقصى ملاعب فرعية"><TextInput inputMode="numeric" value={f.maxPitches ?? ""} onChange={(e) => set("maxPitches", optional(e.target.value))} /></Field>
        <Field label="أقصى موظفين لكل ملعب"><TextInput inputMode="numeric" value={f.maxStaffPerVenue ?? ""} onChange={(e) => set("maxStaffPerVenue", optional(e.target.value))} /></Field>
        <Field label="الترتيب"><TextInput inputMode="numeric" value={String(f.sortOrder)} onChange={(e) => set("sortOrder", Number(e.target.value) || 0)} /></Field>
        <Field label="أيام التجربة"><TextInput inputMode="numeric" value={String(f.trialDays)} onChange={(e) => set("trialDays", Number(e.target.value) || 0)} /></Field>
        <Field label="أيام السماح"><TextInput inputMode="numeric" value={String(f.graceDays)} onChange={(e) => set("graceDays", Number(e.target.value) || 0)} /></Field>
      </div>
      <fieldset className="flex flex-col gap-1.5">
        <legend className="text-[13px] font-semibold mb-1">المميزات</legend>
        {FEATURES.map(([code, title]) => (
          <label key={code} className="flex gap-2 text-sm items-center">
            <input
              type="checkbox"
              checked={(f.features ?? []).includes(code)}
              onChange={(e) => set("features", e.target.checked ? [...(f.features ?? []), code] : (f.features ?? []).filter((x) => x !== code))}
            />
            {title}
          </label>
        ))}
      </fieldset>
      <label className="flex gap-2 text-sm items-center">
        <input type="checkbox" checked={f.isPublic} onChange={(e) => set("isPublic", e.target.checked)} />
        تظهر للملاك في صفحة الأسعار
      </label>
      <div className="flex gap-2 justify-end">
        <button type="button" className={btnOutline} onClick={onCancel}>رجوع</button>
        <button type="button" className={btnPrimary} disabled={!valid || busy} onClick={() => onSave(f)}>حفظ</button>
      </div>
    </Drawer>
  );
}
