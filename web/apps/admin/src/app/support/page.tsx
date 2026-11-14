"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnOutline, btnPrimary, btnSmall, card, inputClass, muted } from "@/components/ui";
import { Drawer, Pager, QueryState, Select, Table, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { ChevronLeft, LayoutGrid, Table2 } from "lucide-react";
import { Pitch, SoccerBall } from "@/components/icons";

type Row = Schemas["TicketSummaryDto"];
const PAGE_SIZE = 25;

const STATUSES: [string, string][] = [["", "كل الحالات"], ["Open", "مفتوحة"], ["InProgress", "جاري العمل"], ["WaitingOnRequester", "مستنية رد العميل"], ["Resolved", "محلولة"], ["Closed", "مقفولة"]];
const CATEGORIES: [string, string][] = [
  ["", "كل الأنواع"],
  ["Booking", "حجز"],
  ["Deposit", "عربون"],
  ["Refund", "استرداد"],
  ["VenueComplaint", "شكوى ملعب"],
  ["Account", "حساب"],
  ["TeamMatchDispute", "نزاع فريق/ماتش"],
  ["Subscription", "اشتراك"],
  ["Other", "أخرى"],
];
const PRIORITIES: [string, string][] = [["Low", "منخفضة"], ["Normal", "عادية"], ["High", "عالية"], ["Urgent", "عاجلة"]];
const PRIORITY_TONE: Record<string, "ok" | "warn" | "bad" | "neutral"> = { Low: "neutral", Normal: "neutral", High: "warn", Urgent: "bad" };
const label = (list: [string, string][], v: string) => list.find(([k]) => k === v)?.[1] ?? v;

export default function SupportPage() {
  const [q, setQ] = useState("");
  const [status, setStatus] = useState("");
  const [category, setCategory] = useState("");
  const [mine, setMine] = useState<"all" | "unassigned">("all");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");

  const tickets = useQuery({
    queryKey: ["admin-tickets", q, status, category, mine, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/support/tickets", {
          params: { query: { q: q || undefined, status: status || undefined, category: category || undefined, unassigned: mine === "unassigned" || undefined, page, pageSize: PAGE_SIZE } },
        }),
      ),
  });

  return (
    <Page>
      <PageHeader title="تذاكر الدعم" sub="الأعجل الأول، وبعدين الأقدم انتظارًا. الملاحظات الداخلية مش بتوصل للعميل." />
      
      <div className="flex flex-wrap gap-2.5 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap gap-2 flex-1 min-w-[280px]">
          <div className="flex-[1_1_220px]"><SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="رقم التذكرة أو الموضوع" /></div>
          <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={STATUSES} />
          <Select label="النوع" value={category} onChange={(v) => { setCategory(v); setPage(1); }} options={CATEGORIES} />
          <Select label="الإسناد" value={mine} onChange={(v) => { setMine(v as "all" | "unassigned"); setPage(1); }} options={[["all", "الكل"], ["unassigned", "غير مسندة"]]} />
        </div>

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
      </div>

      <QueryState
        query={tickets}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="صندوق تذاكر الدعم فارغ"
        emptyDescription={q || status || category || mine !== "all" ? "لا توجد تذاكر تطابق معايير البحث أو الفلتر المحدد. يمكنك مسح البحث أو تغيير الفلاتر." : "رائع! لا توجد أي تذاكر دعم فني مفتوحة أو تتطلب التدخل حالياً."}
        emptyIcon={q || status || category ? "search" : "inbox"}
        emptyBadge="كل شيء هادئ"
        emptyAction={
          q || status || category || mine !== "all"
            ? {
                label: "إعادة ضبط الفلاتر والبحث",
                onClick: () => {
                  setQ("");
                  setStatus("");
                  setCategory("");
                  setMine("all");
                  setPage(1);
                },
                variant: "outline",
              }
            : undefined
        }
      >
        {(data) => (
          <>
            {viewMode === "cards" ? (
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                {data.items.map((t) => {
                  const isSelected = selected === t.id;
                  return (
                    <div
                      key={t.id}
                      onClick={() => setSelected(t.id)}
                      className={`${card} rounded-[20px] p-5 flex flex-col justify-between gap-4 border transition-all cursor-pointer hover:border-slate-400 hover:shadow-md active:scale-[0.99] ${
                        isSelected ? "border-brand-red ring-2 ring-brand-red/20 bg-[#FDFDFE]" : "border-[#E2E6EF]"
                      }`}
                    >
                      {/* Top: Ticket Number, Badges & Priority */}
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex flex-col gap-1 min-w-0">
                          <span dir="ltr" className="font-inter text-xs font-bold text-slate-700 text-right">
                            {t.number}
                          </span>
                          <strong className="text-base font-bold text-slate-900 truncate">
                            {t.subject}
                          </strong>
                        </div>
                        <div className="flex flex-col items-end gap-1 shrink-0">
                          <Pill tone={PRIORITY_TONE[t.priority] ?? "neutral"}>
                            {label(PRIORITIES, t.priority)}
                          </Pill>
                          <span className="text-[11px] px-2 py-0.5 rounded-md font-semibold bg-slate-100 text-slate-700">
                            {label(STATUSES, t.status)}
                          </span>
                        </div>
                      </div>

                      {/* Metadata Details */}
                      <div className="flex flex-col gap-2 p-3 rounded-xl bg-[#F6F8FB] border border-[#E9EDF5] text-xs">
                        <div className="flex items-center justify-between">
                          <span className={muted}>النوع والتصنيف</span>
                          <span className="font-semibold text-slate-800">{label(CATEGORIES, t.category)}</span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span className={muted}>نوع مقدم الطلب</span>
                          <span className="font-medium text-slate-800 inline-flex items-center gap-1">
                            {t.requesterKind === "Venue" ? <Pitch className="size-3.5 text-slate-500" /> : <SoccerBall className="size-3.5 text-slate-500" />}
                            {t.requesterKind === "Venue" ? "صاحب ملعب" : "لاعب"}
                          </span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span className={muted}>آخر نشاط</span>
                          <span className="text-slate-700">{fmtDateTime(t.lastActivityAtUtc)}</span>
                        </div>
                      </div>

                      {/* Footer */}
                      <div className="flex items-center justify-between text-xs pt-2 border-t border-[#F0F2F6]">
                        <span className={muted}>{t.assigneeId ? "مُسندة لمشرف" : "غير مسندة"}</span>
                        <span className="text-brand-red font-bold flex items-center gap-1">
                          متابعة التذكرة <ChevronLeft className="size-3.5" />
                        </span>
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <Table<Row>
                caption="التذاكر"
                rows={data.items}
                rowKey={(t) => t.id}
                selected={selected}
                onSelect={(t) => setSelected(t.id)}
                columns={[
                  { title: "الرقم", cell: (t) => <span dir="ltr" className="font-inter">{t.number}</span> },
                  { title: "الموضوع", cell: (t) => <strong>{t.subject}</strong> },
                  { title: "النوع", cell: (t) => label(CATEGORIES, t.category) },
                  { title: "الأولوية", cell: (t) => <Pill tone={PRIORITY_TONE[t.priority] ?? "neutral"}>{label(PRIORITIES, t.priority)}</Pill> },
                  { title: "الحالة", cell: (t) => label(STATUSES, t.status) },
                  { title: "من", cell: (t) => (t.requesterKind === "Venue" ? "ملعب" : "لاعب") },
                  { title: "آخر نشاط", cell: (t) => fmtDateTime(t.lastActivityAtUtc) },
                ]}
              />
            )}
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <TicketDrawer ticketId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function TicketDrawer({ ticketId, onClose }: { ticketId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [body, setBody] = useState("");
  const [internal, setInternal] = useState(false);
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const me = useQuery({ queryKey: ["me"], queryFn: () => unwrap(api.GET("/api/v1/me")), staleTime: 10 * 60_000 });

  const detail = useQuery({
    queryKey: ["admin-ticket", ticketId],
    enabled: !!ticketId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/support/tickets/{ticketId}", { params: { path: { ticketId: ticketId! } } })),
  });
  const path = { ticketId: ticketId ?? "" };
  const refresh = async (text: string) => {
    notice.ok(text);
    await queryClient.invalidateQueries({ queryKey: ["admin-ticket"] });
    await queryClient.invalidateQueries({ queryKey: ["admin-tickets"] });
  };
  const reply = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/admin/support/tickets/{ticketId}/messages", { params: { path }, body: { message: body, internal } })),
    onSuccess: () => { setBody(""); return refresh(internal ? "اتضافت ملاحظة داخلية." : "الرد اتبعت للعميل."); },
    onError: notice.fail,
  });
  const assign = useMutation({
    mutationFn: (assigneeId: string | null) => unwrap(api.PUT("/api/v1/admin/support/tickets/{ticketId}/assignee", { params: { path }, body: { assigneeId } })),
    onSuccess: (_, id) => refresh(id ? "التذكرة اتسندت ليك." : "اتشال الإسناد."),
    onError: notice.fail,
  });
  const priority = useMutation({
    mutationFn: (value: string) => unwrap(api.PUT("/api/v1/admin/support/tickets/{ticketId}/priority", { params: { path }, body: { priority: value } })),
    onSuccess: () => refresh("اتغيرت الأولوية."),
    onError: notice.fail,
  });
  const finish = useMutation({
    mutationFn: ({ close, text }: { close: boolean; text: string }) =>
      close
        ? unwrap(api.POST("/api/v1/admin/support/tickets/{ticketId}/close", { params: { path }, body: { reason: text } }))
        : unwrap(api.POST("/api/v1/admin/support/tickets/{ticketId}/resolve", { params: { path }, body: { reason: text } })),
    onSuccess: (_, { close }) => refresh(close ? "اتقفلت التذكرة." : "اتحلت التذكرة والعميل وصله السبب."),
    onError: notice.fail,
  });

  return (
    <Drawer title="تذكرة دعم" open={!!ticketId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(d) => {
          const t = d.ticket;
          const closed = t.status === "Closed";
          return (
            <>
              <div className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
                <strong className="text-[16px]">{t.subject}</strong>
                <KV k="الرقم"><span dir="ltr" className="font-inter">{t.number}</span></KV>
                <KV k="النوع">{label(CATEGORIES, t.category)}</KV>
                <KV k="الحالة">{label(STATUSES, t.status)}</KV>
                <KV k="الإسناد">{t.assigneeId ? (t.assigneeId === me.data?.userId ? "أنت" : t.assigneeId.slice(0, 8)) : "غير مسندة"}</KV>
                {t.relatedBookingId && <KV k="حجز مرتبط"><span dir="ltr" className="font-inter text-xs">{t.relatedBookingId}</span></KV>}
                {t.relatedVenueId && <KV k="ملعب مرتبط"><span dir="ltr" className="font-inter text-xs">{t.relatedVenueId}</span></KV>}
                {d.resolutionReason && <KV k="سبب الحل">{d.resolutionReason}</KV>}
              </div>
              {!closed && (
                <div className="flex flex-wrap gap-2 items-center">
                  {t.assigneeId !== me.data?.userId && me.data && <button type="button" className={btnSmall} onClick={() => assign.mutate(me.data!.userId)}>إسناد ليا</button>}
                  {t.assigneeId && <button type="button" className={btnSmall} onClick={() => assign.mutate(null)}>إلغاء الإسناد</button>}
                  <Select label="الأولوية" value={t.priority} onChange={(v) => priority.mutate(v)} options={PRIORITIES} />
                </div>
              )}
              <section className="flex flex-col gap-2" aria-label="المحادثة">
                {d.messages.map((m) => (
                  <div
                    key={m.id}
                    className={`rounded-xl p-3 text-sm leading-[1.7] ${m.isInternal ? "bg-[#FFF7E6] border border-[#F2D48A]" : m.authorKind === "Agent" ? "bg-[#EEF0F5]" : "bg-white border border-[#E2E5EC]"}`}
                  >
                    <span className={`text-[11px] ${muted} block`}>
                      {m.isInternal ? "ملاحظة داخلية" : m.authorKind === "Agent" ? "الدعم" : "العميل"} · {fmtDateTime(m.createdAtUtc)}
                    </span>
                    <p className="whitespace-pre-wrap">{m.body}</p>
                  </div>
                ))}
              </section>
              {!closed && (
                <>
                  <textarea aria-label="الرد" value={body} onChange={(e) => setBody(e.target.value)} placeholder={internal ? "ملاحظة للفريق بس" : "رد للعميل"} maxLength={4000} className={`${inputClass} min-h-[90px]`} />
                  <label className="flex gap-2 text-sm items-center"><input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)} /> ملاحظة داخلية (العميل مش هيشوفها)</label>
                  <div className="flex flex-wrap gap-2">
                    <button type="button" className={btnPrimary} disabled={!body.trim() || reply.isPending} onClick={() => reply.mutate()}>{internal ? "إضافة ملاحظة" : "إرسال الرد"}</button>
                    {t.status !== "Resolved" && (
                      <button type="button" className={btnOutline} onClick={() => setReason({ title: "حل التذكرة", body: "السبب هيوصل للعميل.", confirmLabel: "حل", onConfirm: (text) => finish.mutate({ close: false, text }) })}>حل</button>
                    )}
                    <button type="button" className={btnSmall} onClick={() => setReason({ title: "قفل التذكرة", body: "بعد القفل مفيش ردود تاني.", confirmLabel: "قفل", onConfirm: (text) => finish.mutate({ close: true, text }) })}>قفل</button>
                  </div>
                </>
              )}
            </>
          );
        }}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}
