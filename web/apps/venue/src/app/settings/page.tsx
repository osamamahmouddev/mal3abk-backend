"use client";

import React, { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { Page, Toast, card, chip, inputClass, muted, redShadow } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { AddAreaInline } from "@/components/AddAreaInline";
import { PaymentMethods } from "@/components/PaymentMethods";
import { api, unwrap } from "@/lib/api";
import { useReference } from "@/lib/reference";
import { mediaIdOf, mediaSrc, uploadImage } from "@/lib/upload";

type Tab = "info" | "pay" | "account";

const blank = "";
const NO_PASSWORDS = { currentPassword: blank, newPassword: blank };

const smallBtn = "min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold";

export default function SettingsPage() {
  const { venueId } = useVenue();
  const [tab, setTab] = useState<Tab>("info");
  const [toast, setToast] = useState<string | null>(null);

  return (
    <Page>
      <h1 className="text-[26px] font-bold">الإعدادات</h1>

      <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8] flex-wrap">
        {(
          [
            ["info", "بيانات المكان / النادي"],
            ["pay", "طرق استلام العربون"],
            ["account", "الحساب والأمان"],
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

      {tab === "info" && <VenueInfo onMessage={setToast} />}
      {tab === "pay" && venueId && <PaymentMethods venueId={venueId} onMessage={setToast} />}
      {tab === "account" && <Account onMessage={setToast} />}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function VenueInfo({ onMessage }: { onMessage: (message: string) => void }) {
  const queryClient = useQueryClient();
  const { venue } = useVenue();
  const { facilities, governorates } = useReference();
  const fileInput = useRef<HTMLInputElement>(null);
  const [form, setForm] = useState({ name: "", phoneNumber: "", address: "", description: "", city: "", area: "", facilities: [] as string[] });

  useEffect(() => {
    if (venue) {
      setForm({
        name: venue.name,
        phoneNumber: venue.phoneNumber,
        address: venue.address,
        description: venue.description ?? "",
        city: venue.city,
        area: venue.area,
        facilities: venue.facilities,
      });
    }
  }, [venue]);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["venues"] });

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/venue-manager/venues/{id}", {
          params: { path: { id: venue!.id } },
          body: { ...form, description: form.description.trim() || null, photos: venue!.photos },
        }),
      ),
    onSuccess: async () => {
      await refresh();
      onMessage("اتحفظت البيانات.");
    },
    onError: (e: Error) => onMessage(e.message),
  });

  const upload = useMutation({
    mutationFn: async (file: File) => {
      const mediaId = await uploadImage(file, 0);
      return unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/gallery", { params: { path: { venueId: venue!.id } }, body: { mediaId } }));
    },
    onSuccess: async () => {
      await refresh();
      onMessage("الصورة اترفعت.");
    },
    onError: (e: Error) => onMessage(e.message),
  });

  const removePhoto = useMutation({
    mutationFn: async (photo: string) => {
      const mediaId = mediaIdOf(photo);
      if (mediaId) {
        await unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/gallery/{mediaId}", { params: { path: { venueId: venue!.id, mediaId } } }));
        return;
      }
      // A link to an image hosted elsewhere has no media id: take it out of the saved photo list instead.
      await unwrap(
        api.PUT("/api/v1/venue-manager/venues/{id}", {
          params: { path: { id: venue!.id } },
          body: {
            name: venue!.name,
            phoneNumber: venue!.phoneNumber,
            address: venue!.address,
            description: venue!.description ?? null,
            city: venue!.city,
            area: venue!.area,
            facilities: venue!.facilities,
            photos: venue!.photos.filter((p) => p !== photo),
          },
        }),
      );
    },
    onSuccess: refresh,
    onError: (e: Error) => onMessage(e.message),
  });

  if (!venue) return null;
  const areas = governorates.find((g) => g.nameAr === form.city)?.areas ?? [];

  return (
    <div className="flex flex-wrap gap-4 items-start">
      <section className={`flex-[999_1_460px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
        <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
          اسم المكان / النادي
          <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} className={inputClass} />
        </label>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
            المحافظة
            <select value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value, area: "" })} className={inputClass}>
              {governorates.map((g) => (
                <option key={g.code} value={g.nameAr}>
                  {g.nameAr}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
            المنطقة
            {areas.length > 0 ? (
              <select value={form.area} onChange={(e) => setForm({ ...form, area: e.target.value })} className={inputClass}>
                <option value="">اختار</option>
                {areas.map((a) => (
                  <option key={a.code} value={a.nameAr}>
                    {a.nameAr}
                  </option>
                ))}
              </select>
            ) : (
              <input value={form.area} onChange={(e) => setForm({ ...form, area: e.target.value })} className={inputClass} />
            )}
          </label>
        </div>
        <AddAreaInline
          governorateCode={governorates.find((g) => g.nameAr === form.city)?.code}
          inputClassName={inputClass}
          onAdded={(nameAr) => setForm((f) => ({ ...f, area: nameAr }))}
        />
        <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
          العنوان
          <input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} className={inputClass} />
        </label>
        <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
          رقم التواصل
          <input dir="ltr" value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} className={`${inputClass} text-right`} />
        </label>
        <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
          وصف المكان / النادي
          <textarea rows={3} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} className={`${inputClass} py-2.5`} />
        </label>
        <div className="flex flex-col gap-2">
          <span className="text-[13px] font-semibold">المرافق</span>
          <div className="flex flex-wrap gap-1.5">
            {facilities.map((f) => {
              const on = form.facilities.includes(f.code);
              return (
                <button
                  key={f.code}
                  type="button"
                  aria-pressed={on}
                  onClick={() => setForm({ ...form, facilities: on ? form.facilities.filter((c) => c !== f.code) : [...form.facilities, f.code] })}
                  className={chip(on, "min-h-[36px] px-3 rounded-[10px] text-[13px]")}
                >
                  {f.nameAr}
                </button>
              );
            })}
          </div>
        </div>
        <button
          type="button"
          disabled={save.isPending}
          onClick={() => save.mutate()}
          className={`self-end min-h-[46px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-sm font-bold ${redShadow}`}
        >
          {save.isPending ? "جاري الحفظ…" : "حفظ التعديلات"}
        </button>
      </section>

      <section className={`flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
        <div className="flex flex-col gap-1">
          <h2 className="text-base font-bold">صور المكان / المنشأة العامة</h2>
          <span className={`text-xs ${muted}`}>صور المدخل، الاستراحة، والمرافق العامة. صور أرضيات الملاعب بتترفع من صفحة الملاعب لكل ملعب بشكل منفصل.</span>
        </div>
        <div className="grid grid-cols-2 gap-2">
          {venue.photos.map((photo) => {
            return (
              <div key={photo} className="relative">
                {/* eslint-disable-next-line @next/next/no-img-element -- signed media from our API, already re-encoded as WebP */}
                <img src={mediaSrc(photo)} alt="صورة المنشأة" className="w-full h-24 object-cover rounded-xl" />
                <button
                  type="button"
                  aria-label="مسح الصورة"
                  onClick={() => removePhoto.mutate(photo)}
                  className="absolute top-1.5 left-1.5 w-7 h-7 rounded-full bg-white/90 text-[#A8261C] text-xs font-bold"
                >
                  ✕
                </button>
              </div>
            );
          })}
        </div>
        <input
          ref={fileInput}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) upload.mutate(file);
            e.target.value = "";
          }}
        />
        <button type="button" disabled={upload.isPending} onClick={() => fileInput.current?.click()} className={smallBtn}>
          {upload.isPending ? "جاري الرفع…" : "+ رفع صورة"}
        </button>
        <span className={`text-xs ${muted}`}>JPG أو PNG أو WebP، لحد ٨ ميجا. بنشيل بيانات الموقع من الصورة أوتوماتيك.</span>
      </section>
    </div>
  );
}

