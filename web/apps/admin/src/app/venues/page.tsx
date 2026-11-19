"use client";

import React, { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { KV, Page, PageHeader, Pill, ReasonModal, Switch, type ReasonRequest, SearchBox, btnDanger, btnOutline, btnPrimary, btnSmall, card, inputClass, money, muted, toArabicDigits } from "@/components/ui";
import { Drawer, Field, Pager, QueryState, Section, Select, Table, TextInput, fmtDate, fmtDateTime, num, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";
import { ImageLightbox } from "@/components/ImageLightbox";
import { Camera, Check, ChevronLeft, Eye, Image as ImageIcon, LayoutGrid, MapPin, Package, Smartphone, Table2, X, BadgePercent } from "lucide-react";
import { Pitch, SoccerBall } from "@/components/icons";

type Row = Schemas["AdminVenueListItem"];

const TURF_LABELS: Record<string, string> = {
  ArtificialTurf: "نجيل صناعي",
  NaturalGrass: "نجيل طبيعي",
  Hybrid: "نجيل هجين",
  Tartan: "ترتان / مطاطي",
  HardCourt: "أسمنتي / صلب",
  Parquet: "باركيه صالات",
  Sand: "رملي / شاطئي",
};

function mediaSrc(path: string) {
  if (!path) return "";
  if (/^https?:\/\//i.test(path)) return path;
  if (path.startsWith("/admin/bff")) return path;
  return `/admin/bff${path.startsWith("/") ? "" : "/"}${path}`;
}

const STATUSES: [string, string][] = [
  ["", "كل حالات الاعتماد"],
  ["Approved", "معتمد"],
  ["Submitted", "بانتظار الموافقة"],
  ["NeedsInfo", "بحاجة لمعلومات"],
  ["Rejected", "مرفوض"],
  ["Suspended", "موقوف"],
  ["Draft", "مسودة"],
];

const PRICING_MODELS: [string, string][] = [
  ["", "كل أنظمة التشغيل"],
  ["Subscription", "نظام الاشتراك"],
  ["Commission", "نظام العمولة"],
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
  const [modelFilter, setModelFilter] = useState("");
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
      <div className="flex flex-wrap gap-3 items-center justify-between bg-white p-3.5 rounded-2xl border border-[#EEF1F6] shadow-xs">
        <div className="flex flex-wrap gap-2.5 items-center flex-1 min-w-[280px]">
          <div className="w-full sm:w-72 shrink-0">
            <SearchBox
              value={q}
              onChange={(v) => {
                setQ(v);
                setPage(1);
              }}
              placeholder="ابحث باسم الملعب، المدينة، المنطقة أو المالك..."
            />
          </div>
          <div className="w-36 sm:w-44 shrink-0">
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
          <div className="w-36 sm:w-44 shrink-0">
            <Select
              label="نظام التشغيل"
              value={modelFilter}
              onChange={(v) => {
                setModelFilter(v);
                setPage(1);
              }}
              options={PRICING_MODELS}
            />
          </div>
          {(q || status || modelFilter) && (
            <button
              type="button"
              onClick={() => {
                setQ("");
                setStatus("");
                setModelFilter("");
                setPage(1);
              }}
              className="text-xs font-bold text-slate-500 hover:text-slate-800 px-3 py-1.5 rounded-lg hover:bg-slate-100 transition-colors cursor-pointer"
            >
              إعادة ضبط
            </button>
          )}
        </div>

        {/* View Switcher: Cards vs Table */}
        <div className="inline-flex rounded-xl bg-slate-100 p-1 border border-slate-200 shrink-0">
          <button
            type="button"
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
              viewMode === "cards" ? "bg-white text-slate-900 shadow-xs" : "text-[#5B6475] hover:text-slate-900"
            }`}
            onClick={() => setViewMode("cards")}
          >
            <LayoutGrid className="size-3.5" />
            بطاقات الملاعب
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
          const items = modelFilter
            ? data.items.filter((item) => {
                const isComm = item.pricingModel === "Commission";
                return modelFilter === "Commission" ? isComm : !isComm;
              })
            : data.items;

          if (items.length === 0) {
            return (
              <div className="bg-white rounded-2xl p-12 text-center flex flex-col items-center gap-3 border border-slate-200 shadow-xs">
                <span className="text-base font-bold text-slate-800">لا توجد ملاعب تطابق نظام التشغيل والتسعير المحدد</span>
                <button
                  type="button"
                  onClick={() => setModelFilter("")}
                  className="text-xs font-bold text-emerald-700 hover:text-emerald-800 underline cursor-pointer"
                >
                  إلغاء فلتر النظام وعرض باقي الملاعب
                </button>
              </div>
            );
          }

          if (viewMode === "cards") {
            return (
              <>
                <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5 sm:gap-6">
                  {items.map((r) => {
                    const subStatus = r.subscriptionStatus ?? "Default";
                    const planName = r.planName || "بدون باقة";
                    const subLabel = SUB_STATUS_LABEL[subStatus] ?? subStatus;
                    const subTone = SUB_STATUS_TONE[subStatus] ?? "neutral";
                    const photos = r.photos ?? [];
                    const hasPhotos = photos.length > 0;
                    const isCommission = r.pricingModel === "Commission";

                    return (
                      <div
                        key={r.id}
                        className={`${card} rounded-[20px] p-4 flex flex-col justify-between gap-3.5 border border-[#E2E6EF] hover:border-slate-400 hover:shadow-md transition-all`}
                      >
                        {/* Venue Cover Image or Placeholder Banner */}
                        <div
                          className="relative h-44 w-full rounded-2xl overflow-hidden bg-slate-100 cursor-pointer group border border-slate-200 shrink-0"
                          onClick={() => setSelected(r.id)}
                        >
                          {hasPhotos ? (
                            <>
                              <img
                                src={mediaSrc(photos[0])}
                                alt={r.name}
                                className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                              />
                              <div className="absolute inset-0 bg-gradient-to-t from-black/70 via-black/15 to-black/35" />
                            </>
                          ) : (
                            <div className="w-full h-full bg-gradient-to-br from-emerald-800 via-teal-900 to-slate-900 flex flex-col items-center justify-center gap-1.5 text-white/70 group-hover:text-white transition-colors">
                              <Pitch className="size-10 text-emerald-400/80" strokeWidth={1.5} />
                              <span className="text-[11px] font-semibold text-emerald-200/80">لم يتم رفع صور للنادي</span>
                            </div>
                          )}

                          {/* Top Badges */}
                          <div className="absolute top-2.5 right-2.5 flex items-center gap-1.5 z-10">
                            <Pill tone={TONE[r.approvalStatus] ?? "neutral"} className="text-xs font-bold px-2.5 py-0.5 shadow-sm">
                              {STATUS_MAP[r.approvalStatus] ?? r.approvalStatus}
                            </Pill>
                            <span
                              className={`inline-flex items-center gap-1 text-[11px] font-semibold px-2 py-0.5 rounded-md shadow-sm ${
                                r.isPublished ? "bg-emerald-600 text-white" : "bg-slate-900/85 text-slate-200"
                              }`}
                            >
                              <span className={`size-1.5 rounded-full ${r.isPublished ? "bg-emerald-300" : "bg-slate-400"}`} />
                              {r.isPublished ? "منشور" : "مسودة"}
                            </span>
                          </div>

                          {/* Bottom Badges on Image */}
                          <div className="absolute bottom-2.5 inset-x-2.5 flex items-end justify-between z-10">
                            <div className="text-white drop-shadow-md">
                              <span className="text-xs font-bold flex items-center gap-1.5 bg-black/55 backdrop-blur-xs px-2.5 py-1 rounded-lg">
                                <SoccerBall className="size-3.5 text-emerald-400" />
                                {num(r.pitches)} ملاعب فرعية
                              </span>
                            </div>

                            {hasPhotos && (
                              <span className="bg-black/60 backdrop-blur-xs text-white text-[11px] font-medium px-2 py-1 rounded-lg flex items-center gap-1 shadow-sm">
                                <Camera className="size-3 text-emerald-400" />
                                {toArabicDigits(photos.length)} {photos.length === 1 ? "صورة" : "صور"}
                              </span>
                            )}
                          </div>
                        </div>

                        {/* Venue Title & Location */}
                        <div className="flex items-start justify-between gap-2 px-1">
                          <div className="min-w-0 flex flex-col">
                            <h3
                              className="font-extrabold text-slate-900 text-base truncate cursor-pointer hover:text-emerald-700 transition"
                              title={r.name}
                              onClick={() => setSelected(r.id)}
                            >
                              {r.name}
                            </h3>
                            <div className="flex items-center gap-1.5 text-xs text-[#5B6475] mt-0.5">
                              <MapPin className="size-3.5 shrink-0 text-slate-400" />
                              <span className="font-semibold text-slate-700">{r.city}</span>
                              {r.area && <span>· {r.area}</span>}
                            </div>
                          </div>
                        </div>

                        {/* Middle Box: Owner & Subscription Info */}
                        <div className="bg-[#F8FAFC] rounded-xl p-3.5 border border-[#EEF1F6] flex flex-col gap-2.5">
                          {/* Subscription or Commission Details */}
                          <div className="flex items-center justify-between border-b border-[#E8ECF2] pb-2">
                            {isCommission ? (
                              <div className="flex items-center justify-between w-full">
                                <div className="flex items-center gap-2">
                                  <span className="text-xs text-[#5B6475]">نظام التسعير:</span>
                                  <span className="inline-flex items-center gap-1 font-extrabold text-purple-900 text-xs bg-purple-50 px-2.5 py-1 rounded-lg border border-purple-200">
                                    <BadgePercent className="size-3.5 text-purple-600" />
                                    نظام العمولة
                                  </span>
                                </div>
                                <span className="text-[11px] font-bold text-purple-700 bg-purple-100/60 px-2 py-0.5 rounded-md">
                                  معفى من الاشتراك
                                </span>
                              </div>
                            ) : (
                              <>
                                <div className="flex items-center gap-2">
                                  <span className="text-xs text-[#5B6475]">باقة الاشتراك:</span>
                                  <span className="inline-flex items-center gap-1 font-extrabold text-slate-900 text-xs bg-white px-2.5 py-1 rounded-lg border border-[#E2E6EF] shadow-2xs">
                                    <Package className="size-3.5 text-slate-500" />
                                    {planName}
                                  </span>
                                </div>
                                <Pill tone={subTone} className="text-[11px]">
                                  {subLabel}
                                </Pill>
                              </>
                            )}
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
                                <span dir="ltr" className="font-mono text-slate-800 bg-white px-2 py-0.5 rounded border border-slate-200 mt-0.5 inline-flex items-center gap-1 font-medium">
                                  <Smartphone className="size-3 text-slate-400" />
                                  {r.ownerPhone}
                                </span>
                              </div>
                            )}
                          </div>
                        </div>

                        {/* Details Row: Pitches & Creation Date */}
                        <div className="flex items-center justify-between text-xs px-1 text-slate-600">
                          <span className="flex items-center gap-1.5 font-bold text-slate-800 bg-emerald-50 px-2.5 py-1 rounded-lg border border-emerald-200">
                            <SoccerBall className="size-3.5 text-emerald-700" strokeWidth={1.75} />
                            <span>الملاعب الفرعية:</span>
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
                            معاينة وإدارة الملعب<ChevronLeft className="size-3.5 inline-block align-[-3px] ms-1" />
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
                rows={items}
                rowKey={(r) => r.id}
                selected={selected}
                onSelect={(r) => setSelected(r.id)}
                columns={[
                  {
                    title: "الملعب والمكان",
                    cell: (r) => (
                      <div className="flex flex-col">
                        <strong className="text-slate-900 text-sm">{r.name}</strong>
                        <span className="text-xs text-[#5B6475] inline-flex items-center gap-1">
                          <MapPin className="size-3 shrink-0" />
                          {r.city}، {r.area}
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
                          <span dir="ltr" className="text-xs text-[#5B6475] font-mono inline-flex items-center gap-1">
                            <Smartphone className="size-3 text-slate-400" />
                            {r.ownerPhone}
                          </span>
                        )}
                      </div>
                    ),
                  },
                  {
                    title: "نظام التسعير والاشتراك",
                    cell: (r) => {
                      if (r.pricingModel === "Commission") {
                        return (
                          <div className="flex items-center gap-1.5">
                            <span className="inline-flex items-center gap-1 font-bold text-purple-900 text-xs bg-purple-50 border border-purple-200 px-2 py-0.5 rounded">
                              <BadgePercent className="size-3 text-purple-600" />
                              نظام عمولة
                            </span>
                            <span className="text-[10px] text-purple-700 bg-purple-100/50 px-1.5 py-0.5 rounded">معفى</span>
                          </div>
                        );
                      }
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
                      <span className="inline-flex items-center gap-1 font-bold text-emerald-800 bg-emerald-50 px-2.5 py-1 rounded-lg border border-emerald-200 text-xs">
                        <SoccerBall className="size-3.5" strokeWidth={1.75} />
                        {num(r.pitches)}
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
                      <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${r.isPublished ? "text-emerald-700" : "text-slate-500"}`}>
                        {r.isPublished ? <span className="size-1.5 rounded-full bg-emerald-500" /> : <span className="size-1.5 rounded-full bg-slate-400" />}
                        {r.isPublished ? "منشور" : "مسودة"}
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
                        معاينة<ChevronLeft className="size-3.5 inline-block align-[-3px] ms-1" />
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
  const [gallery, setGallery] = useState<{ photos: string[]; index: number; title: string } | null>(null);
  const [pricingModel, setPricingModel] = useState<"0" | "1">("0");
  const [commissionEnabled, setCommissionEnabled] = useState(false);
  const [commissionType, setCommissionType] = useState<"0" | "1" | "2" | "3">("0");
  const [commissionRate, setCommissionRate] = useState("");

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

  const commission = useQuery({
    queryKey: ["admin-venue-commission", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/{venueId}/commission", { params: { path: { venueId: venueId! } } })),
  });

  useEffect(() => {
    if (!commission.data) return;
    const isComm = commission.data.pricingModel === "Commission" || (commission.data as any).pricingModel === 1;
    setPricingModel(isComm ? "1" : "0");
    setCommissionEnabled(commission.data.customCommissionEnabled);
    setCommissionType(({ PlatformDefault: "0", PerHourFixed: "1", Percentage: "2", Exempt: "3" } as Record<string, "0" | "1" | "2" | "3">)[commission.data.commissionType] ?? "0");
    setCommissionRate(commission.data.commissionRate === null || commission.data.commissionRate === undefined ? "" : String(commission.data.commissionRate));
  }, [commission.data]);

  const saveCommission = useMutation({
    mutationFn: () =>
      unwrap(
        api.PATCH("/api/v1/admin/venues/{venueId}/commission", {
          params: { path: { venueId: venueId! } },
          body: {
            pricingModel: Number(pricingModel) as 0 | 1,
            customCommissionEnabled: pricingModel === "1",
            commissionType: pricingModel === "1" ? (Number(commissionType) as 0 | 1 | 2 | 3) : 3,
            commissionRate: pricingModel === "1" && commissionRate.trim() ? Number(commissionRate) : null,
          },
        }),
      ),
    onSuccess: async () => {
      notice.ok("اتحفظ نظام التشغيل وإعدادات الملعب بنجاح.");
      await queryClient.invalidateQueries({ queryKey: ["admin-venue-commission", venueId] });
      await queryClient.invalidateQueries({ queryKey: ["admin-venue", venueId] });
      await queryClient.invalidateQueries({ queryKey: ["admin-venues"] });
    },
    onError: notice.fail,
  });

  const togglePublish = useMutation({
    mutationFn: (isPublished: boolean) =>
      unwrap(
        (api as any).POST("/api/v1/admin/venues/{venueId}/publish", {
          params: { path: { venueId: venueId! } },
          body: { isPublished },
        }),
      ),
    onSuccess: async (_, isPublished) => {
      notice.ok(isPublished ? "تم نشر الملعب بنجاح وهيظهر في التطبيق الآن." : "تم إلغاء نشر الملعب وتحويله لمسودة.");
      await queryClient.invalidateQueries({ queryKey: ["admin-venue", venueId] });
      await queryClient.invalidateQueries({ queryKey: ["admin-venues"] });
    },
    onError: notice.fail,
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
                    <span className="text-xs text-[#5B6475] flex items-center gap-1"><MapPin className="size-3.5 shrink-0" /> {v.city}، {v.area} · {v.address}</span>
                  </div>
                  <Pill tone={TONE[v.approvalStatus] ?? "neutral"}>
                    {STATUS_MAP[v.approvalStatus] ?? v.approvalStatus}
                  </Pill>
                </div>

                <div className="grid grid-cols-2 gap-2 text-xs pt-2 border-t border-slate-100">
                  <KV k="نظام العمل والتسعير">
                    {v.pricingModel === "Commission" ? (
                      <span className="inline-flex items-center gap-1 font-bold text-purple-900 text-xs bg-purple-50 px-2 py-0.5 rounded border border-purple-200">
                        <BadgePercent className="size-3 text-purple-600" />
                        نظام العمولة (معفى من الاشتراك)
                      </span>
                    ) : (
                      <div className="flex items-center gap-1">
                        <span className="font-bold text-slate-900 bg-slate-100 px-2 py-0.5 rounded me-1">{planName}</span>
                        <Pill tone={subTone} className="text-[10px]">{subLabel}</Pill>
                      </div>
                    )}
                  </KV>
                  <KV k="صاحب الملعب">{v.ownerName ?? "مالك مسجل"}</KV>
                  <KV k="هاتف الملعب/التواصل"><span dir="ltr">{v.phoneNumber || v.ownerPhone || "—"}</span></KV>
                  <KV k="حالة النشر">
                    <span className="inline-flex items-center gap-1.5">
                      {v.isPublished ? <span className="size-1.5 rounded-full bg-emerald-500" /> : <span className="size-1.5 rounded-full bg-slate-400" />}
                      {v.isPublished ? "منشور للجمهور" : "مسودة"}
                    </span>
                  </KV>
                  <KV k="عدد الموظفين">{num(v.staffCount)}</KV>
                  <KV k="تاريخ التسجيل">{fmtDate(v.createdAtUtc)}</KV>
                </div>
              </div>

              <Section title="نظام التشغيل والتسعير (Pricing & Commercial Model)">
                {commission.isLoading ? (
                  <span className="text-xs text-slate-500">جاري تحميل الإعدادات…</span>
                ) : commission.isError ? (
                  <span className="text-xs text-red-700">تعذّر تحميل إعدادات النموذج التجاري والعمولة.</span>
                ) : (
                  <div className="flex flex-col gap-4">
                    {/* Model Selector Cards */}
                    <div>
                      <span className="text-xs font-bold text-slate-700 block mb-2">اختر نظام العمل المعتمد للملعب:</span>
                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                        <label
                          className={`flex flex-col gap-1.5 p-3.5 rounded-xl border-2 cursor-pointer transition-all ${
                            pricingModel === "0"
                              ? "border-emerald-600 bg-emerald-50/50 text-slate-900 shadow-2xs"
                              : "border-slate-200 bg-white hover:border-slate-300 text-slate-600"
                          }`}
                        >
                          <div className="flex items-center justify-between">
                            <span className="font-extrabold text-sm flex items-center gap-1.5 text-slate-900">
                              <Package className="size-4 text-emerald-600" />
                              نظام باقات الاشتراك
                            </span>
                            <input
                              type="radio"
                              name="pricingModel"
                              value="0"
                              checked={pricingModel === "0"}
                              onChange={() => setPricingModel("0")}
                              className="text-emerald-600 focus:ring-emerald-500 size-4"
                            />
                          </div>
                          <p className="text-[11px] text-slate-500 leading-relaxed mt-0.5">
                            يشترك مالك الملعب في باقات ملعبك الدورية، ويحدد أسعاره وطرق الدفع بنفسه وتظهر لديه تبويبة الاشتراكات والفواتير.
                          </p>
                        </label>

                        <label
                          className={`flex flex-col gap-1.5 p-3.5 rounded-xl border-2 cursor-pointer transition-all ${
                            pricingModel === "1"
                              ? "border-purple-600 bg-purple-50/50 text-slate-900 shadow-2xs"
                              : "border-slate-200 bg-white hover:border-slate-300 text-slate-600"
                          }`}
                        >
                          <div className="flex items-center justify-between">
                            <span className="font-extrabold text-sm flex items-center gap-1.5 text-slate-900">
                              <BadgePercent className="size-4 text-purple-600" />
                              نظام العمولة
                            </span>
                            <input
                              type="radio"
                              name="pricingModel"
                              value="1"
                              checked={pricingModel === "1"}
                              onChange={() => setPricingModel("1")}
                              className="text-purple-600 focus:ring-purple-500 size-4"
                            />
                          </div>
                          <p className="text-[11px] text-slate-500 leading-relaxed mt-0.5">
                            معفى من باقات الاشتراك (تختفي صفحة الاشتراكات من لوحة تحكمه). تخصم المنصة العمولة المحددة أدناه على الحجوزات.
                          </p>
                        </label>
                      </div>
                    </div>

                    {/* Model Details / Commission Config */}
                    {pricingModel === "1" ? (
                      <div className="bg-purple-50/60 rounded-xl p-3.5 border border-purple-200 flex flex-col gap-3">
                        <div className="flex items-center gap-1.5 text-xs font-bold text-purple-900">
                          <BadgePercent className="size-3.5 text-purple-600" />
                          تحديد قيمة عمولة المنصة لهذا الملعب:
                        </div>
                        <div className="grid gap-3 sm:grid-cols-2">
                          <Field label="طريقة حساب العمولة">
                            <select
                              value={commissionType}
                              onChange={(e) => setCommissionType(e.target.value as "0" | "1" | "2" | "3")}
                              className="w-full min-h-[40px] rounded-xl border border-slate-200 bg-white px-3 text-sm"
                            >
                              <option value="1">مبلغ ثابت لكل ساعة حجز (ج.م)</option>
                              <option value="2">نسبة مئوية من إجمالي الحجز (%)</option>
                              <option value="0">عمولة المنصة الافتراضية</option>
                              <option value="3">معفي من العمولة (0%)</option>
                            </select>
                          </Field>
                          {(commissionType === "1" || commissionType === "2") && (
                            <Field label={commissionType === "2" ? "النسبة المئوية (%)" : "المبلغ لكل ساعة (ج.م)"}>
                              <TextInput
                                type="number"
                                min="0"
                                step="0.01"
                                value={commissionRate}
                                onChange={(e) => setCommissionRate(e.target.value)}
                                placeholder={commissionType === "2" ? "مثلاً 10%" : "مثلاً 30 ج.م"}
                              />
                            </Field>
                          )}
                        </div>
                      </div>
                    ) : (
                      <div className="p-4 rounded-xl bg-emerald-50/70 border border-emerald-200/80 text-xs text-emerald-950 flex items-start gap-3">
                        <Check className="size-4 text-emerald-600 shrink-0 mt-0.5" />
                        <div className="flex flex-col gap-1">
                          <span className="font-bold text-emerald-900 text-sm">
                            الملعب معتمد بنظام باقات الاشتراك فقط
                          </span>
                          <span className="text-emerald-800 leading-relaxed">
                            يسدد صاحب الملعب رسوم باقة الاشتراك الدورية (شهرياً أو سنوياً)، ولا يتم خصم أي عمولة منصة على حجوزاته. وتظهر له تبويبة الاشتراكات والفواتير في لوحة تحكمه.
                          </span>
                        </div>
                      </div>
                    )}

                    <button
                      type="button"
                      className={`${btnSmall} self-start`}
                      disabled={saveCommission.isPending || !commission.data}
                      onClick={() => saveCommission.mutate()}
                    >
                      {saveCommission.isPending ? "جاري الحفظ…" : "حفظ نظام التشغيل والعمولة"}
                    </button>
                  </div>
                )}
              </Section>

              {/* Action Buttons */}
              <div className="flex flex-wrap gap-2">
                {v.approvalStatus === "Approved" && !v.isPublished && (
                  <button
                    type="button"
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold bg-emerald-600 text-white hover:bg-emerald-700 shadow-sm transition"
                    onClick={() => togglePublish.mutate(true)}
                    disabled={togglePublish.isPending}
                  >
                    {togglePublish.isPending ? "جاري النشر…" : "نشر الملعب للجمهور (تفعيل الظهور)"}
                  </button>
                )}
                {v.approvalStatus === "Approved" && v.isPublished && (
                  <button
                    type="button"
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold bg-slate-700 text-white hover:bg-slate-800 shadow-sm transition"
                    onClick={() => togglePublish.mutate(false)}
                    disabled={togglePublish.isPending}
                  >
                    {togglePublish.isPending ? "جاري التحديث…" : "إلغاء النشر (تحويل لمسودة)"}
                  </button>
                )}
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

              {/* Club Photo Gallery */}
              <Section title={`معرض صور النادي (${num((v.photos ?? []).length)})`}>
                {(v.photos ?? []).length === 0 ? (
                  <div className="p-4 rounded-xl bg-slate-50 border border-slate-200 text-center text-xs text-slate-500">
                    لم يقم النادي برفع صور للمنشأة حتى الآن.
                  </div>
                ) : (
                    <div className="grid grid-cols-2 sm:grid-cols-3 gap-2.5">
                    {v.photos!.map((photo, i) => (
                      <div
                        key={photo}
                        className="relative aspect-video rounded-xl overflow-hidden border border-slate-200 group cursor-pointer bg-slate-100 hover:ring-2 hover:ring-emerald-500 transition-all shadow-2xs"
                        onClick={() =>
                          setGallery({
                            photos: v.photos!.map((p) => mediaSrc(p)),
                            index: i,
                            title: `معرض صور منشأة ${v.name}`,
                          })
                        }
                      >
                        <img
                          src={mediaSrc(photo)}
                          alt={`صورة ${v.name}`}
                          className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-200"
                        />
                        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center text-white text-xs font-semibold gap-1">
                          <Eye className="size-4" />
                          <span>تكبير</span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Section>

              {/* Pitches List & Their Photos */}
              <Section title={`الملاعب الفرعية (${num(v.pitches.length)})`}>
                <div className="flex flex-col gap-3">
                  {v.pitches.map((p) => {
                    const pitchPhotos = p.photos ?? [];
                    return (
                      <div key={p.id} className="rounded-xl border border-slate-200 bg-slate-50 p-3.5 flex flex-col gap-3">
                        {/* Pitch Header */}
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2.5">
                            <span className="grid place-items-center size-9 rounded-xl bg-emerald-100 text-emerald-800 shrink-0">
                              <SoccerBall className="size-5" strokeWidth={1.75} />
                            </span>
                            <div>
                              <strong className="text-slate-900 text-sm block">{p.name}</strong>
                              <span className="text-xs text-slate-500 flex items-center gap-1.5 mt-0.5">
                                <span className="font-semibold text-slate-700">{TURF_LABELS[p.turfType] ?? p.turfType}</span>
                                <span>·</span>
                                <span>السعة: {num(p.capacity)} لاعب</span>
                              </span>
                            </div>
                          </div>
                          <Pill tone={p.isActive ? "ok" : "neutral"}>{p.isActive ? "شغال" : "معطّل"}</Pill>
                        </div>

                        {/* Pitch Photos */}
                        {pitchPhotos.length > 0 ? (
                          <div className="flex flex-col gap-1.5 pt-2 border-t border-slate-200/80">
                            <span className="text-[11px] font-bold text-slate-600 flex items-center gap-1">
                              <Camera className="size-3 text-slate-400" />
                              صور الملعب ({toArabicDigits(pitchPhotos.length)} صور):
                            </span>
                            <div className="grid grid-cols-3 sm:grid-cols-4 gap-2">
                              {pitchPhotos.map((photo, i) => (
                                <div
                                  key={photo}
                                  className="relative aspect-video rounded-lg overflow-hidden border border-slate-200 group cursor-pointer bg-white hover:ring-2 hover:ring-emerald-500 transition-all shadow-2xs"
                                  onClick={() =>
                                    setGallery({
                                      photos: pitchPhotos.map((p) => mediaSrc(p)),
                                      index: i,
                                      title: `صور ${p.name}`,
                                    })
                                  }
                                >
                                  <img
                                    src={mediaSrc(photo)}
                                    alt={`صورة ${p.name}`}
                                    className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-200"
                                  />
                                  <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center text-white text-[11px] font-semibold gap-0.5">
                                    <Eye className="size-3.5" />
                                  </div>
                                </div>
                              ))}
                            </div>
                          </div>
                        ) : (
                          <div className="text-[11px] text-slate-400 italic pt-1 border-t border-slate-200/60">
                            لا توجد صور مرفوعة لهذا الملعب الفرعي
                          </div>
                        )}
                      </div>
                    );
                  })}
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

      <ImageLightbox
        photos={gallery?.photos}
        index={gallery?.index}
        onIndexChange={(newIdx) => setGallery((prev) => (prev ? { ...prev, index: newIdx } : null))}
        title={gallery?.title}
        onClose={() => setGallery(null)}
      />
    </Drawer>
  );
}
