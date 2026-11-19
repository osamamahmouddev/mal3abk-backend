"use client";

import React, { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, Tabs, btnDanger, btnOutline, btnPrimary, btnSmall, card, money, muted, toArabicDigits } from "@/components/ui";
import { Drawer, Field, QueryState, Section, Select, Table, TextInput, fmtDate, fmtDateTime, useNotice } from "@/components/admin";
import { api, openFile, unwrap } from "@/lib/api";
import { InvoiceModal } from "@/components/invoice-modal";
import { CalendarDays, ChevronLeft, CreditCard, ExternalLink, Eye, Hourglass, LayoutGrid, Package, ReceiptText, RefreshCw, Smartphone, Table2, User, X } from "lucide-react";
import { Pitch, SoccerBall } from "@/components/icons";

type Tab = "subscriptions" | "requests" | "invoices";
const STATUS_TONE: Record<string, "ok" | "warn" | "bad" | "neutral"> = { Active: "ok", Trial: "warn", Grace: "warn", Expired: "bad", Suspended: "bad", Cancelled: "neutral" };
const SUBSCRIPTION_STATUS_LABELS: Record<string, string> = {
  Active: "نشط",
  Trial: "تجريبي",
  Grace: "فترة سماح",
  Expired: "منتهي",
  Suspended: "موقوف",
  Cancelled: "ملغي",
};
const STATUSES: [string, string][] = [["", "كل الحالات"], ["0", "تجربة"], ["1", "نشط"], ["2", "فترة سماح"], ["3", "منتهي"], ["4", "موقوف"], ["5", "ملغي"]];
const METHODS: [string, string][] = [["0", "كاش"], ["1", "تحويل بنكي"], ["2", "إنستاباي"], ["3", "محفظة"]];
const CYCLES: [string, string][] = [["0", "شهري"], ["1", "سنوي"]];

/** Media URLs returned by the API are relative to the API host; load them through this app's authenticated BFF. */
function mediaSrc(path: string) {
  if (!path) return "";
  if (/^https?:\/\//i.test(path)) return path;
  if (path.startsWith("/admin/bff")) return path;
  return `/admin/bff${path.startsWith("/") ? "" : "/"}${path}`;
}

export default function BillingPage() {
  const [tab, setTab] = useState<Tab>("subscriptions");
  const pendingRequestsQuery = useQuery({
    queryKey: ["admin-subscription-requests-count"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/subscriptions/requests", {
          params: { query: { status: 0 as Schemas["SubscriptionRequestStatus"] } },
        }),
      ),
    refetchInterval: 30_000,
  });

  const pendingCount = (pendingRequestsQuery.data ?? []).length;

  return (
    <Page>
      <PageHeader
        title="الاشتراكات والفواتير"
        sub="الدفع يدوي (كاش، تحويل، إنستاباي، محفظة): مراجعة طلبات الترقية وتفعيل الباقات للملاك أو تسجيل الدفعات."
      />
      <Tabs<Tab>
        tabs={[
          ["subscriptions", "الاشتراكات"],
          ["requests", pendingCount > 0 ? `طلبات الترقية (${pendingCount})` : "طلبات الترقية"],
          ["invoices", "الفواتير"],
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === "subscriptions" && <Subscriptions />}
      {tab === "requests" && <SubscriptionRequests />}
      {tab === "invoices" && <Invoices />}
    </Page>
  );
}

function getExpiryBadge(endUtc: string, status: string) {
  if (status === "Expired" || status === "Cancelled") {
    return { text: "منتهي", tone: "bad" as const };
  }
  const end = new Date(endUtc);
  const now = new Date();
  const diffDays = Math.ceil((end.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
  if (diffDays < 0) {
    return { text: `انتهى منذ ${toArabicDigits(Math.abs(diffDays))} يوم`, tone: "bad" as const };
  }
  if (diffDays === 0) {
    return { text: "ينتهي اليوم", tone: "warn" as const };
  }
  if (diffDays <= 7) {
    return { text: `متبقي ${toArabicDigits(diffDays)} أيام`, tone: "warn" as const };
  }
  return { text: `متبقي ${toArabicDigits(diffDays)} يوم`, tone: "ok" as const };
}

function QuickRenewModal({
  target,
  onClose,
  onSuccess,
}: {
  target: { ownerId: string; ownerName?: string | null; planName: string; cycle: string } | null;
  onClose: () => void;
  onSuccess: (msg: string) => void;
}) {
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("2");
  const [reference, setReference] = useState("");
  const notice = useNotice();

  const pay = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/payments", {
          params: { path: { ownerId: target?.ownerId ?? "" } },
          body: {
            amount: Number(amount),
            method: Number(method) as Schemas["PaymentMethod"],
            reference: reference || null,
            paidAtUtc: null,
            notes: "تجديد اشتراك من لوحة الأدمن",
          },
        }),
      ),
    onSuccess: (invoice) => {
      onSuccess(`تم تجديد الاشتراك وتسجيل الدفعة بنجاح! فاتورة رقم: ${invoice.number}`);
      onClose();
    },
    onError: notice.fail,
  });

  if (!target) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-xs animate-fade-in">
      <div className="bg-white rounded-2xl p-6 max-w-md w-full shadow-2xl border border-slate-200 flex flex-col gap-4 animate-scale-in">
        <div className="flex items-center justify-between pb-3 border-b border-slate-100">
          <div>
            <h3 className="font-bold text-slate-900 text-lg">تجديد الاشتراك وتسجيل سداد</h3>
            <span className="text-xs text-[#5B6475]">المالك: {target.ownerName || target.ownerId.slice(0, 8)} · باقة {target.planName}</span>
          </div>
          <button type="button" onClick={onClose} className="text-slate-600 hover:text-slate-600 text-xl font-bold">×</button>
        </div>

        {notice.node}

        <Field label="المبلغ المدفوع (ج.م)">
          <TextInput
            placeholder="مثال: 500"
            inputMode="decimal"
            value={amount}
            onChange={(e) => setAmount(e.target.value.replace(/[^\d.]/g, ""))}
          />
        </Field>

        <Field label="طريقة التحويل / السداد">
          <Select label="طريقة السداد" value={method} onChange={setMethod} options={METHODS} />
        </Field>

        <Field label="رقم العملية / المرجع (اختياري)">
          <TextInput
            placeholder="مثال: IPN12345678"
            value={reference}
            onChange={(e) => setReference(e.target.value)}
          />
        </Field>

        <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100">
          <button type="button" className={btnOutline} onClick={onClose} disabled={pay.isPending}>
            إلغاء
          </button>
          <button
            type="button"
            className={btnPrimary}
            disabled={!(Number(amount) > 0) || pay.isPending}
            onClick={() => pay.mutate()}
          >
            {pay.isPending ? "جارٍ التسجيل..." : "تأكيد التجديد وتمديد الاشتراك"}
          </button>
        </div>
      </div>
    </div>
  );
}

