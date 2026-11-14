"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, TONE, btnOutline, card, muted, toArabicDigits, useRevealOnSelect, EmptyState } from "@/components/ui";
import { api, unwrap } from "@/lib/api";
import { Eye } from "lucide-react";
import { ImageLightbox, type LightboxPhoto } from "@/components/ImageLightbox";

type PendingPitch = {
  id?: string;
  name: string;
  turfType?: string;
  capacity?: number;
  lengthMeters?: number | null;
  widthMeters?: number | null;
  photos?: string[];
};

type Pending = Schemas["PendingVenueApprovalDto"] & {
  pitches?: PendingPitch[];
};
type Decision = "reject" | "info";

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

const ACTIONS: Record<Decision, { title: string; placeholder: string; cta: string; btn: string }> = {
  reject: {
    title: "سبب الرفض (مطلوب)",
    placeholder: "المكان مش مطابق للصور المرفوعة…",
    cta: "تأكيد الرفض",
    btn: "bg-[#A8261C] text-white",
  },
  info: {
    title: "المطلوب من المالك",
    placeholder: "محتاجين صورة واضحة من عقد الإيجار…",
    cta: "إرسال الطلب",
    btn: "bg-navy-900 text-white",
  },
};

function since(iso?: string | null) {
  if (!iso) return "";
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
  return days <= 0 ? "قدّم النهارده" : days === 1 ? "قدّم امبارح" : `قدّم من ${toArabicDigits(days)} أيام`;
}

