"use client";

import React, { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useConfirm } from "@malabak/ui";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, btnOutline, btnPrimary, btnSmall, card, chip, muted, redShadow } from "@/components/ui";
import { Field, QueryState, Select, Table, TextInput, fmtDate, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { Star } from "lucide-react";

type Row = Schemas["CustomAreaDto"];
type StatusFilter = "all" | "active" | "focus" | "inactive" | "custom";

export default function AreasPage() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const confirm = useConfirm();

  // Filters & Search
  const [selectedGov, setSelectedGov] = useState<string>("all");
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  // Form State for Adding New Area
  const [newGov, setNewGov] = useState("cairo");
  const [nameAr, setNameAr] = useState("");
  const [nameEn, setNameEn] = useState("");
  const [isFocus, setIsFocus] = useState(false);
  const [showAddForm, setShowAddForm] = useState(false);

  // Queries
  const governorates = useQuery({
    queryKey: ["reference-governorates"],
    queryFn: () => unwrap(api.GET("/api/v1/reference/governorates")),
    staleTime: 5 * 60_000,
  });

  const areas = useQuery({
    queryKey: ["admin-areas", selectedGov, searchQuery, statusFilter],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/areas", {
          params: {
            query: {
              governorate: selectedGov === "all" ? undefined : selectedGov,
              search: searchQuery.trim() || undefined,
              status: statusFilter === "all" ? undefined : statusFilter,
            },
          },
        }),
      ),
  });

  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["admin-areas"] }),
      queryClient.invalidateQueries({ queryKey: ["reference-governorates"] }),
    ]);
  };

  const nameOf = (code: string) => governorates.data?.find((g) => g.code === code)?.nameAr ?? code;

  // Mutations
  const add = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/areas", {
          body: {
            governorateCode: newGov,
            nameAr: nameAr.trim(),
            nameEn: nameEn.trim() || null,
            isFocus,
          },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تمت إضافة المنطقة بنجاح.");
      setNameAr("");
      setNameEn("");
      setIsFocus(false);
      setShowAddForm(false);
      await refresh();
    },
    onError: notice.fail,
  });

  const toggleStatus = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      unwrap(
        api.PUT("/api/v1/admin/areas/{areaId}/status", {
          params: { path: { areaId: id } },
          body: { isActive },
        }),
      ),
    onSuccess: async (_, vars) => {
      notice.ok(vars.isActive ? "تم تفعيل المنطقة وإتاحتها للحجز." : "تم إيقاف المنطقة مؤقتاً وحجبها عن التطبيقات.");
      await refresh();
    },
    onError: notice.fail,
  });

  const toggleFocus = useMutation({
    mutationFn: ({ id, isFocus }: { id: string; isFocus: boolean }) =>
      unwrap(
        api.PUT("/api/v1/admin/areas/{areaId}/focus", {
          params: { path: { areaId: id } },
          body: { isFocus },
        }),
      ),
    onSuccess: async (_, vars) => {
      notice.ok(vars.isFocus ? "تم تمييز المنطقة كنطاق تركيز وتشغيل." : "تم إلغاء نطاق التركيز عن المنطقة.");
      await refresh();
    },
    onError: notice.fail,
  });

  const remove = useMutation({
    mutationFn: (id: string) =>
      unwrap(
        api.DELETE("/api/v1/admin/areas/{areaId}", {
          params: { path: { areaId: id } },
        }),
      ),
    onSuccess: async () => {
      notice.ok("تمت إزالة المنطقة.");
      await refresh();
    },
    onError: notice.fail,
  });

  // Dropdown options
  const govOptions: [string, string][] = [
    ["all", "جميع المحافظات (27 محافظة)"],
    ...((governorates.data ?? []).map((g) => [g.code, `${g.nameAr} (${g.areas.length} منطقة)`] as [string, string])),
  ];

  const addGovOptions: [string, string][] = (governorates.data ?? []).map((g) => [g.code, g.nameAr]);

  // Quick stats
  const stats = useMemo(() => {
    const list = areas.data ?? [];
    return {
      total: list.length,
      active: list.filter((a) => a.isActive).length,
      focus: list.filter((a) => a.isFocus).length,
      inactive: list.filter((a) => !a.isActive).length,
    };
  }, [areas.data]);

  return (
    <Page>
      <PageHeader
        title="إدارة المناطق والمحافظات"
        sub="التحكم الكامل في نطاق العمل والمناطق النشطة ومناطق التركيز في كل محافظات مصر. يمكنك إيقاف أي منطقة أو تحديدها كنطاق تركيز رئيسي."
      >
        <button
          type="button"
          className={showAddForm ? btnOutline : btnPrimary}
          onClick={() => setShowAddForm((s) => !s)}
        >
          {showAddForm ? "إلغاء الإضافة" : "+ إضافة منطقة جديدة"}
        </button>
      </PageHeader>
      {notice.node}

      {/* KPI Stats Cards */}
      <section className="grid grid-cols-2 md:grid-cols-4 gap-3 animate-fade-in">
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1`}>
          <span className={`text-xs font-semibold ${muted}`}>إجمالي المناطق المعروضة</span>
          <strong className="text-2xl font-black text-ink">{num(stats.total)}</strong>
        </div>
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1 border-r-4 border-r-emerald-500`}>
          <span className={`text-xs font-semibold text-emerald-800`}>المناطق النشطة</span>
          <strong className="text-2xl font-black text-emerald-700">{num(stats.active)}</strong>
        </div>
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1 border-r-4 border-r-amber-500`}>
          <span className="text-xs font-semibold text-amber-800 flex items-center gap-1">مناطق التركيز <Star className="size-3.5 fill-amber-400 text-amber-500" /></span>
          <strong className="text-2xl font-black text-amber-700">{num(stats.focus)}</strong>
        </div>
        <div className={`${card} rounded-[18px] p-4 flex flex-col gap-1 border-r-4 border-r-rose-400`}>
          <span className={`text-xs font-semibold text-rose-800`}>المناطق الموقوفة</span>
          <strong className="text-2xl font-black text-rose-600">{num(stats.inactive)}</strong>
        </div>
      </section>

      {/* Foldable Add Area Form */}
      {showAddForm && (
        <section className={`${card} rounded-[20px] p-5 border border-brand-red/20 shadow-md animate-slide-down`}>
          <div className="flex items-center justify-between mb-3 border-b border-[#EDF0F5] pb-2">
            <h3 className="text-sm font-bold text-ink">إضافة منطقة جديدة إلى المحافظة</h3>
            <span className={`text-xs ${muted}`}>ستظهر فوراً في الويب والتطبيقات</span>
          </div>
          <div className="grid md:grid-cols-3 gap-3 items-end">
            <Field label="المحافظة">
              <Select label="المحافظة" value={newGov} onChange={setNewGov} options={addGovOptions} />
            </Field>
            <Field label="اسم المنطقة (عربي)">
              <TextInput placeholder="مثال: التجمع الخامس" maxLength={60} value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
            </Field>
            <Field label="الاسم بالإنجليزي (اختياري)">
              <TextInput placeholder="Fifth Settlement" maxLength={60} dir="ltr" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
            </Field>
            <div className="md:col-span-3 flex flex-wrap items-center justify-between gap-3 pt-2">
              <label className="flex items-center gap-2 cursor-pointer text-sm font-semibold select-none">
                <input
                  type="checkbox"
                  checked={isFocus}
                  onChange={(e) => setIsFocus(e.target.checked)}
                  className="w-4 h-4 rounded text-brand-red focus:ring-brand-red"
                />
                <span>تعيين فوراً كنطاق تركيز وتشغيل مستهدف</span>
                <Star className="size-3.5 fill-amber-400 text-amber-500" />
              </label>
              <div className="flex items-center gap-2">
                <button type="button" className={btnOutline} onClick={() => setShowAddForm(false)}>
                  إلغاء
                </button>
                <button
                  type="button"
                  className={btnPrimary}
                  disabled={nameAr.trim().length < 2 || add.isPending}
                  onClick={() => add.mutate()}
                >
                  {add.isPending ? "جاري الإضافة..." : "حفظ المنطقة"}
                </button>
              </div>
            </div>
          </div>
        </section>
      )}

      {/* Filter and Search Bar */}
      <section className={`${card} rounded-[18px] p-4 flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between`}>
        <div className="flex flex-col sm:flex-row gap-2 flex-1">
          <div className="w-full sm:w-64">
            <Select label="المحافظة" value={selectedGov} onChange={setSelectedGov} options={govOptions} />
          </div>
          <div className="flex-1">
            <TextInput
              placeholder="ابحث باسم المنطقة بالعربي أو بالإنجليزي..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>
        </div>

        {/* Segmented Filter Chips */}
        <div className="flex items-center gap-1 overflow-x-auto pb-1 md:pb-0">
          <button type="button" className={chip(statusFilter === "all")} onClick={() => setStatusFilter("all")}>
            الكل
          </button>
          <button type="button" className={chip(statusFilter === "active")} onClick={() => setStatusFilter("active")}>
            نشطة
          </button>
          <button type="button" className={chip(statusFilter === "focus")} onClick={() => setStatusFilter("focus")}>
            <Star className="size-3.5 fill-amber-400 text-amber-500" />
            مناطق التركيز
          </button>
          <button type="button" className={chip(statusFilter === "inactive")} onClick={() => setStatusFilter("inactive")}>
            موقوفة
          </button>
          <button type="button" className={chip(statusFilter === "custom")} onClick={() => setStatusFilter("custom")}>
            مضافة
          </button>
        </div>
      </section>

      {/* Areas Table */}
      <QueryState
        query={areas}
        isEmpty={(d) => d.length === 0}
        emptyTitle="لا توجد مناطق مطابقة"
        emptyDescription="لا توجد مناطق مسجلة مطابقة للفلتر أو البحث المحدد. يمكنك اختيار محافظة أخرى أو تعديل كلمة البحث."
        emptyIcon="search"
        emptyAction={
          selectedGov || searchQuery || statusFilter !== "all"
            ? {
                label: "إعادة ضبط البحث والفلاتر",
                onClick: () => {
                  setSelectedGov("");
                  setSearchQuery("");
                  setStatusFilter("all");
                },
                variant: "outline",
              }
            : undefined
        }
      >
        {(list) => (
          <Table<Row>
            caption={`المناطق (${list.length})`}
            rows={list}
            rowKey={(a) => a.id}
            columns={[
              {
                title: "المنطقة",
                cell: (a) => (
                  <div className="flex flex-col gap-0.5">
                    <div className="flex items-center gap-2">
                      <strong className="text-sm text-slate-900">{a.nameAr}</strong>
                      {a.isFocus && (
                        <span className="inline-flex items-center text-[10px] font-bold px-1.5 py-0.5 rounded bg-amber-100 text-amber-900">
                          نطاق تركيز
                        </span>
                      )}
                    </div>
                    <span className="text-xs text-slate-400 font-mono" dir="ltr">
                      {a.nameEn}
                    </span>
                  </div>
                ),
              },
              {
                title: "المحافظة",
                cell: (a) => (
                  <span className="inline-flex items-center px-2.5 py-1 rounded-md bg-slate-100 text-slate-700 text-xs font-semibold">
                    {nameOf(a.governorateCode)}
                  </span>
                ),
              },
              {
                title: "النوع",
                cell: (a) => (
                  <Pill tone={a.isCustom ? "navy" : "neutral"}>
                    {a.isCustom ? "مضافة" : "أساسية"}
                  </Pill>
                ),
              },
              {
                title: "نطاق التركيز",
                cell: (a) => (
                  <button
                    type="button"
                    title={a.isFocus ? "إلغاء نطاق التركيز" : "تعيين كنطاق تركيز مستهدف"}
                    disabled={toggleFocus.isPending}
                    onClick={() => toggleFocus.mutate({ id: a.id, isFocus: !a.isFocus })}
                    className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all duration-150 cursor-pointer ${
                      a.isFocus
                        ? "bg-amber-100 text-amber-900 border border-amber-300 hover:bg-amber-200 shadow-xs"
                        : "border border-slate-200 text-slate-500 hover:border-amber-400 hover:text-amber-800 hover:bg-amber-50"
                    }`}
                  >
                    <Star className={`size-3.5 ${a.isFocus ? "fill-amber-400 text-amber-500" : ""}`} />
                    <span>{a.isFocus ? "منطقة تركيز" : "عادي"}</span>
                  </button>
                ),
              },
              {
                title: "الحالة",
                cell: (a) => (
                  <button
                    type="button"
                    title={a.isActive ? "إيقاف مؤقت للمنطقة" : "تفعيل المنطقة"}
                    disabled={toggleStatus.isPending}
                    onClick={() => toggleStatus.mutate({ id: a.id, isActive: !a.isActive })}
                    className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all duration-150 cursor-pointer ${
                      a.isActive
                        ? "bg-emerald-50 text-emerald-800 border border-emerald-200 hover:bg-rose-50 hover:text-rose-700 hover:border-rose-300"
                        : "bg-rose-50 text-rose-800 border border-rose-200 hover:bg-emerald-50 hover:text-emerald-700 hover:border-emerald-300"
                    }`}
                  >
                    <span
                      className={`w-2 h-2 rounded-full ${
                        a.isActive ? "bg-emerald-600" : "bg-rose-500"
                      }`}
                    />
                    <span>{a.isActive ? "نشطة (إيقاف)" : "موقوفة (تفعيل)"}</span>
                  </button>
                ),
              },
              {
                title: "تاريخ الإضافة",
                cell: (a) => <span className={`text-xs ${muted}`}>{fmtDate(a.createdAtUtc)}</span>,
              },
              {
                title: "",
                cell: (a) =>
                  a.isCustom ? (
                    <button
                      type="button"
                      className={`${btnSmall} text-rose-600 hover:bg-rose-50 hover:border-rose-200 cursor-pointer`}
                      disabled={remove.isPending}
                      onClick={async () => {
                        const ok = await confirm({
                          title: "حذف المنطقة المخصصة",
                          message: "هل أنت متأكد من حذف هذه المنطقة؟ سيتم إزالتها من قوائم وتصنيفات النظام.",
                          itemHighlight: `المنطقة: ${a.nameAr}${a.nameEn ? ` (${a.nameEn})` : ""}`,
                          confirmLabel: "نعم، احذف المنطقة",
                          cancelLabel: "إلغاء",
                          destructive: true,
                          badge: "المناطق الجغرافية",
                        });
                        if (ok) {
                          remove.mutate(a.id);
                        }
                      }}
                    >
                      إزالة
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
