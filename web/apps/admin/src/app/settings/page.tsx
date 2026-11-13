"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, Switch, Tabs, btnSmall, card, muted } from "@/components/ui";
import { QueryState, TextInput, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Tab = "settings" | "flags" | "sessions";

const SETTING_LABELS: Record<string, string> = {
  "booking.default_hold_minutes": "مهلة تثبيت الحجز الافتراضية لحين رفع إشعار العربون (للملاعب الجديدة)",
  "booking.default_proof_review_minutes": "مهلة مراجعة إشعار العربون الافتراضية للملعب (للملاعب الجديدة)",
  "otp.max_per_hour": "الحد الأقصى لرموز التحقق (OTP) لكل رقم هاتف في الساعة",
  "otp.resend_cooldown_seconds": "فترة الانتظار بالثواني قبل إعادة طلب رمز التحقق",
  "ranking.win_points": "نقاط تصنيف الفرق عند الفوز",
  "ranking.draw_points": "نقاط تصنيف الفرق عند التعادل",
  "ranking.loss_points": "نقاط تصنيف الفرق عند الخسارة",
  "maintenance.enabled": "وضع الصيانة: إيقاف طلبات المستخدمين وعرض رسالة الصيانة في التطبيقات",
  "maintenance.message": "نص الرسالة المعروضة للمستخدمين أثناء فترة الصيانة",
};

const FLAG_LABELS: Record<string, string> = {
  "app.home.sponsored": "عناصر وإعلانات ممولة في الصفحة الرئيسية لتطبيق اللاعبين",
  "venue.featured": "قسم الملاعب المميزة المختارة بواسطة الإدارة",
  "payment.gateway": "الدفع الإلكتروني للعربون عبر بوابة دفع",
  "tournaments.enabled": "نظام البطولات والدوريات",
  "venue.reviews": "تقييمات ومراجعات الملاعب",
  "referrals.enabled": "برنامج ومكافآت الإحالة",
};

export default function SettingsPage() {
  const [tab, setTab] = useState<Tab>("settings");
  return (
    <Page>
      <PageHeader title="إعدادات المنصة" sub="كل تغيير بيتسجل في سجل التدقيق مع القيمة القديمة والجديدة، وبيتطبق خلال ٣٠ ثانية." />
      <Tabs<Tab> tabs={[["settings", "الإعدادات"], ["flags", "المميزات (Flags)"], ["sessions", "جلسات الإدارة"]]} value={tab} onChange={setTab} />
      {tab === "settings" && <Settings />}
      {tab === "flags" && <Flags />}
      {tab === "sessions" && <Sessions />}
    </Page>
  );
}

function Settings() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const settings = useQuery({ queryKey: ["platform-settings"], queryFn: () => unwrap(api.GET("/api/v1/admin/settings")) });
  const save = useMutation({
    mutationFn: ({ key, value, text }: { key: string; value: string; text: string }) =>
      unwrap(api.PUT("/api/v1/admin/settings/{key}", { params: { path: { key } }, body: { value, reason: text } })),
    onSuccess: async (_, { key }) => {
      notice.ok("اتحفظ الإعداد.");
      setDrafts(({ [key]: _removed, ...rest }) => rest);
      await queryClient.invalidateQueries({ queryKey: ["platform-settings"] });
    },
    onError: notice.fail,
  });

  return (
    <>
      {notice.node}
      <QueryState query={settings}>
        {(list) => (
          <div className={`${card} rounded-[18px] divide-y divide-[#EEF0F5]`}>
            {list.map((s: Schemas["SettingDto"]) => {
              const draft = drafts[s.key];
              const value = draft ?? s.value;
              const description = SETTING_LABELS[s.key] ?? s.description;
              return (
                <div key={s.key} className="p-4 flex flex-wrap gap-3 items-center">
                  <div className="flex-[1_1_260px] flex flex-col gap-0.5">
                    <strong className="text-sm">{description}</strong>
                    <span dir="ltr" className={`text-[11px] font-inter text-right ${muted}`}>{s.key}</span>
                    <span className={`text-[11px] ${muted}`}>
                      الافتراضي: {s.default}
                      {s.min !== null && s.min !== undefined ? ` · من ${s.min}` : ""}
                      {s.max !== null && s.max !== undefined ? ` إلى ${s.max}` : ""}
                      {s.updatedAtUtc ? ` · آخر تعديل ${fmtDateTime(s.updatedAtUtc)}` : ""}
                    </span>
                  </div>
                  {s.type === "Boolean" ? (
                    <Switch on={value === "true"} label={description} onToggle={() => setDrafts((d) => ({ ...d, [s.key]: value === "true" ? "false" : "true" }))} />
                  ) : (
                    <TextInput
                      aria-label={description}
                      className="max-w-[240px]"
                      inputMode={s.type === "Integer" ? "numeric" : undefined}
                      maxLength={s.maxLength ?? undefined}
                      value={value}
                      onChange={(e) => setDrafts((d) => ({ ...d, [s.key]: e.target.value }))}
                    />
                  )}
                  {!s.isDefault && <Pill tone="navy">متعدّل</Pill>}
                  <button
                    type="button"
                    className={btnSmall}
                    disabled={draft === undefined || draft === s.value}
                    onClick={() => setReason({ title: "تغيير إعداد", body: `${description}: ${s.value} ← ${draft}`, confirmLabel: "حفظ", onConfirm: (text) => save.mutate({ key: s.key, value: draft!, text }) })}
                  >
                    حفظ
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </>
  );
}

function Flags() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const flags = useQuery({ queryKey: ["feature-flags"], queryFn: () => unwrap(api.GET("/api/v1/admin/feature-flags")) });
  const toggle = useMutation({
    mutationFn: ({ key, enabled, text }: { key: string; enabled: boolean; text: string }) =>
      unwrap(api.PUT("/api/v1/admin/feature-flags/{key}", { params: { path: { key } }, body: { enabled, reason: text } })),
    onSuccess: async (_, { enabled }) => {
      notice.ok(enabled ? "الميزة اشتغلت." : "الميزة اتقفلت.");
      await queryClient.invalidateQueries({ queryKey: ["feature-flags"] });
    },
    onError: notice.fail,
  });
  return (
    <>
      {notice.node}
      <QueryState query={flags}>
        {(list) => (
          <div className={`${card} rounded-[18px] divide-y divide-[#EEF0F5]`}>
            {list.map((f: Schemas["FlagDto"]) => {
              const description = FLAG_LABELS[f.key] ?? f.description;
              return (
                <div key={f.key} className="p-4 flex gap-3 items-center">
                  <div className="flex-1 flex flex-col gap-0.5">
                    <strong className="text-sm">{description}</strong>
                    <div className="flex items-center gap-1.5 text-[11px] text-[#5B6475]">
                      <span dir="ltr" className="font-inter">{f.key}</span>
                      {f.public && <span className="bg-[#EEF0F5] text-[#0F1B3D] text-[10px] font-medium px-1.5 py-0.5 rounded">متاح للتطبيقات</span>}
                    </div>
                  </div>
                  <Switch
                    on={f.enabled}
                    label={description}
                    onToggle={() =>
                      setReason({
                        title: f.enabled ? "إيقاف ميزة" : "تشغيل ميزة",
                        body: description,
                        confirmLabel: f.enabled ? "إيقاف" : "تشغيل",
                        onConfirm: (text) => toggle.mutate({ key: f.key, enabled: !f.enabled, text }),
                      })
                    }
                  />
                </div>
              );
            })}
          </div>
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </>
  );
}

function Sessions() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const sessions = useQuery({ queryKey: ["admin-sessions"], queryFn: () => unwrap(api.GET("/api/v1/admin/sessions")) });
  const revoke = useMutation({
    mutationFn: (sessionId: string) => unwrap(api.DELETE("/api/v1/admin/sessions/{sessionId}", { params: { path: { sessionId } } })),
    onSuccess: async () => { notice.ok("اتقفلت الجلسة."); await queryClient.invalidateQueries({ queryKey: ["admin-sessions"] }); },
    onError: notice.fail,
  });
  return (
    <>
      {notice.node}
      <QueryState query={sessions} isEmpty={(d) => d.length === 0} empty="مفيش جلسات إدارة مفتوحة.">
        {(list) => (
          <div className={`${card} rounded-[18px] divide-y divide-[#EEF0F5]`}>
            {list.map((s) => (
              <div key={s.sessionId} className="p-4 flex gap-3 items-center text-sm">
                <span className="flex-1">
                  <span dir="ltr" className="font-inter text-xs">{s.userId.slice(0, 8)}</span> · من {fmtDateTime(s.createdAtUtc)} · تنتهي {fmtDateTime(s.expiresAtUtc)}
                </span>
                <button type="button" className={btnSmall} onClick={() => revoke.mutate(s.sessionId)}>قفل</button>
              </div>
            ))}
          </div>
        )}
      </QueryState>
    </>
  );
}
