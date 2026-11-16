"use client";

import React, { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Page, PageHeader, Pill, SearchBox, btnOutline, btnPrimary, btnSmall, card, muted, toArabicDigits } from "@/components/ui";
import { Pager, QueryState, Select, Table, fmtDateTime, useNotice } from "@/components/admin";
import { api, downloadFile, unwrap } from "@/lib/api";
import {
  Check,
  CheckCircle2,
  Copy,
  Download,
  ExternalLink,
  Flame,
  LayoutGrid,
  MessageSquare,
  ShieldCheck,
  Sparkles,
  Table2,
  Users,
} from "lucide-react";

interface WaitlistEntry {
  id: string;
  audience: string | number;
  chosenPlan?: string | null;
  phone: string;
  consentRecordedAtUtc: string;
  createdAtUtc: string;
}

interface WaitlistPagedResponse {
  items: WaitlistEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}

const AUDIENCE_OPTIONS: [string, string][] = [
  ["", "كل المسجلين"],
  ["Player", "لاعبين (Player)"],
  ["Venue", "أصحاب ملاعب (Venue)"],
];

const PLAN_OPTIONS: [string, string][] = [
  ["", "كل الباقات"],
  ["Starter", "Starter — مجاني"],
  ["Growth", "Growth — ٤٩٩ ج.م"],
  ["Pro", "Pro — ٩٩٩ ج.م"],
];

const PAGE_SIZE = 25;

