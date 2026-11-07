"use client";

import React, { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, btnSmall, card, muted } from "@/components/ui";
import { Drawer, Field, Pager, QueryState, Table, TextInput, fmtDateTime } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Row = Schemas["AuditLogDto"];
const PAGE_SIZE = 50;

export default function AuditPage() {
  const [action, setAction] = useState("");
  const [entityType, setEntityType] = useState("");
  const [entityId, setEntityId] = useState("");
  const [actorId, setActorId] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Row | null>(null);

  const logs = useQuery({
    queryKey: ["audit", action, entityType, entityId, actorId, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/audit-logs", {
          params: {
            query: {
              action: action || undefined,
              entityType: entityType || undefined,
              entityId: entityId || undefined,
              actorId: /^[0-9a-f-]{36}$/i.test(actorId) ? actorId : undefined,
              page,
              pageSize: PAGE_SIZE,
            },
          },
        }),
      ),
  });

  return (
    <Page>
      <PageHeader title="سجل التدقيق" sub="سجل للقراءة بس، مايتعدلش ولا يتمسح. البيانات الحساسة متخفية قبل ما تتسجل." />
      <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
        <Field label="الإجراء (بيبدأ بـ)"><TextInput dir="ltr" placeholder="admin.user" value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }} /></Field>
        <Field label="نوع الكيان"><TextInput dir="ltr" placeholder="Venue" value={entityType} onChange={(e) => { setEntityType(e.target.value); setPage(1); }} /></Field>
        <Field label="ID الكيان"><TextInput dir="ltr" value={entityId} onChange={(e) => { setEntityId(e.target.value); setPage(1); }} /></Field>
        <Field label="ID المنفّذ"><TextInput dir="ltr" value={actorId} onChange={(e) => { setActorId(e.target.value); setPage(1); }} /></Field>
      </div>
      <QueryState query={logs} isEmpty={(d) => d.items.length === 0} empty="مفيش سجلات بالفلتر ده.">
        {(data) => (
          <>
            <Table<Row>
              caption="سجل التدقيق"
              rows={data.items}
              rowKey={(r) => r.id}
              selected={selected?.id}
              onSelect={setSelected}
              columns={[
                { title: "الوقت", cell: (r) => fmtDateTime(r.occurredAtUtc) },
                { title: "الإجراء", cell: (r) => <span dir="ltr" className="font-inter text-xs">{r.action}</span> },
                { title: "الكيان", cell: (r) => <span dir="ltr" className="font-inter text-xs">{r.entityType}:{r.entityId.slice(0, 8)}</span> },
                { title: "المنفّذ", cell: (r) => `${r.actorType}${r.impersonatorId ? " (بصفة)" : ""}` },
                { title: "السبب", cell: (r) => <span className="line-clamp-1">{r.reason ?? "—"}</span> },
              ]}
            />
            <Pager page={page} pageSize={PAGE_SIZE} total={data.totalCount} onChange={setPage} />
          </>
        )}
      </QueryState>
      <Drawer title="تفاصيل السجل" open={!!selected} onClose={() => setSelected(null)}>
        {selected && (
          <div className="flex flex-col gap-3 text-sm">
            <span dir="ltr" className="font-inter">{selected.action}</span>
            <span className={muted}>{fmtDateTime(selected.occurredAtUtc)} · IP {selected.ipAddress ?? "—"}</span>
            <span>المنفّذ: <span dir="ltr" className="font-inter text-xs">{selected.actorId ?? "System"}</span></span>
            {selected.impersonatorId && <span>بصفة من: <span dir="ltr" className="font-inter text-xs">{selected.impersonatorId}</span></span>}
            {selected.reason && <p className="bg-[#F6F7FA] rounded-xl p-2">{selected.reason}</p>}
            <Json title="قبل" value={selected.beforeJson} />
            <Json title="بعد" value={selected.afterJson} />
            <button type="button" className={btnSmall} onClick={() => setEntityId(selected.entityId)}>كل السجلات لنفس الكيان</button>
          </div>
        )}
      </Drawer>
    </Page>
  );
}

function Json({ title, value }: { title: string; value?: string | null }) {
  if (!value) return null;
  let pretty = value;
  try {
    pretty = JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    // Show as stored.
  }
  return (
    <div className={`${card} rounded-xl p-2`}>
      <strong className="text-xs">{title}</strong>
      <pre dir="ltr" className="text-[11px] font-inter whitespace-pre-wrap break-all">{pretty}</pre>
    </div>
  );
}
