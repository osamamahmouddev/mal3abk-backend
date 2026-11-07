"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, Switch, Tabs, btnSmall, card, muted } from "@/components/ui";
import { QueryState, TextInput, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Tab = "settings" | "flags" | "sessions";

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
              return (
                <div key={s.key} className="p-4 flex flex-wrap gap-3 items-center">
                  <div className="flex-[1_1_260px] flex flex-col gap-0.5">
                    <strong className="text-sm">{s.description}</strong>
                    <span dir="ltr" className={`text-[11px] font-inter text-right ${muted}`}>{s.key}</span>
                    <span className={`text-[11px] ${muted}`}>
                      الافتراضي: {s.default}
                      {s.min !== null && s.min !== undefined ? ` · من ${s.min}` : ""}
                      {s.max !== null && s.max !== undefined ? ` إلى ${s.max}` : ""}
                      {s.updatedAtUtc ? ` · آخر تعديل ${fmtDateTime(s.updatedAtUtc)}` : ""}
                    </span>
                  </div>
                  {s.type === "Boolean" ? (
                    <Switch on={value === "true"} label={s.description} onToggle={() => setDrafts((d) => ({ ...d, [s.key]: value === "true" ? "false" : "true" }))} />
                  ) : (
                    <TextInput
                      aria-label={s.description}
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
                    onClick={() => setReason({ title: "تغيير إعداد", body: `${s.description}: ${s.value} ← ${draft}`, confirmLabel: "حفظ", onConfirm: (text) => save.mutate({ key: s.key, value: draft!, text }) })}
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
            {list.map((f: Schemas["FlagDto"]) => (
              <div key={f.key} className="p-4 flex gap-3 items-center">
                <div className="flex-1 flex flex-col gap-0.5">
                  <strong className="text-sm">{f.description}</strong>
                  <span dir="ltr" className={`text-[11px] font-inter text-right ${muted}`}>{f.key}{f.public ? " · visible to apps" : ""}</span>
                </div>
                <Switch
                  on={f.enabled}
                  label={f.description}
                  onToggle={() =>
                    setReason({
                      title: f.enabled ? "إيقاف ميزة" : "تشغيل ميزة",
                      body: f.description,
                      confirmLabel: f.enabled ? "إيقاف" : "تشغيل",
                      onConfirm: (text) => toggle.mutate({ key: f.key, enabled: !f.enabled, text }),
                    })
                  }
                />
              </div>
            ))}
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
