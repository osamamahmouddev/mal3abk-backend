"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Badge, Page, Switch, Toast, card, chip, inputClass, muted, redShadow, toArabicDigits } from "@/components/ui";

type Tab = "info" | "pay" | "account";

const AMENITIES: [string, string][] = [
  ["park", "باركينج"],
  ["light", "إضاءة ليلي"],
  ["wc", "حمامات"],
  ["cafe", "كافيتريا"],
  ["locker", "غرف تغيير"],
  ["ball", "كور للإيجار"],
];
// name, short, color, full, masked
const PAYMENTS: [string, string, string, string, string][] = [
  ["فودافون كاش", "VC", "bg-[#E60000]", "0100 123 4567", "0100 ••• ••67"],
  ["إنستاباي", "IP", "bg-[#5B2C83]", "nogoom@instapay", "nog•••@instapay"],
  ["تحويل بنكي", "BK", "bg-navy-900", "CIB · 1000 2233 4455 6677", "CIB · •••• 6677"],
];

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const smallBtn = "min-h-[36px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold";

export default function SettingsPage() {
  const [tab, setTab] = useState<Tab>("info");
  const [vName, setVName] = useState("ملعب النجوم — مدينة نصر");
  const [vAddr, setVAddr] = useState("شارع عباس العقاد، مدينة نصر، القاهرة");
  const [vDesc, setVDesc] = useState("٣ ملاعب نجيل صناعي متكيفين بإضاءة ليلي، جنب سيتي ستارز.");
  const [amen, setAmen] = useState<Record<string, boolean>>({ park: true, light: true, wc: true, cafe: false, locker: true, ball: false });
  const [photos, setPhotos] = useState(3);
  const [pub, setPub] = useState(true);
  const [reveal, setReveal] = useState<Record<number, boolean>>({});
  const [payOn, setPayOn] = useState<Record<number, boolean>>({ 0: true, 1: true, 2: false });
  const [toast, setToast] = useState<string | null>(null);

  const togglePay = (i: number) => {
    const on = !!payOn[i];
    if (on && Object.values(payOn).filter(Boolean).length === 1) {
      setToast("لازم طريقة دفع واحدة على الأقل تفضل شغالة.");
      return;
    }
    setPayOn({ ...payOn, [i]: !on });
  };

  return (
    <Page>
      <h1 className="text-[26px] font-bold">الإعدادات</h1>

      <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8] flex-wrap">
        {(
          [
            ["info", "بيانات الملعب"],
            ["pay", "طرق استلام العربون"],
            ["account", "الحساب"],
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

      {tab === "info" && (
        <div className="flex flex-wrap gap-4 items-start">
          <section className={`flex-[999_1_440px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3.5`}>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="vn" className="text-[13px] font-semibold">
                اسم الملعب
              </label>
              <input id="vn" value={vName} onChange={(e) => setVName(e.target.value)} className={inputClass} />
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="va" className="text-[13px] font-semibold">
                العنوان
              </label>
              <input id="va" value={vAddr} onChange={(e) => setVAddr(e.target.value)} className={inputClass} />
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="vd" className="text-[13px] font-semibold">
                وصف قصير (بيظهر في صفحة الملعب)
              </label>
              <textarea
                id="vd"
                rows={3}
                value={vDesc}
                onChange={(e) => setVDesc(e.target.value)}
                className="border border-[#C9CFD9] rounded-xl px-3 py-2.5 text-sm resize-y focus:outline-none focus:ring-2 focus:ring-navy-900"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">المميزات</span>
              <div className="flex gap-1.5 flex-wrap">
                {AMENITIES.map(([k, label]) => (
                  <button
                    key={k}
                    type="button"
                    aria-pressed={!!amen[k]}
                    onClick={() => setAmen({ ...amen, [k]: !amen[k] })}
                    className={chip(!!amen[k], "min-h-[38px] px-3.5 rounded-[10px] text-[13px]")}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-semibold">الصور</span>
              <div className="flex gap-2 flex-wrap">
                {Array.from({ length: photos }, (_, i) => (
                  <span key={i} className="w-24 h-[72px] rounded-xl flex items-end justify-start p-1" style={{ background: pitchPattern }}>
                    <span className="text-[11px] text-white bg-[rgba(15,27,61,0.6)] rounded-md px-1.5 py-px">
                      {i === 0 ? "الغلاف" : toArabicDigits(i + 1)}
                    </span>
                  </span>
                ))}
                <button
                  type="button"
                  onClick={() => {
                    setToast(photos >= 8 ? "أقصى عدد ٨ صور." : "اتضافت صورة.");
                    setPhotos(Math.min(8, photos + 1));
                  }}
                  className="w-24 h-[72px] rounded-xl border-[1.5px] border-dashed border-[#9AA3B5] bg-white text-[#3B4352] text-[13px] font-semibold"
                >
                  + صورة
                </button>
              </div>
            </div>
          </section>
          <aside className={`flex-[1_1_300px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[17px] font-bold">حالة النشر</h2>
            <Badge tone="ok" className="self-start !py-1 !px-3">
              ✓ معتمد من ملعبك
            </Badge>
            <div className="flex items-center gap-2.5 bg-surface rounded-xl px-3 py-2.5">
              <span className="flex-1 flex flex-col">
                <span className="text-sm font-semibold">{pub ? "ظاهر للاعيبة" : "مخفي مؤقتاً"}</span>
                <span className={`text-xs ${muted}`}>{pub ? "بيظهر في البحث والحجز مفتوح" : "محدش يقدر يحجز جديد"}</span>
              </span>
              <Switch
                on={pub}
                label="نشر الملعب"
                onToggle={() => {
                  setPub(!pub);
                  setToast(pub ? "الملعب اتخفى. الحجوزات المأكدة زي ما هي." : "الملعب رجع ظاهر.");
                }}
              />
            </div>
            <span className={`text-xs ${muted} leading-[1.6]`}>تغيير الاسم أو العنوان بيتراجع من فريق ملعبك قبل ما يظهر للاعيبة.</span>
          </aside>
        </div>
      )}

      {tab === "pay" && (
        <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 flex flex-col`}>
          {PAYMENTS.map(([name, short, color, full, masked], i) => {
            const on = !!payOn[i];
            const shown = !!reveal[i];
            return (
              <div key={name} className="flex flex-wrap items-center gap-3 py-3.5 border-b border-[#EEF0F5]">
                <span className={`w-11 h-11 rounded-xl shrink-0 flex items-center justify-center text-[13px] font-bold text-white ${color}`}>{short}</span>
                <span className="flex-[1_1_200px] flex flex-col gap-0.5">
                  <span className="text-[15px] font-bold">{name}</span>
                  <span dir="ltr" className={`text-[13px] ${muted} text-right`}>
                    {shown ? full : masked}
                  </span>
                </span>
                <button type="button" onClick={() => setReveal({ ...reveal, [i]: !shown })} className={smallBtn}>
                  {shown ? "إخفاء" : "إظهار"}
                </button>
                <Switch on={on} label={`تفعيل ${name}`} onToggle={() => togglePay(i)} />
              </div>
            );
          })}
          <p className={`mt-3 text-xs ${muted}`}>البيانات دي بتظهر كاملة للاعب بس بعد ما يحجز، عشان يحوّل العربون.</p>
        </section>
      )}

      {tab === "account" && (
        <section className={`${card} rounded-[20px] p-5 flex flex-col gap-3.5 max-w-[640px]`}>
          <AccountRow label="صاحب الحساب" value="محمود عبد الله">
            <Link href="/staff" className="text-[13px] font-bold text-navy-900 underline hover:text-brand-red">
              إدارة الموظفين
            </Link>
          </AccountRow>
          <AccountRow label="الموبايل" value={<span dir="ltr">0100 123 4567</span>}>
            <button type="button" onClick={() => setToast("هيوصلك كود على الرقم الجديد للتأكيد.")} className={smallBtn}>
              تغيير
            </button>
          </AccountRow>
          <AccountRow label="كلمة السر" value="••••••••">
            <button type="button" onClick={() => setToast("بعتنالك لينك تغيير كلمة السر على الموبايل.")} className={smallBtn}>
              تغيير
            </button>
          </AccountRow>
          <AccountRow label="الاشتراك" value="باقة Growth">
            <Link href="/subscription" className="text-[13px] font-bold text-navy-900 underline hover:text-brand-red">
              تفاصيل الاشتراك
            </Link>
          </AccountRow>
          <AccountRow label="المساعدة" value="فريق دعم الملاعب">
            <button type="button" onClick={() => setToast("فتحنا محادثة مع دعم الملاعب — هيردّوا خلال ساعة.")} className={smallBtn}>
              كلّم الدعم
            </button>
          </AccountRow>
        </section>
      )}

      {tab !== "account" && (
        <div className="flex justify-end">
          <button
            type="button"
            onClick={() => setToast(tab === "info" ? "اتحفظت البيانات. تغيير الاسم والعنوان هيتراجع الأول." : "اتحفظت طرق الدفع.")}
            className={`min-h-[46px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow}`}
          >
            حفظ التعديلات
          </button>
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function AccountRow({ label, value, children }: { label: string; value: React.ReactNode; children: React.ReactNode }) {
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
