"use client";

import React, { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Segmented, card, money, muted } from "@/components/ui";
import { QueryState, num } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Dashboard = Schemas["AdminDashboardDto"];

const PERIODS: [string, string][] = [
  ["7", "٧ أيام"],
  ["30", "٣٠ يوم"],
  ["90", "٩٠ يوم"],
];

/** Headline tiles: label, figure key, how to show it, and the exact definition shown on hover/focus. */
const TILES: { key: string; label: string; money?: boolean; hint: string }[] = [
  { key: "users.total", label: "المستخدمين", hint: "حسابات غير محذوفة دلوقتي" },
  { key: "users.new", label: "مستخدمين جداد", hint: "اللي سجّلوا في الفترة" },
  { key: "venues.active", label: "ملاعب نشطة", hint: "معتمدة ومنشورة دلوقتي" },
  { key: "pitches.active", label: "ملاعب فرعية نشطة", hint: "الملاعب الفرعية الشغالة في ملاعب نشطة" },
  { key: "bookings.created", label: "حجوزات اتعملت", hint: "حجوزات اتعملت في الفترة (من غير المسودات)" },
  { key: "bookings.confirmed", label: "حجوزات اتأكدت", hint: "حجوزات اتأكدت في الفترة (بوقت التأكيد)" },
  { key: "subscriptions.active", label: "اشتراكات مدفوعة", hint: "اشتراكات Active أو Grace دلوقتي" },
  { key: "mrr", label: "MRR", money: true, hint: "إيراد شهري متكرر: سعر الباقة الشهري، والسنوي ÷ ١٢، للاشتراكات المدفوعة دلوقتي" },
  { key: "revenue.collected", label: "إيراد محصّل", money: true, hint: "مدفوعات اتسجلت في الفترة، من غير الفواتير الملغاة" },
  { key: "venues.pending_approval", label: "ملاعب مستنية موافقة", hint: "طلبات مستنية قرار دلوقتي" },
  { key: "disputes.open", label: "نزاعات مفتوحة", hint: "نزاعات إيصالات لسه متحلتش" },
  { key: "support.open", label: "تذاكر مفتوحة", hint: "تذاكر دعم مش محلولة ولا مقفولة" },
];

const SERIES: { key: string; label: string; money?: boolean }[] = [
  { key: "bookings.created", label: "الحجوزات يوميًا" },
  { key: "users.new", label: "المستخدمين الجداد يوميًا" },
  { key: "revenue.collected", label: "الإيراد المحصّل يوميًا", money: true },
];

export default function OverviewPage() {
  const [days, setDays] = useState("30");
  const dashboard = useQuery({
    queryKey: ["dashboard", days],
    queryFn: () => {
      const to = new Date();
      const from = new Date(to.getTime() - (Number(days) - 1) * 86_400_000);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return unwrap(api.GET("/api/v1/admin/dashboard", { params: { query: { from: iso(from), to: iso(to) } } }));
    },
  });

  return (
    <Page>
      <PageHeader title="نظرة عامة" sub="الأرقام بتوقيت القاهرة. الأرقام اللي عليها «دلوقتي» مش بتتأثر بالفترة.">
        <Segmented label="الفترة" items={PERIODS} value={days} onChange={setDays} />
      </PageHeader>
      <QueryState query={dashboard}>{(data) => <DashboardView data={data} />}</QueryState>
    </Page>
  );
}

function DashboardView({ data }: { data: Dashboard }) {
  const tiles = TILES.filter((t) => t.key in data.figures);
  return (
    <>
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-3 animate-fade-in-up">
        {tiles.map((t) => (
          <div key={t.key} className={`${card} interactive-card rounded-[16px] p-4 flex flex-col gap-1 cursor-default hover:border-slate-300`} title={t.hint}>
            <span className={`text-[13px] ${muted}`}>{t.label}</span>
            <strong className="text-[22px]">{t.money ? money(data.figures[t.key]) : num(data.figures[t.key])}</strong>
            <span className={`text-[11px] ${muted}`}>{t.hint}</span>
          </div>
        ))}
      </div>

      <div className="grid md:grid-cols-2 gap-3 animate-fade-in-up">
        {SERIES.filter((s) => data.series[s.key]).map((s) => (
          <Bars key={s.key} title={s.label} points={data.series[s.key]} asMoney={s.money} />
        ))}
        {Object.entries(data.distributions).map(([key, values]) => (
          <Distribution key={key} title={DISTRIBUTION_TITLES[key] ?? key} values={values} />
        ))}
      </div>
    </>
  );
}

