"use client";

import React, { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Toast, card, chip, muted, redShadow } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { P } from "@/lib/permissions";
import { api, unwrap } from "@/lib/api";
import { Check, Mail, UserPlus } from "lucide-react";

type Member = Schemas["StaffMemberDto"];

const ROLES: [value: Schemas["VenueStaffRole"], name: string, label: string][] = [
  [0, "Manager", "مدير"],
  [1, "Receptionist", "استقبال"],
  [2, "Accountant", "محاسب"],
];
const roleLabel = (name: string) => ROLES.find(([, n]) => n === name)?.[2] ?? name;

// permission, owner, manager, reception, accountant
const MATRIX: [string, boolean, boolean, boolean, boolean][] = [
  ["مراجعة إثباتات الدفع", true, true, true, false],
  ["حجز يدوي وحظر مواعيد", true, true, true, false],
  ["إلغاء حجز مأكد", true, true, false, false],
  ["تعديل الملاعب والمواعيد", true, true, false, false],
  ["التقارير المالية", true, true, false, true],
  ["الاشتراك والموظفين", true, false, false, false],
];

const roleChip = (on: boolean) => chip(on, "min-h-[34px] px-3 rounded-[10px] text-[13px]");
const field = "min-h-[44px] border border-[#C9CFD9] rounded-xl px-3 text-sm focus:outline-none focus:ring-2 focus:ring-navy-900";

