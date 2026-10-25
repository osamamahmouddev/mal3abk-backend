"use client";

import React, { useState } from "react";
import { Badge, Page, Toast, card, chip, muted, redShadow, toArabicDigits } from "@/components/ui";

type Role = "owner" | "mgr" | "rec";

interface Member {
  name: string;
  phone: string;
  role: Role;
  pending?: boolean;
}

const ROLE_LABEL: Record<Role, string> = { owner: "مالك", mgr: "مدير", rec: "استقبال" };
const LIMIT = 5;
// permission, owner, manager, reception
const MATRIX: [string, boolean, boolean, boolean][] = [
  ["مراجعة إثباتات الدفع", true, true, true],
  ["حجز يدوي وحظر مواعيد", true, true, true],
  ["إلغاء حجز مأكد", true, true, false],
  ["تعديل الأسعار والمواعيد", true, true, false],
  ["التقارير المالية", true, true, false],
  ["الاشتراك والموظفين", true, false, false],
];

const roleChip = (on: boolean) => chip(on, "min-h-[34px] px-3 rounded-[10px] text-[13px]");

export default function StaffPage() {
  const [staff, setStaff] = useState<Member[]>([
    { name: "محمود عبد الله", phone: "0100 123 4567", role: "owner" },
    { name: "كريم فتحي", phone: "0111 987 6543", role: "mgr" },
    { name: "سارة عادل", phone: "0122 456 7890", role: "rec" },
    { name: "حسام نبيل", phone: "0155 222 3344", role: "rec", pending: true },
  ]);
  const [iName, setIName] = useState("");
  const [iPhone, setIPhone] = useState("");
  const [iRole, setIRole] = useState<"mgr" | "rec">("rec");
  const [confirm, setConfirm] = useState<number | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const atLimit = staff.length >= LIMIT;
  const digits = iPhone.replace(/\D/g, "");
  const phoneOk = /^01\d{9}$/.test(digits);
  const phoneErr = iPhone.length > 0 && !phoneOk;
  const canInvite = !atLimit && iName.trim().length > 1 && phoneOk;

  const setRole = (i: number, r: Role) => {
    const m = staff[i];
    if (m.role === "owner" || r === "owner" || m.role === r) return;
    setStaff(staff.map((x, j) => (j === i ? { ...x, role: r } : x)));
    setToast(`${m.name} بقى ${ROLE_LABEL[r]}.`);
  };

  const invite = () => {
    if (!canInvite) return;
    setStaff([
      ...staff,
      { name: iName.trim(), phone: digits.replace(/^(\d{4})(\d{3})(\d{4})$/, "$1 $2 $3"), role: iRole, pending: true },
    ]);
    setToast(`اتبعتت دعوة لـ ${iName.trim()}. هيدخل بكود على موبايله.`);
    setIName("");
    setIPhone("");
  };

  const toRemove = confirm != null ? staff[confirm] : undefined;

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">الموظفين والصلاحيات</h1>
        <span className={`text-[13px] ${muted}`}>
          {toArabicDigits(staff.length)} من {toArabicDigits(LIMIT)} حسابات في باقة Growth
        </span>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_460px] min-w-0 ${card} rounded-[20px] px-5 py-2`}>
          {staff.map((m, i) => (
            <div key={`${m.name}-${i}`} className="flex flex-wrap items-center gap-3 py-3.5 border-b border-[#EEF0F5] last:border-b-0">
              <span
                className={`w-11 h-11 rounded-full shrink-0 flex items-center justify-center text-[17px] font-bold ${
                  m.role === "owner" ? "bg-brand-gold text-navy-900" : "bg-navy-900 text-white"
                }`}
              >
                {m.name.charAt(0)}
              </span>
              <span className="flex-[1_1_180px] min-w-0 flex flex-col gap-0.5">
                <span className="text-[15px] font-bold">{m.name}</span>
                <span className={`text-[13px] ${muted}`}>{toArabicDigits(m.phone)}</span>
              </span>
              {m.pending && <Badge tone="warn">الدعوة مستنية</Badge>}
              <div className="flex gap-1 flex-wrap">
                {(["owner", "mgr", "rec"] as Role[]).map((r) => {
                  const locked = m.role === "owner" || r === "owner";
                  return (
                    <button
                      key={r}
                      type="button"
                      aria-pressed={m.role === r}
                      disabled={locked}
                      onClick={() => setRole(i, r)}
                      className={`${roleChip(m.role === r)} ${locked && m.role !== r ? "opacity-45 cursor-not-allowed" : ""}`}
                    >
                      {ROLE_LABEL[r]}
                    </button>
                  );
                })}
              </div>
              {m.role !== "owner" && (
                <button
                  type="button"
                  onClick={() => setConfirm(i)}
                  className="min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-[#A8261C] text-[13px] font-semibold"
                >
                  إزالة
                </button>
              )}
            </div>
          ))}
        </section>

        <aside className="flex-[1_1_320px] min-w-0 flex flex-col gap-4">
          <div className={`${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[17px] font-bold">ادعو موظف</h2>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="sn" className="text-[13px] font-semibold">
                الاسم
              </label>
              <input
                id="sn"
                value={iName}
                onChange={(e) => setIName(e.target.value)}
                className="min-h-[44px] border border-[#C9CFD9] rounded-xl px-3 text-sm focus:outline-none focus:ring-2 focus:ring-navy-900"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="sp" className="text-[13px] font-semibold">
                رقم الموبايل
              </label>
              <input
                id="sp"
                value={iPhone}
                onChange={(e) => setIPhone(e.target.value)}
                inputMode="tel"
                dir="ltr"
                placeholder="01xxxxxxxxx"
                aria-invalid={phoneErr}
                className={`min-h-[44px] border rounded-xl px-3 text-sm text-right focus:outline-none focus:ring-2 focus:ring-navy-900 ${
                  phoneErr ? "border-[#A8261C]" : "border-[#C9CFD9]"
                }`}
              />
              {phoneErr && (
                <span role="alert" className="text-xs text-[#A8261C]">
                  الرقم لازم يكون ١١ رقم ويبدأ بـ 01
                </span>
              )}
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">الدور</span>
              <div className="flex gap-1.5">
                {(
                  [
                    ["mgr", "مدير"],
                    ["rec", "استقبال"],
                  ] as const
                ).map(([id, label]) => (
                  <button
                    key={id}
                    type="button"
                    aria-pressed={iRole === id}
                    onClick={() => setIRole(id)}
                    className={`${roleChip(iRole === id)} flex-1 !min-h-[40px]`}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
            <button
              type="button"
              onClick={invite}
              disabled={!canInvite}
              className={`min-h-[48px] rounded-[14px] text-[15px] font-bold ${
                canInvite ? `bg-brand-red hover:bg-brand-red-hover text-white ${redShadow}` : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
              }`}
            >
              {atLimit ? "وصلت لحد الباقة" : "ابعت الدعوة بـ SMS"}
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
                  <th scope="col" className="px-1 py-2 font-semibold">
                    مالك
                  </th>
                  <th scope="col" className="px-1 py-2 font-semibold">
                    مدير
                  </th>
                  <th scope="col" className="px-1 py-2 font-semibold">
                    استقبال
                  </th>
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
                        {v ? "✓" : "—"}
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
              إزالة {toRemove.name}؟
            </h2>
            <p className={`text-sm ${muted} leading-[1.7]`}>هيتقفل دخوله على اللوحة فوراً. الحجوزات والمراجعات اللي عملها بتفضل متسجلة باسمه.</p>
            <div className="flex gap-2 justify-end">
              <button
                type="button"
                onClick={() => setConfirm(null)}
                className="min-h-[44px] px-[18px] rounded-xl border border-[#C9CFD9] bg-white text-sm font-semibold"
              >
                رجوع
              </button>
              <button
                type="button"
                onClick={() => {
                  setStaff(staff.filter((_, j) => j !== confirm));
                  setConfirm(null);
                  setToast(`اتشال ${toRemove.name} من اللوحة.`);
                }}
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
