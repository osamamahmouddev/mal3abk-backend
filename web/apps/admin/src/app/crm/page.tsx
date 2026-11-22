"use client";

import React, { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { CalendarClock, Download, MapPin, MessageCircle, NotebookPen, Phone, PhoneCall, Plus, UserPlus } from "lucide-react";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, SearchBox, btnOutline, btnPrimary, btnSmall, card, muted, toArabicDigits } from "@/components/ui";
import { Drawer, Field, QueryState, Section, TextInput, fmtDate, fmtDateTime, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { TrashButton } from "@/components/TrashButton";

type Contact = Schemas["CrmContactItem"];
type StageKey = "New" | "Contacted" | "Interested" | "Onboarding" | "Active" | "Lost";

/** Stage names as the API reports them, with their number for updates and how the board shows them. */
const STAGES: { key: StageKey; value: Schemas["CrmStage"]; label: string; dot: string }[] = [
  { key: "New", value: 0, label: "جديد", dot: "bg-slate-400" },
  { key: "Contacted", value: 1, label: "اتكلمنا", dot: "bg-sky-500" },
  { key: "Interested", value: 2, label: "مهتم", dot: "bg-amber-500" },
  { key: "Onboarding", value: 3, label: "بيتجهز", dot: "bg-violet-500" },
  { key: "Active", value: 4, label: "شغّال معانا", dot: "bg-emerald-500" },
  { key: "Lost", value: 5, label: "مش مهتم", dot: "bg-rose-400" },
];
const STAGE = Object.fromEntries(STAGES.map((s) => [s.key, s])) as Record<StageKey, (typeof STAGES)[number]>;
const SOURCE: Record<string, string> = { Manual: "إضافة يدوي", Waitlist: "قائمة الانتظار", Referral: "ترشيح", Event: "فعالية" };
const NOTE_KIND: Record<string, string> = { Note: "ملاحظة", Call: "مكالمة", StageChange: "تغيير مرحلة" };

const cairoDate = (iso: string | Date) => new Date(iso).toLocaleDateString("en-CA", { timeZone: "Africa/Cairo" });
/** A follow-up is a day, kept as Cairo midday so it never slips to the day before or after. */
const followUpIso = (date: string) => (date ? new Date(`${date}T12:00:00+02:00`).toISOString() : null);

function followUpState(iso?: string | null): { label: string; tone: "bad" | "warn" | "neutral" } | null {
  if (!iso) return null;
  const today = cairoDate(new Date());
  const day = cairoDate(iso);
  if (day < today) return { label: `متأخر من ${fmtDate(iso)}`, tone: "bad" };
  if (day === today) return { label: "متابعة النهارده", tone: "warn" };
  return { label: `متابعة ${fmtDate(iso)}`, tone: "neutral" };
}

const whatsapp = (phone: string) => `https://wa.me/${phone.replace(/\D/g, "")}`;

/** Which part of the board is shown: one region, every contact, or the contacts with no region yet. */
const ALL = "__all";
const NONE = "__none";
const REGION_KEY = "crm-region";

function regionQuery(region: string) {
  if (region === ALL) return {};
  if (region === NONE) return { unassigned: true };
  return { region };
}

function readSavedRegion(): string | null {
  try {
    return window.localStorage.getItem(REGION_KEY);
  } catch {
    return null;
  }
}

function saveRegion(region: string) {
  try {
    window.localStorage.setItem(REGION_KEY, region);
  } catch {
    // Remembering the tab is a convenience only.
  }
}

/** The platform team's CRM for venues: who they talk to, where each one stands, and when to call back. */
export default function CrmPage() {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [search, setSearch] = useState("");
  const [q, setQ] = useState("");
  const [dueOnly, setDueOnly] = useState(false);
  const [showLost, setShowLost] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [region, setRegion] = useState<string | null>(null);
  const [newRegions, setNewRegions] = useState<string[]>([]);
  const [namingRegion, setNamingRegion] = useState(false);

  useEffect(() => {
    const t = setTimeout(() => setQ(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  const regions = useQuery({ queryKey: ["crm-regions"], queryFn: () => unwrap(api.GET("/api/v1/admin/crm/regions")) });

  // Regions with contacts, then any the team just named and has not filled yet.
  const regionNames = useMemo(() => {
    const names = (regions.data?.items ?? []).map((r) => r.name);
    return [...names, ...newRegions.filter((n) => !names.includes(n))];
  }, [regions.data, newRegions]);

  // Open on the tab used last time, else the biggest region.
  const regionItems = useMemo(() => regions.data?.items ?? [], [regions.data]);
  const unassigned = regions.data?.unassigned ?? 0;

  useEffect(() => {
    if (region !== null) return;
    // If the regions cannot be read, the whole board is still shown.
    if (regions.isError) {
      setRegion(ALL);
      return;
    }
    if (!regions.data) return;
    const saved = readSavedRegion();
    const valid = saved && (saved === ALL || (saved === NONE && unassigned > 0) || regionNames.includes(saved));
    setRegion(valid ? saved : (regionNames[0] ?? ALL));
  }, [region, regions.data, regions.isError, regionNames, unassigned]);

  const pick = (value: string) => {
    setRegion(value);
    saveRegion(value);
  };

  const current = region ?? ALL;
  const summary = useQuery({
    queryKey: ["crm-summary", current],
    enabled: region !== null,
    queryFn: () => unwrap(api.GET("/api/v1/admin/crm/summary", { params: { query: regionQuery(current) } })),
  });
  const contacts = useQuery({
    queryKey: ["crm-contacts", current, q, dueOnly],
    enabled: region !== null,
    queryFn: () =>
      unwrap(api.GET("/api/v1/admin/crm/contacts", { params: { query: { ...regionQuery(current), q: q || undefined, due: dueOnly || undefined, pageSize: 100 } } })),
  });

  const importWaitlist = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/admin/crm/import-waitlist")),
    onSuccess: async (r) => {
      notice.ok(r.imported ? `اتضاف ${toArabicDigits(r.imported)} صاحب ملعب من قائمة الانتظار.` : "كل أصحاب الملاعب في قائمة الانتظار موجودين بالفعل.");
      await queryClient.invalidateQueries({ queryKey: ["crm-summary"] });
      await queryClient.invalidateQueries({ queryKey: ["crm-contacts"] });
      await queryClient.invalidateQueries({ queryKey: ["crm-regions"] });
    },
    onError: notice.fail,
  });

  const columns = useMemo(() => {
    const items = contacts.data?.items ?? [];
    return STAGES.filter((s) => showLost || s.key !== "Lost").map((s) => ({ ...s, items: items.filter((c) => c.stage === s.key) }));
  }, [contacts.data, showLost]);

  const s = summary.data;
  const waiting = s?.waitlistNotImported ?? 0;

  return (
    <Page>
      <PageHeader title="متابعة الملاعب" sub="كل ملعب بنكلمه: وصل لفين، اتقال إيه، وإمتى نكلمه تاني.">
        <div className="flex flex-wrap gap-2">
          {waiting > 0 && (
            <button type="button" className={btnOutline} disabled={importWaitlist.isPending} onClick={() => importWaitlist.mutate()}>
              <Download className="size-4" />
              {importWaitlist.isPending ? "جاري…" : `استيراد ${toArabicDigits(waiting)} من قائمة الانتظار`}
            </button>
          )}
          <button type="button" className={btnPrimary} onClick={() => setAdding(true)}>
            <UserPlus className="size-4" />
            عميل جديد
          </button>
        </div>
      </PageHeader>
      {notice.node}

      <div className="flex flex-wrap items-center gap-1.5" role="tablist" aria-label="المناطق">
        {regionNames.map((name) => (
          <RegionTab key={name} active={current === name} onClick={() => pick(name)} label={name} count={regionItems.find((r) => r.name === name)?.count ?? 0} />
        ))}
        {unassigned > 0 && <RegionTab active={current === NONE} onClick={() => pick(NONE)} label="من غير منطقة" count={unassigned} />}
        <RegionTab active={current === ALL} onClick={() => pick(ALL)} label="الكل" count={regionItems.reduce((n, r) => n + r.count, 0) + unassigned} />
        <button type="button" onClick={() => setNamingRegion(true)} className={`${btnSmall} border border-dashed border-slate-300 text-slate-600`}>
          <Plus className="size-3.5" />
          منطقة جديدة
        </button>
      </div>

      <div className="grid grid-cols-3 sm:grid-cols-6 gap-2">
        {STAGES.map((st) => (
          <div key={st.key} className={`${card} rounded-2xl px-3 py-2.5 flex flex-col gap-0.5`}>
            <span className="flex items-center gap-1.5 text-[12px] font-semibold text-slate-600">
              <span className={`size-2 rounded-full ${st.dot}`} aria-hidden />
              {st.label}
            </span>
            <strong className="text-[22px] font-extrabold text-slate-900">{toArabicDigits(s?.byStage?.[st.key] ?? 0)}</strong>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <SearchBox value={search} onChange={setSearch} placeholder="اسم، ملعب، منطقة أو رقم موبايل" className="flex-1 min-w-[220px]" />
        <button
          type="button"
          aria-pressed={dueOnly}
          onClick={() => setDueOnly((v) => !v)}
          className={`${btnOutline} ${dueOnly ? "!bg-[#FEF3D6] !border-[#D9A21B] !text-[#7A4D00]" : ""}`}
        >
          <CalendarClock className="size-4" />
          متابعات النهارده {s ? `(${toArabicDigits(s.followUpsDue)})` : ""}
        </button>
        <label className="flex items-center gap-2 text-[13px] font-semibold text-slate-700 cursor-pointer">
          <input type="checkbox" checked={showLost} onChange={(e) => setShowLost(e.target.checked)} className="size-4 accent-brand-red" />
          إظهار «مش مهتم»
        </label>
      </div>

      <QueryState
        query={contacts}
        isEmpty={(d) => d.total === 0 && !q && !dueOnly}
        emptyTitle={current === ALL || current === NONE ? "لسه مفيش عملاء" : `لسه مفيش عملاء في ${current}`}
        emptyDescription="ضيف أول صاحب ملعب بتكلمه، أو استورد اللي سابوا رقمهم في صفحة الهبوط."
        emptyIcon="users"
        emptyAction={{ label: "عميل جديد", onClick: () => setAdding(true) }}
      >
        {() => (
          <div className="flex gap-3 overflow-x-auto pb-2 snap-x">
            {columns.map((col) => (
              <section key={col.key} aria-label={col.label} className="snap-start shrink-0 w-[260px] flex flex-col gap-2 rounded-2xl bg-white/60 border border-[#E4E7EC] p-2.5">
                <header className="flex items-center justify-between px-1">
                  <span className="flex items-center gap-1.5 text-[13px] font-bold text-slate-800">
                    <span className={`size-2 rounded-full ${col.dot}`} aria-hidden />
                    {col.label}
                  </span>
                  <span className={`text-[12px] ${muted}`}>{toArabicDigits(col.items.length)}</span>
                </header>
                {col.items.length === 0 && <p className={`text-[12px] ${muted} px-1 py-3 text-center`}>مفيش حد هنا</p>}
                {col.items.map((c) => (
                  <ContactCard key={c.id} contact={c} showRegion={current === ALL} onOpen={() => setSelected(c.id)} />
                ))}
              </section>
            ))}
          </div>
        )}
      </QueryState>

      {adding && (
        <NewContactDrawer
          regions={regionNames}
          defaultRegion={current === ALL || current === NONE ? "" : current}
          onClose={() => setAdding(false)}
          onCreated={(id) => {
            setAdding(false);
            setSelected(id);
          }}
        />
      )}
      <ContactDrawer contactId={selected} regions={regionNames} onClose={() => setSelected(null)} />
      {namingRegion && (
        <NewRegionModal
          onClose={() => setNamingRegion(false)}
          onAdd={(name) => {
            setNamingRegion(false);
            setNewRegions((list) => (list.includes(name) ? list : [...list, name]));
            pick(name);
          }}
        />
      )}
    </Page>
  );
}

function RegionTab({ label, count, active, onClick }: { label: string; count: number; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={`min-h-[36px] px-3 rounded-[10px] text-[13px] font-semibold border inline-flex items-center gap-1.5 transition-all ${
        active ? "bg-navy-900 text-white border-navy-900" : "bg-white border-[#C9CFD9] hover:bg-slate-50 cursor-pointer"
      }`}
    >
      <MapPin className="size-3.5" aria-hidden />
      {label}
      <span className={active ? "text-white/70" : "text-slate-400"}>{toArabicDigits(count)}</span>
    </button>
  );
}

/** Names a region that has no contacts yet; it becomes a tab and new contacts added there go to it. */
function NewRegionModal({ onClose, onAdd }: { onClose: () => void; onAdd: (name: string) => void }) {
  const [name, setName] = useState("");
  const clean = name.trim();
  return (
    <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] backdrop-blur-sm animate-fade-in flex items-center justify-center p-4">
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby="region-title"
        className="bg-white rounded-[20px] p-6 w-full max-w-[400px] flex flex-col gap-3 animate-modal-in shadow-2xl"
        onSubmit={(e) => {
          e.preventDefault();
          if (clean) onAdd(clean);
        }}
      >
        <h2 id="region-title" className="text-[19px] font-bold">
          منطقة جديدة
        </h2>
        <p className={`text-sm ${muted} leading-[1.7]`}>هتظهر كتبويب، وأي عميل تضيفه وانت فيها هيتسجل عليها.</p>
        <TextInput autoFocus value={name} maxLength={60} onChange={(e) => setName(e.target.value)} placeholder="مثلاً مايو" aria-label="اسم المنطقة" />
        <div className="flex gap-2 justify-end">
          <button type="button" onClick={onClose} className={btnOutline}>
            رجوع
          </button>
          <button type="submit" disabled={!clean} className={btnPrimary}>
            إضافة
          </button>
        </div>
      </form>
    </div>
  );
}

/** Lets the team pick a region already in use or type a new one. */
function RegionInput({ id, regions, ...props }: { id: string; regions: string[] } & React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <>
      <TextInput list={id} maxLength={60} {...props} />
      <datalist id={id}>
        {regions.map((r) => (
          <option key={r} value={r} />
        ))}
      </datalist>
    </>
  );
}

function ContactCard({ contact: c, showRegion, onOpen }: { contact: Contact; showRegion: boolean; onOpen: () => void }) {
  const due = followUpState(c.nextFollowUpAtUtc);
  return (
    <button
      type="button"
      onClick={onOpen}
      className="text-right bg-white rounded-xl border border-[#E4E7EC] p-3 flex flex-col gap-1.5 hover:border-slate-400 hover:shadow-sm transition-all cursor-pointer"
    >
      <strong className="text-[14px] text-slate-900 truncate">{c.name}</strong>
      {(c.venueName || c.area) && <span className={`text-[12px] ${muted} truncate`}>{[c.venueName, c.area].filter(Boolean).join(" · ")}</span>}
      {showRegion && c.region && (
        <span className="text-[11px] text-slate-500 inline-flex items-center gap-1">
          <MapPin className="size-3" aria-hidden />
          {c.region}
        </span>
      )}
      <span dir="ltr" className="text-[12px] text-slate-500 text-right font-mono">{c.phone}</span>
      <div className="flex flex-wrap gap-1">
        {due && <Pill tone={due.tone}>{due.label}</Pill>}
        {c.source === "Waitlist" && <Pill tone="navy">قائمة الانتظار</Pill>}
      </div>
    </button>
  );
}

function NewContactDrawer({
  regions,
  defaultRegion,
  onClose,
  onCreated,
}: {
  regions: string[];
  defaultRegion: string;
  onClose: () => void;
  onCreated: (id: string) => void;
}) {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [form, setForm] = useState({ name: "", phone: "", venueName: "", area: "", region: defaultRegion, followUp: "" });
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });

  const create = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/crm/contacts", {
          body: {
            name: form.name.trim(),
            phone: form.phone.trim(),
            venueName: form.venueName.trim() || null,
            area: form.area.trim() || null,
            region: form.region.trim() || null,
            nextFollowUpAtUtc: followUpIso(form.followUp),
          },
        }),
      ),
    onSuccess: async (d) => {
      await queryClient.invalidateQueries({ queryKey: ["crm-summary"] });
      await queryClient.invalidateQueries({ queryKey: ["crm-contacts"] });
      await queryClient.invalidateQueries({ queryKey: ["crm-regions"] });
      onCreated(d.contact.id);
    },
    onError: notice.fail,
  });

  return (
    <Drawer title="عميل جديد" open onClose={onClose}>
      {notice.node}
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <Field label="الاسم">
          <TextInput value={form.name} onChange={set("name")} maxLength={120} placeholder="مثلاً كابتن أحمد" required />
        </Field>
        <Field label="رقم الموبايل">
          <TextInput value={form.phone} onChange={set("phone")} dir="ltr" inputMode="tel" placeholder="01xxxxxxxxx" required />
        </Field>
        <Field label="المنطقة الكبيرة">
          <RegionInput id="crm-new-regions" regions={regions} value={form.region} onChange={set("region")} placeholder="مثلاً حلوان أو مايو" required />
        </Field>
        <div className="grid grid-cols-2 gap-2">
          <Field label="اسم الملعب (اختياري)">
            <TextInput value={form.venueName} onChange={set("venueName")} maxLength={160} />
          </Field>
          <Field label="الحي (اختياري)">
            <TextInput value={form.area} onChange={set("area")} maxLength={120} />
          </Field>
        </div>
        <Field label="نكلمه تاني يوم (اختياري)">
          <TextInput type="date" value={form.followUp} onChange={set("followUp")} />
        </Field>
        <button type="submit" className={btnPrimary} disabled={create.isPending || !form.name.trim() || !form.phone.trim() || !form.region.trim()}>
          <Plus className="size-4" />
          {create.isPending ? "جاري…" : "إضافة"}
        </button>
      </form>
    </Drawer>
  );
}

