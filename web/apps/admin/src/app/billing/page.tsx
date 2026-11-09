"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, Tabs, btnDanger, btnOutline, btnPrimary, btnSmall, card, money, muted } from "@/components/ui";
import { Drawer, Field, QueryState, Section, Select, Table, TextInput, fmtDate, fmtDateTime, useNotice } from "@/components/admin";
import { api, openFile, unwrap } from "@/lib/api";

type Tab = "subscriptions" | "invoices";
const STATUS_TONE: Record<string, "ok" | "warn" | "bad" | "neutral"> = { Active: "ok", Trial: "warn", Grace: "warn", Expired: "bad", Suspended: "bad", Cancelled: "neutral" };
const STATUSES: [string, string][] = [["", "كل الحالات"], ["0", "تجربة"], ["1", "نشط"], ["2", "فترة سماح"], ["3", "منتهي"], ["4", "موقوف"], ["5", "ملغي"]];
const METHODS: [string, string][] = [["0", "كاش"], ["1", "تحويل بنكي"], ["2", "إنستاباي"], ["3", "محفظة"]];
const CYCLES: [string, string][] = [["0", "شهري"], ["1", "سنوي"]];

export default function BillingPage() {
  const [tab, setTab] = useState<Tab>("subscriptions");
  return (
    <Page>
      <PageHeader title="الاشتراكات والفواتير" sub="الدفع يدوي (كاش، تحويل، إنستاباي، محفظة): تسجيل الدفعة بيمد الاشتراك ويطلع فاتورة مرقّمة." />
      <Tabs<Tab> tabs={[["subscriptions", "الاشتراكات"], ["invoices", "الفواتير"]]} value={tab} onChange={setTab} />
      {tab === "subscriptions" ? <Subscriptions /> : <Invoices />}
    </Page>
  );
}

function Subscriptions() {
  const [status, setStatus] = useState("");
  const [expiring, setExpiring] = useState(false);
  const [owner, setOwner] = useState<string | null>(null);
  const [assign, setAssign] = useState(false);
  const subs = useQuery({
    queryKey: ["admin-subscriptions", status, expiring],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/subscriptions", {
          params: { query: { status: status === "" ? undefined : (Number(status) as Schemas["SubscriptionStatus"]), expiringWithinDays: expiring ? 7 : undefined } },
        }),
      ),
  });
  return (
    <>
      <div className="flex flex-wrap gap-2 items-center">
        <Select label="الحالة" value={status} onChange={setStatus} options={STATUSES} />
        <label className="flex gap-2 text-sm items-center">
          <input type="checkbox" checked={expiring} onChange={(e) => setExpiring(e.target.checked)} /> بتنتهي خلال ٧ أيام
        </label>
        <button type="button" className={`${btnPrimary} ms-auto`} onClick={() => setAssign(true)}>إسناد باقة لمالك</button>
      </div>
      <QueryState query={subs} isEmpty={(d) => d.length === 0} empty="مفيش اشتراكات.">
        {(list) => (
          <Table<Schemas["SubscriptionListItem"]>
            caption="الاشتراكات"
            rows={list}
            rowKey={(s) => s.id}
            selected={owner}
            onSelect={(s) => setOwner(s.ownerId)}
            columns={[
              { title: "المالك", cell: (s) => <span dir="ltr" className="font-inter text-xs">{s.ownerId.slice(0, 8)}</span> },
              { title: "الباقة", cell: (s) => s.planName },
              { title: "الدورة", cell: (s) => (s.cycle === "Annual" ? "سنوي" : "شهري") },
              { title: "الحالة", cell: (s) => <Pill tone={STATUS_TONE[s.status] ?? "neutral"}>{s.status}</Pill> },
              { title: "نهاية الفترة", cell: (s) => fmtDate(s.currentPeriodEndUtc) },
            ]}
          />
        )}
      </QueryState>
      <OwnerDrawer ownerId={owner} onClose={() => setOwner(null)} />
      {assign && <AssignDrawer onClose={() => setAssign(false)} />}
    </>
  );
}

function usePlans() {
  return useQuery({ queryKey: ["admin-plans"], queryFn: () => unwrap(api.GET("/api/v1/admin/plans")) });
}

