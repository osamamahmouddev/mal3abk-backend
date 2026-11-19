"use client";

import React, { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Page, PageHeader, Pill, SearchBox, btnOutline, btnPrimary, btnSmall, card, muted, toArabicDigits } from "@/components/ui";
import { Pager, QueryState, Select, Table, fmtDateTime, useNotice } from "@/components/admin";
import { api, downloadFile, unwrap } from "@/lib/api";
import {
  AlertTriangle,
  Building2,
  Calendar,
  Check,
  CheckCircle2,
  Copy,
  Download,
  ExternalLink,
  Flame,
  LayoutGrid,
  Lock,
  MessageSquare,
  Phone,
  PhoneCall,
  ShieldCheck,
  Sparkles,
  Table2,
  Trash2,
  Trophy,
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

const PAGE_SIZE = 24;

/**
 * Safely parses and formats Egyptian phone numbers from waitlist entries.
 * Avoids any raw "[ENCRYPTED]" or "0[ENCRYPTED]" leakage in UI.
 */
function formatWaitlistPhone(phone: string | null | undefined) {
  if (!phone || phone.includes("ENCRYPTED")) {
    return {
      isEncrypted: true,
      display: "سجل تجريبي سابق",
      formattedDisplay: "سجل تجريبي قديم (فُقد مفتاحه أثناء التطوير)",
      waUrl: null,
      telUrl: null,
      rawDigits: "",
    };
  }

  // Strip non-digits
  let digits = phone.replace(/\D/g, "");

  // Strip international prefix if already present (e.g. 2010... -> 10...)
  if (digits.startsWith("20") && digits.length >= 12) {
    digits = digits.slice(2);
  }
  // Strip leading 0 if present (e.g. 010... -> 10...)
  if (digits.startsWith("0")) {
    digits = digits.slice(1);
  }

  const localNumber = `0${digits}`;

  // Prettify Egyptian mobile numbers (01x xxxx xxxx)
  let formatted = localNumber;
  if (localNumber.length === 11) {
    formatted = `${localNumber.slice(0, 3)} ${localNumber.slice(3, 7)} ${localNumber.slice(7)}`;
  }

  return {
    isEncrypted: false,
    display: localNumber,
    formattedDisplay: formatted,
    waUrl: `https://wa.me/20${digits}`,
    telUrl: `tel:${localNumber}`,
    rawDigits: localNumber,
  };
}

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

  const handleCopyPhone = (phoneText: string, id: string) => {
    if (!phoneText) return;
    navigator.clipboard.writeText(phoneText);
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

  const handleDelete = async (id: string) => {
    if (!confirm("هل أنت متأكد من حذف هذا السجل من قائمة الانتظار؟")) return;
    try {
      await unwrap(
        api.DELETE("/api/v1/admin/waitlist/{id}", {
          params: { path: { id } },
        }),
      );
      notice.ok("تم حذف السجل بنجاح.");
      waitlistQuery.refetch();
    } catch {
      notice.fail("تعذر حذف السجل، يرجى المحاولة لاحقاً.");
    }
  };

  const handlePurgeCorrupted = async () => {
    const corrupted = rawItems.filter((i) => !i.phone || i.phone.includes("ENCRYPTED"));
    if (corrupted.length === 0) {
      notice.ok("لا توجد سجلات تجريبية تالفة في هذه الصفحة.");
      return;
    }
    if (!confirm(`هل أنت متأكد من حذف (${corrupted.length}) سجل تجريبي قديم غير قابل للقراءة؟`)) return;
    try {
      await Promise.all(
        corrupted.map((item) =>
          unwrap(
            api.DELETE("/api/v1/admin/waitlist/{id}", {
              params: { path: { id: item.id } },
            }),
          ),
        ),
      );
      notice.ok(`تم تنظيف وحذف ${corrupted.length} سجل تجريبي قديم بنجاح.`);
      waitlistQuery.refetch();
    } catch {
      notice.fail("تعذر إكمال حذف بعض السجلات.");
      waitlistQuery.refetch();
    }
  };

  const rawItems = waitlistQuery.data?.items ?? [];
  const corruptedCount = rawItems.filter((i) => !i.phone || i.phone.includes("ENCRYPTED")).length;
  const filteredItems = useMemo(() => {
    if (!search.trim()) return rawItems;
    const term = search.trim().toLowerCase();
    return rawItems.filter((item) => {
      const phoneParsed = formatWaitlistPhone(item.phone);
      const matchesPhone = phoneParsed.rawDigits.includes(term) || item.phone.toLowerCase().includes(term);
      const matchesId = item.id.toLowerCase().includes(term);
      const matchesPlan = item.chosenPlan ? item.chosenPlan.toLowerCase().includes(term) : false;
      return matchesPhone || matchesId || matchesPlan;
    });
  }, [rawItems, search]);

  const totalCount = waitlistQuery.data?.totalCount ?? 0;
  const totalPlayersInPage = rawItems.filter((i) => i.audience === "Player" || i.audience === 0 || String(i.audience).toLowerCase() === "player").length;
  const totalVenuesInPage = rawItems.filter((i) => i.audience === "Venue" || i.audience === 1 || String(i.audience).toLowerCase() === "venue").length;

  const isVenue = (row: WaitlistEntry) =>
    row.audience === "Venue" || row.audience === 1 || String(row.audience).toLowerCase() === "venue";

  return (
    <Page>
      <PageHeader
        title="قائمة الانتظار والمسجلين مبكراً (Launch Leads)"
        sub="المسجلون مبكراً من صفحة الهبوط لتطبيق اللاعبين وأصحاب الملاعب للتمييز والمكافآت وتفعيل العضوية المؤسسة."
      >
        <div className="flex items-center gap-2">
          {corruptedCount > 0 && (
            <button
              type="button"
              onClick={handlePurgeCorrupted}
              className={`${btnOutline} gap-1.5 text-xs text-rose-700 border-rose-300 hover:bg-rose-50 shadow-2xs cursor-pointer`}
              title="حذف جميع السجلات التجريبية القديمة غير القابلة للقراءة في هذه الصفحة"
            >
              <Trash2 className="size-3.5 text-rose-600" />
              <span>تنظيف السجلات التجريبية ({toArabicDigits(corruptedCount)})</span>
            </button>
          )}
          <button
            type="button"
            onClick={handleExportCsv}
            disabled={isExporting || totalCount === 0}
            className={`${btnPrimary} gap-2 text-xs shadow-sm hover:shadow-md transition-all`}
          >
            <Download className="size-4" />
            {isExporting ? "جارٍ التصدير..." : "تصدير ملف CSV"}
          </button>
        </div>
      </PageHeader>

      {notice.node}

      {/* Metric Summary Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
        {/* Total Leads */}
        <div className="bg-white rounded-2xl p-5 border border-slate-200/80 shadow-xs flex items-center justify-between relative overflow-hidden group hover:border-slate-300 transition-all">
          <div className="flex flex-col gap-1.5 z-10">
            <span className={`text-xs font-semibold ${muted}`}>إجمالي المسجلين في القائمة</span>
            <div className="flex items-baseline gap-2">
              <b className="text-3xl font-extrabold text-slate-900 tracking-tight">{toArabicDigits(totalCount)}</b>
              <span className="text-xs text-slate-500 font-medium">مهتم بالتطبيق</span>
            </div>
            <span className="text-[11px] text-emerald-700 bg-emerald-50 px-2 py-0.5 rounded-md w-fit flex items-center gap-1 font-semibold border border-emerald-200/60 mt-1">
              <Sparkles className="size-3 text-emerald-600" />
              أعضاء مؤسسين (VIP Early Adopters)
            </span>
          </div>
          <div className="w-12 h-12 rounded-2xl bg-slate-100 text-slate-700 flex items-center justify-center shadow-2xs border border-slate-200/80 group-hover:scale-105 transition-transform shrink-0">
            <Users className="w-6 h-6 text-slate-700" />
          </div>
        </div>

        {/* Players in Current Page */}
        <div className="bg-white rounded-2xl p-5 border border-slate-200/80 shadow-xs flex items-center justify-between relative overflow-hidden group hover:border-emerald-300 transition-all">
          <div className="flex flex-col gap-1.5 z-10">
            <span className={`text-xs font-semibold ${muted}`}>اللاعبين في الصفحة الحالية</span>
            <div className="flex items-baseline gap-2">
              <b className="text-3xl font-extrabold text-emerald-700 tracking-tight">{toArabicDigits(totalPlayersInPage)}</b>
              <span className="text-xs text-emerald-600/80 font-medium">لاعب</span>
            </div>
            <span className="text-[11px] text-emerald-700 bg-emerald-50 px-2 py-0.5 rounded-md w-fit flex items-center gap-1 font-semibold border border-emerald-200/60 mt-1">
              <CheckCircle2 className="size-3 text-emerald-600" />
              مؤهلين لأول حجز بدون أي عمولة
            </span>
          </div>
          <div className="w-12 h-12 rounded-2xl bg-emerald-50 text-emerald-700 flex items-center justify-center shadow-2xs border border-emerald-200/80 group-hover:scale-105 transition-transform shrink-0">
            <Trophy className="w-6 h-6 text-emerald-700" />
          </div>
        </div>

        {/* Venue Owners in Current Page */}
        <div className="bg-white rounded-2xl p-5 border border-slate-200/80 shadow-xs flex items-center justify-between relative overflow-hidden group hover:border-amber-300 transition-all">
          <div className="flex flex-col gap-1.5 z-10">
            <span className={`text-xs font-semibold ${muted}`}>أصحاب الملاعب في الصفحة الحالية</span>
            <div className="flex items-baseline gap-2">
              <b className="text-3xl font-extrabold text-amber-700 tracking-tight">{toArabicDigits(totalVenuesInPage)}</b>
              <span className="text-xs text-amber-600/80 font-medium">منشأة رياضية</span>
            </div>
            <span className="text-[11px] text-amber-800 bg-amber-50 px-2 py-0.5 rounded-md w-fit flex items-center gap-1 font-semibold border border-amber-200/60 mt-1">
              <Flame className="size-3 text-amber-600" />
              مؤهلين لفترة تجريبية مجانية موسعة
            </span>
          </div>
          <div className="w-12 h-12 rounded-2xl bg-amber-50 text-amber-700 flex items-center justify-center shadow-2xs border border-amber-200/80 group-hover:scale-105 transition-transform shrink-0">
            <Building2 className="w-6 h-6 text-amber-700" />
          </div>
        </div>
      </div>

      {/* Search & Filter Toolbar */}
      <div className="bg-white p-3 sm:p-3.5 rounded-2xl border border-slate-200/90 shadow-xs flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2.5 flex-1 min-w-[280px]">
          <div className="w-full sm:w-72 lg:w-80 shrink-0">
            <SearchBox
              value={search}
              onChange={(v) => {
                setSearch(v);
                setPage(1);
              }}
              placeholder="ابحث برقم الموبايل، الكود أو الباقة..."
            />
          </div>

          <div className="w-40 sm:w-44 shrink-0">
            <select
              aria-label="الجمهور"
              value={audience}
              onChange={(e) => {
                setAudience(e.target.value);
                setPage(1);
              }}
              className="w-full min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] bg-white text-xs font-semibold text-slate-800 hover:border-slate-400 focus:outline-none focus:ring-2 focus:ring-ink focus:border-ink transition-all cursor-pointer"
            >
              {AUDIENCE_OPTIONS.map(([v, t]) => (
                <option key={v} value={v}>
                  {t}
                </option>
              ))}
            </select>
          </div>

          <div className="w-40 sm:w-44 shrink-0">
            <select
              aria-label="الباقة المختارة"
              value={plan}
              onChange={(e) => {
                setPlan(e.target.value);
                setPage(1);
              }}
              className="w-full min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] bg-white text-xs font-semibold text-slate-800 hover:border-slate-400 focus:outline-none focus:ring-2 focus:ring-ink focus:border-ink transition-all cursor-pointer"
            >
              {PLAN_OPTIONS.map(([v, t]) => (
                <option key={v} value={v}>
                  {t}
                </option>
              ))}
            </select>
          </div>

          {(audience || plan || search) && (
            <button
              type="button"
              onClick={() => {
                setAudience("");
                setPlan("");
                setSearch("");
                setPage(1);
              }}
              className="min-h-[44px] px-3 rounded-xl text-xs font-bold text-slate-500 hover:text-slate-800 hover:bg-slate-100 transition-colors cursor-pointer"
            >
              إعادة ضبط
            </button>
          )}
        </div>

        {/* View Switcher: Cards vs Table */}
        <div className="inline-flex rounded-xl bg-slate-100 p-1 border border-slate-200 shrink-0">
          <button
            type="button"
            className={`inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg text-xs font-bold transition-all cursor-pointer ${
              viewMode === "cards" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("cards")}
          >
            <LayoutGrid className="size-3.5" />
            <span>بطاقات</span>
          </button>
          <button
            type="button"
            className={`inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg text-xs font-bold transition-all cursor-pointer ${
              viewMode === "table" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("table")}
          >
            <Table2 className="size-3.5" />
            <span>جدول</span>
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
              <div className="bg-white p-12 rounded-2xl border border-slate-200 text-center flex flex-col items-center justify-center gap-2">
                <b className="text-base text-slate-800">لا توجد نتائج مطابقة للبحث</b>
                <span className={`text-xs ${muted}`}>لا يوجد مسجل برقم الموبايل أو المعايير التي أدخلتها.</span>
              </div>
            ) : viewMode === "cards" ? (
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5 sm:gap-6">
                {filteredItems.map((item) => {
                  const isV = isVenue(item);
                  const phoneInfo = formatWaitlistPhone(item.phone);
                  const isCopied = copiedId === item.id;

                  return (
                    <div
                      key={item.id}
                      className="bg-white rounded-2xl p-5 flex flex-col justify-between gap-4 border border-slate-200 shadow-xs hover:shadow-md hover:border-slate-300 transition-all relative overflow-hidden group"
                    >
                      {/* Top Header Row */}
                      <div className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-3">
                          <div
                            className={`w-11 h-11 rounded-xl flex items-center justify-center shrink-0 ${
                              isV
                                ? "bg-amber-100/90 text-amber-800 border border-amber-200/80 shadow-xs"
                                : "bg-emerald-100/90 text-emerald-800 border border-emerald-200/80 shadow-xs"
                            }`}
                          >
                            {isV ? <Building2 className="w-5 h-5 text-amber-800" /> : <Users className="w-5 h-5 text-emerald-800" />}
                          </div>
                          <div className="flex flex-col">
                            <div className="flex items-center gap-2">
                              <span className="font-bold text-sm text-slate-900">
                                {isV ? "صاحب منشأة / ملعب" : "لاعب كرة قدم"}
                              </span>
                              <span
                                className={`text-[10px] font-bold px-2 py-0.5 rounded-full ${
                                  isV
                                    ? "bg-amber-100/80 text-amber-900 border border-amber-200/60"
                                    : "bg-emerald-100/80 text-emerald-900 border border-emerald-200/60"
                                }`}
                              >
                                {isV ? "Venue" : "Player"}
                              </span>
                            </div>
                            <span className="text-[11px] text-slate-500 font-medium">
                              {isV ? "مهتم بإدارة الحجوزات ونمو النادي" : "مهتم بحجز الملاعب وتكوين المباريات"}
                            </span>
                          </div>
                        </div>

                        <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2.5 py-1 rounded-xl bg-gradient-to-r from-amber-50 to-amber-100/60 text-amber-800 border border-amber-300/60 shadow-2xs shrink-0">
                          <Sparkles className="w-3.5 h-3.5 text-amber-600" />
                          مؤسس VIP
                        </span>
                      </div>

                      {/* Phone Box Container */}
                      <div className="bg-slate-50/80 border border-slate-200/80 rounded-xl p-3 flex items-center justify-between gap-3">
                        {phoneInfo.isEncrypted ? (
                          <div className="flex items-center gap-2.5">
                            <div className="w-9 h-9 rounded-lg bg-amber-50 text-amber-600 border border-amber-200/60 flex items-center justify-center shrink-0">
                              <AlertTriangle className="w-4 h-4 text-amber-600" />
                            </div>
                            <div className="flex flex-col">
                              <span className="text-xs font-bold text-amber-900">سجل تجريبي سابق</span>
                              <span className="text-[10px] text-slate-500">فُقد مفتاح اختباره أثناء مرحلة التطوير</span>
                            </div>
                          </div>
                        ) : (
                          <div className="flex items-center gap-2.5">
                            <div className="w-9 h-9 rounded-lg bg-emerald-100/80 text-emerald-800 border border-emerald-200/60 flex items-center justify-center shrink-0">
                              <Phone className="w-4 h-4 text-emerald-700" />
                            </div>
                            <div className="flex flex-col">
                              <span className="text-base font-extrabold font-mono tracking-wider text-slate-900" dir="ltr">
                                {phoneInfo.formattedDisplay}
                              </span>
                              <span className="text-[10px] text-slate-500 font-medium">رقم الموبايل المسجل</span>
                            </div>
                          </div>
                        )}

                        {/* Action Buttons */}
                        <div className="flex items-center gap-1.5 shrink-0">
                          {phoneInfo.isEncrypted ? (
                            <button
                              type="button"
                              onClick={() => handleDelete(item.id)}
                              title="حذف هذا السجل التجريبي القديم"
                              className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 hover:bg-rose-100 text-rose-700 border border-rose-200 transition-colors cursor-pointer"
                            >
                              <Trash2 className="w-3.5 h-3.5 text-rose-600" />
                              <span>حذف</span>
                            </button>
                          ) : (
                            <>
                              <button
                                type="button"
                                onClick={() => handleCopyPhone(phoneInfo.rawDigits, item.id)}
                                title="نسخ الرقم"
                                className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-semibold bg-white hover:bg-slate-100 border border-slate-200 text-slate-700 shadow-2xs transition-colors cursor-pointer"
                              >
                                {isCopied ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <Copy className="w-3.5 h-3.5" />}
                                <span>{isCopied ? "تم" : "نسخ"}</span>
                              </button>

                              {phoneInfo.waUrl && (
                                <a
                                  href={phoneInfo.waUrl}
                                  target="_blank"
                                  rel="noopener noreferrer"
                                  title="محادثة واتساب مباشرة"
                                  className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-bold bg-[#25D366] hover:bg-[#20ba5a] text-white shadow-xs transition-colors"
                                >
                                  <MessageSquare className="w-3.5 h-3.5 fill-white text-white" />
                                  <span>واتساب</span>
                                </a>
                              )}

                              {phoneInfo.telUrl && (
                                <a
                                  href={phoneInfo.telUrl}
                                  title="اتصال هاتفي مباشر"
                                  className="p-1.5 rounded-lg text-slate-600 hover:text-slate-900 hover:bg-slate-100 border border-slate-200 bg-white transition-colors"
                                >
                                  <PhoneCall className="w-3.5 h-3.5" />
                                </a>
                              )}

                              <button
                                type="button"
                                onClick={() => handleDelete(item.id)}
                                title="حذف السجل"
                                className="p-1.5 rounded-lg text-slate-400 hover:text-rose-600 hover:bg-rose-50 border border-transparent hover:border-rose-200 transition-colors cursor-pointer"
                              >
                                <Trash2 className="w-3.5 h-3.5" />
                              </button>
                            </>
                          )}
                        </div>
                      </div>

                      {/* Chosen Plan (Venue) */}
                      {isV && (
                        <div className="flex items-center justify-between text-xs px-1">
                          <span className="text-slate-500 font-medium">الباقة المفضلة:</span>
                          {item.chosenPlan ? (
                            <span
                              className={`font-bold px-2.5 py-0.5 rounded-md text-xs border ${
                                item.chosenPlan === "Pro"
                                  ? "bg-purple-50 text-purple-800 border-purple-200"
                                  : item.chosenPlan === "Growth"
                                  ? "bg-blue-50 text-blue-800 border-blue-200"
                                  : "bg-slate-100 text-slate-800 border-slate-200"
                              }`}
                            >
                              {item.chosenPlan === "Pro"
                                ? "Pro (الاحترافية — ٩٩٩ ج.م)"
                                : item.chosenPlan === "Growth"
                                ? "Growth (النمو — ٤٩٩ ج.م)"
                                : `${item.chosenPlan} (مجانية)`}
                            </span>
                          ) : (
                            <span className="text-slate-400">لم تحدد باقة بعد</span>
                          )}
                        </div>
                      )}

                      {/* Card Footer */}
                      <div className="flex items-center justify-between text-[11px] text-slate-500 pt-3 border-t border-slate-100">
                        <div className="flex items-center gap-1.5 text-emerald-700 font-semibold">
                          <ShieldCheck className="size-3.5 text-emerald-600 shrink-0" />
                          <span>موافقة موثقة (قانون ١٥١/٢٠٢٠)</span>
                        </div>
                        <div className="flex items-center gap-1 text-slate-500 font-medium">
                          <Calendar className="size-3 text-slate-400 shrink-0" />
                          <span>{fmtDateTime(item.createdAtUtc)}</span>
                        </div>
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
                    cell: (r) => {
                      const phoneInfo = formatWaitlistPhone(r.phone);
                      const isCopied = copiedId === r.id;

                      if (phoneInfo.isEncrypted) {
                        return (
                          <div className="flex items-center gap-1.5 text-amber-800 text-xs font-medium">
                            <AlertTriangle className="size-3.5 text-amber-600 shrink-0" />
                            <span>{phoneInfo.formattedDisplay}</span>
                          </div>
                        );
                      }

                      return (
                        <div className="flex items-center gap-2">
                          <span className="font-mono font-bold text-slate-900 text-sm" dir="ltr">
                            {phoneInfo.formattedDisplay}
                          </span>
                          <button
                            type="button"
                            onClick={() => handleCopyPhone(phoneInfo.rawDigits, r.id)}
                            className="text-slate-400 hover:text-slate-700 p-1 cursor-pointer transition-colors"
                            title="نسخ الرقم"
                          >
                            {isCopied ? <Check className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" />}
                          </button>
                        </div>
                      );
                    },
                  },
                  {
                    title: "الفئة",
                    cell: (r) => {
                      const isV = isVenue(r);
                      return (
                        <span
                          className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md text-xs font-bold ${
                            isV ? "bg-amber-100 text-amber-900 border border-amber-200/60" : "bg-emerald-100 text-emerald-900 border border-emerald-200/60"
                          }`}
                        >
                          {isV ? <Building2 className="size-3 text-amber-700" /> : <Users className="size-3 text-emerald-700" />}
                          {isV ? "صاحب ملعب" : "لاعب"}
                        </span>
                      );
                    },
                  },
                  {
                    title: "الباقة المختارة",
                    cell: (r) =>
                      r.chosenPlan ? (
                        <span className="font-bold text-slate-800 text-xs bg-slate-100 px-2 py-0.5 rounded border border-slate-200">
                          {r.chosenPlan}
                        </span>
                      ) : (
                        <span className={muted}>—</span>
                      ),
                  },
                  {
                    title: "الموافقة القانونية",
                    cell: () => (
                      <span className="inline-flex items-center gap-1 text-xs text-emerald-700 font-semibold">
                        <CheckCircle2 className="size-3.5 text-emerald-600" />
                        مسجلة (قانون ١٥١)
                      </span>
                    ),
                  },
                  {
                    title: "تاريخ التسجيل",
                    cell: (r) => <span className="text-xs text-slate-600 font-medium">{fmtDateTime(r.createdAtUtc)}</span>,
                  },
                  {
                    title: "إجراء سريع",
                    cell: (r) => {
                      const phoneInfo = formatWaitlistPhone(r.phone);
                      if (phoneInfo.isEncrypted) {
                        return (
                          <button
                            type="button"
                            onClick={() => handleDelete(r.id)}
                            className="inline-flex items-center gap-1 text-xs text-rose-700 hover:text-rose-900 bg-rose-50 hover:bg-rose-100 px-2 py-1 rounded border border-rose-200 transition-colors cursor-pointer"
                            title="حذف هذا السجل التجريبي القديم"
                          >
                            <Trash2 className="size-3 text-rose-600" />
                            <span>حذف</span>
                          </button>
                        );
                      }

                      return (
                        <div className="flex items-center gap-2">
                          <a
                            href={phoneInfo.waUrl ?? "#"}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-xs font-bold text-emerald-700 hover:text-emerald-800 bg-emerald-50 hover:bg-emerald-100 px-2.5 py-1 rounded-lg border border-emerald-200 transition-colors"
                          >
                            <MessageSquare className="size-3" />
                            <span>واتساب</span>
                          </a>

                          {phoneInfo.telUrl && (
                            <a
                              href={phoneInfo.telUrl}
                              className="text-slate-500 hover:text-slate-800 p-1"
                              title="اتصال هاتفي"
                            >
                              <PhoneCall className="size-3.5" />
                            </a>
                          )}

                          <button
                            type="button"
                            onClick={() => handleDelete(r.id)}
                            className="text-slate-400 hover:text-rose-600 p-1 cursor-pointer transition-colors"
                            title="حذف السجل"
                          >
                            <Trash2 className="size-3.5" />
                          </button>
                        </div>
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
