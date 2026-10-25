"use client";

import React, { useState } from "react";
import { Pill, Page, Switch, Tabs, Toast, card, chip, inputClass, muted, redShadow, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Tab = "booking" | "areas" | "templates" | "team";
type RuleKey = "maxDep" | "proofMin" | "proofMax" | "reviewMax" | "dispute";
type Method = "vc" | "ip" | "bank" | "cash";
type Role = "owner" | "ops" | "finance" | "support";

const RULES: [RuleKey, string, string, [number, string][]][] = [
  ["maxDep", "أقصى نسبة عربون", "الملاعب ماتقدرش تطلب أكتر من كده", [[33, "٣٣٪"], [50, "٥٠٪"], [100, "السعر كله"]]],
  ["proofMin", "أقل مهلة لرفع الإثبات", "عشان اللاعب يلحق يحوّل", [[10, "١٠ دقايق"], [15, "١٥ دقيقة"], [30, "٣٠ دقيقة"]]],
  ["proofMax", "أقصى مهلة لرفع الإثبات", "عشان الميعاد مايفضلش محجوز", [[30, "٣٠ دقيقة"], [60, "ساعة"], [120, "ساعتين"]]],
  ["reviewMax", "أقصى مهلة لمراجعة الملعب", "بعدها الحجز بيتصعّد للإدارة", [[60, "ساعة"], [180, "٣ ساعات"], [360, "٦ ساعات"]]],
  ["dispute", "مهلة قرار النزاع", "من فتح النزاع", [[12, "١٢ ساعة"], [24, "٢٤ ساعة"], [48, "٤٨ ساعة"]]],
];
const METHODS: [Method, string][] = [
  ["vc", "فودافون كاش"],
  ["ip", "إنستاباي"],
  ["bank", "تحويل بنكي"],
  ["cash", "كاش في الملعب"],
];
const AREAS: [string, string][] = [
  ["القاهرة", "١٤٢ ملعب"],
  ["الجيزة", "٦١ ملعب"],
  ["الإسكندرية", "١٨ ملعب"],
  ["الدلتا (المنصورة وطنطا)", "١٠ ملاعب — تجريبي"],
  ["القناة", "لسه مفيش"],
  ["الصعيد", "لسه مفيش"],
];
// name, channels, default body
const TEMPLATES: [string, string, string][] = [
  ["تأكيد الحجز", "SMS + إشعار", "تم تأكيد حجزك في {venue} يوم {date} الساعة {time}. كود الحجز {code}."],
  ["تذكير بالعربون", "SMS + إشعار", "فاضل {minutes} دقيقة تحوّل عربون {deposit} ج.م لـ {venue} وإلا الحجز هيتلغي."],
  ["رفض الإثبات", "إشعار", "الملعب رفض إثبات الدفع لحجز {code}. ارفع إثبات جديد أو كلّم الدعم."],
  ["تذكير قبل الماتش", "إشعار", "ماتشك في {venue} بعد ساعتين. متتأخرش!"],
  ["كود الدخول", "SMS", "كود الدخول لملعبك: {otp}. ماتديهوش لحد."],
];
const SAMPLE: Record<string, string> = {
  venue: "ملعب النجوم",
  date: "السبت ٢٦ يوليو",
  time: "٨ م",
  code: "KH-2607-0142",
  minutes: "١٥",
  deposit: "٥٠",
  otp: "٤٨٢٧",
};
const VARS = ["{venue}", "{date}", "{time}", "{code}", "{deposit}"];
// Arabic SMS segment length (UCS-2).
const SMS_SEGMENT = 70;

const ADMINS: [string, Role, boolean, string][] = [
  ["سارة منصور", "ops", true, "دلوقتي"],
  ["عمرو حمدي", "finance", true, "من ساعة"],
  ["ندى فاروق", "support", false, "امبارح"],
  ["محمد الشريف", "owner", true, "من ٣ أيام"],
];
const ROLE_LABEL: Record<Role, string> = { owner: "مالك المنصة", ops: "عمليات", finance: "مالية", support: "دعم" };

export default function SettingsPage() {
  const [tab, setTab] = useState<Tab>("booking");
  const [rules, setRules] = useState<Record<RuleKey, number>>({ maxDep: 50, proofMin: 15, proofMax: 60, reviewMax: 180, dispute: 24 });
  const [methods, setMethods] = useState<Record<Method, boolean>>({ vc: true, ip: true, bank: true, cash: false });
  const [areas, setAreas] = useState<Record<number, boolean>>({ 0: true, 1: true, 2: true, 3: false, 4: false, 5: false });
  const [tpl, setTpl] = useState(0);
  const detailsRef = useRevealOnSelect<HTMLElement>(tpl);
  const [bodies, setBodies] = useState<Record<number, string>>({});
  const [roles, setRoles] = useState<Record<number, Role>>({});
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<string | null>(null);

  const body = bodies[tpl] ?? TEMPLATES[tpl][2];
  const preview = body.replace(/\{(\w+)\}/g, (_, k: string) => SAMPLE[k] ?? `{${k}}`);
  const parts = Math.ceil(preview.length / SMS_SEGMENT);

  const toggleMethod = (k: Method) => {
    if (methods[k] && Object.values(methods).filter(Boolean).length === 1) {
      setToast("لازم طريقة واحدة على الأقل.");
      return;
    }
    setMethods({ ...methods, [k]: !methods[k] });
    setDirty(true);
  };

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">إعدادات المنصة</h1>
        {dirty && <span className="text-[13px] font-bold bg-[#FEF3D6] text-[#7A4D00] rounded-full px-3 py-1">فيه تعديلات مش محفوظة</span>}
      </div>
      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          ["booking", "قواعد الحجز"],
          ["areas", "المناطق"],
          ["templates", "قوالب الرسائل"],
          ["team", "فريق الإدارة"],
        ]}
      />

      {tab === "booking" && (
        <section className={`${card} rounded-[20px] px-5 pt-2 pb-3 flex flex-col`}>
          {RULES.map(([k, label, hint, opts]) => (
            <div key={k} className="flex flex-wrap items-center gap-2.5 py-3.5 border-b border-[#EEF0F5]">
              <span className="flex-[1_1_260px] flex flex-col gap-0.5">
                <span className="text-sm font-bold">{label}</span>
                <span className={`text-xs ${muted}`}>{hint}</span>
              </span>
              <div className="flex gap-1.5 flex-wrap">
                {opts.map(([val, l]) => (
                  <button
                    key={val}
                    type="button"
                    aria-pressed={rules[k] === val}
                    onClick={() => {
                      setRules({ ...rules, [k]: val });
                      setDirty(true);
                    }}
                    className={chip(rules[k] === val)}
                  >
                    {l}
                  </button>
                ))}
              </div>
            </div>
          ))}
          <div className="flex flex-wrap items-center gap-2.5 py-3.5">
            <span className="flex-[1_1_260px] flex flex-col gap-0.5">
              <span className="text-sm font-bold">طرق دفع العربون المسموحة</span>
              <span className={`text-xs ${muted}`}>الملاعب تختار منها بس</span>
            </span>
            <div className="flex gap-1.5 flex-wrap">
              {METHODS.map(([k, label]) => (
                <button key={k} type="button" aria-pressed={methods[k]} onClick={() => toggleMethod(k)} className={chip(methods[k])}>
                  {label}
                </button>
              ))}
            </div>
          </div>
        </section>
      )}

      {tab === "areas" && (
        <section className={`${card} rounded-[20px] px-5 pt-2 pb-3`}>
          {AREAS.map(([name, meta], i) => {
            const on = !!areas[i];
            return (
              <div key={name} className="flex items-center gap-3 py-3.5 border-b border-[#EEF0F5] last:border-b-0">
                <span className="flex-1 flex flex-col gap-0.5">
                  <span className="text-[15px] font-bold">{name}</span>
                  <span className={`text-[13px] ${muted}`}>
                    {meta} · {on ? "مفتوحة للحجز" : "مقفولة"}
                  </span>
                </span>
                <Switch
                  on={on}
                  label={`تفعيل ${name}`}
                  onToggle={() => {
                    setAreas({ ...areas, [i]: !on });
                    setDirty(true);
                  }}
                />
              </div>
            );
          })}
        </section>
      )}

      {tab === "templates" && (
        <div className="flex flex-wrap gap-4 items-start">
          <section className={`flex-[1_1_280px] min-w-0 ${card} rounded-[20px] overflow-hidden`}>
            {TEMPLATES.map(([label, ch], i) => (
              <button
                key={label}
                type="button"
                aria-pressed={tpl === i}
                onClick={() => setTpl(i)}
                className={`w-full text-right flex flex-col gap-0.5 px-4 py-3 border-b border-[#EEF0F5] text-slate-900 ${
                  tpl === i ? "bg-[#F3F5FB] shadow-[inset_-3px_0_0_#D63B2F]" : "bg-white"
                }`}
              >
                <span className="text-sm font-bold">{label}</span>
                <span className={`text-xs ${muted}`}>{ch}</span>
              </button>
            ))}
          </section>
          <section ref={detailsRef} className={`scroll-mt-20 flex-[999_1_420px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <h2 className="text-[17px] font-bold">{TEMPLATES[tpl][0]}</h2>
            <label htmlFor="tb" className="text-[13px] font-semibold">
              نص الرسالة
            </label>
            <input
              id="tb"
              value={body}
              onChange={(e) => {
                setBodies({ ...bodies, [tpl]: e.target.value });
                setDirty(true);
              }}
              className={inputClass}
            />
            <div className="flex gap-1.5 flex-wrap">
              {VARS.map((v) => (
                <button
                  key={v}
                  type="button"
                  dir="ltr"
                  onClick={() => {
                    setBodies({ ...bodies, [tpl]: `${body} ${v}` });
                    setDirty(true);
                  }}
                  className="min-h-[32px] px-2.5 rounded-lg border border-dashed border-[#9AA3B5] bg-white text-xs font-mono text-[#3B4352]"
                >
                  {v}
                </button>
              ))}
            </div>
            <span className="text-[13px] font-semibold">معاينة SMS</span>
            <div className="self-start max-w-[360px] bg-[#E9EEF7] rounded-[16px_16px_4px_16px] px-3.5 py-3 text-sm leading-[1.7]">{preview}</div>
            <span className={`text-xs font-semibold ${parts > 2 ? "text-[#A8261C]" : muted}`}>
              {toArabicDigits(preview.length)} حرف · {toArabicDigits(parts)} {parts > 1 ? "رسايل SMS" : "رسالة SMS"}
            </span>
          </section>
        </div>
      )}

      {tab === "team" && (
        <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 overflow-x-auto`}>
          <table className="w-full border-collapse text-sm min-w-[560px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                {["العضو", "الدور", "التحقق بخطوتين", "آخر دخول"].map((h) => (
                  <th key={h} scope="col" className="text-right px-2 py-3 font-semibold">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {ADMINS.map(([name, role, tfa, last], i) => {
                const current = roles[i] ?? role;
                const options: Role[] = current === "owner" ? ["owner"] : ["ops", "finance", "support"];
                return (
                  <tr key={name} className="border-t border-[#EEF0F5]">
                    <td className="px-2 py-3 font-bold">{name}</td>
                    <td className="px-2 py-3">
                      <div className="flex gap-1 flex-wrap">
                        {options.map((r) => (
                          <button
                            key={r}
                            type="button"
                            aria-pressed={current === r}
                            disabled={current === "owner"}
                            onClick={() => {
                              if (current === "owner" || current === r) return;
                              setRoles({ ...roles, [i]: r });
                              setToast(`${name} بقى دوره: ${ROLE_LABEL[r]}.`);
                            }}
                            className={chip(current === r, "min-h-[32px] px-3 rounded-[10px] text-[13px]")}
                          >
                            {ROLE_LABEL[r]}
                          </button>
                        ))}
                      </div>
                    </td>
                    <td className="px-2 py-3">
                      <Pill tone={tfa ? "ok" : "bad"}>{tfa ? "مفعّل" : "مش مفعّل"}</Pill>
                    </td>
                    <td className={`px-2 py-3 ${muted}`}>{last}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <button
            type="button"
            onClick={() => setToast("اتبعتت دعوة على إيميل الشغل — لازم يفعّل التحقق بخطوتين قبل أول دخول.")}
            className="mt-3.5 min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold"
          >
            + دعوة عضو إدارة
          </button>
        </section>
      )}

      {tab !== "team" && (
        <div className="flex justify-end">
          <button
            type="button"
            onClick={() => {
              setDirty(false);
              setToast("اتحفظت الإعدادات. القواعد الجديدة بتطبّق على الحجوزات الجديدة بس.");
            }}
            className={`min-h-[46px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow}`}
          >
            حفظ
          </button>
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