function ContactDrawer({ contactId, regions, onClose }: { contactId: string | null; regions: string[]; onClose: () => void }) {
  const queryClient = useQueryClient();
  const notice = useNotice();
  const [reason, setReason] = useState<ReasonRequest | null>(null);
  const [note, setNote] = useState("");
  const [form, setForm] = useState({ name: "", phone: "", venueName: "", area: "", region: "", followUp: "" });

  const detail = useQuery({
    queryKey: ["crm-contact", contactId],
    enabled: !!contactId,
    queryFn: () => unwrap(api.GET("/api/v1/admin/crm/contacts/{id}", { params: { path: { id: contactId! } } })),
  });

  useEffect(() => {
    const c = detail.data?.contact;
    if (!c) return;
    setForm({
      name: c.name,
      phone: c.phone,
      venueName: c.venueName ?? "",
      area: c.area ?? "",
      region: c.region ?? "",
      followUp: c.nextFollowUpAtUtc ? cairoDate(c.nextFollowUpAtUtc) : "",
    });
  }, [detail.data]);

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ["crm-contact", contactId] });
    await queryClient.invalidateQueries({ queryKey: ["crm-contacts"] });
    await queryClient.invalidateQueries({ queryKey: ["crm-summary"] });
    await queryClient.invalidateQueries({ queryKey: ["crm-regions"] });
  };

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/admin/crm/contacts/{id}", {
          params: { path: { id: contactId! } },
          body: {
            name: form.name.trim(),
            phone: form.phone.trim(),
            venueName: form.venueName.trim() || null,
            area: form.area.trim() || null,
            region: form.region.trim() || null,
            nextFollowUpAtUtc: followUpIso(form.followUp),
            assignedToUserId: detail.data?.contact.assignedToUserId ?? null,
          },
        }),
      ),
    onSuccess: async () => {
      notice.ok("اتحفظت البيانات.");
      await refresh();
    },
    onError: notice.fail,
  });

  const move = useMutation({
    mutationFn: (input: { stage: Schemas["CrmStage"]; reason?: string }) =>
      unwrap(api.POST("/api/v1/admin/crm/contacts/{id}/stage", { params: { path: { id: contactId! } }, body: { stage: input.stage, reason: input.reason ?? null } })),
    onSuccess: refresh,
    onError: notice.fail,
  });

  const addNote = useMutation({
    mutationFn: (kind: Schemas["CrmNoteKind"]) =>
      unwrap(api.POST("/api/v1/admin/crm/contacts/{id}/notes", { params: { path: { id: contactId! } }, body: { text: note.trim(), kind } })),
    onSuccess: async () => {
      setNote("");
      await refresh();
    },
    onError: notice.fail,
  });

  const c = detail.data?.contact;
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });

  return (
    <Drawer title={c?.name || "العميل"} open={!!contactId} onClose={onClose}>
      {notice.node}
      <QueryState query={detail}>
        {(d) => (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <a href={`tel:${d.contact.phone}`} className={`${btnSmall} border border-slate-200`}>
                <Phone className="size-3.5" />
                اتصال
              </a>
              <a href={whatsapp(d.contact.phone)} target="_blank" rel="noopener noreferrer" className={`${btnSmall} border border-slate-200`}>
                <MessageCircle className="size-3.5" />
                واتساب
              </a>
              <span className={`text-[12px] ${muted}`}>
                {SOURCE[d.contact.source] ?? d.contact.source} · اتضاف {fmtDate(d.contact.createdAtUtc)}
              </span>
              <TrashButton kind="crm" id={d.contact.id} name={d.contact.name} onDeleted={onClose} className="ms-auto !min-h-[32px] !px-2.5 !text-xs" />
            </div>

            <Section title="المرحلة">
              <div className="flex flex-wrap gap-1.5">
                {STAGES.map((st) => {
                  const on = d.contact.stage === st.key;
                  return (
                    <button
                      key={st.key}
                      type="button"
                      aria-pressed={on}
                      disabled={move.isPending || on}
                      onClick={() =>
                        st.key === "Lost"
                          ? setReason({
                              title: "مش مهتم",
                              body: "اكتب السبب عشان نفتكره لو رجعنا نكلمه.",
                              confirmLabel: "حفظ",
                              onConfirm: (text) => move.mutate({ stage: st.value, reason: text }),
                            })
                          : move.mutate({ stage: st.value })
                      }
                      className={`min-h-[36px] px-3 rounded-[10px] text-[13px] font-semibold border inline-flex items-center gap-1.5 transition-all ${
                        on ? "bg-navy-900 text-white border-navy-900" : "bg-white border-[#C9CFD9] hover:bg-slate-50 cursor-pointer"
                      }`}
                    >
                      <span className={`size-2 rounded-full ${st.dot}`} aria-hidden />
                      {st.label}
                    </button>
                  );
                })}
              </div>
              {d.contact.stage === "Lost" && d.contact.lostReason && <p className={`text-[12px] ${muted}`}>السبب: {d.contact.lostReason}</p>}
            </Section>

            <Section title="البيانات">
              <form
                className="flex flex-col gap-2.5"
                onSubmit={(e) => {
                  e.preventDefault();
                  save.mutate();
                }}
              >
                <div className="grid grid-cols-2 gap-2">
                  <Field label="الاسم">
                    <TextInput value={form.name} onChange={set("name")} maxLength={120} />
                  </Field>
                  <Field label="الموبايل">
                    <TextInput value={form.phone} onChange={set("phone")} dir="ltr" inputMode="tel" />
                  </Field>
                  <Field label="الملعب">
                    <TextInput value={form.venueName} onChange={set("venueName")} maxLength={160} />
                  </Field>
                  <Field label="المنطقة الكبيرة">
                    <RegionInput id="crm-edit-regions" regions={regions} value={form.region} onChange={set("region")} />
                  </Field>
                  <Field label="الحي">
                    <TextInput value={form.area} onChange={set("area")} maxLength={120} />
                  </Field>
                </div>
                <Field label="نكلمه تاني يوم">
                  <TextInput type="date" value={form.followUp} onChange={set("followUp")} />
                </Field>
                <button type="submit" className={`${btnSmall} self-start border border-slate-200`} disabled={save.isPending || !form.name.trim()}>
                  {save.isPending ? "جاري الحفظ…" : "حفظ"}
                </button>
              </form>
            </Section>

            <Section title="الملاحظات والمكالمات">
              <div className="flex flex-col gap-2">
                <textarea
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  maxLength={2000}
                  rows={3}
                  placeholder="اتقال إيه؟"
                  className="w-full rounded-xl border border-[#C9CFD9] px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ink"
                />
                <div className="flex gap-2">
                  <button type="button" className={`${btnSmall} border border-slate-200`} disabled={!note.trim() || addNote.isPending} onClick={() => addNote.mutate(1)}>
                    <PhoneCall className="size-3.5" />
                    سجّل مكالمة
                  </button>
                  <button type="button" className={`${btnSmall} border border-slate-200`} disabled={!note.trim() || addNote.isPending} onClick={() => addNote.mutate(0)}>
                    <NotebookPen className="size-3.5" />
                    ملاحظة
                  </button>
                </div>
              </div>
              {d.notes.length === 0 ? (
                <p className={`text-[13px] ${muted}`}>لسه مفيش حاجة متسجلة.</p>
              ) : (
                <ol className="flex flex-col gap-2 mt-1">
                  {d.notes.map((n) => (
                    <li key={n.id} className={`rounded-xl px-3 py-2 text-[13px] ${n.kind === "StageChange" ? "bg-slate-50 text-slate-600" : "bg-white border border-[#E4E7EC]"}`}>
                      <div className="flex items-center justify-between gap-2 text-[11px] text-slate-500 mb-0.5">
                        <span className="font-semibold">
                          {NOTE_KIND[n.kind] ?? n.kind}
                          {n.authorName ? ` · ${n.authorName}` : ""}
                        </span>
                        <span>{fmtDateTime(n.createdAtUtc)}</span>
                      </div>
                      <p className="whitespace-pre-wrap leading-relaxed">{n.kind === "StageChange" ? stageText(n.text) : n.text}</p>
                    </li>
                  ))}
                </ol>
              )}
            </Section>
          </>
        )}
      </QueryState>
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Drawer>
  );
}

/** "New → Contacted: reason" from the API, in the board's words. */
function stageText(text: string) {
  return text.replace(/\b(New|Contacted|Interested|Onboarding|Active|Lost)\b/g, (k) => STAGE[k as StageKey]?.label ?? k);
}
