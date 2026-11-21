"use client";

import { useConfirm } from "@/components/ConfirmDialog";
import React, { useEffect, useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, Switch, Toast, cardShadow, chip, toArabicDigits, useRevealOnSelect } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { useReference } from "@/lib/reference";
import { APPROVAL_LABELS, CAPACITIES } from "@/lib/venue-format";
import { mediaIdOf, mediaSrc, uploadImage } from "@/lib/upload";
import { ImageLightbox } from "@/components/ImageLightbox";
import { Eye, ImagePlus, Loader2, Plus, X } from "lucide-react";

type Pitch = Schemas["PitchDto"];

const pitchPattern =
  "radial-gradient(closest-side circle at 50% 50%, transparent 0 32%, rgba(255,255,255,0.55) 32% 36%, transparent 36%), linear-gradient(90deg, transparent calc(50% - 1px), rgba(255,255,255,0.55) calc(50% - 1px) calc(50% + 1px), transparent calc(50% + 1px)), repeating-linear-gradient(90deg, #2E8B55 0 12.5%, #287D4C 12.5% 25%)";

const chipClass = (on: boolean) => chip(on, "min-h-[40px] px-3 rounded-[10px] text-[13px]");
const field = "min-h-[44px] px-3 rounded-xl border border-[#C9CFD9] text-sm text-slate-900 bg-white focus:outline-none focus:ring-2 focus:ring-navy-900";

