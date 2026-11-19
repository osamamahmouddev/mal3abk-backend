"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, Tabs, btnDanger, btnOutline, btnPrimary, btnSmall, card, inputClass, muted } from "@/components/ui";
import { Field, Pager, QueryState, Select, Table, TextInput, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Tab = "teams" | "reports" | "results";
const PAGE_SIZE = 20;

export default function TeamsModerationPage() {
  const [tab, setTab] = useState<Tab>("reports");
  const notice = useNotice();
  return (
    <Page>
      <PageHeader title="الفرق والمجتمع" sub="بلاغات اللعب النظيف والطعون، النتائج المتنازع عليها، وإيقاف الفرق. كل قرار بيتسجل في التدقيق." />
      <Tabs<Tab>
        tabs={[
          ["reports", "البلاغات والطعون"],
          ["results", "نتائج متنازع عليها"],
          ["teams", "الفرق"],
        ]}
        value={tab}
        onChange={(t) => { setTab(t); notice.clear(); }}
      />
      {notice.node}
      {tab === "reports" && <Reports notice={notice} />}
      {tab === "results" && <Results notice={notice} />}
      {tab === "teams" && <Teams notice={notice} />}
    </Page>
  );
}

type Notice = ReturnType<typeof useNotice>;

const REPORT_STATUS: [string, string][] = [
  ["1", "مستني مراجعة"],
  ["4", "متطعن فيه"],
  ["2", "اتقبل"],
  ["3", "اترفض"],
  ["5", "الطعن اترفض"],
  ["6", "الطعن اتقبل"],
];
const CATEGORY: Record<number, string> = { 1: "عنف", 2: "غياب", 3: "تزوير قائمة", 4: "إساءة للحكم", 5: "سلوك غير رياضي" };
const PENALTY: [string, string][] = [
  ["1", "إنذار"],
  ["2", "خصم نقاط"],
  ["3", "إيقاف"],
];

function Reports({ notice }: { notice: Notice }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("1");
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [penalty, setPenalty] = useState("1");
  const [points, setPoints] = useState("");
  const [notes, setNotes] = useState("");

  const reports = useQuery({
    queryKey: ["fair-play", status, page],
    queryFn: () => unwrap(api.GET("/api/v1/admin/moderation/reports", { params: { query: { status: Number(status) as Schemas["FairPlayReportStatus"], page, pageSize: PAGE_SIZE } } })),
  });
  const done = async (text: string) => {
    notice.ok(text);
    setOpen(null);
    setNotes("");
    setPoints("");
    await queryClient.invalidateQueries({ queryKey: ["fair-play"] });
  };
  const review = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) =>
      unwrap(
        api.POST("/api/v1/admin/moderation/reports/{id}/review", {
          params: { path: { id } },
          body: {
            isApproved: approve,
            penalty: (approve ? Number(penalty) : 0) as Schemas["FairPlayPenaltyType"],
            pointsDeducted: approve && penalty === "2" ? Number(points) || null : null,
            adminNotes: notes,
          },
        }),
      ),
    onSuccess: (_, { approve }) => done(approve ? "اتقبل البلاغ واتطبقت العقوبة." : "اترفض البلاغ."),
    onError: notice.fail,
  });
  const appeal = useMutation({
    mutationFn: ({ id, uphold }: { id: string; uphold: boolean }) =>
      unwrap(api.POST("/api/v1/admin/moderation/reports/{id}/appeal/resolve", { params: { path: { id } }, body: { upholdViolation: uphold, adminNotes: notes } })),
    onSuccess: (_, { uphold }) => done(uphold ? "العقوبة فضلت (الطعن اترفض)." : "الطعن اتقبل والعقوبة اتلغت."),
    onError: notice.fail,
  });

  return (
    <div className="flex flex-col gap-4">
      <div className="bg-white p-3 sm:p-3.5 rounded-2xl border border-slate-200/90 shadow-xs flex flex-wrap items-center gap-3">
        <div className="w-full sm:w-60">
          <Select label="الحالة" value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={REPORT_STATUS} />
        </div>
      </div>
      <QueryState
        query={reports}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="لا توجد بلاغات مسجلة"
        emptyDescription="ممتاز! لا توجد بلاغات ضد الفرق بالحالة المحددة حالياً."
        emptyIcon="check"
        emptyBadge="السلوك الرياضي منضبط"
      >
        {(data) => (
          <div className="flex flex-col gap-3">
            {data.items.map((r) => (
              <article key={r.id} className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
                <div className="flex flex-wrap gap-2 items-center">
                  <strong>{r.reportedTeamName}</strong>
                  <Pill tone="warn">{CATEGORY[r.category] ?? r.category}</Pill>
                  <span className={`text-xs ${muted} ms-auto`}>{fmtDateTime(r.createdAtUtc)}</span>
                </div>
                <p className="text-sm leading-[1.7]">{r.description}</p>
                {r.appealReason && <p className="text-sm bg-[#F6F7FA] rounded-xl p-2">الطعن: {r.appealReason}</p>}
                {r.adminDecisionNotes && <p className={`text-xs ${muted}`}>قرار سابق: {r.adminDecisionNotes}</p>}
                {(r.status === 1 || r.status === 4) && open !== r.id && (
                  <button type="button" className={btnSmall} onClick={() => { setOpen(r.id); setNotes(""); }}>
                    {r.status === 1 ? "مراجعة" : "الفصل في الطعن"}
                  </button>
                )}
                {open === r.id && (
                  <div className="flex flex-col gap-2 border-t border-[#EEF0F5] pt-2">
                    {r.status === 1 && (
                      <div className="flex flex-wrap gap-2">
                        <Select label="العقوبة" value={penalty} onChange={setPenalty} options={PENALTY} />
                        {penalty === "2" && <TextInput aria-label="النقاط المخصومة" inputMode="numeric" placeholder="عدد النقاط" value={points} onChange={(e) => setPoints(e.target.value.replace(/\D/g, ""))} />}
                      </div>
                    )}
                    <textarea aria-label="ملاحظات القرار" placeholder="سبب القرار (مطلوب)" value={notes} onChange={(e) => setNotes(e.target.value)} className={`${inputClass} min-h-[70px]`} />
                    <div className="flex flex-wrap gap-2">
                      {r.status === 1 ? (
                        <>
                          <button type="button" className={btnDanger} disabled={notes.trim().length < 5} onClick={() => review.mutate({ id: r.id, approve: true })}>قبول وتطبيق العقوبة</button>
                          <button type="button" className={btnOutline} disabled={notes.trim().length < 5} onClick={() => review.mutate({ id: r.id, approve: false })}>رفض البلاغ</button>
                        </>
                      ) : (
                        <>
                          <button type="button" className={btnDanger} disabled={notes.trim().length < 5} onClick={() => appeal.mutate({ id: r.id, uphold: true })}>تثبيت العقوبة</button>
                          <button type="button" className={btnOutline} disabled={notes.trim().length < 5} onClick={() => appeal.mutate({ id: r.id, uphold: false })}>قبول الطعن</button>
                        </>
                      )}
                      <button type="button" className={btnSmall} onClick={() => setOpen(null)}>رجوع</button>
                    </div>
                  </div>
                )}
              </article>
            ))}
            <Pager page={page} pageSize={PAGE_SIZE} total={data.totalCount} onChange={setPage} />
          </div>
        )}
      </QueryState>
    </div>
  );
}

