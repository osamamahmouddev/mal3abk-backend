"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Switch, card, chip, muted, redShadow, toArabicDigits } from "@/components/ui";

const PITCHES: [string, string, string][] = [
  ["ملعب ١", "٥ لاعب · نجيل صناعي", "١٥٠"],
  ["ملعب ٢", "٧ لاعب · نجيل صناعي", "٢٢٠"],
  ["ملعب ٣", "٥ لاعب · نجيل صناعي", "١٥٠"],
  ["ملعب ٤", "٥ لاعب · مغطى", "١٨٠"],
  ["ملعب ٥", "١١ لاعب · نجيل طبيعي", "٤٠٠"],
];
const DAY_NAMES = ["السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة"];
const FACILITIES: [string, string][] = [
  ["light", "إضاءة ليلي"],
  ["rooms", "غرف تغيير"],
  ["shower", "دُش"],
  ["parking", "جراج"],
  ["cafe", "كافيتريا"],
  ["covered", "ملعب مغطى"],
];
const METHODS: [string, string, string][] = [
  ["vf", "فودافون كاش", "0101 234 5678"],
  ["ip", "إنستاباي", "najoum@instapay"],
  ["bank", "تحويل بنكي", "CIB · ••••4821"],
];
const DEPOSITS: [string, string][] = [
  ["0", "بدون عربون"],
  ["50", "٥٠ ج.م ثابت"],
  ["30p", "٣٠٪ من السعر"],
  ["full", "المبلغ كامل"],
];
const DOCS: [string, string, string][] = [
  ["cr", "سجل تجاري أو بطاقة ضريبية", "cr-najoum.pdf"],
  ["lease", "عقد ملكية أو إيجار المكان", "lease.pdf"],
  ["id", "بطاقة المالك", "id-front.jpg"],
];

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const field = "min-h-[46px] border border-[#C9CFD9] rounded-xl px-3 text-[15px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900";
const setupChip = (on: boolean) => chip(on, "min-h-[40px] px-3.5 rounded-xl text-sm");