const DISTRIBUTION_TITLES: Record<string, string> = {
  "bookings.by_status": "الحجوزات حسب الحالة (في الفترة)",
  "users.by_role": "الحسابات حسب الدور",
  "venues.active_by_city": "الملاعب النشطة حسب المدينة",
  "subscriptions.by_plan": "الاشتراكات المدفوعة حسب الباقة",
  "support.open_by_category": "التذاكر المفتوحة حسب النوع",
};

const LABEL_TRANSLATIONS: Record<string, string> = {
  // Roles
  Player: "لاعب",
  VenueOwner: "صاحب ملعب",
  Admin: "مسؤول نظام",
  SupportAgent: "موظف دعم",
  VenueStaff: "موظف ملعب",
  Finance: "مالية ومحاسبة",

  // Booking statuses
  Pending: "قيد الانتظار",
  Confirmed: "مؤكد",
  Completed: "مكتمل",
  Cancelled: "ملغي",
  Expired: "منتهي",
  Refunded: "مسترجع",
  Disputed: "قيد النزاع",

  // Support categories
  Subscription: "اشتراكات وفواتير",
  Booking: "حجوزات",
  Account: "حساب ودخول",
  Venue: "ملاعب",
  General: "عام",
  Technical: "مشاكل تقنية",

  // Egyptian Cities / Governorates (English to Arabic)
  Cairo: "القاهرة",
  Alexandria: "الإسكندرية",
  Giza: "الجيزة",
  Qalyubia: "القليوبية",
  Sharqia: "الشرقية",
  Dakahlia: "الدقهلية",
  Gharbia: "الغربية",
  Monufia: "المنوفية",
  Beheira: "البحيرة",
  KafrElSheikh: "كفر الشيخ",
  Damietta: "دمياط",
  PortSaid: "بورسعيد",
  Ismailia: "الإسماعيلية",
  Suez: "السويس",
  Fayoum: "الفيوم",
  BeniSuef: "بني سويف",
  Minya: "المنيا",
  Asyut: "أسيوط",
  Sohag: "سوهاج",
  Qena: "قنا",
  Luxor: "الأقصر",
  Aswan: "أسوان",
  RedSea: "البحر الأحمر",
  NewValley: "الوادي الجديد",
  Matrouh: "مطروح",
  NorthSinai: "شمال سيناء",
  SouthSinai: "جنوب سيناء",
};

function Bars({ title, points, asMoney }: { title: string; points: Schemas["SeriesPoint"][]; asMoney?: boolean }) {
  const max = Math.max(1, ...points.map((p) => p.value));
  const total = points.reduce((sum, p) => sum + p.value, 0);
  return (
    <figure className={`${card} rounded-[16px] p-4 flex flex-col gap-3`}>
      <figcaption className="flex justify-between text-sm font-bold">
        <span>{title}</span>
        <span className={muted}>{asMoney ? money(total) : num(total)}</span>
      </figcaption>
      <div className="flex items-end gap-[2px] h-28" role="img" aria-label={`${title}: الإجمالي ${asMoney ? money(total) : num(total)}`}>
        {points.map((p) => (
          <div
            key={p.day}
            title={`${p.day}: ${asMoney ? money(p.value) : num(p.value)}`}
            className="flex-1 bg-brand-red/80 hover:bg-brand-red rounded-t-[3px] min-h-[2px] transition-all duration-500 ease-out cursor-pointer"
            style={{ height: `${(p.value / max) * 100}%` }}
          />
        ))}
      </div>
    </figure>
  );
}

function Distribution({ title, values }: { title: string; values: Record<string, number> }) {
  // Normalize and group by translated label in case English & Arabic versions exist for the same entity (e.g. Cairo & القاهرة)
  const grouped: Record<string, number> = {};
  for (const [k, v] of Object.entries(values)) {
    const localized = LABEL_TRANSLATIONS[k] ?? k;
    grouped[localized] = (grouped[localized] ?? 0) + v;
  }

  const entries = Object.entries(grouped).sort((a, b) => b[1] - a[1]);
  const total = Math.max(1, entries.reduce((s, [, v]) => s + v, 0));
  return (
    <section className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
      <h2 className="text-sm font-bold">{title}</h2>
      {entries.length === 0 && <span className={`text-sm ${muted}`}>مفيش بيانات.</span>}
      {entries.map(([k, v]) => (
        <div key={k} className="flex items-center gap-2 text-[13px]">
          <span className="w-32 truncate font-medium text-slate-800" title={k}>
            {k}
          </span>
          <div className="flex-1 h-2 rounded-full bg-[#EEF0F5] overflow-hidden">
            <div className="h-full bg-slate-900 rounded-full transition-all duration-500" style={{ width: `${(v / total) * 100}%` }} />
          </div>
          <span className="w-10 text-left font-bold text-slate-700">{num(v)}</span>
        </div>
      ))}
    </section>
  );
}
