"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Badge, Page, Toast, Tone, card, chip, muted, primaryBtn, secondaryBtn, toArabicDigits, useRevealOnSelect } from "@/components/ui";

type Status = "proof" | "wait" | "ok" | "expired" | "cancel";
type Tab = "pending" | "ok" | "closed" | "all";

interface Booking {
  id: string;
  ref: string;
  who: string;
  src: string;
  pitch: number;
  slot: string;
  dep: string;
  st: Status;
  price: string;
  phone: string;
}

const bookings: Booking[] = [
  { id: "b1", ref: "KH-2607-0142", who: "أحمد سامي", src: "من التطبيق", pitch: 1, slot: "ملعب ١ · السبت ٢٦ · ٨:٠٠ م", dep: "٥٠ ج.م — إيصال مرفوع", st: "proof", price: "١٥٠ ج.م", phone: "٠١٠١٢٣٤٥٦٧٨" },
  { id: "b2", ref: "KH-2607-0147", who: "يوسف حسن", src: "من التطبيق", pitch: 1, slot: "ملعب ١ · السبت ٢٦ · ٩:٠٠ م", dep: "لم يُرفع — ينتهي ٧:٣٨ م", st: "wait", price: "١٥٠ ج.م", phone: "٠١٢٢٣٣٤٤٥٥٦" },
  { id: "b3", ref: "KH-2607-0145", who: "عمر خالد", src: "من التطبيق", pitch: 2, slot: "ملعب ٢ · السبت ٢٦ · ١١:٠٠ م", dep: "٦٠ ج.م — إيصال مرفوع", st: "proof", price: "٢٢٠ ج.م", phone: "٠١١٤٤٣٣٢٢١١" },
  { id: "b4", ref: "KH-2907-0158", who: "مصطفى رمضان", src: "من التطبيق", pitch: 1, slot: "ملعب ١ · الثلاثاء ٢٩ · ١١:٠٠ م", dep: "لم يُرفع — ينتهي ٨:١٠ م", st: "wait", price: "١٥٠ ج.م", phone: "٠١١٥٥٦٦٧٧٨٨" },
  { id: "b5", ref: "KH-3107-0107", who: "هاني سعيد", src: "من التطبيق", pitch: 1, slot: "ملعب ١ · الخميس ٣١ · ١:٠٠ ص", dep: "٥٠ ج.م — إيصال مرفوع", st: "proof", price: "١٥٠ ج.م", phone: "٠١٠٤٤٥٥٦٦٧٧" },
  { id: "b6", ref: "KH-2607-0139", who: "فريق الصقور", src: "من التطبيق", pitch: 1, slot: "ملعب ١ · السبت ٢٦ · ١٢:٠٠ ص", dep: "٥٠ ج.م معتمد", st: "ok", price: "١٥٠ ج.م", phone: "٠١٠٩٨٧٦٥٤٣٢" },
  { id: "b7", ref: "KH-2807-M004", who: "م. فتحي", src: "حجز يدوي — محمود", pitch: 3, slot: "ملعب ٣ · السبت ٢٦ · ٨:٠٠ م", dep: "نقدي في الملعب", st: "ok", price: "١٥٠ ج.م", phone: "٠١٠١١١٢٢٢٣٣" },
  { id: "b8", ref: "KH-2607-0131", who: "كريم عادل", src: "من التطبيق", pitch: 2, slot: "ملعب ٢ · السبت ٢٦ · ٧:٠٠ م", dep: "٦٠ ج.م معتمد", st: "ok", price: "٢٢٠ ج.م", phone: "٠١٠٢٢٣٣٤٤٥٥" },
  { id: "b9", ref: "KH-2407-0133", who: "أحمد سامي", src: "من التطبيق", pitch: 2, slot: "ملعب ٢ · الخميس ٢٤ · ١١:٠٠ م", dep: "ماتحوّلش", st: "expired", price: "٢٢٠ ج.م", phone: "٠١٠١٢٣٤٥٦٧٨" },
  { id: "b10", ref: "KH-2507-0121", who: "زياد طارق", src: "من التطبيق", pitch: 3, slot: "ملعب ٣ · الجمعة ٢٥ · ٩:٠٠ م", dep: "اترد ٥٠ ج.م", st: "cancel", price: "١٥٠ ج.م", phone: "٠١٢٧٧٨٨٩٩٠٠" },
];

const STATUS: Record<Status, [string, Tone]> = {
  proof: ["إيصال للمراجعة", "warn"],
  wait: ["بانتظار العربون", "wait"],
  ok: ["✓ مؤكد", "ok"],
  expired: ["انتهت المهلة", "wait"],
  cancel: ["ملغي", "bad"],
};

