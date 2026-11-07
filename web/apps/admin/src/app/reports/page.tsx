"use client";

import React, { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Page, PageHeader, card, money, muted } from "@/components/ui";
import { Field, QueryState, TextInput, num } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

const METHOD: Record<string, string> = { Cash: "كاش", BankTransfer: "تحويل بنكي", InstaPay: "إنستاباي", MobileWallet: "محفظة" };

const DEFINITIONS: [string, string][] = [
  ["MRR", "الإيراد الشهري المتكرر دلوقتي: سعر الباقة الشهري للاشتراكات الشهرية، والسنوي ÷ ١٢ للسنوية. الاشتراكات النشطة وفترة السماح بس، من غير التجربة."],
  ["الإيراد المحصّل", "فلوس اتسجل استلامها فعلًا في الفترة (بتاريخ الدفع)، من غير الدفعات اللي فاتورتها اتلغت. ده كاش، مش MRR."],
  ["قيمة الحجوزات المؤكدة", "إجمالي سعر الحجوزات اللي اتأكدت في الفترة. ده دخل الملاعب، مش دخل ملعبك: المنصة مش بتحصّل فلوس الحجز."],
  ["اشتراكات مدفوعة", "اشتراكات Active أو Grace دلوقتي."],
];

function isoDay(offsetDays: number) {
  return new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

export default function ReportsPage() {
  const [from, setFrom] = useState(isoDay(-29));
  const [to, setTo] = useState(isoDay(0));
  const valid = from <= to;

  const dashboard = useQuery({
    queryKey: ["reports-dashboard", from, to],
    enabled: valid,
    queryFn: () => unwrap(api.GET("/api/v1/admin/dashboard", { params: { query: { from, to } } })),
  });
  const reconciliation = useQuery({
    queryKey: ["reconciliation", from, to],
    enabled: valid,
    queryFn: () => unwrap(api.GET("/api/v1/admin/invoices/reconciliation", { params: { query: { from, to } } })),
  });

  return (
    <Page>
      <PageHeader title="الإيرادات والتقارير" sub="كل الأرقام بأيام القاهرة، وبداية ونهاية الفترة محسوبين." />
      <div className="flex flex-wrap gap-2">
        <Field label="من"><TextInput type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} /></Field>
        <Field label="إلى"><TextInput type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></Field>
      </div>
      <QueryState query={dashboard}>
        {(d) => (
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <Tile label="MRR (دلوقتي)" value={money(d.figures["mrr"] ?? 0)} />
            <Tile label="الإيراد المحصّل" value={money(d.figures["revenue.collected"] ?? 0)} />
            <Tile label="اشتراكات مدفوعة" value={num(d.figures["subscriptions.active"])} />
            <Tile label="في التجربة" value={num(d.figures["subscriptions.trial"])} />
            <Tile label="قيمة الحجوزات المؤكدة" value={money(d.figures["bookings.confirmed_value"] ?? 0)} />
            <Tile label="حجوزات مؤكدة" value={num(d.figures["bookings.confirmed"])} />
          </div>
        )}
      </QueryState>
      <section className={`${card} rounded-[18px] p-4 flex flex-col gap-2`}>
        <h2 className="text-[15px] font-bold">مطابقة التحصيل حسب طريقة الدفع</h2>
        <QueryState query={reconciliation}>
          {(r) => (
            <>
              <div className="flex flex-wrap gap-4 text-sm">
                <span>فواتير: <strong>{num(r.invoices)}</strong></span>
                <span>محصّل: <strong>{money(r.collected)}</strong></span>
                <span>ملغي: <strong>{money(r.voided)}</strong></span>
              </div>
              {r.byMethod.map((m) => (
                <div key={m.method} className="flex justify-between text-[13px] border-t border-[#EEF0F5] pt-1.5">
                  <span>{METHOD[m.method] ?? m.method} ({num(m.count)})</span>
                  <strong>{money(m.amount)}</strong>
                </div>
              ))}
            </>
          )}
        </QueryState>
      </section>
      <section className={`${card} rounded-[18px] p-4 flex flex-col gap-2`}>
        <h2 className="text-[15px] font-bold">تعريفات الأرقام</h2>
        <dl className="flex flex-col gap-2">
          {DEFINITIONS.map(([term, text]) => (
            <div key={term}>
              <dt className="text-sm font-semibold">{term}</dt>
              <dd className={`text-[13px] ${muted} leading-[1.7]`}>{text}</dd>
            </div>
          ))}
        </dl>
      </section>
    </Page>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className={`${card} rounded-[16px] p-4 flex flex-col gap-1`}>
      <span className={`text-[13px] ${muted}`}>{label}</span>
      <strong className="text-[20px]">{value}</strong>
    </div>
  );
}