function AssignDrawer({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const plans = usePlans();
  const usersQuery = useQuery({
    queryKey: ["admin-users-owners-select"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/users", { params: { query: { role: "VenueOwner", pageSize: 100 } } })),
  });
  const [ownerId, setOwnerId] = useState("");
  const [planId, setPlanId] = useState("");
  const [cycle, setCycle] = useState("0");
  const [trial, setTrial] = useState(true);
  const assign = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/admin/subscriptions", { body: { ownerId: ownerId.trim(), planId, cycle: Number(cycle) as Schemas["BillingCycle"], withTrial: trial } })),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["admin-subscriptions"] }); onClose(); },
    onError: notice.fail,
  });
  const active = (plans.data ?? []).filter((p) => p.status === "Active");
  const ownerOptions: [string, string][] = [
    ["", "— اختر المالك —"],
    ...((usersQuery.data?.items ?? []).map((u) => [u.id, `${u.fullName} (${u.maskedPhone || u.email || ""})`] as [string, string])),
  ];
  return (
    <Drawer title="إسناد باقة" open onClose={onClose}>
      {notice.node}
      <Field label="المالك">
        <Select label="المالك" value={ownerId} onChange={setOwnerId} options={ownerOptions} />
      </Field>
      <Field label="الباقة">
        <Select label="الباقة" value={planId} onChange={setPlanId} options={[["", "اختار باقة"], ...active.map((p) => [p.id, p.name] as [string, string])]} />
      </Field>
      <Field label="الدورة"><Select label="الدورة" value={cycle} onChange={setCycle} options={CYCLES} /></Field>
      <label className="flex gap-2 text-sm items-center"><input type="checkbox" checked={trial} onChange={(e) => setTrial(e.target.checked)} /> ابدأ بفترة التجربة</label>
      <button type="button" className={btnPrimary} disabled={!/^[0-9a-f-]{36}$/i.test(ownerId.trim()) || !planId || assign.isPending} onClick={() => assign.mutate()}>إسناد</button>
    </Drawer>
  );
}

