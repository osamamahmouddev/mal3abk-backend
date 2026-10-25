"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, Toast, card, chip, muted, redShadow } from "@/components/ui";

type Kind = "pay" | "book" | "warn" | "sys";
type Category = "proof" | "book" | "sys";
type Filter = "all" | "unread" | "proof" | "book";

const KIND: Record<Kind, [string, string]> = {
  pay: ["ج", "bg-[#FEF3D6] text-[#7A4D00]"],
  book: ["ح", "bg-[#E5F5EB] text-[#1B6E3E]"],
  warn: ["!", "bg-[#FDECEA] text-[#A8261C]"],
  sys: ["م", "bg-[#EEF0F5] text-[#3B4352]"],
};

// kind, category, title, body, time, cta, href, unread
const NOTIFICATIONS: [Kind, Category, string, string, string, string, string, boolean][] = [
  ["pay", "proof", "إثبات دفع جديد — KH-2607-0145", "عمر خالد رفع صورة تحويل ٥٠ ج.م على فودافون كاش. مستني مراجعتك.", "من ٤ دقايق", "راجع الإثبات", "/reservations/proof-review", true],
  ["warn", "proof", "مهلة المراجعة قربت تخلص", "حجز KH-2607-0142 (أحمد سامي) فاضله ١٥ دقيقة ويتلغي أوتوماتيك لو ماتراجعش.", "من ٢٠ دقيقة", "راجع دلوقتي", "/reservations/proof-review", true],
  ["book", "book", "حجز جديد مستني عربون", "يوسف حسن حجز ملعب ٢ يوم الأحد ٩ م. لسه مادفعش العربون.", "من ساعة", "تفاصيل الحجز", "/reservations", true],
  ["warn", "book", "حجز اتلغى", "فريق الصقور لغى حجز الخميس ٧ م قبل المهلة — العربون اترجع رصيد.", "النهارده ١١ ص", "شوف التقويم", "/calendar?view=day", false],
  ["book", "book", "أكاديمية الدلتا أكدت الحجز الثابت", "حجز كل خميس ١٠ م على ملعب ٢ اتجدد للشهر الجاي.", "امبارح", "الحجوزات الثابتة", "/schedule", false],
  ["sys", "sys", "فاتورة الاشتراك جاهزة", "فاتورة باقة Growth لشهر أغسطس اتصدرت.", "من يومين", "الفواتير", "/subscription", false],
];

const PREF_ROWS: [string, string][] = [
  ["proof", "إثبات دفع اترفع / مهلة المراجعة"],
  ["newB", "حجز جديد"],
  ["cancel", "إلغاء حجز"],
  ["expire", "حجز اتلغى لعدم الدفع"],
  ["review", "تقييم جديد من لاعب"],
  ["billing", "الاشتراك والفواتير"],
];
const CHANNELS = ["اللوحة", "SMS", "واتساب", "إيميل"];