export default function StaffPage() {
  const { venueId, can } = useVenue();
  const canManage = can(P.staffManage);
  const [contact, setContact] = useState("");
  const [role, setRole] = useState<Schemas["VenueStaffRole"]>(1);
  const [toRemove, setToRemove] = useState<Member | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const staff = useQuery({
    queryKey: ["staff", venueId],
    enabled: !!venueId && canManage,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/staff", { params: { path: { venueId: venueId! } } })),
  });
  const done = (message: string) => async () => {
    await staff.refetch();
    setToast(message);
  };
  const fail = (e: Error) => setToast(e.message);

  const isEmail = contact.includes("@");
  const phoneOk = /^01\d{9}$/.test(contact.replace(/\D/g, ""));
  const canInvite = isEmail ? /^\S+@\S+\.\S+$/.test(contact.trim()) : phoneOk;

  const invite = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/staff/invitations", {
          params: { path: { venueId: venueId! } },
          body: { email: isEmail ? contact.trim() : null, phoneNumber: isEmail ? null : contact.replace(/\D/g, ""), role },
        }),
      ),
    onSuccess: async () => {
      setContact("");
      await done(isEmail ? "اتبعتت الدعوة على الإيميل." : "اتبعتت الدعوة في رسالة على الموبايل.")();
    },
    onError: fail,
  });

  const changeRole = useMutation({
    mutationFn: ({ memberId, newRole }: { memberId: string; newRole: Schemas["VenueStaffRole"] }) =>
      unwrap(
        api.PUT("/api/v1/venue-manager/venues/{venueId}/staff/members/{memberId}", {
          params: { path: { venueId: venueId!, memberId } },
          body: { role: newRole },
        }),
      ),
    onSuccess: done("الدور اتغير. الصلاحيات الجديدة شغالة فوراً."),
    onError: fail,
  });

  const remove = useMutation({
    mutationFn: (memberId: string) =>
      unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/staff/members/{memberId}", { params: { path: { venueId: venueId!, memberId } } })),
    onSuccess: async () => {
      const name = toRemove?.fullName ?? "الموظف";
      setToRemove(null);
      await done(`اتشال ${name} من اللوحة.`)();
    },
    onError: fail,
  });

  const resend = useMutation({
    mutationFn: (invitationId: string) =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/staff/invitations/{invitationId}/resend", {
          params: { path: { venueId: venueId!, invitationId } },
        }),
      ),
    onSuccess: done("اتبعتت الدعوة تاني. اللينك القديم مبقاش شغال."),
    onError: fail,
  });

  const revoke = useMutation({
    mutationFn: (invitationId: string) =>
      unwrap(
        api.DELETE("/api/v1/venue-manager/venues/{venueId}/staff/invitations/{invitationId}", {
          params: { path: { venueId: venueId!, invitationId } },
        }),
      ),
    onSuccess: done("الدعوة اتلغت."),
    onError: fail,
  });

  const members = staff.data?.members ?? [];
  const invitations = staff.data?.pendingInvitations ?? [];

  // The API refuses staff management to other roles; say so instead of showing an error.
  if (venueId && !canManage) {
    return (
      <Page>
        <h1 className="text-[26px] font-bold">الموظفين</h1>
        <p role="note" className="bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-4 py-3 text-sm">
          إدارة الموظفين والصلاحيات متاحة لصاحب الملعب أو المدير بس.
        </p>
      </Page>
    );
  }

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">الموظفين والصلاحيات</h1>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_460px] min-w-0 ${card} rounded-[20px] px-5 py-2`}>
          {staff.isLoading && <div className="h-16 my-3 rounded-xl bg-slate-200 animate-pulse" />}
          {!staff.isLoading && members.length === 0 && invitations.length === 0 && (
            <div className="py-12 flex flex-col items-center gap-3 text-center animate-fade-in">
              <span className="grid place-items-center size-14 rounded-2xl bg-[#F3F5F9] text-[#8A93A3]">
                <UserPlus className="size-7" strokeWidth={1.5} />
              </span>
              <strong className="text-[15px]">مفيش موظفين لسه</strong>
              <span className={`text-[13px] ${muted} max-w-[320px]`}>ابعت دعوة لحد من فريقك من الفورم اللي جنبك، وحدد دوره وصلاحياته.</span>
            </div>
          )}
          {members.map((m) => (
            <div key={m.memberId} className="flex flex-wrap items-center gap-3 py-3.5 border-b border-[#EEF0F5] last:border-b-0">
              <span className="w-11 h-11 rounded-full shrink-0 flex items-center justify-center text-[17px] font-bold bg-navy-900 text-white">
                {(m.fullName ?? "؟").charAt(0)}
              </span>
              <span className="flex-[1_1_180px] min-w-0 flex flex-col gap-0.5">
                <span className="text-[15px] font-bold">{m.fullName ?? "موظف"}</span>
                <span className={`text-[13px] ${muted}`}>من {new Date(m.addedAtUtc).toLocaleDateString("ar-EG")}</span>
              </span>
              <div className="flex gap-1 flex-wrap">
                {ROLES.map(([value, name, label]) => (
                  <button
                    key={name}
                    type="button"
                    aria-pressed={m.role === name}
                    disabled={changeRole.isPending || m.role === name}
                    onClick={() => changeRole.mutate({ memberId: m.memberId, newRole: value })}
                    className={roleChip(m.role === name)}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <button
                type="button"
                onClick={() => setToRemove(m)}
                className="min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-[#A8261C] text-[13px] font-semibold"
              >
                إزالة
              </button>
            </div>
          ))}
          {invitations.map((inv) => (
            <div key={inv.invitationId} className="flex flex-wrap items-center gap-3 py-3.5 border-b border-[#EEF0F5] last:border-b-0">
              <span className="w-11 h-11 rounded-full shrink-0 flex items-center justify-center bg-[#EEF0F5] text-[#3B4352]"><Mail className="size-5" /></span>
              <span className="flex-[1_1_180px] min-w-0 flex flex-col gap-0.5">
                <span dir="ltr" className="text-right text-[15px] font-bold">
                  {inv.email ?? inv.phoneNumber}
                </span>
                <span className={`text-[13px] ${muted}`}>
                  {roleLabel(inv.role)} · الدعوة صالحة لحد {new Date(inv.expiresAtUtc).toLocaleString("ar-EG", { day: "numeric", month: "long", hour: "numeric", minute: "2-digit" })}
                </span>
              </span>
              <Badge tone="warn">الدعوة مستنية</Badge>
              <button
                type="button"
                disabled={resend.isPending}
                onClick={() => resend.mutate(inv.invitationId)}
                className="min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
              >
                ابعت تاني
              </button>
              <button
                type="button"
                disabled={revoke.isPending}
                onClick={() => revoke.mutate(inv.invitationId)}
                className="min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-[#A8261C] text-[13px] font-semibold"
              >
                إلغاء
              </button>
            </div>
          ))}
        </section>

        <aside className="flex-[1_1_320px] min-w-0 flex flex-col gap-4">
          <div className={`${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[17px] font-bold">ادعو موظف</h2>
            <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
              الإيميل أو رقم الموبايل
              <input
                value={contact}
                onChange={(e) => setContact(e.target.value)}
                dir="ltr"
                placeholder="01xxxxxxxxx"
                className={`${field} text-right`}
              />
            </label>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">الدور</span>
              <div className="flex gap-1.5">
                {ROLES.map(([value, name, label]) => (
                  <button key={name} type="button" aria-pressed={role === value} onClick={() => setRole(value)} className={`${roleChip(role === value)} flex-1 !min-h-[40px]`}>
                    {label}
                  </button>
                ))}
              </div>
            </div>
            <span className={`text-xs ${muted} leading-[1.6]`}>الموظف بيقبل الدعوة من حسابه بنفس الإيميل أو الموبايل اللي اتبعتتله. الدعوة صالحة ٤٨ ساعة.</span>
            <button
              type="button"
              onClick={() => invite.mutate()}
              disabled={!canInvite || invite.isPending}
              className={`min-h-[48px] rounded-[14px] text-[15px] font-bold ${
                canInvite ? `bg-brand-red hover:bg-brand-red-hover text-white ${redShadow}` : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
              }`}
            >
              {invite.isPending ? "جاري الإرسال…" : "ابعت الدعوة"}
            </button>
          </div>

          <div className={`${card} rounded-[20px] px-[18px] py-4 overflow-x-auto`}>
            <table className="w-full border-collapse text-[13px]">
              <caption className="text-right text-base font-bold pb-2">مين يقدر يعمل إيه</caption>
              <thead>
                <tr className={muted}>
                  <th scope="col" className="text-right px-1 py-2 font-semibold">
                    الصلاحية
                  </th>
                  {["مالك", "مدير", "استقبال", "محاسب"].map((h) => (
                    <th key={h} scope="col" className="px-1 py-2 font-semibold">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {MATRIX.map(([label, ...vals]) => (
                  <tr key={label} className="border-t border-[#EEF0F5]">
                    <th scope="row" className="text-right px-1 py-2 font-medium">
                      {label}
                    </th>
                    {vals.map((v, i) => (
                      <td key={i} className={`px-1 py-2 text-center ${v ? "text-[#1B6E3E] font-bold" : "text-[#A8261C]"}`}>
                        {v ? <Check className="size-4 mx-auto" strokeWidth={2.5} aria-label="مسموح" /> : "—"}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </aside>
      </div>

      {toRemove && (
        <div className="fixed inset-0 z-50 bg-[rgba(15,27,61,0.45)] flex items-center justify-center p-4">
          <div role="dialog" aria-modal="true" aria-labelledby="rmT" className="bg-white rounded-[20px] p-6 w-full max-w-[420px] flex flex-col gap-3">
            <h2 id="rmT" className="text-lg font-bold">
              إزالة {toRemove.fullName ?? "الموظف"}؟
            </h2>
            <p className={`text-sm ${muted} leading-[1.7]`}>هيتقفل دخوله على اللوحة فوراً. الحجوزات والمراجعات اللي عملها بتفضل متسجلة باسمه.</p>
            <div className="flex gap-2 justify-end">
              <button type="button" onClick={() => setToRemove(null)} className="min-h-[44px] px-[18px] rounded-xl border border-[#C9CFD9] bg-white text-sm font-semibold">
                رجوع
              </button>
              <button
                type="button"
                disabled={remove.isPending}
                onClick={() => remove.mutate(toRemove.memberId)}
                className="min-h-[44px] px-[18px] rounded-xl bg-brand-red text-white text-sm font-bold"
              >
                إزالة
              </button>
            </div>
          </div>
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