export default function WaitlistPage() {
  const [audience, setAudience] = useState("");
  const [plan, setPlan] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [isExporting, setIsExporting] = useState(false);
  const notice = useNotice();

  const waitlistQuery = useQuery({
    queryKey: ["admin-waitlist", audience, plan, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/waitlist", {
          params: {
            query: {
              audience: audience || undefined,
              plan: plan || undefined,
              page,
              pageSize: PAGE_SIZE,
            },
          },
        }),
      ) as Promise<WaitlistPagedResponse>,
  });

  const handleCopyPhone = (phone: string, id: string) => {
    navigator.clipboard.writeText(phone);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const handleExportCsv = async () => {
    try {
      setIsExporting(true);
      const params = new URLSearchParams();
      params.set("export", "true");
      if (audience) params.set("audience", audience);
      if (plan) params.set("plan", plan);

      const filename = `malabak-waitlist-${new Date().toISOString().slice(0, 10)}.csv`;
      await downloadFile(`/api/v1/admin/waitlist?${params.toString()}`, filename);
      notice.ok("تم تصدير ملف المسجلين في قائمة الانتظار بنجاح.");
    } catch {
      notice.fail("تعذر تصدير ملف CSV، تأكد من صلاحيات الحساب وحاول ثانية.");
    } finally {
      setIsExporting(false);
    }
  };

  const rawItems = waitlistQuery.data?.items ?? [];
  const filteredItems = useMemo(() => {
    if (!search.trim()) return rawItems;
    const term = search.trim();
    return rawItems.filter(
      (item) => item.phone.includes(term) || item.id.toLowerCase().includes(term.toLowerCase()) || (item.chosenPlan && item.chosenPlan.toLowerCase().includes(term.toLowerCase())),
    );
  }, [rawItems, search]);

  const totalCount = waitlistQuery.data?.totalCount ?? 0;
  const totalPlayersInPage = rawItems.filter((i) => i.audience === "Player" || i.audience === 0).length;
  const totalVenuesInPage = rawItems.filter((i) => i.audience === "Venue" || i.audience === 1).length;

  const isVenue = (row: WaitlistEntry) => row.audience === "Venue" || row.audience === 1 || String(row.audience).toLowerCase() === "venue";

  return (
    <Page>
      <PageHeader
        title="قائمة الانتظار (Launch Leads)"
        sub="المسجلون مبكراً من صفحة الهبوط لتطبيق اللاعبين وأصحاب الملاعب للتمييز والمكافآت وتفعيل العضوية المؤسسة."
      >
        <button
          type="button"
          onClick={handleExportCsv}
          disabled={isExporting || totalCount === 0}
          className={`${btnPrimary} gap-2 text-xs`}
        >
          <Download className="size-4" />
          {isExporting ? "جارٍ التصدير..." : "تصدير ملف CSV"}
        </button>
      </PageHeader>

      {notice.node}

      {/* Metric Summary Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3.5">
        <div className={`${card} p-4 rounded-2xl flex items-center justify-between`}>
          <div className="flex flex-col gap-1">
            <span className={`text-xs font-semibold ${muted}`}>إجمالي المسجلين في القائمة</span>
            <b className="text-2xl text-slate-900">{toArabicDigits(totalCount)}</b>
            <span className="text-[11px] text-brand-green flex items-center gap-1 font-semibold">
              <Sparkles className="size-3" />
              أعضاء مؤسسين (VIP)
            </span>
          </div>
          <div className="size-11 rounded-2xl bg-slate-100 flex items-center justify-center text-slate-700">
            <Users className="size-5" />
          </div>
        </div>

        <div className={`${card} p-4 rounded-2xl flex items-center justify-between`}>
          <div className="flex flex-col gap-1">
            <span className={`text-xs font-semibold ${muted}`}>لاعبين في الصفحة الحالية</span>
            <b className="text-2xl text-emerald-700">{toArabicDigits(totalPlayersInPage)}</b>
            <span className="text-[11px] text-emerald-600 font-semibold">مؤهلين لأول حجز بدون عمولة</span>
          </div>
          <div className="size-11 rounded-2xl bg-emerald-50 text-emerald-700 flex items-center justify-center">
            <CheckCircle2 className="size-5" />
          </div>
        </div>

        <div className={`${card} p-4 rounded-2xl flex items-center justify-between`}>
          <div className="flex flex-col gap-1">
            <span className={`text-xs font-semibold ${muted}`}>أصحاب ملاعب في الصفحة الحالية</span>
            <b className="text-2xl text-amber-700">{toArabicDigits(totalVenuesInPage)}</b>
            <span className="text-[11px] text-amber-600 font-semibold">مؤهلين لتجربة مجانية موسعة</span>
          </div>
          <div className="size-11 rounded-2xl bg-amber-50 text-amber-700 flex items-center justify-center">
            <Flame className="size-5" />
          </div>
        </div>
      </div>

      {/* Search & Filter Toolbar */}
      <div className="flex flex-wrap gap-2.5 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap gap-2 flex-1 min-w-[280px]">
          <div className="flex-[1_1_240px]">
            <SearchBox
              value={search}
              onChange={(v) => {
                setSearch(v);
                setPage(1);
              }}
              placeholder="ابحث برقم الموبايل أو الباقة..."
            />
          </div>
          <Select
            label="الجمهور"
            value={audience}
            onChange={(v) => {
              setAudience(v);
              setPage(1);
            }}
            options={AUDIENCE_OPTIONS}
          />
          <Select
            label="الباقة المختارة"
            value={plan}
            onChange={(v) => {
              setPlan(v);
              setPage(1);
            }}
            options={PLAN_OPTIONS}
          />
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

      {/* Main Query State & Data Display */}
      <QueryState
        query={waitlistQuery}
        isEmpty={(d) => d.items.length === 0}
        emptyTitle="قائمة الانتظار فارغة حالياً"
        emptyDescription={
          audience || plan
            ? "لا يوجد مسجلون يطابقون الفلاتر المحددة حالياً. يمكنك تغيير أو إعادة ضبط الفلتر."
            : "لم يسجل أي زائر في قائمة انتظار صفحة الهبوط حتى الآن."
        }
        emptyIcon="users"
        emptyAction={
          audience || plan
            ? {
                label: "إعادة ضبط الفلاتر",
                onClick: () => {
                  setAudience("");
                  setPlan("");
                  setPage(1);
                },
              }
            : undefined
        }
      >
        {() => (
          <>
            {filteredItems.length === 0 ? (
              <div className={`${card} p-8 rounded-2xl text-center flex flex-col items-center justify-center gap-2`}>
                <b className="text-base text-slate-800">لا توجد نتائج مطابقة للبحث</b>
                <span className={`text-xs ${muted}`}>لا يوجد مسجل برقم الموبايل أو المعايير التي أدخلتها.</span>
              </div>
            ) : viewMode === "cards" ? (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3.5">
                {filteredItems.map((item) => {
                  const isV = isVenue(item);
                  const formattedPhone = item.phone.startsWith("0") ? item.phone : `0${item.phone}`;
                  const waNumber = formattedPhone.startsWith("0") ? `2${formattedPhone}` : formattedPhone;
                  const isCopied = copiedId === item.id;

                  return (
                    <div
                      key={item.id}
                      className={`${card} rounded-2xl p-4.5 flex flex-col justify-between gap-3 border transition-all hover:border-slate-300 hover:shadow-md relative overflow-hidden`}
                    >
                      {/* VIP Ribbon indicator */}
                      <div className="flex items-center justify-between gap-2">
                        <span
                          className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-bold ${
                            isV ? "bg-amber-100 text-amber-900 border border-amber-200" : "bg-emerald-100 text-emerald-900 border border-emerald-200"
                          }`}
                        >
                          {isV ? <Flame className="size-3.5 text-amber-700" /> : <Users className="size-3.5 text-emerald-700" />}
                          {isV ? "صاحب ملعب" : "لاعب"}
                        </span>

                        <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-slate-500">
                          <Sparkles className="size-3 text-brand-gold" />
                          عضو مؤسس
                        </span>
                      </div>

                      {/* Phone & Details */}
                      <div className="flex flex-col gap-1.5 py-1">
                        <div className="flex items-center justify-between">
                          <b className="text-lg font-mono tracking-wider text-slate-900" dir="ltr">
                            {formattedPhone}
                          </b>
                          <div className="flex items-center gap-1">
                            <button
                              type="button"
                              onClick={() => handleCopyPhone(formattedPhone, item.id)}
                              title="نسخ الرقم"
                              className={`${btnSmall} !px-2 text-slate-600`}
                            >
                              {isCopied ? <Check className="size-3 text-emerald-600" /> : <Copy className="size-3" />}
                              <span className="text-[11px]">{isCopied ? "تم" : "نسخ"}</span>
                            </button>
                            <a
                              href={`https://wa.me/${waNumber}`}
                              target="_blank"
                              rel="noopener noreferrer"
                              title="محادثة واتساب"
                              className={`${btnSmall} !px-2 bg-emerald-50 text-emerald-700 border-emerald-200 hover:bg-emerald-100`}
                            >
                              <MessageSquare className="size-3" />
                              <span className="text-[11px]">واتساب</span>
                            </a>
                          </div>
                        </div>

                        {isV && item.chosenPlan && (
                          <div className="flex items-center gap-1.5 text-xs text-slate-700 mt-1">
                            <span className={muted}>الباقة المفضلة:</span>
                            <span className="font-bold bg-slate-100 px-2 py-0.5 rounded-md border border-slate-200">
                              {item.chosenPlan}
                            </span>
                          </div>
                        )}

                        <div className="flex items-center gap-1 text-[11px] text-emerald-700 mt-1">
                          <ShieldCheck className="size-3.5 text-emerald-600" />
                          <span>تم تسجيل الموافقة الصريحة (قانون ١٥١/٢٠٢٠)</span>
                        </div>
                      </div>

                      {/* Footer & Date */}
                      <div className="flex items-center justify-between text-[11px] text-slate-500 pt-2 border-t border-[#F0F2F6]">
                        <span>تاريخ التسجيل:</span>
                        <span className="font-semibold text-slate-700">{fmtDateTime(item.createdAtUtc)}</span>
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <Table
                caption="قائمة الانتظار"
                rows={filteredItems}
                rowKey={(r) => r.id}
                columns={[
                  {
                    title: "رقم الموبايل",
                    cell: (r) => (
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-bold text-slate-900" dir="ltr">
                          {r.phone.startsWith("0") ? r.phone : `0${r.phone}`}
                        </span>
                        <button
                          type="button"
                          onClick={() => handleCopyPhone(r.phone, r.id)}
                          className="text-slate-400 hover:text-slate-700 p-1"
                          title="نسخ الرقم"
                        >
                          {copiedId === r.id ? <Check className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" />}
                        </button>
                      </div>
                    ),
                  },
                  {
                    title: "الفئة",
                    cell: (r) => {
                      const isV = isVenue(r);
                      return (
                        <span
                          className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md text-xs font-bold ${
                            isV ? "bg-amber-100 text-amber-900" : "bg-emerald-100 text-emerald-900"
                          }`}
                        >
                          {isV ? "صاحب ملعب" : "لاعب"}
                        </span>
                      );
                    },
                  },
                  {
                    title: "الباقة المختارة",
                    cell: (r) => (r.chosenPlan ? <span className="font-semibold text-slate-800">{r.chosenPlan}</span> : <span className={muted}>—</span>),
                  },
                  {
                    title: "الموافقة القانونية",
                    cell: () => (
                      <span className="inline-flex items-center gap-1 text-xs text-emerald-700 font-semibold">
                        <CheckCircle2 className="size-3.5 text-emerald-600" />
                        مسجلة
                      </span>
                    ),
                  },
                  {
                    title: "تاريخ التسجيل",
                    cell: (r) => <span className="text-xs text-slate-600">{fmtDateTime(r.createdAtUtc)}</span>,
                  },
                  {
                    title: "إجراء سريع",
                    cell: (r) => {
                      const formattedPhone = r.phone.startsWith("0") ? r.phone : `0${r.phone}`;
                      const waNumber = formattedPhone.startsWith("0") ? `2${formattedPhone}` : formattedPhone;
                      return (
                        <a
                          href={`https://wa.me/${waNumber}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="inline-flex items-center gap-1 text-xs font-semibold text-emerald-700 hover:underline"
                        >
                          واتساب
                          <ExternalLink className="size-3" />
                        </a>
                      );
                    },
                  },
                ]}
              />
            )}

            <Pager
              page={page}
              pageSize={PAGE_SIZE}
              total={totalCount}
              onChange={(p) => setPage(p)}
            />
          </>
        )}
      </QueryState>
    </Page>
  );
}