function Subscriptions() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [status, setStatus] = useState("");
  const [expiring, setExpiring] = useState(false);
  const [search, setSearch] = useState("");
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");
  const [owner, setOwner] = useState<string | null>(null);
  const [assign, setAssign] = useState(false);
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [renewTarget, setRenewTarget] = useState<{ ownerId: string; ownerName?: string | null; planName: string; cycle: string } | null>(null);

  const subs = useQuery({
    queryKey: ["admin-subscriptions", status, expiring],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/subscriptions", {
          params: { query: { status: status === "" ? undefined : (Number(status) as Schemas["SubscriptionStatus"]), expiringWithinDays: expiring ? 7 : undefined } },
        }),
      ),
  });

  const lifecycle = useMutation({
    mutationFn: ({ ownerId, kind, text }: { ownerId: string; kind: "suspend" | "reactivate" | "cancel"; text: string }) => {
      if (kind === "suspend") return unwrap(api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/suspend", { params: { path: { ownerId } }, body: { reason: text } }));
      if (kind === "reactivate") return unwrap(api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/reactivate", { params: { path: { ownerId } }, body: { reason: text } }));
      return unwrap(api.POST("/api/v1/admin/subscriptions/owners/{ownerId}/cancel", { params: { path: { ownerId } }, body: { reason: text } }));
    },
    onSuccess: async (_, { kind }) => {
      await queryClient.invalidateQueries({ queryKey: ["admin-subscriptions"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-subscription"] });
      notice.ok({ suspend: "اتوقف الاشتراك بنجاح.", reactivate: "تمت إعادة تنشيط الاشتراك.", cancel: "اتلغى الاشتراك." }[kind]);
    },
    onError: notice.fail,
  });

  const filterList = (list: Schemas["SubscriptionListItem"][]) => {
    if (!search.trim()) return list;
    const q = search.trim().toLowerCase();
    return list.filter((s) => {
      const matchOwner = (s.ownerName || "").toLowerCase().includes(q);
      const matchPhone = (s.ownerPhone || "").includes(q);
      const matchEmail = (s.ownerEmail || "").toLowerCase().includes(q);
      const matchPlan = (s.planName || "").toLowerCase().includes(q);
      const matchVenues = (s.venues || []).some((v) => v.name.toLowerCase().includes(q) || v.city.toLowerCase().includes(q));
      const matchId = s.ownerId.toLowerCase().includes(q);
      return matchOwner || matchPhone || matchEmail || matchPlan || matchVenues || matchId;
    });
  };

  return (
    <>
      {notice.node}
      <div className="flex flex-wrap gap-3 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap gap-2.5 items-center flex-1 min-w-[280px]">
          <div className="w-full sm:w-72 shrink-0">
            <TextInput
              placeholder="ابحث بالاسم، الموبايل، أو الملعب..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <div className="w-36 sm:w-44 shrink-0">
            <Select label="الحالة" value={status} onChange={setStatus} options={STATUSES} />
          </div>
          <label className="flex gap-2 text-xs font-semibold items-center text-slate-700 cursor-pointer select-none px-2 py-2 rounded-xl hover:bg-slate-50 transition-colors">
            <input
              type="checkbox"
              className="w-4 h-4 rounded text-ink focus:ring-ink"
              checked={expiring}
              onChange={(e) => setExpiring(e.target.checked)}
            />
            بتنتهي خلال ٧ أيام
          </label>
          {(search || status || expiring) && (
            <button
              type="button"
              onClick={() => {
                setSearch("");
                setStatus("");
                setExpiring(false);
              }}
              className="text-xs font-bold text-slate-500 hover:text-slate-800 px-3 py-1.5 rounded-lg hover:bg-slate-100 transition-colors cursor-pointer"
            >
              إعادة ضبط
            </button>
          )}
        </div>

        <div className="flex items-center gap-2">
          {/* Toggle between Cards & Table */}
          <div className="inline-flex rounded-xl bg-slate-100 p-1 border border-slate-200">
            <button
              type="button"
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
                viewMode === "cards" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
              }`}
              onClick={() => setViewMode("cards")}
            >
              <LayoutGrid className="size-3.5" />
              بطاقات
            </button>
            <button
              type="button"
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
                viewMode === "table" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
              }`}
              onClick={() => setViewMode("table")}
            >
              <Table2 className="size-3.5" />
              جدول
            </button>
          </div>

          <button type="button" className={`${btnPrimary}`} onClick={() => setAssign(true)}>
            + إسناد باقة لمالك
          </button>
        </div>
      </div>

      <QueryState
        query={subs}
        isEmpty={(d) => filterList(d).length === 0}
        emptyTitle="لم يتم العثور على أي اشتراكات"
        emptyDescription={search || status || expiring ? "لا توجد اشتراكات تطابق البحث أو الفلتر المحدد. يمكنك إعادة ضبط الفلاتر أو إسناد باقة جديدة." : "لا توجد اشتراكات مسجلة لأصحاب الملاعب حتى الآن."}
        emptyIcon={search || status || expiring ? "search" : "receipt"}
        emptyAction={
          search || status || expiring
            ? {
                label: "إعادة ضبط الفلاتر",
                onClick: () => {
                  setSearch("");
                  setStatus("");
                  setExpiring(false);
                },
                variant: "outline",
              }
            : {
                label: "إسناد باقة لمالك الآن",
                onClick: () => setAssign(true),
                variant: "primary",
              }
        }
      >
        {(list) => {
          const filtered = filterList(list);
          if (viewMode === "cards") {
            return (
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                {filtered.map((s) => {
                  const exp = getExpiryBadge(s.currentPeriodEndUtc, s.status);
                  return (
                    <div
                      key={s.id}
                      className={`${card} rounded-[20px] p-5 flex flex-col justify-between gap-4 border border-[#E2E6EF] hover:border-slate-400 hover:shadow-md transition-all`}
                    >
                      {/* Top: Owner & Status */}
                      <div className="flex items-start justify-between gap-3">
                        <div className="flex items-center gap-3 min-w-0">
                          <div className="w-11 h-11 rounded-2xl bg-gradient-to-br from-slate-900 to-slate-700 text-white flex items-center justify-center font-bold text-base shrink-0 shadow-sm">
                            {(s.ownerName || "م")[0]}
                          </div>
                          <div className="min-w-0 flex flex-col">
                            <div className="flex items-center gap-2">
                              <h3 className="font-bold text-slate-900 text-base truncate">{s.ownerName || "مالك ملعب"}</h3>
                              <span dir="ltr" className="text-[10px] font-mono text-slate-600 bg-slate-100 px-1.5 py-0.5 rounded border border-slate-200">
                                {s.ownerId.slice(0, 8)}
                              </span>
                            </div>
                            <div className="flex flex-wrap items-center gap-1.5 text-xs text-[#5B6475] mt-1">
                              {s.ownerPhone && (
                                <span dir="ltr" className="font-medium text-slate-800 bg-[#F4F6F9] px-2 py-0.5 rounded-md flex items-center gap-1">
                                  <Smartphone className="size-3 text-[#8A93A3]" />
                                  {s.ownerPhone}
                                </span>
                              )}
                              {s.ownerEmail && <span className="truncate text-slate-600">{s.ownerEmail}</span>}
                            </div>
                          </div>
                        </div>

                        <div className="shrink-0 flex items-center gap-1.5">
                          <Pill tone={STATUS_TONE[s.status] ?? "neutral"} className="flex items-center gap-1.5 text-xs py-1 px-3">
                            {s.status === "Active" && <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />}
                            {SUBSCRIPTION_STATUS_LABELS[s.status] ?? s.status}
                          </Pill>
                        </div>
                      </div>

                      {/* Middle: Plan info & Dates */}
                      <div className="bg-[#F8FAFC] rounded-xl p-3.5 border border-[#EEF1F6] flex flex-col gap-2.5">
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2">
                            <span className="text-xs text-[#5B6475]">الباقة:</span>
                            <span className="font-extrabold text-slate-900 text-sm bg-white px-2.5 py-1 rounded-lg border border-[#E2E6EF] shadow-xs">
                              {s.planName}
                            </span>
                            <span className="text-xs bg-[#EBF0FF] text-[#1E3A8A] font-semibold px-2 py-0.5 rounded-md">
                              {s.cycle === "Annual" ? "سنوي" : "شهري"}
                            </span>
                          </div>
                          <div>
                            <Pill tone={exp.tone} className="text-[11px] font-medium">
                              {exp.text}
                            </Pill>
                          </div>
                        </div>

                        <div className="grid grid-cols-2 gap-2 text-xs pt-2 border-t border-[#E8ECF2]">
                          <div>
                            <span className="text-[#5B6475] flex items-center gap-1 text-[11px]"><CalendarDays className="size-3" /> تاريخ الاشتراك</span>
                            <span className="font-bold text-slate-800 mt-0.5 block">{fmtDate(s.startedAtUtc)}</span>
                          </div>
                          <div>
                            <span className="text-[#5B6475] flex items-center gap-1 text-[11px]"><RefreshCw className="size-3" /> تاريخ التجديد</span>
                            <span className="font-bold text-slate-800 mt-0.5 block">{fmtDate(s.currentPeriodEndUtc)}</span>
                          </div>
                          {s.graceEndsAtUtc && (
                            <div className="col-span-2 pt-1 text-[11px] text-amber-700 bg-amber-50 p-1.5 rounded border border-amber-200 flex items-center gap-1">
                              <Hourglass className="size-3 shrink-0" /> فترة السماح تنتهي في: <b>{fmtDate(s.graceEndsAtUtc)}</b>
                            </div>
                          )}
                        </div>
                      </div>

                      {/* Venues Section */}
                      <div className="flex flex-col gap-1.5">
                        <div className="flex items-center justify-between text-xs text-[#5B6475] font-semibold">
                          <span className="flex items-center gap-1.5"><Pitch className="size-3.5" /> الملاعب المشتركة ({toArabicDigits(s.venues?.length ?? 0)}):</span>
                        </div>
                        <div className="flex flex-wrap gap-1.5 min-h-[30px]">
                          {(s.venues?.length ?? 0) > 0 ? (
                            s.venues.map((v) => (
                              <span
                                key={v.id}
                                className="inline-flex items-center gap-1.5 bg-white border border-[#DCE2ED] text-slate-800 px-2.5 py-1 rounded-lg text-xs font-semibold shadow-2xs"
                              >
                                <SoccerBall className="size-3.5 text-emerald-700" strokeWidth={1.75} />
                                <span>{v.name}</span>
                                {v.city && <span className="text-[11px] text-[#5B6475]">({v.city})</span>}
                              </span>
                            ))
                          ) : (
                            <span className="text-xs text-slate-600 italic">لا توجد ملاعب مسجلة لهذا المالك بعد</span>
                          )}
                        </div>
                      </div>

                      {/* Bottom: Action Buttons */}
                      <div className="pt-3 border-t border-[#EEF0F5] flex flex-wrap items-center justify-between gap-2 mt-auto">
                        <div className="flex flex-wrap gap-1.5">
                          <button
                            type="button"
                            className={`${btnSmall} bg-slate-900 text-white hover:bg-slate-800 border-none font-bold`}
                            onClick={() => setRenewTarget({ ownerId: s.ownerId, ownerName: s.ownerName, planName: s.planName, cycle: s.cycle })}
                          >
                            <CreditCard className="size-3.5" />
                            تجديد / سداد
                          </button>

                          {s.status !== "Suspended" && s.status !== "Cancelled" && (
                            <button
                              type="button"
                              className={`${btnSmall} text-[#A8261C] border-[#F3C4BF] hover:bg-red-50`}
                              onClick={() =>
                                setReason({
                                  title: "إيقاف الاشتراك",
                                  body: `هل أنت متأكد من إيقاف اشتراك المالك (${s.ownerName || s.ownerId.slice(0, 8)})؟ لن يتمكن من إضافة ملاعب أو موظفين جديدة.`,
                                  confirmLabel: "إيقاف الاشتراك",
                                  onConfirm: (text) => lifecycle.mutate({ ownerId: s.ownerId, kind: "suspend", text }),
                                })
                              }
                            >
                              إيقاف
                            </button>
                          )}

                          {s.status === "Suspended" && (
                            <button
                              type="button"
                              className={`${btnSmall} text-emerald-700 border-emerald-300 hover:bg-emerald-50`}
                              onClick={() =>
                                setReason({
                                  title: "إعادة تنشيط الاشتراك",
                                  body: `سيتم إعادة تنشيط اشتراك المالك (${s.ownerName || s.ownerId.slice(0, 8)}) فوراً.`,
                                  confirmLabel: "إعادة التنشيط",
                                  onConfirm: (text) => lifecycle.mutate({ ownerId: s.ownerId, kind: "reactivate", text }),
                                })
                              }
                            >
                              إعادة التنشيط
                            </button>
                          )}
                        </div>

                        <button
                          type="button"
                          className={`${btnSmall} text-slate-700 hover:bg-slate-100 font-medium`}
                          onClick={() => setOwner(s.ownerId)}
                        >
                          التفاصيل والفواتير<ChevronLeft className="size-3.5 inline-block align-[-3px] ms-1" />
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            );
          }

          return (
            <Table<Schemas["SubscriptionListItem"]>
              caption="الاشتراكات"
              rows={filtered}
              rowKey={(s) => s.id}
              selected={owner}
              onSelect={(s) => setOwner(s.ownerId)}
              columns={[
                {
                  title: "المالك",
                  cell: (s) => (
                    <div className="flex flex-col">
                      <span className="font-bold text-slate-900">{s.ownerName || "مالك ملعب"}</span>
                      <div className="flex items-center gap-1.5 text-xs text-[#5B6475]">
                        {s.ownerPhone && <span dir="ltr">{s.ownerPhone}</span>}
                        <span dir="ltr" className="text-[10px] font-mono text-slate-600 bg-slate-100 px-1 rounded">
                          {s.ownerId.slice(0, 8)}
                        </span>
                      </div>
                    </div>
                  ),
                },
                {
                  title: "الملاعب المشتركة",
                  cell: (s) => (
                    <div className="flex flex-wrap gap-1 max-w-xs">
                      {(s.venues?.length ?? 0) > 0 ? (
                        s.venues.map((v) => (
                          <span key={v.id} className="text-xs bg-slate-100 text-slate-800 px-2 py-0.5 rounded border border-slate-200 inline-flex items-center gap-1">
                            <SoccerBall className="size-3 text-emerald-700" strokeWidth={1.75} />
                            {v.name}
                          </span>
                        ))
                      ) : (
                        <span className="text-xs text-slate-600 italic">بدون ملاعب</span>
                      )}
                    </div>
                  ),
                },
                { title: "الباقة", cell: (s) => s.planName },
                { title: "الدورة", cell: (s) => (s.cycle === "Annual" ? "سنوي" : "شهري") },
                {
                  title: "الحالة",
                  cell: (s) => <Pill tone={STATUS_TONE[s.status] ?? "neutral"}>{SUBSCRIPTION_STATUS_LABELS[s.status] ?? s.status}</Pill>,
                },
                { title: "تاريخ الاشتراك", cell: (s) => fmtDate(s.startedAtUtc) },
                {
                  title: "تاريخ التجديد",
                  cell: (s) => {
                    const exp = getExpiryBadge(s.currentPeriodEndUtc, s.status);
                    return (
                      <div className="flex flex-col">
                        <span>{fmtDate(s.currentPeriodEndUtc)}</span>
                        <span className="text-[10px] text-[#5B6475]">{exp.text}</span>
                      </div>
                    );
                  },
                },
                {
                  title: "الإجراءات",
                  cell: (s) => (
                    <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        className={`${btnSmall} bg-slate-900 text-white hover:bg-slate-800 border-none`}
                        onClick={() => setRenewTarget({ ownerId: s.ownerId, ownerName: s.ownerName, planName: s.planName, cycle: s.cycle })}
                      >
                        تجديد
                      </button>
                      {s.status !== "Suspended" && s.status !== "Cancelled" && (
                        <button
                          type="button"
                          className={`${btnSmall} text-[#A8261C] border-[#F3C4BF] hover:bg-red-50`}
                          onClick={() =>
                            setReason({
                              title: "إيقاف الاشتراك",
                              body: `إيقاف اشتراك المالك (${s.ownerName || s.ownerId.slice(0, 8)})؟`,
                              confirmLabel: "إيقاف",
                              onConfirm: (text) => lifecycle.mutate({ ownerId: s.ownerId, kind: "suspend", text }),
                            })
                          }
                        >
                          إيقاف
                        </button>
                      )}
                      {s.status === "Suspended" && (
                        <button
                          type="button"
                          className={`${btnSmall} text-emerald-700 border-emerald-300 hover:bg-emerald-50`}
                          onClick={() =>
                            setReason({
                              title: "إعادة تنشيط الاشتراك",
                              body: `إعادة تنشيط اشتراك المالك (${s.ownerName || s.ownerId.slice(0, 8)})؟`,
                              confirmLabel: "تنشيط",
                              onConfirm: (text) => lifecycle.mutate({ ownerId: s.ownerId, kind: "reactivate", text }),
                            })
                          }
                        >
                          تنشيط
                        </button>
                      )}
                    </div>
                  ),
                },
              ]}
            />
          );
        }}
      </QueryState>

      <OwnerDrawer ownerId={owner} onClose={() => setOwner(null)} />
      {assign && <AssignDrawer onClose={() => setAssign(false)} />}
      <QuickRenewModal
        target={renewTarget}
        onClose={() => setRenewTarget(null)}
        onSuccess={async (msg) => {
          notice.ok(msg);
          await queryClient.invalidateQueries({ queryKey: ["admin-subscriptions"] });
          await queryClient.invalidateQueries({ queryKey: ["admin-invoices"] });
        }}
      />
      <ReasonModal request={reason} onClose={() => setReason(null)} />
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

  const venuesQuery = useQuery({
    queryKey: ["admin-venues-for-assign"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues", { params: { query: { pageSize: 100 } } })),
  });

  const subsQuery = useQuery({
    queryKey: ["admin-subscriptions"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/subscriptions")),
  });

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
      unwrap(api.POST("/api/v1/admin/subscriptions/assign-or-change", { body: { ownerId: ownerId.trim(), planId, cycle: Number(cycle) as Schemas["BillingCycle"], withTrial: trial } })),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["admin-subscriptions"] }); onClose(); },
    onError: notice.fail,
  });

  const active = (plans.data ?? []).filter((p) => p.status === "Active");

  interface OwnerEntry {
    id: string;
    name: string;
    phone: string;
    email: string;
    venues: string[];
    currentPlan?: string;
  }

  const ownersMap = useMemo(() => {
    const map = new Map<string, OwnerEntry>();

    // 1. From Venues
    for (const v of venuesQuery.data?.items ?? []) {
      if (!v.ownerId) continue;
      const existing = map.get(v.ownerId) ?? {
        id: v.ownerId,
        name: v.ownerName || "مالك ملعب",
        phone: v.ownerPhone || "",
        email: "",
        venues: [],
        currentPlan: v.planName || undefined,
      };
      const venueStr = `${v.name}${v.city ? ` (${v.city})` : ""}`;
      if (!existing.venues.includes(venueStr)) {
        existing.venues.push(venueStr);
      }
      if (v.ownerName && (!existing.name || existing.name === "مالك ملعب")) existing.name = v.ownerName;
      if (v.ownerPhone && !existing.phone) existing.phone = v.ownerPhone;
      if (v.planName && !existing.currentPlan) existing.currentPlan = v.planName;
      map.set(v.ownerId, existing);
    }

    // 2. From Subscriptions
    for (const s of subsQuery.data ?? []) {
      if (!s.ownerId) continue;
      const existing = map.get(s.ownerId) ?? {
        id: s.ownerId,
        name: s.ownerName || "مالك ملعب",
        phone: s.ownerPhone || "",
        email: s.ownerEmail || "",
        venues: [],
        currentPlan: s.planName || undefined,
      };
      if (s.ownerName) existing.name = s.ownerName;
      if (s.ownerPhone) existing.phone = s.ownerPhone;
      if (s.ownerEmail) existing.email = s.ownerEmail;
      if (s.planName) existing.currentPlan = s.planName;
      for (const v of s.venues ?? []) {
        const venueStr = `${v.name}${v.city ? ` (${v.city})` : ""}`;
        if (!existing.venues.includes(venueStr)) existing.venues.push(venueStr);
      }
      map.set(s.ownerId, existing);
    }

    // 3. From Users (registered owners who might not have a venue yet)
    for (const u of usersQuery.data?.items ?? []) {
      if (!u.id) continue;
      const existing = map.get(u.id) ?? {
        id: u.id,
        name: u.fullName,
        phone: u.maskedPhone || "",
        email: u.email || "",
        venues: [],
      };
      if (u.fullName && (!existing.name || existing.name === "مالك ملعب")) existing.name = u.fullName;
      if (u.maskedPhone && !existing.phone) existing.phone = u.maskedPhone;
      if (u.email && !existing.email) existing.email = u.email;
      map.set(u.id, existing);
    }

    return Array.from(map.values()).sort((a, b) => {
      // Prioritize owners with venues, then alphabetically
      if (a.venues.length > 0 && b.venues.length === 0) return -1;
      if (a.venues.length === 0 && b.venues.length > 0) return 1;
      return a.name.localeCompare(b.name, "ar");
    });
  }, [venuesQuery.data, subsQuery.data, usersQuery.data]);

  const ownerOptions: [string, string][] = [
    ["", "— اختر المالك أو الملعب —"],
    ...ownersMap.map((o) => {
      const venueLabel = o.venues.length > 0 ? o.venues.join("، ") : "بدون ملاعب مسجلة";
      const contact = o.phone || o.email || "";
      const text = `${o.name} — ${venueLabel}${contact ? ` (${contact})` : ""}`;
      return [o.id, text] as [string, string];
    }),
  ];

  const selectedOwner = ownersMap.find((o) => o.id === ownerId);

  return (
    <Drawer title="إسناد / تغيير باقة لمالك" open onClose={onClose}>
      {notice.node}
      <Field label="المالك والملعب التابع">
        <Select label="المالك والملعب التابع" value={ownerId} onChange={setOwnerId} options={ownerOptions} />
        {(venuesQuery.isLoading || usersQuery.isLoading) && (
          <span className="text-xs text-muted mt-1 block">جاري تحميل بيانات الملاك والملاعب...</span>
        )}
      </Field>

      {selectedOwner && (
        <div className="p-3.5 bg-blue-50/70 border border-blue-200/80 rounded-xl flex flex-col gap-2 text-xs animate-fade-in -mt-2">
          <div className="flex items-center justify-between border-b border-blue-100 pb-1.5">
            <span className="font-bold text-navy-950 text-sm flex items-center gap-1.5"><User className="size-4 text-gray-500" /> {selectedOwner.name}</span>
            <span className="text-gray-500 font-mono text-[11px]">{selectedOwner.phone || selectedOwner.email}</span>
          </div>
          <div className="flex items-start gap-1.5 text-navy-900">
            <span className="font-semibold text-gray-600 shrink-0 flex items-center gap-1"><Pitch className="size-3.5" /> الملاعب التابعة:</span>
            <span className="font-bold text-emerald-800">
              {selectedOwner.venues.length > 0 ? selectedOwner.venues.join("، ") : "لا توجد ملاعب مسجلة باسمه بعد"}
            </span>
          </div>
          {selectedOwner.currentPlan && (
            <div className="flex items-center gap-1.5 text-navy-800">
              <span className="font-semibold text-gray-600 shrink-0 flex items-center gap-1"><Package className="size-3.5" /> الاشتراك الحالي:</span>
              <span className="font-bold text-blue-700 bg-blue-100 px-2 py-0.5 rounded-md">
                {selectedOwner.currentPlan}
              </span>
            </div>
          )}
        </div>
      )}

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
              <KV k="الحالة"><Pill tone={STATUS_TONE[d.subscription.status] ?? "neutral"}>{SUBSCRIPTION_STATUS_LABELS[d.subscription.status] ?? d.subscription.status}</Pill></KV>
              <KV k="نهاية الفترة">{fmtDate(d.subscription.currentPeriodEndUtc)}</KV>
              {d.subscription.graceEndsAtUtc && <KV k="نهاية السماح">{fmtDate(d.subscription.graceEndsAtUtc)}</KV>}
              {d.subscription.statusReason && <KV k="السبب">{d.subscription.statusReason}</KV>}
              <KV k="يقدر يضيف ملاعب">{d.subscription.allowsNewResources ? "نعم" : "لا"}</KV>
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

function SubscriptionRequests() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [replyModal, setReplyModal] = useState<{ id: string; defaultMessage: string } | null>(null);
  const [replyText, setReplyText] = useState("");
  const [rejectReason, setRejectReason] = useState<ReasonRequest | null>(null);
  const [viewReceipt, setViewReceipt] = useState<Schemas["SubscriptionRequestDto"] | null>(null);

  const requests = useQuery({
    queryKey: ["admin-subscription-requests", statusFilter],
    refetchInterval: 15_000,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/subscriptions/requests", {
          params: { query: { status: statusFilter === "" ? undefined : (Number(statusFilter) as Schemas["SubscriptionRequestStatus"]) } },
        }),
      ),
  });

  const approve = useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST("/api/v1/admin/subscriptions/requests/{requestId}/approve", { params: { path: { requestId: id } } })),
    onSuccess: async () => {
      notice.ok("تمت الموافقة وترقية الباقة للمالك بنجاح!");
      await queryClient.invalidateQueries({ queryKey: ["admin-subscription-requests"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-subscription-requests-count"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-subscriptions"] });
    },
    onError: notice.fail,
  });

  const reply = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/subscriptions/requests/{requestId}/reply", {
          params: { path: { requestId: replyModal!.id } },
          body: { instructions: replyText.trim() },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تم إرسال تعليمات الدفع للمالك بنجاح.");
      setReplyModal(null);
      await queryClient.invalidateQueries({ queryKey: ["admin-subscription-requests"] });
    },
    onError: notice.fail,
  });

  const reject = useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) =>
      unwrap(
        api.POST("/api/v1/admin/subscriptions/requests/{requestId}/reject", {
          params: { path: { requestId: id } },
          body: { reason: text || null },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تم رفض طلب الترقية وإشعار المالك.");
      await queryClient.invalidateQueries({ queryKey: ["admin-subscription-requests"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-subscription-requests-count"] });
    },
    onError: notice.fail,
  });

  const REQUEST_STATUSES: [string, string][] = [
    ["", "كل الحالات"],
    ["0", "قيد الانتظار"],
    ["1", "تمت الموافقة"],
    ["2", "مرفوض"],
  ];

  return (
    <>
      {notice.node}
      <div className="flex flex-wrap gap-2 items-center bg-white p-3 rounded-2xl border border-slate-200/90 shadow-xs mb-3">
        <div className="w-full sm:w-56 shrink-0">
          <Select label="الحالة" value={statusFilter} onChange={setStatusFilter} options={REQUEST_STATUSES} />
        </div>
      </div>
      <QueryState
        query={requests}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد طلبات تغيير باقات"
        emptyDescription="رائع! تمت مراجعة ومعالجة جميع طلبات ترقية أو تجديد الباقات المقدمة من الملاك."
        emptyIcon="check"
        emptyBadge="كل الطلبات منتهية"
      >
        {(list) => (
          <Table<Schemas["SubscriptionRequestDto"]>
            caption="طلبات تغيير الباقات"
            rows={list}
            rowKey={(r) => r.id}
            columns={[
              {
                title: "صاحب الحساب / المالك",
                cell: (r) => (
                  <div className="flex flex-col">
                    <span className="font-bold text-[#111827]">{r.ownerName || "مالك"}</span>
                    <span className="text-xs text-[#6B7280]">{r.ownerPhone || r.ownerEmail || r.ownerId.slice(0, 8)}</span>
                  </div>
                ),
              },
              { title: "الباقة الحالية", cell: (r) => <span className="text-xs text-[#4B5563]">{r.currentPlanName}</span> },
              {
                title: "الباقة المطلوبة",
                cell: (r) => (
                  <div className="flex flex-col">
                    <span className="font-bold text-navy-900">{r.requestedPlan?.name ?? "باقة"}</span>
                    <span className="text-xs text-[#6B7280]">
                      {r.cycle === "Annual" ? "سنوي" : "شهري"} · {money(r.amount)}
                    </span>
                  </div>
                ),
              },
              { title: "تاريخ الطلب", cell: (r) => <span className="text-xs text-[#4B5563]">{fmtDateTime(r.createdAtUtc)}</span> },
              {
                title: "الحالة",
                cell: (r) => (
                  <Pill tone={r.status === "Approved" ? "ok" : r.status === "Pending" ? "warn" : "bad"}>
                    {r.status === "Approved" ? "تم التفعيل" : r.status === "Pending" ? "قيد الانتظار" : "مرفوض"}
                  </Pill>
                ),
              },
              {
                title: "حالة السداد والإيصال",
                cell: (r) => (
                  <div className="flex flex-col gap-1 max-w-[200px]">
                    {r.isPaymentReported ? (
                      <>
                        <div className="flex items-center gap-1.5">
                          <Pill tone={r.status === "Approved" ? "ok" : "warn"}>
                            {r.status === "Approved" ? "تم قبول التحويل" : "قيد الانتظار"}
                          </Pill>
                        </div>
                        {r.transferNotes && (
                          <span className="text-[11px] text-gray-700 leading-tight">
                            {r.transferNotes}
                          </span>
                        )}
                        {r.transferReference && (
                          <span className="text-[10px] font-mono text-gray-500">
                            رقم: {r.transferReference}
                          </span>
                        )}
                        {r.paymentProofUrl && (
                          <button
                            type="button"
                            onClick={() => setViewReceipt(r)}
                            className="text-xs text-blue-600 hover:text-blue-800 underline font-semibold flex items-center gap-1 w-fit cursor-pointer mt-0.5"
                          >
                            <Eye className="size-3.5" />
                            <span>معاينة الإيصال</span>
                          </button>
                        )}
                      </>
                    ) : (
                      <span className="text-xs text-gray-400">لم يتم إرفاق إيصال بعد</span>
                    )}
                  </div>
                ),
              },
              {
                title: "ملاحظات / تعليمات",
                cell: (r) => (
                  <div className="max-w-[220px] text-xs flex flex-col gap-1">
                    {r.paymentProofUrl && (
                      <button type="button" onClick={() => setViewReceipt(r)} className="w-fit rounded-lg border border-emerald-200 bg-emerald-50 p-1 hover:bg-emerald-100" title="معاينة صورة التحويل">
                        <img src={mediaSrc(r.paymentProofUrl)} alt="صورة إيصال التحويل" className="h-14 w-20 rounded object-cover" />
                      </button>
                    )}
                    {r.notes && <p className="text-[#374151] italic">"{r.notes}"</p>}
                    {r.adminReply && <p className="text-[#1E40AF] bg-blue-50 p-1.5 rounded text-[11px]">رد الإدارة: {r.adminReply}</p>}
                  </div>
                ),
              },
              {
                title: "",
                cell: (r) =>
                  r.status === "Pending" ? (
                    <div className="flex items-center gap-1.5">
                      <button
                        type="button"
                        className={`${btnSmall} min-h-9 px-3 whitespace-nowrap font-bold ${r.isPaymentReported ? "bg-emerald-600 hover:bg-emerald-700 text-white" : "bg-gray-100 text-gray-500 cursor-not-allowed"}`}
                        disabled={approve.isPending || !r.isPaymentReported}
                        onClick={() => approve.mutate(r.id)}
                        title={r.isPaymentReported ? "قبول التحويل وتفعيل الباقة" : "لا يمكن التفعيل قبل إرفاق إثبات الدفع"}
                      >
                        {r.isPaymentReported ? "قبول التحويل" : "بانتظار الإيصال"}
                      </button>
                      <button
                        type="button"
                        className={btnSmall}
                        onClick={() => {
                          const def = `برجاء تحويل مبلغ ${money(r.amount)} عبر إنستاباي على حساب (osama@instapay) أو محفظة فودافون كاش على رقم (010xxxxxxxx) وتأكيد التحويل لتفعيل باقة ${r.requestedPlan?.name ?? ""}.`;
                          setReplyText(def);
                          setReplyModal({ id: r.id, defaultMessage: def });
                        }}
                        title="إرسال تعليمات الدفع بالإنستاباي / فودافون كاش"
                      >
                        تعليمات الدفع
                      </button>
                      <button
                        type="button"
                        className={`${btnSmall} text-red-600 hover:bg-red-50`}
                        onClick={() =>
                          setRejectReason({
                            title: "رفض طلب الترقية",
                            body: "حدد سبب الرفض (اختياري) وهيتم إرساله للمالك في الإشعارات.",
                            confirmLabel: "رفض الطلب",
                            onConfirm: (text) => reject.mutate({ id: r.id, text }),
                          })
                        }
                      >
                        رفض
                      </button>
                    </div>
                  ) : null,
              },
            ]}
          />
        )}
      </QueryState>

      {replyModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
          <div className="bg-white rounded-2xl max-w-lg w-full p-6 shadow-xl flex flex-col gap-4">
            <h3 className="text-lg font-bold text-navy-900">إرسال تعليمات الدفع للمالك</h3>
            <p className="text-sm text-gray-600">
              الرسالة دي هتوصل للمالك كإشعار في حسابه وعلى صفحة الاشتراك، وهتظهرله بيانات السداد (إنستاباي / فودافون كاش).
            </p>
            <textarea
              className="w-full h-32 p-3 border border-gray-300 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-navy-900"
              value={replyText}
              onChange={(e) => setReplyText(e.target.value)}
              placeholder="اكتب تعليمات السداد..."
            />
            <div className="flex justify-end gap-2 mt-2">
              <button
                type="button"
                className={btnOutline}
                onClick={() => setReplyModal(null)}
              >
                إلغاء
              </button>
              <button
                type="button"
                className={btnPrimary}
                disabled={!replyText.trim() || reply.isPending}
                onClick={() => reply.mutate()}
              >
                إرسال التعليمات
              </button>
            </div>
          </div>
        </div>
      )}

      {viewReceipt && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
          <div className="bg-white rounded-2xl max-w-2xl w-full p-6 shadow-2xl flex flex-col gap-4 max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between border-b pb-3">
              <div className="flex items-center gap-2">
                <span className="grid place-items-center size-9 rounded-lg bg-slate-100 text-slate-700"><ReceiptText className="size-5" /></span>
                <div>
                  <h3 className="text-base font-bold text-navy-900">إيصال تحويل الاشتراك</h3>
                  <span className="text-xs text-gray-500">
                    المالك: {viewReceipt.ownerName || viewReceipt.ownerPhone || viewReceipt.ownerId} · باقة {viewReceipt.requestedPlan?.name}
                  </span>
                </div>
              </div>
              <button
                type="button"
                className="text-gray-400 hover:text-gray-600 p-1 rounded-md hover:bg-gray-100 cursor-pointer"
                onClick={() => setViewReceipt(null)}
                aria-label="إغلاق"
              >
                <X className="size-5" />
              </button>
            </div>

            <div className="grid sm:grid-cols-2 gap-3 text-xs bg-gray-50 p-3.5 rounded-xl border border-gray-200">
              <div>
                <span className="text-gray-500 block mb-0.5">تفاصيل التحويل / الحساب:</span>
                <span className="font-bold text-gray-900">{viewReceipt.transferNotes || "—"}</span>
              </div>
              <div>
                <span className="text-gray-500 block mb-0.5">رقم العملية / الحوالة:</span>
                <span className="font-bold font-mono text-gray-900">{viewReceipt.transferReference || "—"}</span>
              </div>
              <div>
                <span className="text-gray-500 block mb-0.5">تاريخ الإرسال:</span>
                <span className="font-semibold text-gray-900">{fmtDateTime(viewReceipt.paymentTransferredAtUtc)}</span>
              </div>
              <div>
                <span className="text-gray-500 block mb-0.5">المبلغ المطلوب:</span>
                <span className="font-bold text-navy-900">{money(viewReceipt.amount)}</span>
              </div>
            </div>

            {viewReceipt.paymentProofUrl ? (
              <div className="flex flex-col items-center gap-2 border border-gray-200 rounded-xl p-3 bg-gray-100/50">
                <img
                  src={mediaSrc(viewReceipt.paymentProofUrl)}
                  alt="إيصال التحويل"
                  className="max-h-[50vh] object-contain rounded-lg shadow-sm"
                />
                <a
                  href={mediaSrc(viewReceipt.paymentProofUrl)}
                  target="_blank"
                  rel="noreferrer"
                  className="text-xs text-blue-600 hover:text-blue-800 underline font-semibold mt-1"
                >
                  فتح الصورة بالحجم الكامل في نافذة جديدة<ExternalLink className="size-3.5 inline-block align-[-3px] ms-1" />
                </a>
              </div>
            ) : (
              <div className="p-8 text-center text-sm text-gray-500">لا توجد صورة مرفقة للإيصال</div>
            )}

            <div className="flex items-center justify-between pt-2 border-t mt-2">
              <button
                type="button"
                className={btnOutline}
                onClick={() => setViewReceipt(null)}
              >
                إغلاق
              </button>
              {viewReceipt.status === "Pending" && (
                <button
                  type="button"
                  className={`${btnPrimary} bg-emerald-600 hover:bg-emerald-700`}
                  disabled={approve.isPending}
                  onClick={async () => {
                    await approve.mutateAsync(viewReceipt.id);
                    setViewReceipt(null);
                  }}
                >
                  {approve.isPending ? "جاري التفعيل..." : "الموافقة وتفعيل الباقة فوراً"}
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      <ReasonModal request={rejectReason} onClose={() => setRejectReason(null)} />
    </>
  );
}

function Invoices() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [viewInvoice, setViewInvoice] = useState<Schemas["InvoiceDto"] | null>(null);
  const invoices = useQuery({ queryKey: ["admin-invoices"], queryFn: () => unwrap(api.GET("/api/v1/admin/invoices", { params: { query: {} } })) });
  const voidInvoice = useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) => unwrap(api.POST("/api/v1/admin/invoices/{invoiceId}/void", { params: { path: { invoiceId: id } }, body: { reason: text } })),
    onSuccess: async () => { notice.ok("اتلغت الفاتورة (بتفضل في السجل)."); await queryClient.invalidateQueries({ queryKey: ["admin-invoices"] }); },
    onError: notice.fail,
  });
  return (
    <>
      {notice.node}
      <QueryState
        query={invoices}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد فواتير صادرة"
        emptyDescription="لم يتم إصدار أي فواتير اشتراكات بعد. ستظهر الفواتير هنا فور سداد الاشتراكات أو إصدارها يدوياً."
        emptyIcon="receipt"
      >
        {(list) => (
          <Table<Schemas["InvoiceDto"]>
            caption="الفواتير"
            rows={list}
            rowKey={(i) => i.id}
            columns={[
              {
                title: "الرقم",
                cell: (i) => (
                  <button
                    type="button"
                    onClick={() => setViewInvoice(i)}
                    className="font-inter font-bold text-navy-900 hover:text-brand-red transition-colors text-right cursor-pointer"
                    dir="ltr"
                  >
                    {i.number}
                  </button>
                ),
              },
              {
                title: "العميل (اسم النادي / الملعب)",
                cell: (i) => (
                  <div className="flex flex-col">
                    <span className="font-bold text-[#071832] flex items-center gap-1.5">
                      <Pitch className="size-4 text-slate-500" />
                      <span>{i.venueName || "نادي وملعب رياضي"}</span>
                    </span>
                    {i.ownerName && (
                      <span className="text-[11px] text-gray-500">
                        المسؤول: {i.ownerName} {i.ownerPhone ? `(${toArabicDigits(i.ownerPhone)})` : ""}
                      </span>
                    )}
                  </div>
                ),
              },
              {
                title: "الباقة",
                cell: (i) => (
                  <div className="flex flex-col">
                    <span className="font-medium text-gray-900">{i.planName}</span>
                    <span className="text-[11px] text-gray-500">
                      {i.cycle === "Annual" || i.cycle === "1" ? "سنوي" : "شهري"}
                    </span>
                  </div>
                ),
              },
              { title: "المبلغ", cell: (i) => <strong className="text-gray-900">{money(i.amount)}</strong> },
              { title: "التاريخ", cell: (i) => fmtDate(i.issuedAtUtc) },
              { title: "الحالة", cell: (i) => <Pill tone={i.status === "Void" ? "bad" : "ok"}>{i.status === "Void" ? "ملغاة" : "مسددة"}</Pill> },
              {
                title: "",
                cell: (i) => (
                  <div className="flex gap-1.5 items-center">
                    <button
                      type="button"
                      className={`${btnSmall} bg-[#071832] hover:bg-slate-800 text-white font-semibold flex items-center gap-1`}
                      onClick={() => setViewInvoice(i)}
                      title="عرض الفاتورة الرسمية بالعربي وطباعتها"
                    >
                      <ReceiptText className="size-3.5" />
                      <span>عرض الفاتورة</span>
                    </button>
                    <button
                      type="button"
                      className={btnSmall}
                      onClick={() => openFile(`/api/v1/admin/invoices/${i.id}/pdf`).catch(notice.fail)}
                      title="تحميل ملف PDF"
                    >
                      PDF
                    </button>
                    {i.status !== "Void" && (
                      <button
                        type="button"
                        className={`${btnSmall} text-red-600 hover:bg-red-50`}
                        onClick={() =>
                          setReason({
                            title: `إلغاء الفاتورة ${i.number}`,
                            body: "الفاتورة مش بتتمسح، بتتعلّم ملغاة وبتطلع من الإيراد.",
                            confirmLabel: "إلغاء",
                            onConfirm: (text) => voidInvoice.mutate({ id: i.id, text }),
                          })
                        }
                      >
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
      <InvoiceModal invoice={viewInvoice} onClose={() => setViewInvoice(null)} />
    </>
  );
}