export default function PitchesPage() {
  const queryClient = useQueryClient();
  const { venue, venues, venueId, selectVenue } = useVenue();
  const { surfaces, governorates } = useReference();
  const [tab, setTab] = useState<"pitches" | "venues">("pitches");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [draft, setDraft] = useState<Pitch | null>(null);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);
  const detailsRef = useRevealOnSelect<HTMLElement>(selectedId);
  const [toast, setToast] = useState<string | null>(null);
  const [branchOpen, setBranchOpen] = useState(false);
  const [branchForm, setBranchForm] = useState({ name: "", city: "", area: "", phoneNumber: "", address: "", description: "" });

  const branchGovernorate = governorates.find((g) => g.nameAr === branchForm.city);

  const pitches = useQuery({
    queryKey: ["pitches", venueId],
    enabled: !!venueId,
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/pitches", { params: { path: { venueId: venueId! } } })),
  });
  const list = pitches.data ?? [];
  const selected = list.find((p) => p.id === selectedId) ?? list[0] ?? null;

  useEffect(() => {
    setDraft(selected ? { ...selected } : null);
  }, [selected]);

  const refresh = () => Promise.all([pitches.refetch(), queryClient.invalidateQueries({ queryKey: ["venues"] })]);

  const add = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/pitches", {
          params: { path: { venueId: venueId! } },
          body: { name: `ملعب ${toArabicDigits(list.length + 1)}`, turfType: "ArtificialTurf", capacity: 5, photos: [] },
        }),
      ),
    onSuccess: async (pitch) => {
      await refresh();
      setSelectedId(pitch.id);
      setTab("pitches");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const toggleActive = useMutation({
    mutationFn: (p: Pitch) =>
      unwrap(api.PUT("/api/v1/venue-manager/pitches/{id}/active", { params: { path: { id: p.id } }, body: { isActive: !p.isActive } })),
    onSuccess: async (result) => {
      await refresh();
      const name = result.pitch.name;
      if (result.pitch.isActive) setToast(`${name} رجع شغال وظاهر للاعبين.`);
      else if (result.upcomingBookings > 0)
        setToast(`${name} اتوقف ومش هيظهر للحجز. لسه عليه ${toArabicDigits(result.upcomingBookings)} حجز جاي — راجعهم من صفحة الحجوزات.`);
      else setToast(`${name} اتوقف ومش هيظهر للحجز.`);
    },
    onError: (e: Error) => setToast(e.message),
  });

  const addPhoto = useMutation({
    mutationFn: async ({ pitchId, file }: { pitchId: string; file: File }) => {
      const mediaId = await uploadImage(file, 1);
      return unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/pitches/{pitchId}/gallery", { params: { path: { venueId: venueId!, pitchId } }, body: { mediaId } }),
      );
    },
    onSuccess: async () => {
      await refresh();
      setToast("الصورة اترفعت.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const removePhoto = useMutation({
    mutationFn: async ({ pitchId, photo }: { pitchId: string; photo: string }) => {
      const mediaId = mediaIdOf(photo);
      if (mediaId) {
        await unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/pitches/{pitchId}/gallery/{mediaId}", { params: { path: { venueId: venueId!, pitchId, mediaId } } }));
        return;
      }
      // A link to an image hosted elsewhere has no media id: take it out of the saved photo list instead.
      const p = list.find((x) => x.id === pitchId)!;
      await unwrap(
        api.PUT("/api/v1/venue-manager/pitches/{id}", {
          params: { path: { id: pitchId } },
          body: {
            name: p.name,
            turfType: p.turfType,
            capacity: p.capacity,
            lengthMeters: p.lengthMeters ?? null,
            widthMeters: p.widthMeters ?? null,
            photos: p.photos.filter((x) => x !== photo),
          },
        }),
      );
    },
    onSuccess: refresh,
    onError: (e: Error) => setToast(e.message),
  });

  const save = useMutation({
    mutationFn: (p: Pitch) =>
      unwrap(
        api.PUT("/api/v1/venue-manager/pitches/{id}", {
          params: { path: { id: p.id } },
          body: {
            name: p.name.trim(),
            turfType: p.turfType,
            capacity: p.capacity,
            lengthMeters: p.lengthMeters ?? null,
            widthMeters: p.widthMeters ?? null,
            photos: p.photos,
          },
        }),
      ),
    onSuccess: async (p) => {
      await refresh();
      setToast(`اتحفظت بيانات ${p.name}. التغيير بيظهر في التطبيق فوراً.`);
    },
    onError: (e: Error) => setToast(e.message),
  });

  const [confirm, confirmDialog] = useConfirm();
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/v1/venue-manager/pitches/{id}", { params: { path: { id } } })),
    onSuccess: async () => {
      setSelectedId(null);
      await refresh();
      setToast("الملعب اتمسح.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const publish = useMutation({
    mutationFn: ({ id, isPublished }: { id: string; isPublished: boolean }) =>
      unwrap(api.POST("/api/v1/venue-manager/venues/{id}/publish", { params: { path: { id } }, body: { isPublished } })),
    onSuccess: async (_, { isPublished }) => {
      await queryClient.invalidateQueries({ queryKey: ["venues"] });
      setToast(isPublished ? "الفرع بقى ظاهر للاعبين." : "الفرع اتخفى من التطبيق. الحجوزات الحالية زي ما هي.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const createBranch = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues", {
          body: {
            name: branchForm.name.trim(),
            city: branchForm.city,
            area: branchForm.area,
            phoneNumber: branchForm.phoneNumber.trim(),
            address: branchForm.address.trim(),
            description: branchForm.description.trim() || null,
            facilities: [],
            photos: [],
          },
        }),
      ),
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: ["venues"] });
      selectVenue(created.id);
      setBranchOpen(false);
      setBranchForm({ name: "", city: "", area: "", phoneNumber: "", address: "", description: "" });
      setToast("الفرع اتضاف. كمّل بياناته وابعتُه للمراجعة.");
    },
    onError: (e: Error) => setToast(e.message),
  });

  const branchValid =
    branchForm.name.trim().length >= 3 &&
    branchForm.city &&
    branchForm.area &&
    branchForm.phoneNumber.trim() &&
    branchForm.address.trim();

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold text-slate-900">الفروع والملاعب</h1>
        <span className="text-[13px] text-[#5B6475]">{toArabicDigits(list.length)} ملاعب في {venue?.name ?? "الفرع"}</span>
        <button
          type="button"
          onClick={() => (tab === "venues" ? setBranchOpen(true) : add.mutate())}
          disabled={tab === "pitches" ? !venueId || add.isPending : createBranch.isPending}
          className="btn-press min-h-[44px] px-[18px] rounded-xl text-sm font-bold transition bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white inline-flex items-center gap-2 shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)]"
        >
          <Plus className="size-4" strokeWidth={2.5} />
          {tab === "venues" ? "إضافة فرع" : "إضافة ملعب"}
        </button>
      </div>

      <div role="tablist" className="flex gap-1.5">
        {(
          [
            ["pitches", "الملاعب"],
            ["venues", "الفروع"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setTab(id)}
            className={`min-h-[42px] px-[18px] rounded-xl text-sm font-semibold border transition ${
              tab === id ? "bg-navy-900 text-white border-navy-900" : "bg-white text-slate-900 border-[#DCE0E8] hover:bg-slate-50"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === "venues" && (
        <div className="grid gap-4 grid-cols-[repeat(auto-fit,minmax(min(320px,100%),1fr))]">
          {venues.map((v) => {
            const approved = v.isApproved;
            const status = APPROVAL_LABELS[v.approvalStatus] ?? { label: v.approvalStatus, tone: "bg-[#EEF0F5] text-[#3B4352]" };
            return (
              <article key={v.id} className={`bg-white border border-[#EDF0F5] rounded-[20px] overflow-hidden flex flex-col ${cardShadow}`}>
                <div className="h-[120px]" style={{ background: pitchPattern }} />
                <div className="p-4 flex flex-col gap-2.5">
                  <div className="flex justify-between items-center gap-2">
                    <span className="text-[17px] font-bold">
                      {v.name} — {v.area}
                    </span>
                    <span className={`text-xs font-bold rounded-full px-2.5 py-[3px] whitespace-nowrap ${status.tone}`}>{status.label}</span>
                  </div>
                  <span className="text-[13px] text-[#5B6475]">
                    {toArabicDigits(v.pitches.length)} ملاعب · {v.address}
                  </span>
                  <div className="flex items-center gap-2.5 bg-surface rounded-xl px-3 py-2.5">
                    <span className="flex-1 flex flex-col">
                      <span className="text-sm font-semibold">ظاهر للاعبين</span>
                      <span className="text-xs text-[#5B6475]">
                        {approved ? (v.isPublished ? "اللاعبين شايفينه ويقدروا يحجزوا" : "مخفي — مش بيظهر في البحث") : "هيتاح النشر بعد الموافقة"}
                      </span>
                    </span>
                    <Switch
                      on={v.isPublished}
                      label="نشر الفرع"
                      disabled={!approved || publish.isPending}
                      onToggle={() => approved && publish.mutate({ id: v.id, isPublished: !v.isPublished })}
                    />
                  </div>
                  {v.approvalStatus !== "Approved" && v.approvalStatus !== "Submitted" && (
                    <Link href="/setup" className="text-sm font-semibold text-navy-900 underline">
                      كمّل البيانات وابعت للمراجعة
                    </Link>
                  )}
                </div>
              </article>
            );
          })}
        </div>
      )}

      {branchOpen && (
        <div className="fixed inset-0 z-50 bg-black/40 p-4 grid place-items-center" role="dialog" aria-modal="true" aria-labelledby="add-branch-title">
          <form
            onSubmit={(e) => {
              e.preventDefault();
              if (branchValid) createBranch.mutate();
            }}
            className="w-full max-w-2xl max-h-[90vh] overflow-y-auto rounded-[22px] bg-white p-5 sm:p-6 shadow-2xl flex flex-col gap-4"
          >
            <div className="flex items-center justify-between gap-3">
              <div>
                <h2 id="add-branch-title" className="text-xl font-bold text-slate-900">إضافة فرع جديد</h2>
                <p className="text-sm text-[#5B6475] mt-1">اكتب مكان الفرع، وبعدها أضف الملاعب والمواعيد من بياناته.</p>
              </div>
              <button type="button" onClick={() => setBranchOpen(false)} className="w-9 h-9 rounded-full border border-[#DCE0E8] text-slate-600 hover:bg-slate-50" aria-label="إغلاق">×</button>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3.5">
              <label className="flex flex-col gap-1.5 text-sm font-semibold sm:col-span-2">
                اسم الفرع <input required minLength={3} value={branchForm.name} onChange={(e) => setBranchForm({ ...branchForm, name: e.target.value })} className={field} placeholder="مثال: فرع مدينة نصر" />
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-semibold">
                المحافظة
                <select required value={branchForm.city} onChange={(e) => setBranchForm({ ...branchForm, city: e.target.value, area: "" })} className={field}>
                  <option value="">اختار المحافظة</option>
                  {governorates.map((g) => <option key={g.code} value={g.nameAr}>{g.nameAr}</option>)}
                </select>
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-semibold">
                المنطقة
                {branchGovernorate?.areas.length ? (
                  <select required value={branchForm.area} onChange={(e) => setBranchForm({ ...branchForm, area: e.target.value })} className={field}>
                    <option value="">اختار المنطقة</option>
                    {branchGovernorate.areas.map((a) => <option key={a.code} value={a.nameAr}>{a.nameAr}</option>)}
                  </select>
                ) : (
                  <input required value={branchForm.area} onChange={(e) => setBranchForm({ ...branchForm, area: e.target.value })} className={field} placeholder="اكتب المنطقة" />
                )}
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-semibold">
                رقم التواصل
                <input required type="tel" dir="ltr" value={branchForm.phoneNumber} onChange={(e) => setBranchForm({ ...branchForm, phoneNumber: e.target.value })} className={`${field} text-right`} />
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-semibold sm:col-span-2">
                العنوان بالتفصيل
                <input required value={branchForm.address} onChange={(e) => setBranchForm({ ...branchForm, address: e.target.value })} className={field} />
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-semibold sm:col-span-2">
                وصف مختصر (اختياري)
                <textarea rows={3} value={branchForm.description} onChange={(e) => setBranchForm({ ...branchForm, description: e.target.value })} className={`${field} py-2.5`} />
              </label>
            </div>

            <div className="flex flex-col-reverse sm:flex-row gap-2.5 sm:justify-end pt-1">
              <button type="button" onClick={() => setBranchOpen(false)} className="min-h-[44px] px-5 rounded-xl border border-[#C9CFD9] text-sm font-semibold">إلغاء</button>
              <button type="submit" disabled={!branchValid || createBranch.isPending} className="min-h-[44px] px-5 rounded-xl bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-sm font-bold">
                {createBranch.isPending ? "جاري الإضافة…" : "إضافة الفرع"}
              </button>
            </div>
          </form>
        </div>
      )}

      {tab === "pitches" && (
        <div className="flex flex-wrap gap-4 items-start">
          <section className="flex-[999_1_460px] min-w-0 flex flex-col gap-3">
            {pitches.isLoading && <div className="h-24 rounded-[18px] skeleton-shimmer" />}
            {!pitches.isLoading && list.length === 0 && (
              <div className="bg-white rounded-[18px] p-6 text-center text-sm text-[#5B6475] border border-[#EDF0F5]">مفيش ملاعب لسه. اضغط «إضافة ملعب».</div>
            )}
            {list.map((item) => {
              const isSelected = item.id === selected?.id;
              return (
                <article
                  key={item.id}
                  className={`bg-white rounded-[18px] px-4 py-3.5 flex flex-wrap sm:flex-nowrap items-center gap-3.5 ${cardShadow} transition-all duration-200 ease-out hover:-translate-y-0.5 hover:shadow-lg ${
                    isSelected ? "border-2 border-navy-900 shadow-md" : "border border-[#EDF0F5] hover:border-slate-300"
                  }`}
                >
                  <span className={`w-[72px] h-[72px] shrink-0 rounded-[14px] transition-opacity duration-200 ${item.isActive ? "" : "opacity-45"}`} style={{ background: pitchPattern }} />
                  <span className="flex-1 min-w-[150px] flex flex-col gap-[3px]">
                    <span className="text-base font-bold text-slate-900">{item.name}</span>
                    <span className="text-[13px] text-[#5B6475]">
                      {toArabicDigits(item.capacity)} لاعب · {item.surfaceArabicDisplayName}
                    </span>
                    <span className={`text-xs font-bold flex items-center gap-1.5 ${item.isActive ? "text-[#1B6E3E]" : "text-[#8A94AA]"}`}>
                      <span className={`w-2 h-2 rounded-full ${item.isActive ? "bg-[#1B6E3E]" : "bg-[#8A94AA]"}`} />
                      <span>{item.isActive ? "شغال" : "متوقف"}</span>
                    </span>
                  </span>
                  <span className="w-full sm:w-auto flex items-center justify-end gap-3.5 pt-3 sm:pt-0 border-t sm:border-t-0 border-[#EEF0F5]">
                    <Switch
                      label={item.isActive ? `إيقاف ${item.name}` : `تشغيل ${item.name}`}
                      isChecked={item.isActive}
                      isDisabled={toggleActive.isPending}
                      handleToggle={() => toggleActive.mutate(item)}
                    />
                    <button
                      type="button"
                      onClick={() => setSelectedId(item.id)}
                      className="min-h-[40px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white hover:bg-slate-50 active:scale-95 text-navy-900 text-[13px] font-semibold transition-all cursor-pointer"
                    >
                      تعديل
                    </button>
                  </span>
                </article>
              );
            })}
          </section>

          {draft && (
            <aside
              ref={detailsRef}
              aria-label="تعديل الملعب"
              className={`flex-[1_1_340px] min-w-0 bg-white border border-[#EDF0F5] rounded-[20px] p-5 flex flex-col gap-3.5 ${cardShadow}`}
            >
              <h2 className="text-lg font-bold">تعديل {selected?.name}</h2>

              <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
                اسم الملعب
                <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={field} />
              </label>

              <div className="flex flex-col gap-1.5">
                <span className="text-[13px] font-semibold">لاعب في كل فريق</span>
                <div className="flex flex-wrap gap-1.5">
                  {CAPACITIES.map((c) => (
                    <button
                      key={c}
                      type="button"
                      aria-pressed={draft.capacity === c}
                      onClick={() => setDraft({ ...draft, capacity: c })}
                      className={chipClass(draft.capacity === c)}
                    >
                      {toArabicDigits(c)}
                    </button>
                  ))}
                </div>
              </div>

              <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
                الأرضية
                <select value={draft.turfType} onChange={(e) => setDraft({ ...draft, turfType: e.target.value })} className={field}>
                  {surfaces.map((s) => (
                    <option key={s.code} value={s.code}>
                      {s.nameAr}
                    </option>
                  ))}
                </select>
              </label>

              <div className="grid grid-cols-2 gap-2.5">
                <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
                  الطول (متر)
                  <input
                    type="number"
                    min={10}
                    value={draft.lengthMeters ?? ""}
                    onChange={(e) => setDraft({ ...draft, lengthMeters: e.target.value ? Number(e.target.value) : null })}
                    className={field}
                  />
                </label>
                <label className="flex flex-col gap-1.5 text-[13px] font-semibold">
                  العرض (متر)
                  <input
                    type="number"
                    min={5}
                    value={draft.widthMeters ?? ""}
                    onChange={(e) => setDraft({ ...draft, widthMeters: e.target.value ? Number(e.target.value) : null })}
                    className={field}
                  />
                </label>
              </div>

              <div className="flex flex-col gap-1.5">
                <span className="text-[13px] font-semibold">صور الملعب</span>
                <div className="grid grid-cols-3 gap-2">
                  {(selected?.photos ?? []).map((photo, idx) => {
                    return (
                      <div
                        key={photo}
                        className="relative group rounded-xl overflow-hidden aspect-[4/3] bg-slate-100 border border-slate-200 cursor-pointer shadow-2xs hover:ring-2 hover:ring-emerald-500 transition-all"
                        onClick={() => setLightboxIndex(idx)}
                      >
                        {/* eslint-disable-next-line @next/next/no-img-element -- media from our API, re-encoded on upload */}
                        <img
                          src={mediaSrc(photo)}
                          alt={`صورة ${selected?.name} ${toArabicDigits(idx + 1)}`}
                          className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-200"
                        />
                        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center text-white text-xs font-semibold gap-1 pointer-events-none">
                          <Eye className="size-4" />
                          <span>معاينة</span>
                        </div>
                        <button
                          type="button"
                          aria-label="مسح الصورة"
                          onClick={(e) => {
                            e.stopPropagation();
                            if (selected) removePhoto.mutate({ pitchId: selected.id, photo });
                          }}
                          className="absolute top-1 left-1 w-6 h-6 rounded-full bg-white/90 hover:bg-[#A8261C] text-[#A8261C] hover:text-white text-xs font-bold grid place-items-center transition-colors shadow z-10"
                        >
                          <X className="size-3.5" strokeWidth={2.5} />
                        </button>
                      </div>
                    );
                  })}
                  <label className="h-20 rounded-xl border-2 border-dashed border-[#C9CFD9] flex flex-col items-center justify-center gap-1 text-xs text-[#5B6475] cursor-pointer hover:bg-slate-50 hover:border-slate-400 transition-colors">
                    {addPhoto.isPending ? <Loader2 className="size-5 animate-spin" /> : <ImagePlus className="size-5" strokeWidth={1.75} />}
                    {addPhoto.isPending ? "بيترفع…" : "صورة"}
                    <input
                      type="file"
                      accept="image/jpeg,image/png,image/webp"
                      className="sr-only"
                      disabled={addPhoto.isPending}
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        if (file && selected) addPhoto.mutate({ pitchId: selected.id, file });
                        e.target.value = "";
                      }}
                    />
                  </label>
                </div>
              </div>

              <div className="flex items-center justify-between bg-surface rounded-xl px-3 py-2.5 gap-2">
                <span className="text-sm">مدد الحجز والإشعار المسبق</span>
                <Link href="/schedule" className="text-sm font-bold text-navy-900 hover:text-brand-red">
                  من صفحة المواعيد
                </Link>
              </div>

              <button
                type="button"
                disabled={save.isPending || !draft.name.trim()}
                onClick={() => save.mutate(draft)}
                className="min-h-[48px] rounded-[14px] bg-brand-red hover:bg-brand-red-hover disabled:opacity-60 text-white text-[15px] font-bold shadow-[0_10px_22px_-10px_rgba(214,59,47,0.65)] transition"
              >
                {save.isPending ? "جاري الحفظ…" : "حفظ"}
              </button>
              <button
                type="button"
                disabled={remove.isPending}
                onClick={() => confirm({ title: `تمسح ${draft.name}؟`, message: "الملعب هيتشال من التطبيق وحجوزاته القادمة لازم تتعالج الأول.", confirmLabel: "مسح الملعب", destructive: true }, () => remove.mutate(draft.id))}
                className="min-h-[44px] rounded-[12px] border border-[#F3C4BF] bg-white text-[#A8261C] text-sm font-semibold"
              >
                مسح الملعب
              </button>
            </aside>
          )}
        </div>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
      {confirmDialog}

      <ImageLightbox
        photos={(selected?.photos ?? []).map((p) => mediaSrc(p))}
        index={lightboxIndex ?? undefined}
        onIndexChange={setLightboxIndex}
        title={selected ? `صور ${selected.name}` : undefined}
        onClose={() => setLightboxIndex(null)}
      />
    </Page>
  );
}
