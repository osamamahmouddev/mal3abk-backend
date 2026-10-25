"use client";

import React, { useState } from "react";
import Link from "next/link";
import { KV, Page, PageHeader, Pill, Toast, Tone, card, chip, muted, useRevealOnSelect } from "@/components/ui";

type Actor = "sara" | "amr" | "nada" | "owner";
type Kind = "venue" | "billing" | "user" | "support" | "plans" | "settings" | "booking";
type KindGroup = "all" | "venue" | "user" | "money" | "sys";
// actor, kind, action, when, before, after, reason, link, link label
type Entry = [Actor, Kind, string, string, string, string, string, string, string];

const LOG: Entry[] = [
  ["sara", "venue", "وافقت على ملعب «أكاديمية الدلتا — فرع ٢»", "من ١٠ دقايق", "status: pending", "status: live", "الورق كامل والصور مطابقة للزيارة.", "/approvals", "فتح الموافقات"],
  ["amr", "billing", "فعّل اشتراك Growth لـ«ملاعب الرواد» بعد تأكيد التحويل", "من ساعة", "subscription: overdue", "subscription: active (Growth)", "تحويل CIB مرجع 88412 اتأكد في كشف الحساب.", "/billing", "فتح الاشتراكات"],
  ["sara", "user", "أوقفت حساب «حازم فوزي»", "من ٣ ساعات", "status: active", "status: suspended", "٣ بلاغات مؤكدة لنفس صورة الإيصال في حجوزات مختلفة.", "/users", "فتح ملف اللاعب"],
  ["nada", "support", "قفلت تذكرة «التطبيق بيقفل وأنا برفع الإيصال»", "امبارح", "ticket: open", "ticket: closed", "اتصلح في إصدار ٢٫٣٫١.", "/support", "فتح التذكرة"],
  ["amr", "plans", "عدّل حد الملاعب في باقة Growth من ٤ لـ ٥", "امبارح", "growth.pitches: 4", "growth.pitches: 5", "طلبات كتير من ملاعب عندها ٥ ملاعب.", "/plans", "فتح الباقات"],
  ["sara", "venue", "خفّت «جرين فيلد» من البحث", "من يومين", "visible: true", "visible: false", "شكاوي متكررة من رفض إثباتات سليمة.", "/venues", "فتح الملعب"],
  ["owner", "settings", "غيّر مهلة قرار النزاع لـ ٢٤ ساعة", "من ٣ أيام", "dispute_sla_hours: 48", "dispute_sla_hours: 24", "تقليل وقت انتظار اللاعيبة.", "/settings", "فتح الإعدادات"],
  ["sara", "booking", "لغت حجز «SH-2307-1102» إدارياً", "من ٤ أيام", "status: confirmed", "status: cancelled_by_admin", "الملعب اتقفل فجأة بسبب عطل كهربا.", "/bookings", "فتح الحجز"],
];

const ACTOR: Record<Actor, string> = { sara: "سارة منصور", amr: "عمرو حمدي", nada: "ندى فاروق", owner: "محمد الشريف" };
const KIND: Record<Kind, [string, Tone]> = {
  venue: ["ملاعب", "ok"],
  billing: ["فواتير", "navy"],
  user: ["مستخدمين", "bad"],
  support: ["دعم", "neutral"],
  plans: ["باقات", "navy"],
  settings: ["إعدادات", "warn"],
  booking: ["حجوزات", "bad"],
};
const KIND_GROUPS: Record<KindGroup, Kind[] | null> = {
  all: null,
  venue: ["venue"],
  user: ["user", "booking"],
  money: ["billing", "plans"],
  sys: ["settings", "support"],
};

