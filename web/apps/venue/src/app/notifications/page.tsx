"use client";

import React, { useState } from "react";
import { useRouter } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, Switch, Toast, card, chip, muted, toArabicDigits } from "@/components/ui";
import { api, unwrap } from "@/lib/api";

type Filter = "all" | "unread";

const CATEGORY: Record<string, { value: Schemas["NotificationCategory"]; label: string; icon: [string, string] }> = {
  Bookings: { value: 0, label: "الحجوزات", icon: ["ح", "bg-[#E5F5EB] text-[#1B6E3E]"] },
  Payments: { value: 1, label: "المدفوعات وإثباتات الدفع", icon: ["ج", "bg-[#FEF3D6] text-[#7A4D00]"] },
  Venues: { value: 2, label: "الملعب والمراجعة", icon: ["م", "bg-[#E8EEF9] text-[#0F1B3D]"] },
  Teams: { value: 3, label: "الفرق", icon: ["ف", "bg-[#EEF0F5] text-[#3B4352]"] },
  Security: { value: 4, label: "أمان الحساب", icon: ["!", "bg-[#FDECEA] text-[#A8261C]"] },
  Marketing: { value: 5, label: "عروض وأخبار", icon: ["ع", "bg-[#EEF0F5] text-[#3B4352]"] },
};

function timeAgo(iso: string) {
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) return "دلوقتي";
  if (minutes < 60) return `من ${toArabicDigits(minutes)} دقيقة`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `من ${toArabicDigits(hours)} ساعة`;
  return new Date(iso).toLocaleDateString("ar-EG", { day: "numeric", month: "long" });
}

export default function NotificationsPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<"inbox" | "prefs">("inbox");
  const [filter, setFilter] = useState<Filter>("all");
  const [toast, setToast] = useState<string | null>(null);

  const page = useQuery({
    queryKey: ["notifications", "list"],
    queryFn: () => unwrap(api.GET("/api/v1/me/notifications", { params: { query: { page: 1, pageSize: 50 } } })),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["notifications"] });

  const markRead = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/v1/me/notifications/{id}/read", { params: { path: { id } } })),
    onSuccess: refresh,
  });
  const readAll = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/me/notifications/read-all")),
    onSuccess: async () => {
      await refresh();
      setToast("كل الإشعارات اتعلّم عليها مقروءة.");
    },
  });

  const items = (page.data?.items ?? []).filter((n) => filter === "all" || !n.isRead);
  const unread = page.data?.unreadCount ?? 0;

  const open = (n: Schemas["NotificationDto"]) => {
    if (!n.isRead) markRead.mutate(n.id);
    if (n.deepLink?.startsWith("/")) router.push(n.deepLink);
  };

  return (
    <Page>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-[1_1_240px] text-[26px] font-bold">الإشعارات</h1>
        {tab === "inbox" && unread > 0 && (
          <button
            type="button"
            disabled={readAll.isPending}
            onClick={() => readAll.mutate()}
            className="min-h-[40px] px-4 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
          >
            علّم الكل مقروء
          </button>
        )}
      </div>

      <div role="tablist" className="flex gap-1.5 border-b border-[#DCE0E8]">
        {(
          [
            ["inbox", `الوارد${unread ? ` (${toArabicDigits(unread)})` : ""}`],
            ["prefs", "تفضيلات الإشعارات"],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setTab(id)}
            className={`min-h-[44px] px-4 bg-transparent text-sm -mb-px border-b-[3px] ${
              tab === id ? "text-navy-900 font-bold border-brand-red" : `${muted} font-medium border-transparent`
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === "inbox" && (
        <>
          <div className="flex gap-1.5">
            {(
              [
                ["all", "الكل"],
                ["unread", "غير مقروء"],
              ] as const
            ).map(([id, label]) => (
              <button key={id} type="button" aria-pressed={filter === id} onClick={() => setFilter(id)} className={chip(filter === id, "min-h-[36px] px-3.5 rounded-[10px] text-[13px]")}>
                {label}
              </button>
            ))}
          </div>
          <section className={`${card} rounded-[20px] overflow-hidden`}>
            {page.isLoading && <div className="h-20 m-4 rounded-xl bg-slate-200 animate-pulse" />}
            {!page.isLoading && items.length === 0 && <p className={`p-8 text-center text-sm ${muted}`}>مفيش إشعارات.</p>}
            {items.map((n) => {
              const [letter, tone] = CATEGORY[n.category]?.icon ?? ["•", "bg-[#EEF0F5] text-[#3B4352]"];
              return (
                <button
                  key={n.id}
                  type="button"
                  onClick={() => open(n)}
                  className={`w-full text-right flex gap-3 px-4 py-3.5 border-b border-[#EEF0F5] last:border-b-0 hover:bg-slate-50 ${n.isRead ? "" : "bg-[#F7F9FD]"}`}
                >
                  <span className={`w-10 h-10 rounded-full shrink-0 flex items-center justify-center text-sm font-bold ${tone}`}>{letter}</span>
                  <span className="flex-1 min-w-0 flex flex-col gap-0.5">
                    <span className="flex items-center gap-2">
                      <span className={`text-sm ${n.isRead ? "font-semibold" : "font-bold"}`}>{n.title}</span>
                      {!n.isRead && <span className="w-2 h-2 rounded-full bg-brand-red" aria-label="غير مقروء" />}
                    </span>
                    <span className="text-[13px] text-[#333B4A] leading-[1.6]">{n.body}</span>
                    <span className={`text-xs ${muted}`}>{timeAgo(n.createdAtUtc)}</span>
                  </span>
                </button>
              );
            })}
          </section>
        </>
      )}

      {tab === "prefs" && <Preferences onMessage={setToast} />}

      <Toast message={toast} onClose={() => setToast(null)} />
    </Page>
  );
}