export default function ApprovalsPage() {
  const queryClient = useQueryClient();
  const [selId, setSelId] = useState<string | null>(null);
  const detailsRef = useRevealOnSelect<HTMLElement>(selId);
  const [action, setAction] = useState<Decision | null>(null);
  const [message, setMessage] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [facilityNames, setFacilityNames] = useState<Record<string, string>>({});
  const [gallery, setGallery] = useState<{ photos: string[]; index: number; title: string } | null>(null);

  const pending = useQuery({
    queryKey: ["approvals"],
    queryFn: () => unwrap(api.GET("/api/v1/admin/venues/pending-approval", { params: { query: { page: 1, pageSize: 50 } } })),
  });
  useQuery({
    queryKey: ["reference", "facilities"],
    staleTime: 86_400_000,
    queryFn: async () => {
      const list = await unwrap(api.GET("/api/v1/reference/facilities"));
      setFacilityNames(Object.fromEntries(list.map((f) => [f.code, f.nameAr])));
      return list;
    },
  });

  const list = pending.data ?? [];
  const cur: Pending | undefined = list.find((v) => v.venueId === selId) ?? list[0];

  const decide = useMutation({
    mutationFn: async ({ kind, note }: { kind: "approve" | Decision; note: string | null }) => {
      const params = { params: { path: { venueId: cur!.venueId } }, body: { message: note } };
      if (kind === "approve") return unwrap(api.POST("/api/v1/admin/venues/{venueId}/approve", params));
      if (kind === "reject") return unwrap(api.POST("/api/v1/admin/venues/{venueId}/reject", params));
      return unwrap(api.POST("/api/v1/admin/venues/{venueId}/request-info", params));
    },
    onSuccess: async (_, { kind }) => {
      const name = cur?.name;
      setAction(null);
      setMessage("");
      setSelId(null);
      await queryClient.invalidateQueries({ queryKey: ["approvals"] });
      setNotice(
        kind === "approve"
          ? `تمت الموافقة على ${name}. المالك وصله إشعار يكمّل الإعداد وينشر.`
          : kind === "reject"
            ? `اترفض طلب ${name} والسبب اتبعت للمالك.`
            : `اتبعت طلب المعلومات لمالك ${name}.`,
      );
    },
    onError: (e: Error) => setNotice(e.message),
  });

  return (
    <Page>
      <PageHeader
        title="موافقات الملاعب"
        sub="الموافقة بتسمح للمالك ينشر ملعبه. الظهور للاعبين بيتحكم فيه المالك بعد إكمال المواعيد والأسعار."
      />

      {notice && (
        <div role="status" className="bg-[#EEF0F5] rounded-xl px-4 py-3 text-sm font-semibold text-[#1F2A44]">
          {notice}
        </div>
      )}

      {pending.isLoading && <div className={`${card} rounded-[18px] h-40 animate-pulse`} />}
      {!pending.isLoading && list.length === 0 && (
        <EmptyState
          icon="check"
          badge="كل الملاعب مراجعة"
          title="لا توجد طلبات اعتماد معلقة حالياً"
          description="رائع جداً! تمت مراجعة واعتماد جميع طلبات تسجيل الملاعب. عند قيام أي مالك جديد بتسجيل ملعبه، سيظهر طلبه هنا فوراً للمراجعة والتدقيق."
          action={{
            label: "استعراض جميع الملاعب",
            href: "/venues",
            variant: "primary",
          }}
          secondaryAction={{
            label: "تحديث القائمة",
            onClick: () => pending.refetch(),
          }}
          hint="نصيحة: يمكنك متابعة الملاعب المفعلة وتعديل إعداداتها وحالة اشتراكاتها من صفحة الملاعب."
        />
      )}

      {cur && (
        <div className="flex flex-wrap gap-4 items-start">
          <section aria-label="طلبات التسجيل" className={`flex-[1_1_300px] min-w-0 ${card} rounded-[18px] overflow-hidden`}>
            {list.map((v) => {
              const on = v.venueId === cur.venueId;
              return (
                <button
                  key={v.venueId}
                  type="button"
                  aria-pressed={on}
                  onClick={() => {
                    setSelId(v.venueId);
                    setAction(null);
                    setNotice(null);
                  }}
                  className={`w-full min-h-[68px] px-4 py-2.5 border-b border-[#EEF0F5] flex items-center gap-3 ${
                    on ? "bg-[#EEF0F5] shadow-[inset_-4px_0_0_#D63B2F]" : "bg-white"
                  }`}
                >
                  <span className="flex-1 min-w-0 flex flex-col gap-0.5 text-right">
                    <span className="text-sm font-bold text-slate-900">{v.name}</span>
                    <span className={`text-xs ${muted}`}>
                      {v.area} · {since(v.submittedAtUtc)}
                    </span>
                  </span>
                  <span className={`shrink-0 text-[11px] font-bold rounded-full px-2 py-[3px] ${TONE.warn}`}>جديد</span>
                </button>
              );
            })}
          </section>

          <section ref={detailsRef} className={`scroll-mt-20 flex-[999_1_560px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-[18px]`}>
            <div className="flex flex-wrap justify-between gap-2 items-center">
              <div className="flex flex-col gap-0.5">
                <h2 className="text-[21px] font-bold">{cur.name}</h2>
                <span className={`text-[13px] ${muted}`}>
                  {cur.city} · {cur.area} · {since(cur.submittedAtUtc)}
                </span>
              </div>
              <span className={`text-xs font-bold rounded-full px-3 py-1 ${TONE.warn}`}>مستني المراجعة</span>
            </div>

            <div className="grid gap-x-5 gap-y-3 grid-cols-[repeat(auto-fit,minmax(min(180px,100%),1fr))] text-sm">
              {[
                ["المالك", cur.ownerName ?? "—", "font-semibold"],
                ["العنوان", cur.address, ""],
                ["رقم التواصل", cur.phoneNumber, ""],
                ["الملاعب", `${toArabicDigits(cur.pitchCount)} ملاعب`, ""],
              ].map(([k, v, cls]) => (
                <div key={k} className="flex flex-col gap-0.5">
                  <span className={`text-xs ${muted}`}>{k}</span>
                  <span className={cls} dir={k === "رقم التواصل" ? "ltr" : undefined}>
                    {v}
                  </span>
                </div>
              ))}
            </div>

            {cur.description && <p className="text-sm leading-[1.7] text-[#333B4A]">{cur.description}</p>}

            {(cur.facilities?.length ?? 0) > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {cur.facilities!.map((f) => (
                  <span key={f} className="text-xs font-semibold bg-[#EEF0F5] text-[#3B4352] rounded-full px-2.5 py-1">
                    {facilityNames[f] ?? f}
                  </span>
                ))}
              </div>
            )}

            {/* صور المنشأة العامة */}
            <div className="flex flex-col gap-2 pt-2 border-t border-[#EEF0F5]">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-bold text-slate-800">صور المكان / المنشأة العامة</h3>
                <span className={`text-xs ${muted}`}>({toArabicDigits((cur.photos ?? []).length)} صور)</span>
              </div>
              {(cur.photos ?? []).length === 0 ? (
                <span className={`text-xs ${muted}`}>المالك مارفعش صور عامة للمنشأة.</span>
              ) : (
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
                  {cur.photos!.map((photo, pIdx) => (
                    <div
                      key={photo}
                      className="relative group rounded-[10px] overflow-hidden aspect-[4/3] bg-slate-100 border border-[#E4E7EC] cursor-pointer hover:ring-2 hover:ring-navy-900 transition-all shadow-2xs"
                      onClick={() =>
                        setGallery({
                          photos: (cur.photos ?? []).map((p) => mediaSrc(p)),
                          index: pIdx,
                          title: `صور منشأة ${cur.name}`,
                        })
                      }
                    >
                      {/* eslint-disable-next-line @next/next/no-img-element -- re-encoded WebP from our media API */}
                      <img
                        src={mediaSrc(photo)}
                        alt={`صورة ${cur.name}`}
                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-200"
                      />
                      <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center text-white text-xs font-semibold gap-1 pointer-events-none">
                        <Eye className="size-4" />
                        <span>معاينة</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* ملاعب المكان وتفاصيلها وصور كل ملعب */}
            {cur.pitches && cur.pitches.length > 0 && (
              <div className="flex flex-col gap-3 pt-3 border-t border-[#EEF0F5]">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-bold text-slate-800">ملاعب المكان وتفاصيلها</h3>
                  <span className={`text-xs ${muted}`}>({toArabicDigits(cur.pitches.length)} ملاعب)</span>
                </div>
                <div className="flex flex-col gap-3">
                  {cur.pitches.map((pitch, idx) => (
                    <div key={pitch.id ?? idx} className="rounded-xl border border-[#E4E7EC] bg-[#F8F9FB] p-3 flex flex-col gap-2.5">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="text-sm font-bold text-slate-900">{pitch.name}</span>
                        <div className="flex flex-wrap gap-1.5 text-xs">
                          {pitch.turfType && (
                            <span className="bg-white border border-[#DCE0E8] rounded-full px-2.5 py-0.5 text-slate-700 font-medium">
                              {TURF_LABELS[pitch.turfType] ?? pitch.turfType}
                            </span>
                          )}
                          {typeof pitch.capacity === "number" && (
                            <span className="bg-white border border-[#DCE0E8] rounded-full px-2.5 py-0.5 text-slate-700 font-medium">
                              {toArabicDigits(pitch.capacity)} ضد {toArabicDigits(pitch.capacity)}
                            </span>
                          )}
                          {pitch.lengthMeters && pitch.widthMeters && (
                            <span className="bg-white border border-[#DCE0E8] rounded-full px-2.5 py-0.5 text-slate-700 font-medium">
                              {toArabicDigits(pitch.lengthMeters)} × {toArabicDigits(pitch.widthMeters)} م
                            </span>
                          )}
                        </div>
                      </div>

                      {/* صور الملعب ده */}
                      {(pitch.photos ?? []).length === 0 ? (
                        <span className={`text-xs ${muted}`}>المالك مارفعش صور للملعب ده لسه.</span>
                      ) : (
                        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
                          {pitch.photos!.map((photo, pIdx) => (
                            <div
                              key={photo}
                              className="relative group rounded-[10px] overflow-hidden aspect-[4/3] bg-white border border-[#DCE0E8] cursor-pointer hover:ring-2 hover:ring-navy-900 transition-all shadow-2xs"
                              onClick={() =>
                                setGallery({
                                  photos: (pitch.photos ?? []).map((p) => mediaSrc(p)),
                                  index: pIdx,
                                  title: `صور ${pitch.name}`,
                                })
                              }
                            >
                              {/* eslint-disable-next-line @next/next/no-img-element -- re-encoded WebP from our media API */}
                              <img
                                src={mediaSrc(photo)}
                                alt={`صورة ${pitch.name}`}
                                className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-200"
                              />
                              <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center text-white text-xs font-semibold gap-1 pointer-events-none">
                                <Eye className="size-4" />
                                <span>معاينة</span>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {cur.submissionMessage && (
              <div className="bg-[#F8F9FB] rounded-[10px] px-3.5 py-2.5 text-sm">
                <span className={`text-xs ${muted}`}>رسالة المالك: </span>
                {cur.submissionMessage}
              </div>
            )}

            {!action && (
              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  disabled={decide.isPending}
                  onClick={() => decide.mutate({ kind: "approve", note: null })}
                  className="min-h-[46px] px-[22px] rounded-[10px] text-sm font-bold bg-ink text-white disabled:opacity-60"
                >
                  موافقة
                </button>
                <button type="button" onClick={() => setAction("info")} className={`${btnOutline} !min-h-[46px] !rounded-[10px]`}>
                  طلب معلومات إضافية
                </button>
                <button
                  type="button"
                  onClick={() => setAction("reject")}
                  className="min-h-[46px] px-[18px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-bold"
                >
                  رفض
                </button>
              </div>
            )}

            {action && (
              <div className="border border-[#E4E7EC] rounded-xl p-3.5 flex flex-col gap-2.5">
                <label htmlFor="note" className="text-sm font-bold">
                  {ACTIONS[action].title}
                </label>
                <textarea
                  id="note"
                  rows={3}
                  value={message}
                  placeholder={ACTIONS[action].placeholder}
                  onChange={(e) => setMessage(e.target.value)}
                  className="border border-[#C9CFD9] rounded-[10px] px-3 py-2.5 text-sm leading-[22px] resize-none text-slate-900"
                />
                <span className={`text-xs ${muted}`}>الرسالة هتوصل للمالك في اللوحة، والإجراء هيتسجل في سجل التدقيق باسمك.</span>
                <div className="flex gap-2">
                  <button
                    type="button"
                    disabled={message.trim().length < 5 || decide.isPending}
                    onClick={() => decide.mutate({ kind: action, note: message.trim() })}
                    className={`min-h-[44px] px-[18px] rounded-[10px] text-sm font-bold disabled:opacity-50 ${ACTIONS[action].btn}`}
                  >
                    {ACTIONS[action].cta}
                  </button>
                  <button type="button" onClick={() => setAction(null)} className={`${btnOutline} !rounded-[10px]`}>
                    إلغاء
                  </button>
                </div>
              </div>
            )}
          </section>
        </div>
      )}

      <ImageLightbox
        photos={gallery?.photos}
        index={gallery?.index}
        onIndexChange={(newIdx) => setGallery((prev) => (prev ? { ...prev, index: newIdx } : null))}
        title={gallery?.title}
        onClose={() => setGallery(null)}
      />
    </Page>
  );
}
