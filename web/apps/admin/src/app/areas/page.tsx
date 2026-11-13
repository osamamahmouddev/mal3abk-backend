"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, btnPrimary, btnSmall, card } from "@/components/ui";
import { Field, QueryState, Select, Table, TextInput, fmtDate, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";

type Row = Schemas["CustomAreaDto"];

export default function AreasPage() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [governorate, setGovernorate] = useState("cairo");
  const [nameAr, setNameAr] = useState("");
  const [nameEn, setNameEn] = useState("");

  const governorates = useQuery({
    queryKey: ["reference-governorates"],
    queryFn: () => unwrap(api.GET("/api/v1/reference/governorates")),
    staleTime: 5 * 60_000,
  });
  const areas = useQuery({ queryKey: ["admin-areas"], queryFn: () => unwrap(api.GET("/api/v1/admin/areas")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin-areas"] });
  const nameOf = (code: string) => governorates.data?.find((g) => g.code === code)?.nameAr ?? code;

  const add = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/admin/areas", { body: { governorateCode: governorate, nameAr: nameAr.trim(), nameEn: nameEn.trim() || null } })),
    onSuccess: async () => { notice.ok("اتضافت المنطقة."); setNameAr(""); setNameEn(""); await refresh(); },
    onError: notice.fail,
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/v1/admin/areas/{areaId}", { params: { path: { areaId: id } } })),
    onSuccess: async () => { notice.ok("اتشالت المنطقة."); await refresh(); },
    onError: notice.fail,
  });

  const options: [string, string][] = (governorates.data ?? []).map((g) => [g.code, g.nameAr]);

  return (
    <Page>
      <PageHeader title="المناطق" sub="المناطق اللي بتظهر في كل محافظة. أصحاب الملاعب كمان يقدروا يضيفوا منطقة مش موجودة، وتظهر هنا عشان تشيلها لو مش مناسبة." />
      {notice.node}
      <section className={`${card} rounded-[18px] p-4 grid md:grid-cols-3 gap-2 items-end`}>
        <Field label="المحافظة"><Select label="المحافظة" value={governorate} onChange={setGovernorate} options={options} /></Field>
        <Field label="اسم المنطقة (عربي)"><TextInput maxLength={60} value={nameAr} onChange={(e) => setNameAr(e.target.value)} /></Field>
        <Field label="الاسم بالإنجليزي (اختياري)"><TextInput maxLength={60} dir="ltr" value={nameEn} onChange={(e) => setNameEn(e.target.value)} /></Field>
        <button type="button" className={`${btnPrimary} md:col-span-3`} disabled={nameAr.trim().length < 2 || add.isPending} onClick={() => add.mutate()}>
          إضافة منطقة
        </button>
      </section>
      <QueryState query={areas} isEmpty={(d) => d.length === 0} empty="لسه محدش أضاف مناطق جديدة. المناطق الأساسية موجودة تلقائيًا.">
        {(list) => (
          <Table<Row>
            caption="المناطق المضافة"
            rows={list}
            rowKey={(a) => a.id}
            columns={[
              { title: "المنطقة", cell: (a) => <strong>{a.nameAr}</strong> },
              { title: "المحافظة", cell: (a) => nameOf(a.governorateCode) },
              { title: "تاريخ الإضافة", cell: (a) => fmtDate(a.createdAtUtc) },
              { title: "", cell: (a) => <button type="button" className={btnSmall} onClick={() => remove.mutate(a.id)}>إزالة</button> },
            ]}
          />
        )}
      </QueryState>
    </Page>
  );
}