function Account({ onMessage }: { onMessage: (message: string) => void }) {
  const profile = useQuery({ queryKey: ["me"], queryFn: () => unwrap(api.GET("/api/v1/me")) });
  const sessions = useQuery({ queryKey: ["sessions"], queryFn: () => unwrap(api.GET("/api/v1/me/sessions")) });
  const [passwords, setPasswords] = useState(NO_PASSWORDS);
  const [enrollment, setEnrollment] = useState<{ secret: string; provisioningUri: string } | null>(null);
  const [totpCode, setTotpCode] = useState("");

  const changePassword = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/password/change", { body: passwords })),
    onSuccess: () => {
      setPasswords(NO_PASSWORDS);
      onMessage("كلمة السر اتغيرت، والأجهزة التانية اتسجّل خروجها.");
    },
    onError: (e: Error) => onMessage(e.message),
  });

  const enroll = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/totp/enroll")),
    onSuccess: setEnrollment,
    onError: (e: Error) => onMessage(e.message),
  });

  const confirmTotp = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/totp/confirm", { body: { code: totpCode } })),
    onSuccess: () => {
      setEnrollment(null);
      setTotpCode("");
      onMessage("التحقق بخطوتين اتفعّل. هتحتاج الكود مع كلمة السر كل مرة تدخل.");
    },
    onError: (e: Error) => onMessage(e.message),
  });

  const revoke = useMutation({
    mutationFn: (sessionId: string) => unwrap(api.DELETE("/api/v1/me/sessions/{sessionId}", { params: { path: { sessionId } } })),
    onSuccess: () => sessions.refetch(),
    onError: (e: Error) => onMessage(e.message),
  });
  const revokeOthers = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/me/sessions/revoke-others")),
    onSuccess: async () => {
      await sessions.refetch();
      onMessage("اتسجّل الخروج من كل الأجهزة التانية.");
    },
    onError: (e: Error) => onMessage(e.message),
  });

  return (
    <div className="flex flex-wrap gap-4 items-start">
      <div className="flex-[999_1_460px] min-w-0 flex flex-col gap-4">
        <section className={`${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
          <AccountRow label="صاحب الحساب" value={profile.data?.fullName ?? "…"}>
            <Link href="/staff" className="text-[13px] font-bold text-navy-900 underline hover:text-brand-red">
              إدارة الموظفين
            </Link>
          </AccountRow>
          <AccountRow label="الإيميل" value={<span dir="ltr">{profile.data?.email ?? "—"}</span>} />
          <AccountRow label="الموبايل" value={<span dir="ltr">{profile.data?.phoneNumber ?? "—"}</span>} />
        </section>

        <section className={`${card} rounded-[20px] p-5 flex flex-col gap-3`}>
          <h2 className="text-base font-bold">تغيير كلمة السر</h2>
          <input
            type="password"
            autoComplete="current-password"
            aria-label="كلمة السر الحالية"
            placeholder="كلمة السر الحالية"
            value={passwords.currentPassword}
            onChange={(e) => setPasswords({ ...passwords, currentPassword: e.target.value })}
            className={inputClass}
          />
          <input
            type="password"
            autoComplete="new-password"
            aria-label="كلمة السر الجديدة"
            placeholder="كلمة السر الجديدة (١٠ حروف على الأقل)"
            minLength={10}
            value={passwords.newPassword}
            onChange={(e) => setPasswords({ ...passwords, newPassword: e.target.value })}
            className={inputClass}
          />
          <button
            type="button"
            disabled={changePassword.isPending || !passwords.currentPassword || passwords.newPassword.length < 10}
            onClick={() => changePassword.mutate()}
            className={`self-start ${smallBtn} disabled:opacity-60`}
          >
            تغيير
          </button>
        </section>

        <section className={`${card} rounded-[20px] p-5 flex flex-col gap-3`}>
          <h2 className="text-base font-bold">التحقق بخطوتين</h2>
          <span className={`text-[13px] ${muted} leading-[1.6]`}>
            بعد التفعيل هتحتاج كود من تطبيق زي Google Authenticator مع كلمة السر. مطلوب للمالك عشان يحمي فلوس الملعب.
          </span>
          {enrollment ? (
            <div className="flex flex-wrap gap-4 items-center">
              <QRCodeSVG value={enrollment.provisioningUri} size={148} className="border border-[#EDF0F5] rounded-xl p-2" />
              <div className="flex-1 min-w-[200px] flex flex-col gap-2">
                <span className="text-[13px]">امسح الكود بالتطبيق، أو اكتب المفتاح ده:</span>
                <code dir="ltr" className="text-[13px] bg-surface rounded-lg px-2 py-1 break-all">
                  {enrollment.secret}
                </code>
                <input
                  dir="ltr"
                  inputMode="numeric"
                  maxLength={6}
                  aria-label="الكود من التطبيق"
                  placeholder="123456"
                  value={totpCode}
                  onChange={(e) => setTotpCode(e.target.value.replace(/\D/g, ""))}
                  className={`${inputClass} text-center tracking-[0.3em]`}
                />
                <button
                  type="button"
                  disabled={totpCode.length !== 6 || confirmTotp.isPending}
                  onClick={() => confirmTotp.mutate()}
                  className={`min-h-[42px] rounded-xl bg-navy-900 disabled:opacity-60 text-white text-sm font-bold`}
                >
                  تأكيد وتفعيل
                </button>
              </div>
            </div>
          ) : (
            <button type="button" disabled={enroll.isPending} onClick={() => enroll.mutate()} className={`self-start ${smallBtn}`}>
              تفعيل التحقق بخطوتين
            </button>
          )}
        </section>
      </div>

      <section className={`flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
        <h2 className="text-base font-bold">الأجهزة المسجّل منها</h2>
        {(sessions.data ?? []).map((s) => (
          <div key={s.sessionId} className="flex items-center gap-2.5 border-b border-[#EEF0F5] last:border-b-0 pb-2.5">
            <span className="flex-1 min-w-0 flex flex-col">
              <span className="text-sm font-bold truncate">
                {s.deviceName || "جهاز"} {s.isCurrent && <span className="text-xs text-[#1B6E3E]">· الجهاز ده</span>}
              </span>
              <span className={`text-xs ${muted}`}>آخر نشاط {new Date(s.lastActivityAtUtc).toLocaleString("ar-EG", { day: "numeric", month: "long", hour: "numeric", minute: "2-digit" })}</span>
            </span>
            {!s.isCurrent && (
              <button type="button" disabled={revoke.isPending} onClick={() => revoke.mutate(s.sessionId)} className={smallBtn}>
                خروج
              </button>
            )}
          </div>
        ))}
        {(sessions.data?.length ?? 0) > 1 && (
          <button type="button" disabled={revokeOthers.isPending} onClick={() => revokeOthers.mutate()} className={`${smallBtn} text-[#A8261C]`}>
            اخرج من كل الأجهزة التانية
          </button>
        )}
      </section>
    </div>
  );
}

function AccountRow({ label, value, children }: { label: string; value: React.ReactNode; children?: React.ReactNode }) {
  return (
    <div className="flex justify-between items-center gap-2.5">
      <span className="flex flex-col">
        <span className={`text-[13px] ${muted}`}>{label}</span>
        <span className="text-[15px] font-bold">{value}</span>
      </span>
      {children}
    </div>
  );
}