export default function AuditPage() {
  const [who, setWho] = useState<"all" | Exclude<Actor, "owner">>("all");
  const [kind, setKind] = useState<KindGroup>("all");
  const [selected, setSelected] = useState(0);
  const detailsRef = useRevealOnSelect<HTMLElement>(selected);
  const [toast, setToast] = useState<string | null>(null);

  const idx = LOG.map((_, i) => i).filter((i) => (who === "all" || LOG[i][0] === who) && (!KIND_GROUPS[kind] || KIND_GROUPS[kind]!.includes(LOG[i][1])));
  const sel = idx.includes(selected) ? selected : idx[0] ?? 0;
  const d = LOG[sel];

  return (
    <Page>
      <PageHeader title="سجل التدقيق" sub="كل إجراء إداري بيتسجل ومينفعش يتمسح.">
        <button
          type="button"
          onClick={() => setToast("جاري تصدير السجل (CSV موقّع).")}
          className="min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold"
        >
          تصدير
        </button>
      </PageHeader>
      <div className="flex flex-wrap gap-2.5 items-center">
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "الكل"],
              ["sara", "سارة"],
              ["amr", "عمرو"],
              ["nada", "ندى"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={who === id} onClick={() => setWho(id)} className={chip(who === id)}>
              {label}
            </button>
          ))}
        </div>
        <div className="flex gap-1.5 flex-wrap">
          {(
            [
              ["all", "كل الأنواع"],
              ["venue", "ملاعب"],
              ["user", "مستخدمين وحجوزات"],
              ["money", "فلوس وباقات"],
              ["sys", "نظام ودعم"],
            ] as const
          ).map(([id, label]) => (
            <button key={id} type="button" aria-pressed={kind === id} onClick={() => setKind(id)} className={chip(kind === id)}>
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_520px] min-w-0 ${card} rounded-[20px] overflow-hidden`}>
          {idx.map((i) => {
            const r = LOG[i];
            return (
              <button
                key={i}
                type="button"
                aria-pressed={i === sel}
                onClick={() => setSelected(i)}
                className={`w-full flex items-center gap-3 px-[18px] py-3.5 border-b border-[#EEF0F5] text-slate-900 ${
                  i === sel ? "bg-[#F3F5FB] shadow-[inset_-3px_0_0_#D63B2F]" : "bg-white"
                }`}
              >
                <Pill tone={KIND[r[1]][1]}>{KIND[r[1]][0]}</Pill>
                <span className="flex-1 min-w-0 text-sm text-right">
                  <b>{ACTOR[r[0]]}</b> {r[2]}
                </span>
                <span className="text-xs text-[#8A94AA] whitespace-nowrap">{r[3]}</span>
              </button>
            );
          })}
          {idx.length === 0 && <p className={`p-8 text-center ${muted}`}>مفيش إجراءات بالفلتر ده.</p>}
        </section>

        {idx.length > 0 && (
          <aside ref={detailsRef} aria-label="تفاصيل الإجراء" className={`scroll-mt-20 flex-[1_1_320px] min-w-0 ${card} rounded-[20px] p-5 flex flex-col gap-3`}>
            <Pill tone={KIND[d[1]][1]} className="self-start">
              {KIND[d[1]][0]}
            </Pill>
            <h2 className="text-[17px] font-bold leading-[1.6]">
              {ACTOR[d[0]]} {d[2]}
            </h2>
            {(
              [
                ["الوقت", d[3]],
                ["عنوان IP", "41.233.x.x"],
                ["الجهاز", "Chrome · Windows"],
                ["رقم الإجراء", `AUD-${90412 - sel}`],
              ] as const
            ).map(([k, v]) => (
              <KV key={k} k={k}>
                <b className="font-semibold" dir="auto">
                  {v}
                </b>
              </KV>
            ))}
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] font-bold">التغيير</span>
              <div className="flex flex-col gap-1 font-mono text-[13px]" dir="ltr">
                <span className="bg-[#FDECEA] text-[#A8261C] rounded-lg px-2.5 py-1.5">− {d[4]}</span>
                <span className="bg-[#E5F5EB] text-[#1B6E3E] rounded-lg px-2.5 py-1.5">+ {d[5]}</span>
              </div>
            </div>
            <div className="bg-surface rounded-xl px-3 py-2.5 text-[13px] leading-[1.6]">
              <b>السبب:</b> {d[6]}
            </div>
            <Link
              href={d[7]}
              className="min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-ink text-[13px] font-semibold flex items-center justify-center"
            >
              {d[8]}
            </Link>
          </aside>
        )}
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
