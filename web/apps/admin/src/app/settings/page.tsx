"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, Switch, Tabs, btnSmall, card, muted } from "@/components/ui";
import { QueryState, TextInput, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { Building2, Check, CreditCard, ShieldCheck, Smartphone, Wrench, Zap } from "lucide-react";

type Tab = "deposits" | "settings" | "flags" | "sessions";

const SETTING_LABELS: Record<string, string> = {
  "platform.deposit.enabled": "تفعيل استلام العربون مركزياً على حسابات المنصة بدلاً من حسابات الملعب المباشرة",
  "platform.deposit.instapay_address": "عنوان إنستاباي الرسمي للمنصة (InstaPay)",
  "platform.deposit.vodafone_cash_number": "رقم محفظة فودافون كاش / المحافظ الإلكترونية للمنصة (Vodafone Cash)",
  "platform.deposit.bank_account_iban": "رقم الحساب البنكي أو الآيبان (IBAN) للتحويل البنكي للمنصة",
  "platform.deposit.bank_name": "اسم البنك التابع له الحساب البنكي للمنصة",
  "platform.deposit.recipient_name": "اسم المستلم الرسمي المعروض للاعب عند تحويل العربون",
  "platform.deposit.instructions": "تعليمات تحويل العربون المعروضة للاعب في التطبيق",
  "pricing.default_hourly_commission": "عمولة المنصة الافتراضية لكل ساعة حجز بالجنيه المصري",
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
  const [tab, setTab] = useState<Tab>("deposits");
  return (
    <Page>
      <PageHeader title="إعدادات المنصة" sub="كل تغيير بيتسجل في سجل التدقيق مع القيمة القديمة والجديدة، وبيتطبق خلال ٣٠ ثانية." />
      <Tabs<Tab>
        tabs={[
          ["deposits", "طرق دفع العربون والمدفوعات"],
          ["settings", "الإعدادات العامة"],
          ["flags", "المميزات (Flags)"],
          ["sessions", "جلسات الإدارة"],
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === "deposits" && <DepositSettings />}
      {tab === "settings" && <Settings />}
      {tab === "flags" && <Flags />}
      {tab === "sessions" && <Sessions />}
    </Page>
  );
}

function DepositSettings() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const settings = useQuery({ queryKey: ["platform-settings"], queryFn: () => unwrap(api.GET("/api/v1/admin/settings")) });

  const save = useMutation({
    mutationFn: ({ key, value, text }: { key: string; value: string; text: string }) =>
      unwrap(api.PUT("/api/v1/admin/settings/{key}", { params: { path: { key } }, body: { value, reason: text } })),
    onSuccess: async (_, { key }) => {
      notice.ok("اتحفظت بيانات الدفع بنجاح.");
      setDrafts(({ [key]: _removed, ...rest }) => rest);
      await queryClient.invalidateQueries({ queryKey: ["platform-settings"] });
    },
    onError: notice.fail,
  });

  const getSetting = (k: string) => settings.data?.find((s) => s.key === k);

  const renderField = (key: string, label: string, hint: string, placeholder = "") => {
    const s = getSetting(key);
    const draft = drafts[key];
    const currentVal = draft ?? s?.value ?? "";
    const isDirty = draft !== undefined && draft !== s?.value;

    return (
      <div className="flex flex-col gap-1.5 p-3.5 bg-slate-50 border border-slate-200 rounded-xl">
        <div className="flex items-center justify-between gap-2">
          <label htmlFor={`field-${key}`} className="text-sm font-bold text-slate-900">
            {label}
          </label>
          {s && !s.isDefault && <Pill tone="navy">متعدل</Pill>}
        </div>
        <p className="text-xs text-slate-500">{hint}</p>
        <div className="flex gap-2 items-center mt-1">
          <TextInput
            id={`field-${key}`}
            value={currentVal}
            placeholder={placeholder}
            className="flex-1"
            onChange={(e) => setDrafts((prev) => ({ ...prev, [key]: e.target.value }))}
          />
          <button
            type="button"
            disabled={!isDirty || save.isPending}
            onClick={() =>
              setReason({
                title: `تعديل ${label}`,
                body: `القيمة الجديدة: ${currentVal}`,
                confirmLabel: "حفظ وتطبيق",
                onConfirm: (text) => save.mutate({ key, value: currentVal, text }),
              })
            }
            className={`${btnSmall} bg-navy-900 text-white font-bold px-4 py-2 hover:bg-navy-800 disabled:opacity-40 cursor-pointer`}
          >
            {save.isPending ? "حفظ…" : "حفظ"}
          </button>
        </div>
      </div>
    );
  };

  return (
    <>
      {notice.node}

      <div className="flex flex-col gap-6">
        {/* 1. Paymob Hero Card: Coming Soon */}
        <div className={`${card} rounded-2xl p-5 flex flex-col md:flex-row md:items-center justify-between gap-4`}>
          <div className="flex items-start gap-3.5">
            <div className="size-10 rounded-xl bg-[#FDF3DC] text-[#9A5B00] flex items-center justify-center shrink-0">
              <CreditCard className="size-5" />
            </div>
            <div className="flex flex-col gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="text-[16px] font-bold text-slate-900">بوابة الدفع الإلكتروني المباشر (Paymob)</h3>
                <Pill tone="warn">قريباً</Pill>
              </div>
              <p className={`text-[13px] ${muted} leading-relaxed max-w-2xl`}>
                بوابة Paymob للدفع الإلكتروني الفوري ببطاقات (فيزا، ماستركارد، وميزة المصرية) والمحافظ الذكية مدرجة في النظام كـ «قريباً».
                يتم حالياً استلام مبالغ العربون وتأكيدها مركزياً عبر إنستاباي، فودافون كاش، والتحويل البنكي أدناه.
              </p>
            </div>
          </div>
          <span className={`text-[12px] ${muted} shrink-0`}>تفعيل البوابة قيد التجهيز</span>
        </div>

        {/* 2. How deposits flow (commission model: always to the platform) */}
        <div className={`${card} rounded-2xl p-5 flex flex-col gap-2`}>
          <strong className="text-base font-bold text-slate-900">العربون بيتدفع لحسابات المنصة دايماً</strong>
          <p className="text-[13px] text-slate-600 leading-relaxed">
            اللاعب بيشوف الحسابات اللي تحت بس (الملاعب مبقاش ليها وسائل دفع). الفريق بيراجع الإيصال من صفحة «مراجعة العرابين» ويأكد الحجز،
            وبعدين المنصة بتاخد عمولتها من العربون وتحوّل الباقي للملعب. باقي سعر الحجز اللاعب بيدفعه في الملعب.
          </p>
        </div>

        {/* 3. Active Methods Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* InstaPay */}
          <div className={`${card} rounded-[20px] p-5 flex flex-col gap-3.5 border border-slate-200`}>
            <div className="flex items-center gap-2.5 pb-2 border-b border-slate-100">
              <div className="size-9 rounded-xl bg-purple-50 text-purple-700 flex items-center justify-center">
                <Zap className="size-[18px]" />
              </div>
              <div>
                <h4 className="text-sm font-black text-slate-900">إنستاباي (InstaPay)</h4>
                <p className="text-xs text-slate-500">حساب المنصة الرسمي على شبكة المدفوعات اللحظية</p>
              </div>
            </div>
            {renderField(
              "platform.deposit.instapay_address",
              "عنوان أو رقم إنستاباي (IPA)",
              "مثال: mal3abk@instapay أو رقم الموبايل المرتبط بالحساب",
              "mal3abk@instapay"
            )}
          </div>

          {/* Vodafone Cash & Mobile Wallets */}
          <div className={`${card} rounded-[20px] p-5 flex flex-col gap-3.5 border border-slate-200`}>
            <div className="flex items-center gap-2.5 pb-2 border-b border-slate-100">
              <div className="size-9 rounded-xl bg-red-50 text-red-600 flex items-center justify-center">
                <Smartphone className="size-5" />
              </div>
              <div>
                <h4 className="text-sm font-black text-slate-900">فودافون كاش والمحافظ الإلكترونية</h4>
                <p className="text-xs text-slate-500">رقم المحفظة الذكية للمنصة (فودافون، أورنج، اتصالات، وي)</p>
              </div>
            </div>
            {renderField(
              "platform.deposit.vodafone_cash_number",
              "رقم محفظة الكاش",
              "رقم الموبايل المصري المخصص لاستقبال تحويلات المحافظ الذكية",
              "010xxxxxxxx"
            )}
          </div>

          {/* Bank Transfer */}
          <div className={`${card} rounded-[20px] p-5 flex flex-col gap-3.5 border border-slate-200 md:col-span-2`}>
            <div className="flex items-center gap-2.5 pb-2 border-b border-slate-100">
              <div className="size-9 rounded-xl bg-blue-50 text-blue-700 flex items-center justify-center">
                <Building2 className="size-5" />
              </div>
              <div>
                <h4 className="text-sm font-black text-slate-900">التحويل البنكي المباشر (Bank Transfer)</h4>
                <p className="text-xs text-slate-500">بيانات الحساب البنكي أو الآيبان الرسمي للمنصة في البنوك المصرية</p>
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {renderField(
                "platform.deposit.bank_name",
                "اسم البنك",
                "مثال: البنك الأهلي المصري، بنك مصر، CIB",
                "البنك الأهلي المصري"
              )}
              {renderField(
                "platform.deposit.bank_account_iban",
                "رقم الحساب أو الآيبان (IBAN)",
                "رقم الحساب البنكي أو الآيبان الدولي لتحويلات البنوك",
                "EG0000000000000000000000000"
              )}
            </div>
          </div>

          {/* Recipient & Instructions */}
          <div className={`${card} rounded-[20px] p-5 flex flex-col gap-3.5 border border-slate-200 md:col-span-2`}>
            <div className="flex items-center gap-2.5 pb-2 border-b border-slate-100">
              <div className="size-9 rounded-xl bg-emerald-50 text-emerald-700 flex items-center justify-center">
                <ShieldCheck className="size-5" />
              </div>
              <div>
                <h4 className="text-sm font-black text-slate-900">بيانات المستلم وتعليمات التحويل للاعبين</h4>
                <p className="text-xs text-slate-500">النصوص والتعليمات التي تظهر للاعب في شاشة سداد العربون</p>
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {renderField(
                "platform.deposit.recipient_name",
                "اسم المستلم الرسمي",
                "الاسم المعروض للاعب كاسم صاحب الحساب أو المحفظة",
                "منصة ملعبك الرسمية"
              )}
              {renderField(
                "pricing.default_hourly_commission",
                "العمولة الافتراضية للساعة (ج.م)",
                "قيمة العمولة الافتراضية لكل ساعة حجز بالجنيه",
                "50.00"
              )}
              <div className="sm:col-span-2">
                {renderField(
                  "platform.deposit.instructions",
                  "تعليمات وملاحظات التحويل",
                  "الرسالة التوضيحية للاعب (مثال: كتابة رقم الحجز في الملاحظات ورفع صورة الإيصال)",
                  "برجاء كتابة رقم الحجز في الملاحظات ورفع صورة التحويل بعد الإتمام."
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </>
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

  const maintenanceSetting = settings.data?.find((s) => s.key === "maintenance.enabled");
  const isMaintenanceOn = maintenanceSetting?.value === "true";

  return (
    <>
      {notice.node}

      {isMaintenanceOn && (
        <div className="bg-red-50 border-2 border-red-300 text-red-950 p-4 sm:p-5 rounded-2xl flex flex-wrap items-center justify-between gap-4 shadow-sm">
          <div className="flex items-center gap-3.5">
            <div className="w-12 h-12 rounded-xl bg-red-100 text-red-700 flex items-center justify-center shrink-0">
              <Wrench className="size-6" />
            </div>
            <div>
              <strong className="text-base font-black text-red-900 block">
                وضع الصيانة مفعّل حالياً على منصة ملعبك!
              </strong>
              <span className="text-xs text-red-800 leading-relaxed block mt-0.5">
                جميع طلبات المستخدمين متوقفة حالياً وتظهر لهم رسالة الصيانة الرسمية. لوحة تحكم الإدارة فقط هي المستثناة.
              </span>
            </div>
          </div>
          <button
            type="button"
            className={`${btnSmall} bg-red-600 hover:bg-red-700 text-white font-bold px-4 py-2 text-xs border-none shadow-sm cursor-pointer`}
            onClick={() =>
              setReason({
                title: "إلغاء وضع الصيانة",
                body: "إلغاء وضع الصيانة وعودة المنصة والتطبيقات للعمل الطبيعي فوراً.",
                confirmLabel: "إلغاء الصيانة الآن",
                onConfirm: (text) => save.mutate({ key: "maintenance.enabled", value: "false", text }),
              })
            }
          >
            <Check className="size-4" />
            إلغاء وضع الصيانة وإعادة المنصة للعمل
          </button>
        </div>
      )}

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
                    <Switch
                      on={s.value === "true"}
                      label={description}
                      disabled={save.isPending}
                      onToggle={() =>
                        setReason({
                          title: s.value === "true" ? `إيقاف ${description}` : `تشغيل ${description}`,
                          body: `${description}: ${s.value === "true" ? "مفعل" : "معطل"} ← ${s.value === "true" ? "معطل" : "مفعل"}`,
                          confirmLabel: s.value === "true" ? "إيقاف" : "تشغيل",
                          onConfirm: (text) => save.mutate({ key: s.key, value: s.value === "true" ? "false" : "true", text }),
                        })
                      }
                    />
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
                  {s.type !== "Boolean" && (
                    <button
                      type="button"
                      className={btnSmall}
                      disabled={draft === undefined || draft === s.value || save.isPending}
                      onClick={() => setReason({ title: "تغيير إعداد", body: `${description}: ${s.value} ← ${draft}`, confirmLabel: "حفظ", onConfirm: (text) => save.mutate({ key: s.key, value: draft!, text }) })}
                    >
                      حفظ
                    </button>
                  )}
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
      <QueryState
        query={sessions}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد جلسات إدارة مفتوحة"
        emptyDescription="لا توجد جلسات دخول نشطة أخرى للمشرفين في الوقت الحالي."
        emptyIcon="shield"
      >
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