function Results({ notice }: { notice: Notice }) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [home, setHome] = useState("");
  const [away, setAway] = useState("");
  const [notes, setNotes] = useState("");
  const [correctId, setCorrectId] = useState("");

  const disputes = useQuery({
    queryKey: ["match-disputes", page],
    queryFn: () => unwrap(api.GET("/api/v1/admin/moderation/matches/disputes", { params: { query: { page, pageSize: PAGE_SIZE } } })),
  });
  const reset = async (text: string) => {
    notice.ok(text);
    setOpen(null);
    setHome("");
    setAway("");
    setNotes("");
    setCorrectId("");
    await queryClient.invalidateQueries({ queryKey: ["match-disputes"] });
  };
  const resolve = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 1 | 2 | 3 }) =>
      unwrap(
        api.POST("/api/v1/admin/moderation/matches/{id}/disputes/resolve", {
          params: { path: { id } },
          body: { action, resolutionNotes: notes, officialHomeScore: action === 2 ? Number(home) : null, officialAwayScore: action === 2 ? Number(away) : null },
        }),
      ),
    onSuccess: () => reset("اتسجل القرار واتعاد حساب الترتيب."),
    onError: notice.fail,
  });
  const correct = useMutation({
    mutationFn: () =>
      stepUp((headers) =>
        unwrap(
          api.POST("/api/v1/admin/moderation/matches/{matchId}/correct-result", {
            params: { path: { matchId: correctId.trim() } },
            headers,
            body: { homeScore: Number(home), awayScore: Number(away), reason: notes },
          }),
        ),
      ),
    onSuccess: () => reset("اتصححت النتيجة واتعاد حساب الترتيب."),
    onError: notice.fail,
  });
  const scoresOk = /^\d{1,2}$/.test(home) && /^\d{1,2}$/.test(away);

  return (
    <>
      <QueryState
        query={disputes}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="لا توجد نتائج متنازع عليها"
        emptyDescription="جميع نتائج المباريات معتمدة ومقبولة من الفرق دون اعتراضات معلقة."
        emptyIcon="shield"
        emptyBadge="كل النتائج مستقرة"
      >
        {(data) => (
          <div className="flex flex-col gap-3">
            {data.items.map((m) => (
              <article key={m.matchId} className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
                <strong>{m.homeTeamName} {num(m.homeScore ?? 0)} — {num(m.awayScore ?? 0)} {m.awayTeamName}</strong>
                <span className={`text-sm ${muted}`}>اعترض: {m.disputingTeamName ?? "—"} · {m.disputeReason ?? ""}</span>
                {open !== m.matchId ? (
                  <button type="button" className={btnSmall} onClick={() => setOpen(m.matchId)}>الفصل</button>
                ) : (
                  <div className="flex flex-col gap-2 border-t border-[#EEF0F5] pt-2">
                    <div className="flex gap-2">
                      <TextInput aria-label={`أهداف ${m.homeTeamName}`} inputMode="numeric" placeholder={m.homeTeamName} value={home} onChange={(e) => setHome(e.target.value.replace(/\D/g, ""))} />
                      <TextInput aria-label={`أهداف ${m.awayTeamName}`} inputMode="numeric" placeholder={m.awayTeamName} value={away} onChange={(e) => setAway(e.target.value.replace(/\D/g, ""))} />
                    </div>
                    <textarea aria-label="سبب القرار" placeholder="سبب القرار (مطلوب)" value={notes} onChange={(e) => setNotes(e.target.value)} className={`${inputClass} min-h-[70px]`} />
                    <div className="flex flex-wrap gap-2">
                      <button type="button" className={btnPrimary} disabled={notes.trim().length < 5} onClick={() => resolve.mutate({ id: m.matchId, action: 1 })}>تثبيت النتيجة المرفوعة</button>
                      <button type="button" className={btnOutline} disabled={!scoresOk || notes.trim().length < 5} onClick={() => resolve.mutate({ id: m.matchId, action: 2 })}>تحديد نتيجة رسمية</button>
                      <button type="button" className={btnDanger} disabled={notes.trim().length < 5} onClick={() => resolve.mutate({ id: m.matchId, action: 3 })}>إلغاء الماتش</button>
                    </div>
                  </div>
                )}
              </article>
            ))}
            <Pager page={page} pageSize={PAGE_SIZE} total={data.totalCount} onChange={setPage} />
          </div>
        )}
      </QueryState>
      <section className={`${card} rounded-[16px] p-4 flex flex-col gap-2`}>
        <h2 className="text-[15px] font-bold">تصحيح نتيجة مؤكدة</h2>
        <p className={`text-xs ${muted}`}>لو ظهر إن نتيجة مؤكدة غلط. الترتيب بيتعاد حسابه والكابتنين بيوصلهم إشعار. محتاج تأكيد هوية.</p>
        <Field label="ID الماتش">
          <TextInput dir="ltr" value={correctId} onChange={(e) => setCorrectId(e.target.value)} />
        </Field>
        <div className="flex gap-2">
          <TextInput aria-label="أهداف صاحب الأرض" inputMode="numeric" placeholder="صاحب الأرض" value={open ? "" : home} onChange={(e) => setHome(e.target.value.replace(/\D/g, ""))} />
          <TextInput aria-label="أهداف الضيف" inputMode="numeric" placeholder="الضيف" value={open ? "" : away} onChange={(e) => setAway(e.target.value.replace(/\D/g, ""))} />
        </div>
        <textarea aria-label="سبب التصحيح" placeholder="سبب التصحيح" value={open ? "" : notes} onChange={(e) => setNotes(e.target.value)} className={`${inputClass} min-h-[60px]`} />
        <button type="button" className={btnDanger} disabled={!!open || !/^[0-9a-f-]{36}$/i.test(correctId.trim()) || !scoresOk || notes.trim().length < 5} onClick={() => correct.mutate()}>
          تصحيح النتيجة
        </button>
      </section>
    </>
  );
}

