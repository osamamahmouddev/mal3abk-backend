"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, btnPrimary, btnSmall, card, muted } from "@/components/ui";
import { Field, QueryState, Select, Table, TextInput, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { TriangleAlert } from "lucide-react";

type Row = Schemas["FeaturedPlacementDto"];
const SLOTS: [string, string][] = [
  ["Home", "الصفحة الرئيسية"],
  ["Explore", "أعلى الاستكشاف"],
];

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

  const flags = useQuery({
    queryKey: ["feature-flags"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/feature-flags")),
  });

  const toggleFlag = useMutation({
    mutationFn: ({ key, enabled }: { key: string; enabled: boolean }) =>
      unwrap(
        api.PUT("/api/v1/admin/feature-flags/{key}", {
          params: { path: { key } },
          body: { enabled, reason: "تعديل من شاشة الملاعب المميزة" },
        }),
      ),
    onSuccess: async (_, vars) => {
      notice.ok(`تم ${vars.enabled ? "تفعيل" : "إيقاف"} ميزة ${vars.key === "venues.featured" ? "الاستكشاف" : "الرئيسية"}.`);
      await queryClient.invalidateQueries({ queryKey: ["feature-flags"] });
    },
    onError: notice.fail,
  });

  const venuesQuery = useQuery({
    queryKey: ["admin-venues-select"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/venues", {
          params: { query: { approvalStatus: "Approved", pageSize: 100 } },
        }),
      ),
  });

  const placements = useQuery({
    queryKey: ["featured", history],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/featured-venues", {
          params: { query: { includeEnded: history } },
        }),
      ),
  });

  const selectedVenue = (venuesQuery.data?.items ?? []).find((v) => v.id === venueId);

  const venueOptions: [string, string][] = [
    ["", "— اختر الملعب —"],
    ...((venuesQuery.data?.items ?? []).map((v) => [
      v.id,
      v.isPublished ? `${v.name} (منشور)` : `${v.name} (غير منشور)`,
    ] as [string, string])),
  ];

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["featured"] });

  const create = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/featured-venues", {
          body: {
            venueId: venueId.trim(),
            slot,
            title: title || null,
            startsAtUtc: new Date(starts).toISOString(),
            endsAtUtc: new Date(ends).toISOString(),
            sortOrder: Number(sort) || 0,
          },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تمت جدولة الملعب المميز بنجاح.");
      setVenueId("");
      setTitle("");
      await refresh();
    },
    onError: notice.fail,
  });

  const cancel = useMutation({
    mutationFn: (id: string) =>
      unwrap(
        api.POST("/api/v1/admin/featured-venues/{placementId}/cancel", {
          params: { path: { placementId: id } },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تم إيقاف العرض الترويجي.");
      await refresh();
    },
    onError: notice.fail,
  });

  const flagOn = (key: string) => flags.data?.find((f) => f.key === key)?.enabled;

  return (
    <Page>
      <PageHeader
        title="ملاعب مميزة"
        sub="بتظهر للاعبين بعلامة «إعلان» دايمًا في المساحات الإعلانية، ولما الميزة تكون شغالة. الملاعب الموقوفة أو غير المنشورة بتختفي تلقائيًا."
      />
      {notice.node}

      {/* Feature Flags Status & Quick Toggles */}
      <div className="flex flex-wrap items-center justify-between gap-3 bg-white p-4 rounded-2xl border border-gray-200 shadow-xs">
        <div className="flex flex-wrap items-center gap-4">
          <span className="text-xs font-bold text-navy-900">مفاتيح تفعيل الإعلانات:</span>
          
          <div className="flex items-center gap-2">
            <Pill tone={flagOn("venues.featured") ? "ok" : "neutral"}>
              الاستكشاف: {flagOn("venues.featured") ? "مفعل" : "معطل"}
            </Pill>
            <button
              type="button"
              className={`${btnSmall} text-xs font-semibold`}
              disabled={toggleFlag.isPending}
              onClick={() =>
                toggleFlag.mutate({
                  key: "venues.featured",
                  enabled: !flagOn("venues.featured"),
                })
              }
            >
              {flagOn("venues.featured") ? "تعطيل" : "تفعيل"}
            </button>
          </div>

          <div className="flex items-center gap-2">
            <Pill tone={flagOn("home.sponsored") ? "ok" : "neutral"}>
              الرئيسية: {flagOn("home.sponsored") ? "مفعل" : "معطل"}
            </Pill>
            <button
              type="button"
              className={`${btnSmall} text-xs font-semibold`}
              disabled={toggleFlag.isPending}
              onClick={() =>
                toggleFlag.mutate({
                  key: "home.sponsored",
                  enabled: !flagOn("home.sponsored"),
                })
              }
            >
              {flagOn("home.sponsored") ? "تعطيل" : "تفعيل"}
            </button>
          </div>
        </div>

        <span className="text-xs text-gray-500">
          ملاحظة: تظهر الملاعب المميزة فقط عندما يكون المفتاح الخاص بها مفعلاً أعلاه.
        </span>
      </div>

      {/* Scheduling Form */}
      <section className={`${card} rounded-[18px] p-5 grid md:grid-cols-3 gap-3 items-end`}>
        <Field label="الملعب">
          <Select label="الملعب" value={venueId} onChange={setVenueId} options={venueOptions} />
        </Field>
        <Field label="المكان">
          <Select label="المكان" value={slot} onChange={setSlot} options={SLOTS} />
        </Field>
        <Field label="العنوان الترويجي (اختياري، ٨٠ حرف)">
          <TextInput
            placeholder="مثال: خصم ٢٠٪ للحجوزات الصباحية"
            maxLength={80}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
        </Field>
        <Field label="تاريخ البدء">
          <TextInput type="datetime-local" value={starts} onChange={(e) => setStarts(e.target.value)} />
        </Field>
        <Field label="تاريخ الانتهاء (٩٠ يوم كحد أقصى)">
          <TextInput type="datetime-local" value={ends} onChange={(e) => setEnds(e.target.value)} />
        </Field>
        <Field label="ترتيب الأولوية">
          <TextInput
            inputMode="numeric"
            value={sort}
            onChange={(e) => setSort(e.target.value.replace(/\D/g, ""))}
            placeholder="0"
          />
        </Field>

        {selectedVenue && !selectedVenue.isPublished && (
          <div className="md:col-span-3 bg-amber-50 border border-amber-200 text-amber-900 px-4 py-3 rounded-xl text-xs flex items-center gap-2">
            <TriangleAlert className="size-4 shrink-0 text-amber-600" />
            <span>
              الملعب المحدد <strong>({selectedVenue.name})</strong> معتمد لكنه <strong>غير منشور حالياً</strong>.
              لا يمكن جدولة إعلانات إلا للملاعب المنشورة التي يمكن للاعبين حجزها.
            </span>
          </div>
        )}

        <button
          type="button"
          className={`${btnPrimary} md:col-span-3`}
          disabled={
            !/^[0-9a-f-]{36}$/i.test(venueId.trim()) ||
            (selectedVenue ? !selectedVenue.isPublished : false) ||
            create.isPending
          }
          onClick={() => create.mutate()}
        >
          {create.isPending ? "جاري الجدولة..." : "جدولة الملعب كإعلان مميز"}
        </button>
      </section>

      <div className="flex items-center justify-between">
        <label className="flex gap-2 text-sm items-center cursor-pointer select-none">
          <input
            type="checkbox"
            className="rounded border-gray-300 text-navy-900 focus:ring-navy-900"
            checked={history}
            onChange={(e) => setHistory(e.target.checked)}
          />
          <span className="font-semibold text-gray-700">عرض العروض المنتهية والملغاة</span>
        </label>
      </div>

      <QueryState
        query={placements}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد عروض ترويجية مجدولة"
        emptyDescription="لم يتم جدولة أي ملاعب مميزة في الصفحة الرئيسية أو قوائم البحث حتى الآن."
        emptyIcon="sparkles"
        emptyBadge="المساحات الإعلانية شاغرة"
      >
        {(list) => (
          <Table<Row>
            caption="العروض الترويجية المجدولة"
            rows={list}
            rowKey={(p) => p.id}
            columns={[
              {
                title: "الملعب",
                cell: (p) => (
                  <div className="flex flex-col">
                    <strong className="text-navy-900">{p.venueName || p.venueId.slice(0, 8)}</strong>
                    {p.title && <span className="text-xs text-gray-500">{p.title}</span>}
                  </div>
                ),
              },
              {
                title: "المكان",
                cell: (p) => (
                  <span className="font-semibold text-xs text-gray-700">
                    {SLOTS.find(([k]) => k === p.slot)?.[1] ?? p.slot}
                  </span>
                ),
              },
              {
                title: "الفترة",
                cell: (p) => (
                  <span className="text-xs font-mono text-gray-600" dir="ltr">
                    {fmtDateTime(p.startsAtUtc)} إلى {fmtDateTime(p.endsAtUtc)}
                  </span>
                ),
              },
              {
                title: "الترتيب",
                cell: (p) => num(p.sortOrder),
              },
              {
                title: "الحالة",
                cell: (p) => (
                  <Pill tone={p.isCancelled ? "bad" : p.isLive ? "ok" : "neutral"}>
                    {p.isCancelled ? "ملغي" : p.isLive ? "معروض حالياً" : "مجدول"}
                  </Pill>
                ),
              },
              {
                title: "",
                cell: (p) =>
                  !p.isCancelled ? (
                    <button
                      type="button"
                      className={`${btnSmall} text-red-600 hover:bg-red-50`}
                      onClick={() => cancel.mutate(p.id)}
                      disabled={cancel.isPending}
                    >
                      إيقاف العرض
                    </button>
                  ) : null,
              },
            ]}
          />
        )}
      </QueryState>
    </Page>
  );
}
