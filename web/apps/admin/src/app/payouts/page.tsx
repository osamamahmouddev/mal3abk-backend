"use client";

import React, { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Banknote, HandCoins, Landmark, ReceiptText, Wallet } from "lucide-react";
import { KpiCard, Page, PageHeader, Pill, btnPrimary, btnSmall, money, muted } from "@/components/ui";
import { Drawer, Field, QueryState, Section, Select, Table, TextInput, fmtDate, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Balance = Schemas["VenuePayoutBalance"];

const METHODS: [string, string][] = [
  ["InstaPay", "إنستاباي"],
  ["Wallet", "محفظة كاش"],
  ["BankTransfer", "تحويل بنكي"],
  ["Cash", "كاش"],
];
const METHOD_LABEL = Object.fromEntries(METHODS);

const STATUS_LABEL: Record<string, string> = { Confirmed: "مؤكد", Completed: "اتلعب", Cancelled: "ملغي", NoShow: "ماجاش" };

/**
 * Settlement with venues (commission model): the deposits are paid to the platform, which keeps its commission and transfers
 * the rest. This page shows what is owed to each venue and records the transfers; the API never lets a payout exceed the balance.
 */
export default function PayoutsPage() {
  const [selected, setSelected] = useState<string | null>(null);
  const balances = useQuery({
    queryKey: ["admin-payouts"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/payouts")),
  });

  const totals = useMemo(() => {
    const rows = balances.data ?? [];
    return {
      owed: rows.reduce((s, r) => s + Math.max(0, r.balance), 0),
      received: rows.reduce((s, r) => s + r.depositsReceived, 0),
      commission: rows.reduce((s, r) => s + r.commission, 0),
      paid: rows.reduce((s, r) => s + r.paidOut, 0),
      venuesOwed: rows.filter((r) => r.balance > 0).length,
    };
  }, [balances.data]);

  return (
    <Page>
      <PageHeader
        title="مستحقات الملاعب"
        sub="العربون بيوصل لحسابات المنصة. بعد ما الحجز يتأكد بنخصم العمولة، والباقي حق الملعب لحد ما نحوّله ونسجّل التحويل هنا."
      />

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <KpiCard label="مستحق للملاعب دلوقتي" value={money(totals.owed)} hint={`${num(totals.venuesOwed)} ملعب مستني تحويل`} tone="amber" icon={<HandCoins className="size-4" />} />
        <KpiCard label="عرابين وصلت المنصة" value={money(totals.received)} tone="navy" icon={<Wallet className="size-4" />} />
        <KpiCard label="عمولة المنصة" value={money(totals.commission)} tone="green" icon={<ReceiptText className="size-4" />} />
        <KpiCard label="اتحوّل للملاعب" value={money(totals.paid)} tone="slate" icon={<Landmark className="size-4" />} />
      </div>

      <QueryState
        query={balances}
        isEmpty={(rows) => rows.length === 0}
        emptyTitle="مفيش مستحقات لسه"
        emptyDescription="أول ما فريق المنصة يأكد عربون حجز من التطبيق، نصيب الملعب هيظهر هنا."
        emptyIcon="receipt"
      >
        {(rows) => (
          <Table<Balance>
            caption="مستحقات كل ملعب"
            rows={rows}
            rowKey={(r) => r.venueId}
            selected={selected}
            onSelect={(r) => setSelected(r.venueId)}
            columns={[
              { title: "الملعب", cell: (r) => <strong className="text-slate-900">{r.venueName || "—"}</strong> },
              { title: "حجوزات", cell: (r) => num(r.settledBookings) },
              { title: "العرابين", cell: (r) => money(r.depositsReceived) },
              { title: "العمولة", cell: (r) => money(r.commission) },
              { title: "اترجع للاعيبة", cell: (r) => (r.refundedToPlayers ? money(r.refundedToPlayers) : "—") },
              { title: "اتحوّل", cell: (r) => money(r.paidOut) },
              {
                title: "المستحق",
                cell: (r) => <Pill tone={r.balance > 0 ? "warn" : "ok"}>{r.balance > 0 ? money(r.balance) : "متسوّي"}</Pill>,
              },
              { title: "آخر تحويل", cell: (r) => (r.lastPayoutAtUtc ? fmtDate(r.lastPayoutAtUtc) : "—") },
            ]}
          />
        )}
      </QueryState>

      <StatementDrawer venueId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function StatementDrawer({ venueId, onClose }: { venueId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("InstaPay");
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");

  const statement = useQuery({
    queryKey: ["admin-payout-statement", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/payouts/venues/{venueId}", { params: { path: { venueId: venueId! } } })),
  });

  const record = useMutation({
    mutationFn: () =>
      stepUp((headers) =>
        unwrap(
          api.POST("/api/v1/admin/payouts/venues/{venueId}", {
            params: { path: { venueId: venueId! } },
            headers,
            body: { amount: Number(amount), method, reference: reference.trim(), notes: notes.trim() || null },
          }),
        ),
      ),
    onSuccess: async () => {
      notice.ok("اتسجّل التحويل وبعتنا إشعار للملعب.");
      setAmount("");
      setReference("");
      setNotes("");
      await queryClient.invalidateQueries({ queryKey: ["admin-payout-statement", venueId] });
      await queryClient.invalidateQueries({ queryKey: ["admin-payouts"] });
    },
    onError: notice.fail,
  });

  const balance = statement.data?.balance;
  const amountValue = Number(amount);
  const canRecord =
    !!balance && amount.trim() !== "" && amountValue > 0 && amountValue <= balance.balance && reference.trim().length > 0 && !record.isPending;

  return (
    <Drawer title={balance?.venueName || "مستحقات الملعب"} open={!!venueId} onClose={onClose}>
      {notice.node}
      <QueryState query={statement}>
        {(s) => (
          <>
            <div className="grid grid-cols-2 gap-2">
              <KpiCard label="المستحق دلوقتي" value={money(s.balance.balance)} tone={s.balance.balance > 0 ? "amber" : "green"} />
              <KpiCard label="نصيب الملعب من العرابين" value={money(s.balance.venueShare)} hint={`بعد عمولة ${money(s.balance.commission)}`} />
            </div>

            <Section title="تسجيل تحويل للملعب">
              {s.balance.balance > 0 ? (
                <form
                  className="flex flex-col gap-3"
                  onSubmit={(e) => {
                    e.preventDefault();
                    if (canRecord) record.mutate();
                  }}
                >
                  <div className="grid grid-cols-2 gap-2">
                    <Field label="المبلغ (ج.م)">
                      <TextInput inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder={String(s.balance.balance)} />
                    </Field>
                    <Field label="طريقة التحويل">
                      <Select label="طريقة التحويل" value={method} onChange={setMethod} options={METHODS} />
                    </Field>
                  </div>
                  <Field label="رقم التحويل / المرجع">
                    <TextInput value={reference} maxLength={120} onChange={(e) => setReference(e.target.value)} placeholder="مثلاً رقم عملية إنستاباي" />
                  </Field>
                  <Field label="ملاحظات (اختياري)">
                    <TextInput value={notes} maxLength={1000} onChange={(e) => setNotes(e.target.value)} />
                  </Field>
                  <div className="flex flex-wrap items-center gap-2">
                    <button type="submit" disabled={!canRecord} className={btnPrimary}>
                      <Banknote className="size-4" />
                      سجّل التحويل
                    </button>
                    <button type="button" className={btnSmall} onClick={() => setAmount(String(s.balance.balance))}>
                      المستحق كله
                    </button>
                  </div>
                  {amount.trim() !== "" && amountValue > s.balance.balance && (
                    <span className="text-[12px] text-[#A8261C]">المبلغ أكبر من المستحق للملعب.</span>
                  )}
                  <span className={`text-[12px] ${muted}`}>التحويل بيتسجّل مرة واحدة ومينفعش يتعدّل، فاتأكد من المبلغ والمرجع.</span>
                </form>
              ) : (
                <p className={`text-[13px] ${muted}`}>مفيش مستحقات للملعب دلوقتي.</p>
              )}
            </Section>

            <Section title="التحويلات اللي اتعملت">
              {s.payouts.length === 0 ? (
                <p className={`text-[13px] ${muted}`}>لسه مفيش تحويلات.</p>
              ) : (
                <ul className="flex flex-col divide-y divide-slate-100">
                  {s.payouts.map((p) => (
                    <li key={p.id} className="py-2 flex items-center justify-between gap-3 text-[13px]">
                      <span className="flex flex-col">
                        <strong>{money(p.amount)}</strong>
                        <span className={muted}>
                          {METHOD_LABEL[p.method] ?? p.method} · <span dir="ltr">{p.reference}</span>
                        </span>
                        {p.notes && <span className={muted}>{p.notes}</span>}
                      </span>
                      <span className={muted}>{fmtDateTime(p.paidAtUtc)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </Section>

            <Section title="الحجوزات اللي ورا المستحق">
              {s.bookings.length === 0 ? (
                <p className={`text-[13px] ${muted}`}>مفيش حجوزات اتدفع عربونها للمنصة.</p>
              ) : (
                <ul className="flex flex-col divide-y divide-slate-100">
                  {s.bookings.map((b) => (
                    <li key={b.bookingId} className="py-2 flex items-center justify-between gap-3 text-[13px]">
                      <span className="flex flex-col">
                        <span dir="ltr" className="font-mono text-right">{b.reference}</span>
                        <span className={muted}>
                          {fmtDate(b.startUtc)} · {STATUS_LABEL[b.status] ?? b.status}
                        </span>
                      </span>
                      <span className="flex flex-col items-end">
                        <strong>{money(b.venueShare)}</strong>
                        <span className={muted}>
                          عربون {money(b.depositReceived)} − عمولة {money(b.commission)}
                          {b.refunded ? ` − مرتجع ${money(b.refunded)}` : ""}
                        </span>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </Section>
          </>
        )}
      </QueryState>
    </Drawer>
  );
}