function Preferences({ onMessage }: { onMessage: (message: string) => void }) {
  const prefs = useQuery({
    queryKey: ["notification-preferences"],
    queryFn: () => unwrap(api.GET("/api/v1/me/notification-preferences")),
  });
  const update = useMutation({
    mutationFn: ({ category, inAppEnabled, pushEnabled }: { category: string; inAppEnabled: boolean; pushEnabled: boolean }) =>
      unwrap(
        api.PUT("/api/v1/me/notification-preferences/{category}", {
          params: { path: { category: CATEGORY[category].value } },
          body: { inAppEnabled, pushEnabled },
        }),
      ),
    onSuccess: () => prefs.refetch(),
    onError: (e: Error) => onMessage(e.message),
  });

  return (
    <section className={`${card} rounded-[20px] px-5 py-2`}>
      <div className={`hidden sm:flex gap-3 py-2 text-xs font-semibold ${muted}`}>
        <span className="flex-1">النوع</span>
        <span className="w-24 text-center">في اللوحة</span>
        <span className="w-24 text-center">إشعار الموبايل</span>
      </div>
      {(prefs.data ?? []).map((p) => (
        <div key={p.category} className="flex flex-wrap items-center gap-3 py-3 border-t border-[#EEF0F5]">
          <span className="flex-1 min-w-[180px] flex flex-col">
            <span className="text-sm font-bold">{CATEGORY[p.category]?.label ?? p.category}</span>
            {!p.canDisable && <span className={`text-xs ${muted}`}>تنبيهات الأمان بتفضل شغالة دايماً</span>}
          </span>
          <span className="w-24 flex justify-center">
            <Switch
              on={p.inAppEnabled}
              label={`${CATEGORY[p.category]?.label} في اللوحة`}
              disabled={!p.canDisable || update.isPending}
              onToggle={() => update.mutate({ category: p.category, inAppEnabled: !p.inAppEnabled, pushEnabled: p.pushEnabled })}
            />
          </span>
          <span className="w-24 flex justify-center">
            <Switch
              on={p.pushEnabled}
              label={`${CATEGORY[p.category]?.label} على الموبايل`}
              disabled={!p.canDisable || update.isPending}
              onToggle={() => update.mutate({ category: p.category, inAppEnabled: p.inAppEnabled, pushEnabled: !p.pushEnabled })}
            />
          </span>
        </div>
      ))}
    </section>
  );
}