function OwnerDrawer({ ownerId, onClose }: { ownerId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const plans = usePlans();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("2");
  const [reference, setReference] = useState("");
  const [newPlan, setNewPlan] = useState("");
  const [newCycle, setNewCycle] = useState("0");

  const detail = useQuery({
    queryKey: ["admin-subscription", ownerId],
    enabled: !!ownerId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/subscriptions/owners/{ownerId}", { params: { path: { ownerId: ownerId! } } })),
  });
  const refresh = async (text: string) => {
    notice.ok(text);
    await queryClient.invalidateQueries({ queryKey: ["admin-subscription"] });
    await queryClient.invalidateQueries({ queryKey: ["admin-subscriptions"] });
    await queryClient.invalidateQueries({ queryKey: ["admin-invoices"] });
  };
  const path = { ownerId: ownerId ?? "" };
  const pay = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/payments", {
          params: { path },
          body: { amount: Number(amount), method: Number(method) as Schemas["PaymentMethod"], reference: reference || null, paidAtUtc: null, notes: null },
        }),
      ),
    onSuccess: (invoice) => { setAmount(""); setReference(""); return refresh(`اتسجلت الدفعة وطلعت فاتورة ${invoice.number}.`); },
    onError: notice.fail,
  });
  const changePlan = useMutation({
    mutationFn: () => unwrap(api.PUT("/api/v1/admin/subscriptions/owners/{ownerId}/plan", { params: { path }, body: { planId: newPlan, cycle: Number(newCycle) as Schemas["BillingCycle"], note: null } })),
    onSuccess: () => refresh("اتغيرت الباقة."),
    onError: notice.fail,
  });
  const lifecycle = useMutation({
    mutationFn: ({ kind, text }: { kind: "suspend" | "reactivate" | "cancel"; text: string }) => {
      if (kind === "suspend") return unwrap(api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/suspend", { params: { path }, body: { reason: text } }));
      if (kind === "reactivate") return unwrap(api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/reactivate", { params: { path }, body: { reason: text } }));
      return unwrap(api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/cancel", { params: { path }, body: { reason: text } }));
    },
    onSuccess: (_, { kind }) => refresh({ suspend: "اتوقف الاشتراك.", reactivate: "الاشتراك رجع.", cancel: "اتلغى الاشتراك." }[kind]),
    onError: notice.fail,
  });

  return (
    <Drawer title="اشتراك المالك" open={!!ownerId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(d) => (
          <>
            <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
              <KV k="الباقة">{d.subscription.plan.name} ({d.subscription.cycle === "Annual" ? "سنوي" : "شهري"})</KV>
              <KV k="الحالة"><Pill tone={STATUS_TONE[d.subscription.status] ?? "neutral"}>{d.subscription.status}</Pill></KV>
              <KV k="نهاية الفترة">{fmtDate(d.subscription.currentPeriodEndUtc)}</KV>
              {d.subscription.graceEndsAtUtc && <KV k="نهاية السماح">{fmtDate(d.subscription.graceEndsAtUtc)}</KV>}
              {d.subscription.statusReason && <KV k="السبب">{d.subscription.statusReason}</KV>}
              <KV k="يقدر يضيف ملاعب">{d.subscription.allowsNewResources ? "أيوه" : "لأ"}</KV>
            </div>
            <Section title="تسجيل دفعة">
              <div className="grid grid-cols-2 gap-2">
                <Field label="المبلغ"><TextInput inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value.replace(/[^\d.]/g, ""))} /></Field>
                <Field label="الطريقة"><Select label="الطريقة" value={method} onChange={setMethod} options={METHODS} /></Field>
              </div>
              <Field label="رقم العملية (اختياري)"><TextInput value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
              <span className={`text-xs ${muted}`}>سعر الباقة: {money(d.subscription.cycle === "Annual" ? d.subscription.plan.annualPrice : d.subscription.plan.monthlyPrice)}</span>
              <button type="button" className={btnPrimary} disabled={!(Number(amount) > 0) || pay.isPending} onClick={() => pay.mutate()}>تسجيل الدفعة</button>
            </Section>
            <Section title="تغيير الباقة">
              <div className="grid grid-cols-2 gap-2">
                <Select label="الباقة الجديدة" value={newPlan} onChange={setNewPlan} options={[["", "اختار"], ...(plans.data ?? []).filter((p) => p.status === "Active").map((p) => [p.id, p.name] as [string, string])]} />
                <Select label="الدورة" value={newCycle} onChange={setNewCycle} options={CYCLES} />
              </div>
              <button type="button" className={btnOutline} disabled={!newPlan} onClick={() => changePlan.mutate()}>تغيير (التخفيض لازم يناسب الاستخدام الحالي)</button>
            </Section>
            <div className="flex flex-wrap gap-2">
              {d.subscription.status !== "Suspended" && d.subscription.status !== "Cancelled" && (
                <button type="button" className={btnDanger} onClick={() => setReason({ title: "إيقاف الاشتراك", body: "المالك مش هيقدر يضيف ملاعب أو موظفين. الحجوزات الحالية مش هتتأثر.", confirmLabel: "إيقاف", onConfirm: (text) => lifecycle.mutate({ kind: "suspend", text }) })}>إيقاف</button>
              )}
              {d.subscription.status === "Suspended" && (
                <button type="button" className={btnOutline} onClick={() => setReason({ title: "إعادة الاشتراك", body: "", confirmLabel: "إعادة", onConfirm: (text) => lifecycle.mutate({ kind: "reactivate", text }) })}>إعادة</button>
              )}
              {d.subscription.status !== "Cancelled" && (
                <button type="button" className={btnSmall} onClick={() => setReason({ title: "إلغاء الاشتراك", body: "", confirmLabel: "إلغاء", onConfirm: (text) => lifecycle.mutate({ kind: "cancel", text }) })}>إلغاء</button>
              )}
            </div>
            <Section title="المدفوعات">
              {d.payments.length === 0 && <span className={`text-sm ${muted}`}>مفيش مدفوعات.</span>}
              {d.payments.map((p) => (
                <div key={p.id} className="flex justify-between text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <span>{money(p.amount)} · {p.method}</span>
                  <span className={muted}>{fmtDateTime(p.paidAtUtc)}</span>
                </div>
              ))}
            </Section>
            <Section title="السجل">
              {d.history.map((h) => (
                <div key={`${h.type}-${h.occurredAtUtc}`} className="text-[13px] border-b border-[#EEF0F5] py-1.5">
                  <strong>{h.type}</strong> <span className={muted}>· {fmtDateTime(h.occurredAtUtc)}</span>
                  {h.note && <p className={muted}>{h.note}</p>}
                </div>
              ))}
            </Section>
          </>
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}

function Invoices() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const invoices = useQuery({ queryKey: ["admin-invoices"], queryFn: () => unwrap(api.GET("/api/v1/admin/invoices", { params: { query: {} } })) });
  const voidInvoice = useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) => unwrap(api.POST("/api/v1/admin/invoices/{invoiceId}/void", { params: { path: { invoiceId: id } }, body: { reason: text } })),
    onSuccess: async () => { notice.ok("اتلغت الفاتورة (بتفضل في السجل)."); await queryClient.invalidateQueries({ queryKey: ["admin-invoices"] }); },
    onError: notice.fail,
  });
  return (
    <>
      {notice.node}
      <QueryState query={invoices} isEmpty={(d) => d.length === 0} empty="مفيش فواتير.">
        {(list) => (
          <Table<Schemas["InvoiceDto"]>
            caption="الفواتير"
            rows={list}
            rowKey={(i) => i.id}
            columns={[
              { title: "الرقم", cell: (i) => <span dir="ltr" className="font-inter">{i.number}</span> },
              { title: "الباقة", cell: (i) => i.planName },
              { title: "المبلغ", cell: (i) => money(i.amount) },
              { title: "التاريخ", cell: (i) => fmtDate(i.issuedAtUtc) },
              { title: "الحالة", cell: (i) => <Pill tone={i.status === "Void" ? "bad" : "ok"}>{i.status}</Pill> },
              {
                title: "",
                cell: (i) => (
                  <div className="flex gap-1">
                    <button type="button" className={btnSmall} onClick={() => openFile(`/api/v1/admin/invoices/${i.id}/pdf`).catch(notice.fail)}>PDF</button>
                    {i.status !== "Void" && (
                      <button type="button" className={btnSmall} onClick={() => setReason({ title: `إلغاء الفاتورة ${i.number}`, body: "الفاتورة مش بتتمسح، بتتعلّم ملغاة وبتطلع من الإيراد.", confirmLabel: "إلغاء", onConfirm: (text) => voidInvoice.mutate({ id: i.id, text }) })}>
                        إلغاء
                      </button>
                    )}
                  </div>
                ),
              },
            ]}
          />
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </>
  );
}
