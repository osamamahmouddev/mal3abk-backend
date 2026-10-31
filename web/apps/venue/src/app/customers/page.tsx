"use client";

import React, { useEffect, useState } from "react";
import Link from "next/link";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Badge, Page, Toast, Tone, card, chip, muted, secondaryBtn, toArabicDigits, useRevealOnSelect } from "@/components/ui";
import { useVenue } from "@/components/Providers";
import { api, unwrap } from "@/lib/api";
import { BOOKING_STATUS, cairoDateTime, cairoDay, money } from "@/lib/booking-format";
import { downloadCsv } from "@/lib/csv";

type Customer = Schemas["CustomerSummaryDto"];
type Filter = "all" | "regular" | "noshow";

const PAGE_SIZE = 50;

const initials = (name: string) =>
  name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0])
    .join("") || "؟";

function tagOf(c: Customer): [string, Tone] {
  if (c.noShows > 0) return ["عنده عدم حضور", "bad"];
  if (c.totalBookings >= 10) return ["منتظم", "ok"];
  return ["جديد نسبياً", "wait"];
}

export default function CustomersPage() {
  const { venueId } = useVenue();
  const [q, setQ] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState<Filter>("all");
  const [selKey, setSelKey] = useState<string | null>(null);
  const detailsRef = useRevealOnSelect<HTMLElement>(selKey);
  const [toast, setToast] = useState<string | null>(null);

  // Search on the server after a short pause in typing.
  useEffect(() => {
    const t = setTimeout(() => {
      setSearch(q.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(t);
  }, [q]);

  const customers = useQuery({
    queryKey: ["customers", venueId, search, page],
    enabled: !!venueId,
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/venue-manager/venues/{venueId}/customers", {
          params: { path: { venueId: venueId! }, query: { search: search || undefined, page, pageSize: PAGE_SIZE } },
        }),
      ),
  });

  const all = customers.data?.items ?? [];
  const list = all.filter((c) => filter === "all" || (filter === "regular" && c.totalBookings >= 10) || (filter === "noshow" && c.noShows > 0));
  const total = customers.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const selected = list.find((c) => c.customerKey === selKey) ?? list[0] ?? null;

  const exportList = () => {
    downloadCsv(
      "customers.csv",
      ["الاسم", "الموبايل", "النوع", "الحجوزات", "مكتمل", "إلغاء", "عدم حضور", "المدفوع", "آخر حجز"],
      list.map((c) => [
        c.name,
        c.phone ?? "",
        c.isRegisteredPlayer ? "لاعب على التطبيق" : "حجز يدوي",
        String(c.totalBookings),
        String(c.completed),
        String(c.cancelled),
        String(c.noShows),
        String(c.totalSpent),
        c.lastBookingUtc.slice(0, 10),
      ]),
    );
    setToast(`اتنزّل ملف بـ ${toArabicDigits(list.length)} عميل.`);
  };

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex-[1_1_260px] flex flex-col gap-0.5">
          <h1 className="text-[26px] font-bold">العملاء</h1>
          <span className={`text-[13px] ${muted}`}>بس اللي حجزوا في ملعبك — بياناتهم مش بتظهر لأي ملعب تاني.</span>
        </div>
        <button type="button" onClick={exportList} disabled={list.length === 0} className={`${secondaryBtn} rounded-xl disabled:opacity-50`}>
          تصدير
        </button>
      </div>

      <div className="flex flex-wrap gap-2.5 items-center">
        <div className="flex-[1_1_280px] min-w-0 flex items-center gap-2 min-h-[44px] bg-white border border-[#DCE0E8] rounded-xl px-3">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5B6475" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="M20 20l-4-4" />
          </svg>
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="ابحث بالاسم أو الموبايل"
            aria-label="بحث"
            maxLength={100}
            className="flex-1 min-w-0 border-none outline-none text-sm bg-transparent text-slate-900"
          />
        </div>
        {(
          [
            ["all", "الكل"],
            ["regular", "منتظمين"],
            ["noshow", "ليهم عدم حضور"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            aria-pressed={filter === id}
            onClick={() => setFilter(id)}
            className={chip(filter === id, "min-h-[42px] px-3.5 rounded-xl text-[13px]")}
          >
            {label}
          </button>
        ))}
      </div>

      {customers.isError && (
        <div role="alert" className="bg-[#FDECEA] text-[#A8261C] rounded-[14px] px-4 py-3 text-sm flex gap-3">
          <span className="flex-1">{(customers.error as Error).message}</span>
          <button type="button" className="font-bold underline" onClick={() => customers.refetch()}>
            جرّب تاني
          </button>
        </div>
      )}

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[999_1_560px] min-w-0 ${card} rounded-[18px] overflow-hidden`} aria-busy={customers.isFetching}>
          <div className="overflow-x-auto" tabIndex={0} role="region" aria-label="جدول العملاء">
            <table className="w-full min-w-[620px] border-collapse text-sm">
              <thead>
                <tr className={`bg-[#F8F9FB] ${muted} text-xs text-right`}>
                  <th scope="col" className="px-[18px] py-3 font-semibold">العميل</th>
                  <th scope="col" className="p-3 font-semibold">حجوزات</th>
                  <th scope="col" className="p-3 font-semibold">إلغاء</th>
                  <th scope="col" className="p-3 font-semibold">عدم حضور</th>
                  <th scope="col" className="p-3 font-semibold">آخر حجز</th>
                </tr>
              </thead>
              <tbody>
                {list.map((c) => (
                  <tr key={c.customerKey} className={`border-t border-[#EEF1F5] ${c.customerKey === selected?.customerKey ? "bg-[#F5F7FB]" : ""}`}>
                    <td className="px-[18px] py-2.5">
                      <button
                        type="button"
                        onClick={() => setSelKey(c.customerKey)}
                        aria-current={c.customerKey === selected?.customerKey}
                        className="bg-transparent p-0 flex items-center gap-2.5 text-right text-slate-900"
                      >
                        <span className="w-9 h-9 rounded-full bg-[#EEF1F7] text-navy-900 flex items-center justify-center text-xs font-bold" aria-hidden="true">
                          {initials(c.name)}
                        </span>
                        <span className="flex flex-col">
                          <span className="text-sm font-bold">{c.name || "عميل"}</span>
                          <span dir="ltr" className={`text-xs ${muted} text-right`}>
                            {c.phone ?? (c.isRegisteredPlayer ? "لاعب على التطبيق" : "")}
                          </span>
                        </span>
                      </button>
                    </td>
                    <td className="px-3 py-2.5 font-bold">{toArabicDigits(c.totalBookings)}</td>
                    <td className="px-3 py-2.5">{toArabicDigits(c.cancelled)}</td>
                    <td className="px-3 py-2.5">
                      <span className={`font-bold ${c.noShows ? "text-[#A8261C]" : muted}`}>{toArabicDigits(c.noShows)}</span>
                    </td>
                    <td className="px-3 py-2.5 text-[#333B4A]">{cairoDay(c.lastBookingUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {customers.isLoading && <div className={`p-9 text-center text-sm ${muted}`}>جاري التحميل…</div>}
          {!customers.isLoading && list.length === 0 && (
            <div className={`p-9 text-center text-sm ${muted}`}>{search ? "مفيش عميل بالاسم أو الرقم ده." : "لسه مفيش عملاء — أول ما حد يحجز هيظهر هنا."}</div>
          )}
          {pages > 1 && (
            <nav aria-label="صفحات العملاء" className="flex items-center justify-between px-4 py-3 border-t border-[#EEF1F5] text-sm">
              <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="font-semibold disabled:opacity-40">
                السابق
              </button>
              <span className={muted}>
                صفحة {toArabicDigits(page)} من {toArabicDigits(pages)}
              </span>
              <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="font-semibold disabled:opacity-40">
                التالي
              </button>
            </nav>
          )}
        </section>

        {selected && venueId && (
          <CustomerPanel key={selected.customerKey} venueId={venueId} customer={selected} panelRef={detailsRef} onToast={setToast} />
        )}
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function CustomerPanel({
  venueId,
  customer,
  panelRef,
  onToast,
}: {
  venueId: string;
  customer: Customer;
  panelRef: React.Ref<HTMLElement>;
  onToast: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const { venue } = useVenue();
  const [note, setNote] = useState("");
  const path = { venueId, customerKey: customer.customerKey };

  const detail = useQuery({
    queryKey: ["customer", venueId, customer.customerKey],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/customers/{customerKey}", { params: { path } })),
  });

  const addNote = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/customers/{customerKey}/notes", { params: { path }, body: { text: note.trim() } })),
    onSuccess: async () => {
      setNote("");
      await queryClient.invalidateQueries({ queryKey: ["customer", venueId, customer.customerKey] });
      onToast("اتحفظت الملاحظة — بيشوفها الموظفين بس.");
    },
    onError: (e: Error) => onToast(e.message),
  });

  const deleteNote = useMutation({
    mutationFn: (noteId: string) =>
      unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/customers/{customerKey}/notes/{noteId}", { params: { path: { ...path, noteId } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["customer", venueId, customer.customerKey] }),
    onError: (e: Error) => onToast(e.message),
  });

  const pitchName = (id: string) => venue?.pitches.find((p) => p.id === id)?.name ?? "ملعب";
  const [tag, tone] = tagOf(customer);

  return (
    <aside ref={panelRef} aria-label="ملف العميل" className={`scroll-mt-20 flex-[1_1_340px] min-w-0 ${card} rounded-[18px] p-5 flex flex-col gap-3.5`}>
      <div className="flex items-center gap-3">
        <span className="w-[52px] h-[52px] rounded-full bg-navy-900 text-white flex items-center justify-center text-base font-bold" aria-hidden="true">
          {initials(customer.name)}
        </span>
        <div className="flex-1 flex flex-col min-w-0">
          <span className="text-lg font-bold truncate">{customer.name || "عميل"}</span>
          <span dir="ltr" className={`text-[13px] ${muted} text-right`}>
            {customer.phone ?? "لاعب على التطبيق — رقمه بيظهر في تفاصيل الحجز"}
          </span>
        </div>
        <Badge tone={tone}>{tag}</Badge>
      </div>

      <div className="grid grid-cols-3 gap-2 text-center">
        {(
          [
            [toArabicDigits(customer.totalBookings), "حجز"],
            [money(customer.totalSpent, customer.currency), "مدفوعة"],
            [toArabicDigits(customer.noShows), "عدم حضور مؤكد"],
          ] as const
        ).map(([v, l]) => (
          <div key={l} className="bg-surface rounded-xl py-2 flex flex-col">
            <span className="text-lg font-bold">{v}</span>
            <span className={`text-[11px] ${muted}`}>{l}</span>
          </div>
        ))}
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-bold">آخر الحجوزات</span>
        {detail.isLoading && <span className={`text-[13px] ${muted}`}>جاري التحميل…</span>}
        {detail.data?.bookings.slice(0, 8).map((b) => (
          <div key={b.bookingId} className="flex justify-between gap-2 text-[13px] py-1.5 border-b border-[#EEF1F5]">
            <span>
              {pitchName(b.pitchId)} · {cairoDateTime(b.startUtc)}
            </span>
            <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
          </div>
        ))}
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="customer-note" className="text-sm font-bold">
          ملاحظات الموظفين <span className={`font-normal text-xs ${muted}`}>(مش بتظهر للعميل)</span>
        </label>
        {detail.data?.notes.map((n) => (
          <div key={n.id} className="bg-surface rounded-xl px-3 py-2 text-[13px] flex flex-col gap-1">
            <span className="whitespace-pre-wrap break-words">{n.text}</span>
            <span className={`text-[11px] ${muted} flex justify-between gap-2`}>
              <span>
                {n.authorName || "موظف"} · {cairoDateTime(n.createdAtUtc)}
              </span>
              {n.canDelete && (
                <button type="button" onClick={() => deleteNote.mutate(n.id)} className="text-[#A8261C] font-semibold" disabled={deleteNote.isPending}>
                  حذف
                </button>
              )}
            </span>
          </div>
        ))}
        <textarea
          id="customer-note"
          rows={3}
          maxLength={1000}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="مثلاً: بيحب ملعب ١ وبيدفع كاش"
          className="border border-[#C9CFD9] rounded-xl px-3 py-2.5 text-sm resize-none text-slate-900 focus:outline-none focus:ring-2 focus:ring-navy-900"
        />
        <button
          type="button"
          onClick={() => addNote.mutate()}
          disabled={!note.trim() || addNote.isPending}
          className="self-start min-h-[38px] px-3.5 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold disabled:opacity-50"
        >
          {addNote.isPending ? "بيتحفظ…" : "حفظ الملاحظة"}
        </button>
      </div>

      <Link href="/reservations/manual" className="min-h-[46px] rounded-xl bg-navy-900 text-white text-sm font-bold flex items-center justify-center">
        حجز يدوي للعميل ده
      </Link>
    </aside>
  );
}