export default function NotificationsPage() {
  const [tab, setTab] = useState<"inbox" | "prefs">("inbox");
  const [filter, setFilter] = useState<Filter>("all");
  const [read, setRead] = useState<Record<string, boolean>>({});
  const [prefs, setPrefs] = useState<Record<string, number[]>>({
    proof: [1, 1, 1, 0],
    newB: [1, 0, 1, 0],
    cancel: [1, 1, 1, 0],
    expire: [1, 0, 0, 0],
    review: [1, 0, 0, 1],
    billing: [1, 0, 0, 1],
  });
  const [toast, setToast] = useState<string | null>(null);

  const list = NOTIFICATIONS.filter(
    (n) => filter === "all" || (filter === "unread" ? n[7] && !read[n[2]] : n[1] === filter)
  );

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_200px] text-[26px] font-bold">الإشعارات</h1>
        <button
          type="button"
          onClick={() => {
            setRead(Object.fromEntries(NOTIFICATIONS.map((n) => [n[2], true])));
            setToast("كل الإشعارات اتعلّمت كمقروءة.");
          }}
          className="min-h-[40px] px-4 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
        >
          علّم الكل كمقروء
        </button>
      </div>

      <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8]">
        {(
          [
            ["inbox", "الوارد"],
            ["prefs", "إعدادات الإشعارات"],
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

      {tab === "inbox" && (
        <div className="flex flex-col gap-3">
          <div className="flex gap-1.5 flex-wrap">
            {(
              [
                ["all", "الكل"],
                ["unread", "مش مقروء"],
                ["proof", "الدفع"],
                ["book", "الحجوزات"],
              ] as const
            ).map(([id, label]) => (
              <button
                key={id}
                type="button"
                aria-pressed={filter === id}
                onClick={() => setFilter(id)}
                className={chip(filter === id, "min-h-[38px] px-3.5 rounded-[10px] text-[13px]")}
              >
                {label}
              </button>
            ))}
          </div>
          <section className={`${card} rounded-[20px] overflow-hidden`}>
            {list.map(([kind, , title, body, time, cta, href, un]) => {
              const unread = un && !read[title];
              const [glyph, iconCls] = KIND[kind];
              return (
                <div
                  key={title}
                  className={`flex flex-wrap items-center gap-3.5 px-5 py-4 border-b border-[#EEF0F5] ${unread ? "bg-[#FBFCFE]" : ""}`}
                >
                  <span className={`w-10 h-10 rounded-xl shrink-0 flex items-center justify-center text-base font-bold ${iconCls}`}>{glyph}</span>
                  <span className="flex-[1_1_260px] min-w-0 flex flex-col gap-[3px]">
                    <span className="text-[15px] font-bold">{title}</span>
                    <span className={`text-[13px] ${muted} leading-[1.6]`}>{body}</span>
                    <span className="text-xs text-[#8A94AA]">{time}</span>
                  </span>
                  <Link
                    href={href}
                    onClick={() => setRead({ ...read, [title]: true })}
                    className="min-h-[38px] px-3.5 rounded-[10px] border border-navy-900 text-navy-900 text-[13px] font-bold flex items-center whitespace-nowrap"
                  >
                    {cta}
                  </Link>
                  {unread && <span aria-label="مش مقروء" className="w-2.5 h-2.5 rounded-full bg-brand-red shrink-0" />}
                </div>
              );
            })}
            {list.length === 0 && <p className={`p-8 text-center ${muted}`}>مفيش إشعارات هنا.</p>}
          </section>
        </div>
      )}

      {tab === "prefs" && (
        <section className={`${card} rounded-[20px] px-5 pt-2 pb-4 overflow-x-auto`}>
          <table className="w-full border-collapse text-sm min-w-[560px]">
            <thead>
              <tr className={`${muted} text-[13px]`}>
                <th scope="col" className="text-right px-2 py-3 font-semibold">
                  الحدث
                </th>
                {["في اللوحة", "SMS", "واتساب", "إيميل"].map((c) => (
                  <th key={c} scope="col" className="px-2 py-3 font-semibold">
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {PREF_ROWS.map(([k, label]) => (
                <tr key={k} className="border-t border-[#EEF0F5]">
                  <th scope="row" className="text-right px-2 py-3 font-semibold">
                    {label}
                  </th>
                  {CHANNELS.map((c, ci) => {
                    const on = !!prefs[k][ci];
                    const locked = k === "proof" && ci === 0;
                    return (
                      <td key={c} className="px-2 py-3 text-center">
                        <button
                          type="button"
                          role="checkbox"
                          aria-checked={on}
                          aria-label={`${label} — ${c}`}
                          disabled={locked}
                          onClick={() => {
                            if (locked) return;
                            const arr = [...prefs[k]];
                            arr[ci] = on ? 0 : 1;
                            setPrefs({ ...prefs, [k]: arr });
                          }}
                          className={`w-7 h-7 rounded-lg text-[15px] font-bold inline-flex items-center justify-center border ${
                            on ? "bg-navy-900 text-white border-navy-900" : "bg-white text-navy-900 border-[#C9CFD9]"
                          } ${locked ? "opacity-55 cursor-not-allowed" : ""}`}
                        >
                          {on ? "✓" : ""}
                        </button>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
          <p className={`mt-3 text-xs ${muted}`}>إشعارات اللوحة لإثباتات الدفع مش بتتقفل — دي أهم حاجة عشان الحجز مايتلغيش أوتوماتيك.</p>
          <button
            type="button"
            onClick={() => setToast("اتحفظت إعدادات الإشعارات.")}
            className={`mt-3.5 min-h-[46px] px-6 rounded-[14px] bg-brand-red hover:bg-brand-red-hover text-white text-sm font-bold ${redShadow}`}
          >
            حفظ
          </button>
        </section>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