export default function SetupPage() {
  const [step, setStep] = useState(1);
  const [name, setName] = useState("ملعب النجوم");
  const [touched, setTouched] = useState(false);
  const [fac, setFac] = useState<Record<string, boolean>>({ light: true, rooms: true, parking: true });
  const [pitchCount, setPitchCount] = useState(2);
  const [days, setDays] = useState<Record<number, boolean>>({ 0: true, 1: true, 2: true, 3: true, 4: true, 5: true, 6: true });
  const [methods, setMethods] = useState<Record<string, boolean>>({ vf: true, ip: true });
  const [dep, setDep] = useState("50");
  const [docs, setDocs] = useState<Record<string, boolean>>({ cr: true, lease: false, id: true });
  const [submitted, setSubmitted] = useState(false);

  const nameOk = name.trim().length >= 3;
  const nameErr = touched && !nameOk;
  const docsOk = !!(docs.cr && docs.lease && docs.id);
  const atLimit = pitchCount >= 5;

  const steps: [string, string][] = [
    ["بيانات المكان", "الاسم والعنوان والمرافق"],
    ["الملاعب", `${toArabicDigits(pitchCount)} ملاعب`],
    ["مواعيد الشغل", "المواعيد الأسبوعية"],
    ["الدفع والعربون", "طرق الاستلام"],
    ["المستندات", docsOk ? "جاهزة للإرسال" : "ناقص مستند"],
  ];

  const next = () => {
    if (step === 1 && !nameOk) {
      setTouched(true);
      return;
    }
    if (step < 5) {
      setStep(step + 1);
      return;
    }
    if (docsOk) setSubmitted(true);
  };

  return (
    <div dir="rtl" className="w-full min-h-screen bg-surface text-slate-900 flex flex-col">
      <header className="bg-white border-b border-[#EDF0F5] px-4 sm:px-8 py-4 flex items-center gap-x-4 gap-y-2 flex-wrap">
        <span className="text-2xl font-bold">ملعبك</span>
        <span className="text-[11px] font-bold text-navy-900 bg-brand-gold rounded-md px-2 py-0.5">للملاعب</span>
        <span className="flex-1" />
        <span className={`text-[13px] ${muted}`}>بيتحفظ تلقائياً</span>
        <Link href="/login" className="text-sm font-semibold text-navy-900 underline hover:text-brand-red">
          خروج
        </Link>
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
                onClick={() => {
                  if (n <= step || nameOk) setStep(n);
                }}
                className={`min-h-[60px] px-2.5 py-2 rounded-xl flex items-center gap-3 text-slate-900 ${cur ? "bg-[#EEF1F7]" : "bg-transparent"}`}
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
                <div className="flex flex-col gap-1.5 sm:col-span-2">
                  <label htmlFor="vn" className="text-sm font-semibold">
                    اسم المكان <span className="text-[#A8261C]">*</span>
                  </label>
                  <input
                    id="vn"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    aria-invalid={nameErr}
                    className={`${field} ${nameErr ? "!border-2 !border-[#A8261C]" : ""}`}
                  />
                  {nameErr && <span className="text-xs text-[#A8261C]">اكتب اسم المكان زي ما اللاعبين يعرفوه</span>}
                </div>
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="ar" className="text-sm font-semibold">
                    المنطقة
                  </label>
                  <select id="ar" className={`${field} bg-white`}>
                    <option>مدينة نصر</option>
                    <option>مصر الجديدة</option>
                    <option>التجمع الخامس</option>
                  </select>
                </div>
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="ph" className="text-sm font-semibold">
                    رقم التواصل للاعبين
                  </label>
                  <input id="ph" dir="ltr" defaultValue="0100 044 5566" className={`${field} text-right`} />
                </div>
                <div className="flex flex-col gap-1.5 sm:col-span-2">
                  <label htmlFor="ad" className="text-sm font-semibold">
                    العنوان بالتفصيل
                  </label>
                  <input id="ad" defaultValue="شارع عباس العقاد، بجوار نادي السكة، مدينة نصر" className={field} />
                </div>
              </div>
              <div role="img" aria-label="خريطة لتحديد مكان الملعب" className="h-[180px] rounded-2xl bg-[#E6E9EE] relative overflow-hidden">
                <div className="absolute -left-5 top-[70px] w-[120%] h-3.5 bg-white -rotate-[5deg]" />
                <div className="absolute left-[55%] -top-5 w-3 h-[240px] bg-white rotate-[12deg]" />
                <span className="absolute left-[48%] top-[52px] w-[30px] h-[30px] rounded-[15px_15px_15px_2px] bg-brand-red border-[3px] border-white -rotate-45" />
                <span className="absolute bottom-2.5 right-2.5 bg-white rounded-lg px-2.5 py-1 text-xs font-semibold">اسحب الدبوس لمكان المدخل بالظبط</span>
              </div>
              <div className="flex flex-col gap-2">
                <span className="text-sm font-semibold">المرافق</span>
                <div className="flex flex-wrap gap-2">
                  {FACILITIES.map(([id, label]) => (
                    <button
                      key={id}
                      type="button"
                      aria-pressed={!!fac[id]}
                      onClick={() => setFac({ ...fac, [id]: !fac[id] })}
                      className={setupChip(!!fac[id])}
                    >
                      {fac[id] ? "✓ " : ""}
                      {label}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="flex flex-col gap-4">
              <h1 className="text-2xl font-bold">الملاعب</h1>
              <span className={`text-sm ${muted}`}>كل ملعب ليه مواعيده وسعره. باقة Growth تسمح بـ ٥ ملاعب.</span>
              {PITCHES.slice(0, pitchCount).map(([pname, meta, price]) => (
                <div key={pname} className="border border-[#EDF0F5] rounded-2xl px-4 py-3.5 flex items-center gap-3.5">
                  <span className="w-14 h-14 shrink-0 rounded-xl" style={{ background: pitchPattern }} />
                  <span className="flex-1 flex flex-col gap-0.5">
                    <span className="text-[15px] font-bold">{pname}</span>
                    <span className={`text-[13px] ${muted}`}>{meta}</span>
                  </span>
                  <span className="text-[15px] font-bold">{price} ج.م/ساعة</span>
                  <button
                    type="button"
                    aria-label={`حذف ${pname}`}
                    onClick={() => setPitchCount(Math.max(1, pitchCount - 1))}
                    className="min-w-[40px] min-h-[40px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C]"
                  >
                    ✕
                  </button>
                </div>
              ))}
              <button
                type="button"
                disabled={atLimit}
                onClick={() => !atLimit && setPitchCount(pitchCount + 1)}
                className={`min-h-[52px] rounded-[14px] border-2 border-dashed bg-white text-[15px] font-bold ${
                  atLimit ? "border-[#DCE0E8] text-[#8A94AA] cursor-not-allowed" : "border-[#9AA3B2] text-navy-900"
                }`}
              >
                {atLimit ? "وصلت لحد الباقة (٥ ملاعب)" : "+ إضافة ملعب"}
              </button>
            </div>
          )}

          {step === 3 && (
            <div className="flex flex-col gap-3.5">
              <h1 className="text-2xl font-bold">مواعيد الشغل</h1>
              <span className={`text-sm ${muted}`}>
                المواعيد بعد نص الليل بتتحسب على نفس يوم التشغيل. تقدر تعدّل لكل ملعب بعدين من صفحة المواعيد.
              </span>
              {DAY_NAMES.map((dname, i) => {
                const on = !!days[i];
                return (
                  <div key={dname} className="flex items-center gap-3 py-2.5 border-b border-[#EEF1F5]">
                    <span className="w-[90px] text-sm font-bold">{dname}</span>
                    <Switch on={on} label={`تشغيل يوم ${dname}`} onToggle={() => setDays({ ...days, [i]: !on })} />
                    <span className="flex-1 text-sm text-[#333B4A]">
                      {on ? (i === 6 ? "٢:٠٠ م – ٢:٠٠ ص (اليوم التالي)" : "٤:٠٠ م – ٢:٠٠ ص (اليوم التالي)") : "مقفول"}
                    </span>
                  </div>
                );
              })}
            </div>
          )}

          {step === 4 && (
            <div className="flex flex-col gap-4">
              <h1 className="text-2xl font-bold">الدفع والعربون</h1>
              <div className="flex flex-col gap-2">
                <span className="text-sm font-semibold">طرق استلام العربون</span>
                {METHODS.map(([id, label, detail]) => {
                  const on = !!methods[id];
                  return (
                    <button
                      key={id}
                      type="button"
                      aria-pressed={on}
                      onClick={() => setMethods({ ...methods, [id]: !on })}
                      className={`min-h-[60px] px-3.5 py-2.5 rounded-[14px] bg-white flex items-center gap-3 ${
                        on ? "border-2 border-navy-900" : "border border-[#DCE0E8]"
                      }`}
                    >
                      <span
                        className={`w-[22px] h-[22px] rounded-md flex items-center justify-center text-[13px] font-bold ${
                          on ? "bg-navy-900 text-white" : "border-2 border-[#9AA3B2]"
                        }`}
                      >
                        {on ? "✓" : ""}
                      </span>
                      <span className="flex-1 text-right flex flex-col">
                        <span className="text-sm font-bold text-slate-900">{label}</span>
                        <span dir="ltr" className={`text-right text-[13px] ${muted}`}>
                          {detail}
                        </span>
                      </span>
                    </button>
                  );
                })}
              </div>
              <div className="flex flex-col gap-2">
                <span className="text-sm font-semibold">العربون المطلوب</span>
                <div className="flex gap-2 flex-wrap">
                  {DEPOSITS.map(([id, label]) => (
                    <button key={id} type="button" aria-pressed={dep === id} onClick={() => setDep(id)} className={setupChip(dep === id)}>
                      {label}
                    </button>
                  ))}
                </div>
              </div>
              <div className="bg-surface rounded-xl px-3.5 py-3 text-[13px] leading-[21px] text-[#333B4A]">
                ملعبك مش بيستلم فلوس. اللاعب بيحوّل لك مباشرة، وإنت بتراجع الإيصال من اللوحة.
              </div>
            </div>
          )}

          {step === 5 && (
            <div className="flex flex-col gap-4">
              <h1 className="text-2xl font-bold">المستندات والإرسال للمراجعة</h1>
              {DOCS.map(([id, label, file]) => {
                const ok = !!docs[id];
                return (
                  <div key={id} className="border border-dashed border-[#C9CFD9] rounded-[14px] px-3.5 py-3 flex items-center gap-3">
                    <span
                      className={`w-[30px] h-[30px] rounded-full flex items-center justify-center text-[13px] font-bold ${
                        ok ? "bg-[#E5F5EB] text-[#1B6E3E]" : "bg-[#FDECEA] text-[#A8261C]"
                      }`}
                    >
                      {ok ? "✓" : "!"}
                    </span>
                    <span className="flex-1 flex flex-col">
                      <span className="text-sm font-bold">{label}</span>
                      <span className={`text-xs ${muted}`}>{ok ? `${file} · اترفع` : "مطلوب — PDF أو صورة"}</span>
                    </span>
                    <button
                      type="button"
                      onClick={() => setDocs({ ...docs, [id]: true })}
                      className="min-h-[40px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
                    >
                      {ok ? "استبدال" : "رفع"}
                    </button>
                  </div>
                );
              })}
              {submitted && (
                <>
                  <div role="status" className="bg-[#E5F5EB] text-[#155C33] rounded-[14px] px-4 py-3.5 text-sm leading-[22px] font-semibold">
                    ✓ اتبعت للمراجعة. فريق ملعبك هيرد خلال يومين عمل، وتقدر تستخدم اللوحة دلوقتي وتجهّز الحجوزات.
                  </div>
                  <Link
                    href="/overview"
                    className="min-h-[50px] rounded-[14px] bg-navy-900 text-white text-[15px] font-bold flex items-center justify-center"
                  >
                    افتح لوحة الملعب
                  </Link>
                </>
              )}
            </div>
          )}

          <div className="flex gap-2.5 border-t border-[#EEF1F5] pt-[18px]">
            {step > 1 && !submitted && (
              <button
                type="button"
                onClick={() => setStep(step - 1)}
                className="min-h-[48px] px-[22px] rounded-[14px] border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold"
              >
                رجوع
              </button>
            )}
            <span className="flex-1" />
            {!submitted && (
              <button
                type="button"
                onClick={next}
                className={`min-h-[48px] px-7 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-[15px] font-bold ${redShadow}`}
              >
                {step < 5 ? "التالي" : docsOk ? "إرسال للمراجعة" : "ارفع المستندات الناقصة الأول"}
              </button>
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