const LOGS: Record<Status, string[]> = {
  proof: ["طلب من التطبيق", "رُفع إيصال — بانتظار مراجعتك"],
  wait: ["طلب من التطبيق", "الميعاد محجوز مؤقتاً لحد رفع الإيصال"],
  ok: ["طلب أو حجز يدوي", "العربون اتعمد — الحجز مؤكد"],
  expired: ["طلب من التطبيق", "مارفعش إيصال خلال ٣٠ دقيقة — الميعاد اتاح"],
  cancel: ["حجز مؤكد", "اتلغى — العربون مستحق الاسترداد"],
};

const TABS: [Tab, string][] = [
  ["pending", "طلبات معلقة"],
  ["ok", "مؤكدة"],
  ["closed", "ملغية ومنتهية"],
  ["all", "الكل"],
];

const REASONS: [string, string][] = [
  ["maint", "صيانة طارئة"],
  ["dup", "حجز مكرر"],
  ["other", "سبب تاني"],
];

const inTab = (tab: Tab, st: Status) =>
  tab === "all" ||
  (tab === "pending" && (st === "proof" || st === "wait")) ||
  (tab === "ok" && st === "ok") ||
  (tab === "closed" && (st === "cancel" || st === "expired"));

export default function ReservationsPage() {
  const [tab, setTab] = useState<Tab>("pending");
  const [q, setQ] = useState("");
  const [pitch, setPitch] = useState<"all" | number>("all");
  const [page, setPage] = useState(1);
  const [selId, setSelId] = useState<string | null>("b2");
  const detailsRef = useRevealOnSelect<HTMLElement>(selId);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [reason, setReason] = useState<string | null>(null);
  const [cancelled, setCancelled] = useState<Record<string, boolean>>({});
  const [toast, setToast] = useState<string | null>(null);

  const rows = bookings.map((r) => (cancelled[r.id] ? { ...r, st: "cancel" as Status, dep: "استرداد مستحق" } : r));
  const query = q.trim();
  const list = rows.filter(
    (r) =>
      inTab(tab, r.st) &&
      (pitch === "all" || r.pitch === pitch) &&
      (!query || r.who.includes(query) || r.ref.includes(query.toUpperCase()))
  );
  const sel = rows.find((r) => r.id === selId);

  const confirmCancel = () => {
    if (!reason || !sel) return;
    setCancelled({ ...cancelled, [sel.id]: true });
    setCancelOpen(false);
    setToast(`اتلغى حجز ${sel.who} ووصله إشعار بالسبب. سجّل الاسترداد بعد التحويل.`);
  };

  return (
    <Page className="gap-[18px]">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">الحجوزات</h1>
        <button
          type="button"
          onClick={() => setToast(`اتجهّز ملف Excel بـ ${toArabicDigits(list.length)} حجز — هيتحمّل على جهازك.`)}
          className={`${secondaryBtn} rounded-xl`}
        >
          تصدير Excel
        </button>
        <Link href="/reservations/manual" className={primaryBtn}>
          + حجز يدوي
        </Link>
      </div>

      <div role="tablist" className="flex gap-1.5 flex-wrap">
        {TABS.map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => {
              setTab(id);
              setPage(1);
            }}
            className={`${chip(tab === id, "min-h-[42px] px-4 rounded-xl text-sm")} flex items-center gap-2`}
          >
            {label}
            <span
              className={`min-w-[22px] h-[22px] px-1.5 rounded-[11px] text-xs inline-flex items-center justify-center ${
                tab === id ? "bg-white/[0.18]" : "bg-[#EEF0F5]"
              }`}
            >
              {toArabicDigits(rows.filter((r) => inTab(id, r.st)).length)}
            </span>
          </button>
        ))}
      </div>

      <div className="flex flex-wrap gap-2.5 items-center">
        <div className="flex-[1_1_280px] min-w-0 flex items-center gap-2 min-h-[44px] bg-white border border-[#DCE0E8] rounded-xl px-3">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5B6475" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="M20 20l-4-4" />
          </svg>
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="ابحث باسم العميل أو رقم الحجز"
            aria-label="بحث"
            className="flex-1 min-w-0 border-none outline-none text-sm bg-transparent text-slate-900"
          />
        </div>
        <div role="group" aria-label="الملعب" className="flex gap-1 bg-white border border-[#DCE0E8] rounded-xl p-[3px]">
          {(
            [
              ["all", "كل الملاعب"],
              [1, "ملعب ١"],
              [2, "ملعب ٢"],
              [3, "ملعب ٣"],
            ] as const
          ).map(([id, label]) => (
            <button
              key={id}
              type="button"
              aria-pressed={pitch === id}
              onClick={() => setPitch(id)}
              className={`min-h-[36px] px-3 rounded-[9px] text-[13px] ${
                pitch === id ? "bg-navy-900 text-white font-bold" : "bg-transparent text-[#3B4352]"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_600px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
          {/* Phones: one card per booking */}
          <ul className="md:hidden divide-y divide-[#EEF1F5]">
            {list.map((r) => (
              <li key={r.id} className={`p-4 flex flex-col gap-2 ${r.id === selId ? "bg-[#F5F7FB]" : ""}`}>
                <div className="flex items-start gap-2">
                  <div className="flex-1 min-w-0 flex flex-col">
                    <span className="font-semibold">{r.who}</span>
                    <span className={`text-xs ${muted}`}>{r.src}</span>
                  </div>
                  <Badge tone={STATUS[r.st][1]}>{STATUS[r.st][0]}</Badge>
                </div>
                <div className="text-sm">{r.slot}</div>
                <div className={`flex flex-wrap gap-x-3 gap-y-1 text-xs ${muted}`}>
                  <span dir="ltr">{r.ref}</span>
                  <span>{r.dep}</span>
                </div>
                <div className="flex flex-wrap gap-2 pt-1">
                  {r.st === "proof" && (
                    <Link href="/reservations/proof-review" className="min-h-[40px] px-3.5 rounded-[10px] bg-navy-900 text-white text-[13px] font-bold inline-flex items-center">
                      مراجعة الإيصال
                    </Link>
                  )}
                  {r.st === "wait" && (
                    <button
                      type="button"
                      onClick={() => setToast(`اتبعت تذكير لـ${r.who} يرفع إيصال العربون.`)}
                      className="min-h-[40px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
                    >
                      تذكير
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => setSelId(r.id)}
                    className="min-h-[40px] px-3.5 rounded-[10px] bg-[#EEF1F7] text-navy-900 text-[13px] font-semibold"
                  >
                    التفاصيل
                  </button>
                </div>
              </li>
            ))}
          </ul>

          {/* Tablet and up: table */}
          <div className="hidden md:block overflow-x-auto">
            <table className="w-full min-w-[760px] border-collapse text-sm">
              <thead>
                <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
                  <th className="px-[18px] py-3 font-semibold">رقم الحجز</th>
                  <th className="p-3 font-semibold">العميل</th>
                  <th className="p-3 font-semibold">الملعب والميعاد</th>
                  <th className="p-3 font-semibold">العربون</th>
                  <th className="p-3 font-semibold">الحالة</th>
                  <th className="px-[18px] py-3 font-semibold" />
                </tr>
              </thead>
              <tbody>
                {list.map((r) => (
                  <tr key={r.id} className={`border-t border-[#EEF1F5] ${r.id === selId ? "bg-[#F5F7FB]" : ""}`}>
                    <td dir="ltr" className="px-[18px] py-3 text-right text-[13px] text-[#3B4352]">
                      {r.ref}
                    </td>
                    <td className="p-3">
                      <div className="flex flex-col">
                        <span className="font-semibold">{r.who}</span>
                        <span className={`text-xs ${muted}`}>{r.src}</span>
                      </div>
                    </td>
                    <td className="p-3 whitespace-nowrap">{r.slot}</td>
                    <td className="p-3 whitespace-nowrap text-[13px] text-[#333B4A]">{r.dep}</td>
                    <td className="p-3">
                      <Badge tone={STATUS[r.st][1]}>{STATUS[r.st][0]}</Badge>
                    </td>
                    <td className="px-[18px] py-2 whitespace-nowrap">
                      {r.st === "proof" && (
                        <Link
                          href="/reservations/proof-review"
                          className="min-h-[34px] px-3 rounded-[9px] bg-navy-900 text-white text-xs font-bold inline-flex items-center ml-1.5"
                        >
                          مراجعة الإيصال
                        </Link>
                      )}
                      {r.st === "wait" && (
                        <button
                          type="button"
                          onClick={() => setToast(`اتبعت تذكير لـ${r.who} يرفع إيصال العربون.`)}
                          className="min-h-[34px] px-3 rounded-[9px] border border-[#C9CFD9] bg-white text-navy-900 text-xs font-semibold ml-1.5"
                        >
                          تذكير
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => setSelId(r.id)}
                        className="min-h-[34px] px-3 rounded-[9px] bg-[#EEF1F7] text-navy-900 text-xs font-semibold"
                      >
                        التفاصيل
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {list.length === 0 && (
            <div className={`p-10 text-center text-sm ${muted}`}>
              مفيش حجوزات بالبحث والفلاتر دي.{" "}
              <button
                type="button"
                onClick={() => {
                  setQ("");
                  setPitch("all");
                  setTab("all");
                }}
                className="bg-transparent text-navy-900 font-bold underline text-sm"
              >
                امسح الفلاتر
              </button>
            </div>
          )}
          <div className={`px-[18px] py-3 border-t border-[#EEF1F5] flex items-center gap-2 text-[13px] ${muted}`}>
            <span className="flex-1">
              عرض {toArabicDigits(list.length)} من {toArabicDigits(list.length + (tab === "all" ? 128 : 0))} حجز
            </span>
            {[1, 2, 3].map((n) => (
              <button
                key={n}
                type="button"
                aria-current={n === page ? "page" : undefined}
                onClick={() => setPage(n)}
                className={`min-w-[34px] min-h-[34px] rounded-[9px] text-[13px] ${
                  n === page ? "bg-navy-900 text-white" : "bg-white text-slate-900 border border-[#DCE0E8]"
                }`}
              >
                {toArabicDigits(n)}
              </button>
            ))}
          </div>
        </section>

        {sel && (
          <aside ref={detailsRef} aria-label="تفاصيل الحجز" className={`scroll-mt-20 flex-[1_1_320px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-3.5`}>
            <div className="flex justify-between items-center">
              <h2 className="text-lg font-bold">{sel.who}</h2>
              <button
                type="button"
                onClick={() => setSelId(null)}
                aria-label="إغلاق"
                className="min-w-[36px] min-h-[36px] rounded-[10px] bg-surface text-navy-900"
              >
                ✕
              </button>
            </div>
            <Badge tone={STATUS[sel.st][1]} className="self-start">
              {STATUS[sel.st][0]}
            </Badge>
            <div className="flex flex-col gap-2 text-sm">
              {[
                ["رقم الحجز", <span key="r" dir="ltr">{sel.ref}</span>],
                ["الميعاد", sel.slot],
                ["السعر", sel.price],
                ["العربون", sel.dep],
                ["الهاتف", sel.phone],
              ].map(([k, v]) => (
                <div key={k as string} className="flex justify-between">
                  <span className={muted}>{k}</span>
                  <span>{v}</span>
                </div>
              ))}
            </div>
            <div className="flex flex-col gap-1.5 border-t border-[#EEF1F5] pt-3">
              <span className="text-[13px] font-bold">سجل الحالة</span>
              {LOGS[sel.st].map((l) => (
                <span key={l} className="text-[13px] text-[#333B4A]">
                  • {l}
                </span>
              ))}
            </div>
            <Link href="/customers" className="text-[13px] font-semibold text-navy-900 underline hover:text-brand-red">
              ملف العميل وسجل حجوزاته
            </Link>
            {sel.st !== "cancel" && sel.st !== "expired" && (
              <button
                type="button"
                onClick={() => {
                  setReason(null);
                  setCancelOpen(true);
                }}
                className="min-h-[44px] rounded-xl border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-bold"
              >
                إلغاء الحجز (يتطلب سبب)
              </button>
            )}
          </aside>
        )}
      </div>

      {cancelOpen && sel && (
        <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
          <div role="dialog" aria-modal="true" aria-labelledby="cx-t" className="w-full max-w-[460px] bg-white rounded-[20px] p-6 flex flex-col gap-3">
            <h2 id="cx-t" className="text-[19px] font-bold">
              إلغاء حجز {sel.who}
            </h2>
            <span className="text-sm font-semibold">السبب (مطلوب — بيوصل للاعب)</span>
            <div className="flex flex-wrap gap-2">
              {REASONS.map(([id, label]) => (
                <button
                  key={id}
                  type="button"
                  aria-pressed={reason === id}
                  onClick={() => setReason(id)}
                  className={chip(reason === id, "min-h-[40px] px-3.5 rounded-[10px] text-[13px]")}
                >
                  {label}
                </button>
              ))}
            </div>
            <div className="bg-[#FEF3D6] text-[#6B4400] rounded-xl px-3 py-2.5 text-[13px] leading-5">
              الإلغاء من الملعب = استرداد العربون كامل للاعب. هتسجّل الاسترداد يدوي بعد التحويل.
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={confirmCancel}
                disabled={!reason}
                className={`min-h-[46px] px-5 rounded-xl text-[15px] font-bold ${
                  reason ? "bg-[#A8261C] text-white" : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"
                }`}
              >
                تأكيد الإلغاء
              </button>
              <button
                type="button"
                onClick={() => setCancelOpen(false)}
                className="min-h-[46px] px-5 rounded-xl border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold"
              >
                رجوع
              </button>
            </div>
          </div>
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}