function Teams({ notice }: { notice: Notice }) {
  const queryClient = useQueryClient();
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const teams = useQuery({
    queryKey: ["admin-teams", q, page],
    queryFn: () => unwrap(api.GET("/api/v1/admin/moderation/teams", { params: { query: { q: q || undefined, page, pageSize: PAGE_SIZE } } })),
  });
  const change = useMutation({
    mutationFn: ({ id, suspend, text }: { id: string; suspend: boolean; text: string }) =>
      suspend
        ? unwrap(api.POST("/api/v1/admin/moderation/teams/{teamId}/suspend", { params: { path: { teamId: id } }, body: { reason: text } }))
        : unwrap(api.POST("/api/v1/admin/moderation/teams/{teamId}/reactivate", { params: { path: { teamId: id } }, body: { reason: text } })),
    onSuccess: async (_, { suspend }) => {
      notice.ok(suspend ? "اتوقف الفريق." : "الفريق رجع نشط.");
      await queryClient.invalidateQueries({ queryKey: ["admin-teams"] });
    },
    onError: notice.fail,
  });

  return (
    <div className="flex flex-col gap-4">
      <div className="bg-white p-3 sm:p-3.5 rounded-2xl border border-slate-200/90 shadow-xs flex flex-wrap items-center justify-between gap-3">
        <div className="w-full sm:w-80">
          <SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="ابحث باسم الفريق أو المدينة..." />
        </div>
        {q && (
          <button
            type="button"
            onClick={() => {
              setQ("");
              setPage(1);
            }}
            className="text-xs font-bold text-slate-500 hover:text-slate-800 px-3 py-1.5 rounded-lg hover:bg-slate-100 transition-colors cursor-pointer"
          >
            إعادة ضبط
          </button>
        )}
      </div>

      <QueryState
        query={teams}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="لم يتم العثور على أي فرق"
        emptyDescription={q ? "لا توجد فرق تطابق كلمات البحث المحددة. يمكنك تجربة كلمات أخرى." : "لا توجد فرق رياضية مسجلة في المنصة حتى الآن."}
        emptyIcon={q ? "search" : "users"}
        emptyAction={
          q
            ? {
                label: "إعادة ضبط البحث",
                onClick: () => {
                  setQ("");
                  setPage(1);
                },
                variant: "outline",
              }
            : undefined
        }
      >
        {(data) => (
          <>
            <Table<Schemas["AdminTeamListItem"]>
              caption="الفرق"
              rows={data.items}
              rowKey={(t) => t.id}
              columns={[
                { title: "الفريق", cell: (t) => <strong>{t.name}</strong> },
                { title: "المدينة", cell: (t) => t.city },
                { title: "الأعضاء", cell: (t) => num(t.members) },
                { title: "التقييم", cell: (t) => num(t.rating) },
                { title: "بلاغات مفتوحة", cell: (t) => (t.openReportsAgainst > 0 ? <Pill tone="warn">{num(t.openReportsAgainst)}</Pill> : "—") },
                {
                  title: "",
                  cell: (t) =>
                    t.isSuspended ? (
                      <button type="button" className={btnSmall} onClick={() => setReason({ title: "رفع إيقاف الفريق", body: t.suspensionReason ?? "", confirmLabel: "رفع الإيقاف", onConfirm: (text) => change.mutate({ id: t.id, suspend: false, text }) })}>
                        رفع الإيقاف
                      </button>
                    ) : (
                      <button type="button" className={btnSmall} onClick={() => setReason({ title: `إيقاف ${t.name}`, body: "مفيش تحديات ولا نتائج ولا أعضاء جداد لحد ما الإيقاف يترفع. الكابتن هيوصله السبب.", confirmLabel: "إيقاف", onConfirm: (text) => change.mutate({ id: t.id, suspend: true, text }) })}>
                        إيقاف
                      </button>
                    ),
                },
              ]}
            />
            <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onChange={setPage} />
          </>
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </div>
  );
}
