"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnDanger, btnOutline, btnPrimary, btnSmall, card, inputClass, money, muted, toArabicDigits } from "@/components/ui";
import { Drawer, Field, Pager, QueryState, Section, Select, Table, TextInput, fmtDate, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Row = Schemas["AdminVenueListItem"];

const STATUSES: [string, string][] = [
  ["", "كل حالات الاعتماد"],
  ["Approved", "معتمد"],
  ["Submitted", "بانتظار الموافقة"],
  ["NeedsInfo", "بحاجة لمعلومات"],
  ["Rejected", "مرفوض"],
  ["Suspended", "موقوف"],
  ["Draft", "مسودة"],
];

const STATUS_MAP = Object.fromEntries(STATUSES);
const TONE: Record<string, "ok" | "warn" | "bad" | "neutral"> = {
  Approved: "ok",
  Submitted: "warn",
  NeedsInfo: "warn",
  Rejected: "bad",
  Suspended: "bad",
  Draft: "neutral",
};

const SUB_STATUS_TONE: Record<string, "ok" | "warn" | "bad" | "neutral" | "navy"> = {
  Active: "ok",
  Trial: "warn",
  Grace: "warn",
  Expired: "bad",
  Suspended: "bad",
  Cancelled: "neutral",
  Default: "navy",
};

const SUB_STATUS_LABEL: Record<string, string> = {
  Active: "نشط",
  Trial: "تجريبي",
  Grace: "فترة سماح",
  Expired: "منتهي",
  Suspended: "موقوف",
  Cancelled: "ملغي",
  Default: "افتراضي",
};

const PAGE_SIZE = 24;

export default function VenuesPage() {
  const [q, setQ] = useState("");
  const [status, setStatus] = useState("");
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);

  const venues = useQuery({
    queryKey: ["admin-venues", q, status, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/venues", {
          params: { query: { q: q || undefined, approvalStatus: status || undefined, page, pageSize: PAGE_SIZE } },
        }),
      ),
  });

  return (
    <Page>
      <PageHeader
        title="دليل الملاعب وإدارتها"
        sub="استعراض كافة الملاعب المسجلة على المنصة، متابعة باقات اشتراك الملاك، وحالات النشر والاعتماد."
      />

      {/* Control & Search Bar */}
      <div className="flex flex-wrap gap-3 items-center justify-between bg-white p-4 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap gap-2.5 items-center flex-1 min-w-[280px]">
          <div className="relative min-w-[240px] flex-1 max-w-md">
            <SearchBox
              value={q}
              onChange={(v) => {
                setQ(v);
                setPage(1);
              }}
              placeholder="ابحث باسم الملعب، المدينة، المنطقة أو المالك..."
            />
          </div>
          <Select
            label="حالة الاعتماد"
            value={status}
            onChange={(v) => {
              setStatus(v);
              setPage(1);
            }}
            options={STATUSES}
          />
        </div>

        {/* View Switcher: Cards vs Table */}
        <div className="inline-flex rounded-xl bg-slate-100 p-1 border border-slate-200">
          <button
            type="button"
            className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
              viewMode === "cards" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("cards")}
          >
            🗂️ بطاقات الملاعب
          </button>
          <button
            type="button"
            className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
              viewMode === "table" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("table")}
          >
            📊 جدول
          </button>
        </div>
      </div>

      {/* Results View */}
      <QueryState
        query={venues}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="لم يتم العثور على أي ملاعب"
        emptyDescription={q || status ? "لا توجد ملاعب تطابق معايير البحث أو الفلتر المحدد. يمكنك مسح البحث أو تغيير الفلتر لعرض باقي الملاعب." : "لا توجد ملاعب مسجلة في المنصة حتى الآن."}
        emptyIcon={q || status ? "search" : "stadium"}
        emptyAction={
          q || status
            ? {
                label: "إعادة ضبط الفلاتر والبحث",
                onClick: () => {
                  setQ("");
                  setStatus("");
                  setPage(1);
                },
                variant: "outline",
              }
            : undefined
        }
      >
        {(data) => {
          if (viewMode === "cards") {
            return (
              <>
                <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                  {data.items.map((r) => {
                    const subStatus = r.subscriptionStatus ?? "Default";
                    const planName = r.planName || "بدون باقة";
                    const subLabel = SUB_STATUS_LABEL[subStatus] ?? subStatus;
                    const subTone = SUB_STATUS_TONE[subStatus] ?? "neutral";

                    return (
                      <div
                        key={r.id}
                        className={`${card} rounded-[20px] p-5 flex flex-col justify-between gap-4 border border-[#E2E6EF] hover:border-slate-400 hover:shadow-md transition-all`}
                      >
                        {/* Top: Venue Header & Statuses */}
                        <div className="flex items-start justify-between gap-3">
                          <div className="flex items-center gap-3 min-w-0">
                            <div className="w-12 h-12 rounded-2xl bg-gradient-to-br from-emerald-600 to-teal-800 text-white flex items-center justify-center font-bold text-xl shrink-0 shadow-sm">
                              🏟️
                            </div>
                            <div className="min-w-0 flex flex-col">
                              <h3 className="font-extrabold text-slate-900 text-base truncate" title={r.name}>
                                {r.name}
                              </h3>
                              <div className="flex items-center gap-1.5 text-xs text-[#5B6475] mt-0.5">
                                <span>📍</span>
                                <span className="font-semibold text-slate-700">{r.city}</span>
                                {r.area && <span>· {r.area}</span>}
                              </div>
                            </div>
                          </div>

                          <div className="shrink-0 flex flex-col items-end gap-1.5">
                            <Pill tone={TONE[r.approvalStatus] ?? "neutral"} className="text-xs font-bold px-2.5 py-0.5">
                              {STATUS_MAP[r.approvalStatus] ?? r.approvalStatus}
                            </Pill>
                            <span
                              className={`text-[11px] font-semibold px-2 py-0.5 rounded-md ${
                                r.isPublished ? "bg-emerald-50 text-emerald-800 border border-emerald-200" : "bg-slate-100 text-slate-600"
                              }`}
                            >
                              {r.isPublished ? "🟢 منشور للجمهور" : "⚪ مسودة"}
                            </span>
                          </div>
                        </div>

                        {/* Middle Box: Owner & Subscription Info */}
                        <div className="bg-[#F8FAFC] rounded-xl p-3.5 border border-[#EEF1F6] flex flex-col gap-2.5">
                          {/* Subscription Details */}
                          <div className="flex items-center justify-between border-b border-[#E8ECF2] pb-2">
                            <div className="flex items-center gap-2">
                              <span className="text-xs text-[#5B6475]">باقة الاشتراك:</span>
                              <span className="font-extrabold text-slate-900 text-xs bg-white px-2.5 py-1 rounded-lg border border-[#E2E6EF] shadow-2xs">
                                📦 {planName}
                              </span>
                            </div>
                            <Pill tone={subTone} className="text-[11px]">
                              {subLabel}
                            </Pill>
                          </div>

                          {/* Owner Details */}
                          <div className="flex items-center justify-between text-xs pt-1">
                            <div>
                              <span className="text-[#5B6475] block text-[11px]">صاحب الملعب (المالك):</span>
                              <span className="font-bold text-slate-900 mt-0.5 block">{r.ownerName || "مالك مسجل"}</span>
                            </div>
                            {r.ownerPhone && (
                              <div className="text-left">
                                <span className="text-[#5B6475] block text-[11px]">رقم التواصل:</span>
                                <span dir="ltr" className="font-mono text-slate-800 bg-white px-2 py-0.5 rounded border border-slate-200 mt-0.5 inline-block font-medium">
                                  📱 {r.ownerPhone}
                                </span>
                              </div>
                            )}
                          </div>
                        </div>

                        {/* Details Row: Pitches & Creation Date */}
                        <div className="flex items-center justify-between text-xs px-1 text-slate-600">
                          <span className="flex items-center gap-1.5 font-bold text-slate-800 bg-emerald-50 px-2.5 py-1 rounded-lg border border-emerald-200">
                            <span>⚽ الملاعب الفرعية:</span>
                            <span className="font-extrabold text-emerald-900">{num(r.pitches)}</span>
                          </span>
                          <span className="text-[11px] text-[#5B6475]">
                            أُضيف في: <b>{fmtDate(r.createdAtUtc)}</b>
                          </span>
                        </div>

                        {/* Bottom Action Bar */}
                        <div className="pt-3 border-t border-[#EEF0F5] flex items-center justify-between gap-2 mt-auto">
                          <span dir="ltr" className="text-[10px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">
                            {r.id.slice(0, 8)}
                          </span>

                          <button
                            type="button"
                            className={`${btnPrimary} min-h-[36px] text-xs px-4`}
                            onClick={() => setSelected(r.id)}
                          >
                            معاينة وإدارة الملعب ↗
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
                <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
              </>
            );
          }

          return (
            <>
              <Table<Row>
                caption="الملاعب"
                rows={data.items}
                rowKey={(r) => r.id}
                selected={selected}
                onSelect={(r) => setSelected(r.id)}
                columns={[
                  {
                    title: "الملعب والمكان",
                    cell: (r) => (
                      <div className="flex flex-col">
                        <strong className="text-slate-900 text-sm">{r.name}</strong>
                        <span className="text-xs text-[#5B6475]">
                          📍 {r.city}، {r.area}
                        </span>
                      </div>
                    ),
                  },
                  {
                    title: "المالك والتواصل",
                    cell: (r) => (
                      <div className="flex flex-col">
                        <span className="font-bold text-slate-800 text-xs">{r.ownerName || "مالك مسجل"}</span>
                        {r.ownerPhone && (
                          <span dir="ltr" className="text-xs text-[#5B6475] font-mono">
                            📱 {r.ownerPhone}
                          </span>
                        )}
                      </div>
                    ),
                  },
                  {
                    title: "باقة الاشتراك",
                    cell: (r) => {
                      const subStatus = r.subscriptionStatus ?? "Default";
                      const planName = r.planName || "بدون باقة";
                      const subLabel = SUB_STATUS_LABEL[subStatus] ?? subStatus;
                      const subTone = SUB_STATUS_TONE[subStatus] ?? "neutral";
                      return (
                        <div className="flex items-center gap-1.5">
                          <span className="font-bold text-slate-900 text-xs bg-slate-100 px-2 py-0.5 rounded">
                            {planName}
                          </span>
                          <Pill tone={subTone} className="text-[10px]">
                            {subLabel}
                          </Pill>
                        </div>
                      );
                    },
                  },
                  {
                    title: "الملاعب الفرعية",
                    cell: (r) => (
                      <span className="font-bold text-emerald-800 bg-emerald-50 px-2.5 py-1 rounded-lg border border-emerald-200 text-xs">
                        ⚽ {num(r.pitches)}
                      </span>
                    ),
                  },
                  {
                    title: "الاعتماد",
                    cell: (r) => <Pill tone={TONE[r.approvalStatus] ?? "neutral"}>{STATUS_MAP[r.approvalStatus] ?? r.approvalStatus}</Pill>,
                  },
                  {
                    title: "النشر",
                    cell: (r) => (
                      <span className={`text-xs font-semibold ${r.isPublished ? "text-emerald-700" : "text-slate-500"}`}>
                        {r.isPublished ? "🟢 منشور" : "⚪ مسودة"}
                      </span>
                    ),
                  },
                  { title: "الإضافة", cell: (r) => fmtDate(r.createdAtUtc) },
                  {
                    title: "الإجراءات",
                    cell: (r) => (
                      <button
                        type="button"
                        className={`${btnSmall} text-slate-700 hover:bg-slate-100`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setSelected(r.id);
                        }}
                      >
                        معاينة ↗
                      </button>
                    ),
                  },
                ]}
              />
              <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
            </>
          );
        }}
      </QueryState>

      <VenueDrawer venueId={selected} onClose={() => setSelected(null)} />
    </Page>
  );
}

function VenueDrawer({ venueId, onClose }: { venueId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const [newOwner, setNewOwner] = useState("");

  const usersQuery = useQuery({
    queryKey: ["admin-users-candidates-select"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/users", { params: { query: { pageSize: 100 } } })),
  });

  const userOptions: [string, string][] = [
    ["", "— اختر الحساب الجديد —"],
    ...((usersQuery.data?.items ?? []).map((u) => [u.id, `${u.fullName} (${u.maskedPhone || u.email || ""})`] as [string, string])),
  ];

  const detail = useQuery({
    queryKey: ["admin-venue", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/{venueId}", { params: { path: { venueId: venueId! } } })),
  });

  const complaints = useQuery({
    queryKey: ["admin-venue-complaints", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/{venueId}/complaints", { params: { path: { venueId: venueId! } } })),
  });

  type Kind = "suspend" | "reactivate" | "ask" | "note" | "owner";
  const action = useMutation({
    mutationFn: ({ kind, text }: { kind: Kind; text: string }) => {
      const path = { venueId: venueId! };
      switch (kind) {
        case "suspend":
          return stepUp((headers) => unwrap(api.POST("/api/v1/admin/venues/{venueId}/suspend", { params: { path }, headers, body: { reason: text } })));
        case "reactivate":
          return unwrap(api.POST("/api/v1/admin/venues/{venueId}/reactivate", { params: { path }, body: { reason: text } }));
        case "ask":
          return unwrap(api.POST("/api/v1/admin/venues/{venueId}/ask-owner", { params: { path }, body: { reason: text } }));
        case "note":
          return unwrap(api.PUT("/api/v1/admin/venues/{venueId}/admin-note", { params: { path }, body: { note: text } }));
        default:
          return stepUp((headers) =>
            unwrap(api.POST("/api/v1/admin/venues/{venueId}/ownership", { params: { path }, headers, body: { newOwnerId: newOwner.trim(), reason: text } })),
          );
      }
    },
    onSuccess: async (_, { kind }) => {
      notice.ok({ suspend: "اتوقف الملعب.", reactivate: "الملعب رجع نشط.", ask: "اتبعت الطلب للمالك.", note: "اتحفظت الملاحظة.", owner: "اتنقلت الملكية." }[kind]);
      setNote(null);
      setNewOwner("");
      await queryClient.invalidateQueries({ queryKey: ["admin-venue"] });
      await queryClient.invalidateQueries({ queryKey: ["admin-venues"] });
    },
    onError: notice.fail,
  });

  const ask = (kind: Kind, title: string, body: string, confirmLabel: string) =>
    setReason({ title, body, confirmLabel, onConfirm: (text) => action.mutate({ kind, text }) });

  return (
    <Drawer title="تفاصيل وإدارة الملعب" open={!!venueId} onClose={() => { notice.clear(); onClose(); }}>
      {notice.node}
      <QueryState query={detail}>
        {(v) => {
          const subStatus = v.subscriptionStatus ?? "Default";
          const planName = v.planName || "بدون باقة";
          const subLabel = SUB_STATUS_LABEL[subStatus] ?? subStatus;
          const subTone = SUB_STATUS_TONE[subStatus] ?? "neutral";

          return (
            <>
              {/* Main Card */}
              <div className={`${card} rounded-[18px] p-4 flex flex-col gap-2.5 border border-slate-200`}>
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <h3 className="text-lg font-extrabold text-slate-900">{v.name}</h3>
                    <span className="text-xs text-[#5B6475]">📍 {v.city}، {v.area} · {v.address}</span>
                  </div>
                  <Pill tone={TONE[v.approvalStatus] ?? "neutral"}>
                    {STATUS_MAP[v.approvalStatus] ?? v.approvalStatus}
                  </Pill>
                </div>

                <div className="grid grid-cols-2 gap-2 text-xs pt-2 border-t border-slate-100">
                  <KV k="باقة الاشتراك">
                    <span className="font-bold text-slate-900 bg-slate-100 px-2 py-0.5 rounded me-1">{planName}</span>
                    <Pill tone={subTone} className="text-[10px]">{subLabel}</Pill>
                  </KV>
                  <KV k="صاحب الملعب">{v.ownerName ?? "مالك مسجل"}</KV>
                  <KV k="هاتف الملعب/التواصل"><span dir="ltr">{v.phoneNumber || v.ownerPhone || "—"}</span></KV>
                  <KV k="حالة النشر">{v.isPublished ? "🟢 منشور للجمهور" : "⚪ مسودة"}</KV>
                  <KV k="عدد الموظفين">{num(v.staffCount)}</KV>
                  <KV k="تاريخ التسجيل">{fmtDate(v.createdAtUtc)}</KV>
                </div>
              </div>

              {/* Action Buttons */}
              <div className="flex flex-wrap gap-2">
                {v.approvalStatus === "Approved" && (
                  <button type="button" className={btnDanger} onClick={() => ask("suspend", "إيقاف الملعب", "الملعب هيختفي من التطبيق ومحدش هيقدر يحجز، بس الحجوزات القديمة هتفضل.", "إيقاف")}>
                    إيقاف الملعب
                  </button>
                )}
                {v.approvalStatus === "Suspended" && (
                  <button type="button" className={btnOutline} onClick={() => ask("reactivate", "إعادة تشغيل الملعب", "الملعب هيرجع يظهر تاني في التطبيق.", "إعادة تشغيل")}>
                    إعادة تشغيل
                  </button>
                )}
                <button type="button" className={btnSmall} onClick={() => ask("ask", "طلب معلومات من المالك", "اكتب رسالة توضّح للمالك إيه المطلوب يحدّثه في بيانات الملعب.", "إرسال")}>
                  طلب معلومات من المالك
                </button>
              </div>

              {/* Pitches List */}
              <Section title={`الملاعب الفرعية (${num(v.pitches.length)})`}>
                <div className="grid grid-cols-1 gap-2">
                  {v.pitches.map((p) => (
                    <div key={p.id} className="flex items-center justify-between text-xs p-3 bg-slate-50 rounded-xl border border-slate-200">
                      <div className="flex items-center gap-2">
                        <span className="text-base">⚽</span>
                        <div className="flex flex-col">
                          <strong className="text-slate-900 text-sm">{p.name}</strong>
                          <span className={muted}>نوع النجيل: {p.turfType} · السعة: {num(p.capacity)} لاعب</span>
                        </div>
                      </div>
                      <Pill tone={p.isActive ? "ok" : "neutral"}>{p.isActive ? "شغال" : "معطّل"}</Pill>
                    </div>
                  ))}
                </div>
              </Section>

              {/* 30-Days Stats */}
              <Section title="إحصائيات آخر ٣٠ يوم">
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <div className={`${card} p-3 rounded-xl`}>
                    <span className={`block ${muted}`}>إجمالي الحجوزات</span>
                    <strong className="text-base text-slate-900">{num(v.last30Days.total)}</strong>
                  </div>
                  <div className={`${card} p-3 rounded-xl`}>
                    <span className={`block ${muted}`}>قيمة الحجوزات المؤكدة</span>
                    <strong className="text-base text-slate-900">{money(v.last30Days.confirmedValue)}</strong>
                  </div>
                </div>
              </Section>

              {/* Transfer Ownership */}
              <Section title="نقل الملكية لحساب آخر">
                <div className="flex flex-col gap-2">
                  <Field label="الحساب الجديد">
                    <Select label="الحساب الجديد" value={newOwner} onChange={setNewOwner} options={userOptions} />
                  </Field>
                  <button
                    type="button"
                    className={btnOutline}
                    disabled={!/^[0-9a-f-]{36}$/i.test(newOwner.trim())}
                    onClick={() => ask("owner", "نقل ملكية الملعب", "المالك الجديد هيبقى ليه كامل الصلاحيات والمالك القديم هيفقد الإدارة.", "تأكيد النقل")}
                  >
                    نقل الملكية
                  </button>
                </div>
              </Section>

              {/* Internal Admin Note */}
              <Section title="ملاحظة إدارية داخلية (خاصة بالأدمن فقط)">
                {note !== null ? (
                  <div className="flex flex-col gap-2">
                    <TextInput value={note} onChange={(e) => setNote(e.target.value)} placeholder="اكتب ملاحظة للفريق..." />
                    <div className="flex gap-2 justify-end">
                      <button type="button" className={btnSmall} onClick={() => setNote(null)}>إلغاء</button>
                      <button type="button" className={btnPrimary} onClick={() => action.mutate({ kind: "note", text: note.trim() })}>حفظ الملاحظة</button>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center justify-between text-xs p-3 bg-slate-50 rounded-xl border border-slate-200">
                    <span className={v.adminNote ? "text-slate-800" : muted}>{v.adminNote || "مفيش ملاحظات مسجلة."}</span>
                    <button type="button" className={btnSmall} onClick={() => setNote(v.adminNote ?? "")}>تعديل</button>
                  </div>
                )}
              </Section>
            </>
          );
        }}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}
