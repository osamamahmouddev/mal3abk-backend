"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, btnPrimary, btnSmall, card, muted } from "@/components/ui";
import { Field, QueryState, Select, Table, TextInput, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Row = Schemas["FeaturedPlacementDto"];
const SLOTS: [string, string][] = [["Home", "الصفحة الرئيسية"], ["Explore", "أعلى الاستكشاف"]];

function localInput(offsetDays: number) {
  const d = new Date(Date.now() + offsetDays * 86_400_000);
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
  return d.toISOString().slice(0, 16);
}

export default function FeaturedPage() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [history, setHistory] = useState(false);
  const [venueId, setVenueId] = useState("");
  const [slot, setSlot] = useState("Explore");
  const [title, setTitle] = useState("");
  const [starts, setStarts] = useState(localInput(0));
  const [ends, setEnds] = useState(localInput(7));
  const [sort, setSort] = useState("0");

  const flags = useQuery({ queryKey: ["feature-flags"], queryFn: () => unwrap(api.GET("/api/v1/admin/feature-flags")) });
  const venuesQuery = useQuery({
    queryKey: ["admin-venues-select"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues", { params: { query: { approvalStatus: "Approved", pageSize: 100 } } })),
  });
  const placements = useQuery({
    queryKey: ["featured", history],
    queryFn: () => unwrap(api.GET("/api/v1/admin/featured-venues", { params: { query: { includeEnded: history } } })),
  });
  const venueOptions: [string, string][] = [
    ["", "— اختر الملعب —"],
    ...((venuesQuery.data?.items ?? []).map((v) => [v.id, v.name] as [string, string])),
  ];
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["featured"] });
  const create = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/featured-venues", {
          body: { venueId: venueId.trim(), slot, title: title || null, startsAtUtc: new Date(starts).toISOString(), endsAtUtc: new Date(ends).toISOString(), sortOrder: Number(sort) || 0 },
        }),
      ),
    onSuccess: async () => { notice.ok("اتجدول الملعب المميز."); setVenueId(""); setTitle(""); await refresh(); },
    onError: notice.fail,
  });
  const cancel = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/v1/admin/featured-venues/{placementId}/cancel", { params: { path: { placementId: id } } })),
    onSuccess: async () => { notice.ok("اتوقف العرض."); await refresh(); },
    onError: notice.fail,
  });

  const flagOn = (key: string) => flags.data?.find((f) => f.key === key)?.enabled;

  return (
    <Page>
      <PageHeader title="ملاعب مميزة" sub="بتظهر للاعبين بعلامة «إعلان» دايمًا، ولما الميزة تكون شغالة بس. الملاعب الموقوفة أو المخفية بتختفي تلقائيًا." />
      {notice.node}
      <div className="flex flex-wrap gap-2 text-sm">
        <Pill tone={flagOn("venues.featured") ? "ok" : "neutral"}>الاستكشاف: {flagOn("venues.featured") ? "شغال" : "مقفول"}</Pill>
        <Pill tone={flagOn("home.sponsored") ? "ok" : "neutral"}>الرئيسية: {flagOn("home.sponsored") ? "شغال" : "مقفول"}</Pill>
        <span className={muted}>التشغيل من إعدادات المنصة ← المميزات.</span>
      </div>
      <section className={`${card} rounded-[18px] p-4 grid md:grid-cols-3 gap-2 items-end`}>
        <Field label="الملعب">
          <Select label="الملعب" value={venueId} onChange={setVenueId} options={venueOptions} />
        </Field>
        <Field label="المكان"><Select label="المكان" value={slot} onChange={setSlot} options={SLOTS} /></Field>
        <Field label="العنوان (اختياري، ٨٠ حرف)"><TextInput maxLength={80} value={title} onChange={(e) => setTitle(e.target.value)} /></Field>
        <Field label="من"><TextInput type="datetime-local" value={starts} onChange={(e) => setStarts(e.target.value)} /></Field>
        <Field label="إلى (٩٠ يوم بالكتير)"><TextInput type="datetime-local" value={ends} onChange={(e) => setEnds(e.target.value)} /></Field>
        <Field label="الترتيب"><TextInput inputMode="numeric" value={sort} onChange={(e) => setSort(e.target.value.replace(/\D/g, ""))} /></Field>
        <button type="button" className={`${btnPrimary} md:col-span-3`} disabled={!/^[0-9a-f-]{36}$/i.test(venueId.trim()) || create.isPending} onClick={() => create.mutate()}>
          جدولة
        </button>
      </section>
      <label className="flex gap-2 text-sm items-center"><input type="checkbox" checked={history} onChange={(e) => setHistory(e.target.checked)} /> عرض المنتهي والملغي</label>
      <QueryState query={placements} isEmpty={(d) => d.length === 0} empty="مفيش عروض مجدولة.">
        {(list) => (
          <Table<Row>
            caption="العروض"
            rows={list}
            rowKey={(p) => p.id}
            columns={[
              { title: "الملعب", cell: (p) => <strong>{p.venueName || p.venueId.slice(0, 8)}</strong> },
              { title: "المكان", cell: (p) => SLOTS.find(([k]) => k === p.slot)?.[1] ?? p.slot },
              { title: "العنوان", cell: (p) => p.title ?? "—" },
              { title: "الفترة", cell: (p) => `${fmtDateTime(p.startsAtUtc)} ← ${fmtDateTime(p.endsAtUtc)}` },
              { title: "الترتيب", cell: (p) => num(p.sortOrder) },
              { title: "الحالة", cell: (p) => <Pill tone={p.isCancelled ? "bad" : p.isLive ? "ok" : "neutral"}>{p.isCancelled ? "ملغي" : p.isLive ? "معروض" : "مجدول"}</Pill> },
              { title: "", cell: (p) => (!p.isCancelled ? <button type="button" className={btnSmall} onClick={() => cancel.mutate(p.id)}>إيقاف</button> : null) },
            ]}
          />
        )}
      </QueryState>
    </Page>
  );
}
