"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Handshake, RotateCcw, Shield, Trash2, Trophy, User } from "lucide-react";
import { Page, PageHeader, Pill, btnDanger, btnOutline, btnSmall, card, inputClass, muted, toArabicDigits } from "@/components/ui";
import { QueryState, fmtDateTime, useNotice } from "@/components/admin";
import { Pitch } from "@/components/icons";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Item = Schemas["TrashItemDto"];

const KINDS: Record<string, { label: string; icon: React.ComponentType<{ className?: string }> }> = {
  user: { label: "مستخدم", icon: User },
  venue: { label: "ملعب", icon: Pitch },
  team: { label: "فريق", icon: Trophy },
  crm: { label: "عميل متابعة", icon: Handshake },
};
const FILTERS: [string, string][] = [["", "الكل"], ["user", "المستخدمين"], ["venue", "الملاعب"], ["team", "الفرق"], ["crm", "متابعة الملاعب"]];
const EMPTY_ALL = "مسح الكل";

const daysLeft = (iso: string) => Math.max(0, Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000));

/**
 * Recently deleted ("المحذوف مؤخرا"): everything the team deleted from the console, restorable for 30 days and then purged
 * automatically. Purging now is irreversible, so it asks for the item's name (or «مسح الكل») and a fresh step-up.
 */
export default function TrashPage() {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [kind, setKind] = useState("");
  const [confirming, setConfirming] = useState<{ item: Item | null } | null>(null);
  const [typed, setTyped] = useState("");

  const list = useQuery({
    queryKey: ["admin-trash", kind],
    queryFn: () => unwrap(api.GET("/api/v1/admin/trash", { params: { query: { kind: kind || undefined } } })),
  });

  const refresh = () => queryClient.invalidateQueries();

  const restore = useMutation({
    mutationFn: (item: Item) => unwrap(api.POST("/api/v1/admin/trash/{entryId}/restore", { params: { path: { entryId: item.id } } })),
    onSuccess: async (_, item) => {
      notice.ok(`«${item.title}» رجع مكانه.`);
      await refresh();
    },
    onError: notice.fail,
  });

  const purge = useMutation({
    mutationFn: (item: Item | null) =>
      stepUp((headers) =>
        item
          ? unwrap(api.POST("/api/v1/admin/trash/{entryId}/purge", { params: { path: { entryId: item.id } }, headers, body: { confirmName: typed.trim() } }))
          : unwrap(api.POST("/api/v1/admin/trash/purge-all", { headers, body: { confirmation: typed.trim() } })),
      ),
    onSuccess: async (r, item) => {
      setConfirming(null);
      setTyped("");
      const all = r as Schemas["TrashPurgeAllResult"] | undefined;
      notice.ok(item ? `«${item.title}» اتمسح نهائي.` : `اتمسح ${toArabicDigits(all?.purged ?? 0)} عنصر نهائي.`);
      await refresh();
    },
    onError: notice.fail,
  });

  const items = list.data ?? [];
  const expected = confirming?.item ? confirming.item.title : EMPTY_ALL;

  return (
    <Page>
      <PageHeader title="المحذوف مؤخراً" sub="أي حاجة بتتحذف من لوحة الإدارة بتفضل هنا ٣٠ يوم تقدر ترجّعها فيهم، وبعدين بتتمسح نهائي لوحدها.">
        {items.length > 0 && !kind && (
          <button type="button" className={btnDanger} onClick={() => { setTyped(""); setConfirming({ item: null }); }}>
            <Trash2 className="size-4" />
            مسح الكل نهائي
          </button>
        )}
      </PageHeader>
      {notice.node}

      <div className="flex flex-wrap gap-1.5" role="tablist" aria-label="نوع المحذوف">
        {FILTERS.map(([value, label]) => (
          <button
            key={value || "all"}
            type="button"
            role="tab"
            aria-selected={kind === value}
            onClick={() => setKind(value)}
            className={`min-h-[36px] px-3 rounded-[10px] text-[13px] font-semibold border transition-all ${
              kind === value ? "bg-navy-900 text-white border-navy-900" : "bg-white border-[#C9CFD9] hover:bg-slate-50 cursor-pointer"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      <QueryState
        query={list}
        isEmpty={(d) => d.length === 0}
        emptyTitle="المحذوف فاضي"
        emptyDescription="أي مستخدم أو ملعب أو فريق أو عميل تحذفه هيظهر هنا ٣٠ يوم."
        emptyIcon="check"
      >
        {(rows) => (
          <ul className="flex flex-col gap-2">
            {rows.map((item) => {
              const k = KINDS[item.kind] ?? { label: item.kind, icon: Shield };
              const left = daysLeft(item.purgeAtUtc);
              return (
                <li key={item.id} className={`${card} rounded-2xl p-4 flex flex-wrap items-center gap-3`}>
                  <span className="grid place-items-center size-10 rounded-xl bg-[#F3F5F9] text-slate-600 shrink-0">
                    <k.icon className="size-5" />
                  </span>
                  <div className="flex-1 min-w-[200px] flex flex-col gap-0.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <strong className="text-[15px] text-slate-900">{item.title}</strong>
                      <Pill tone="neutral">{k.label}</Pill>
                      <Pill tone={left <= 3 ? "bad" : "warn"}>{left === 0 ? "هيتمسح النهارده" : `هيتمسح بعد ${toArabicDigits(left)} يوم`}</Pill>
                    </div>
                    {item.subtitle && <span className={`text-[12px] ${muted}`}>{item.subtitle}</span>}
                    <span className={`text-[12px] ${muted}`}>
                      اتحذف {fmtDateTime(item.deletedAtUtc)}
                      {item.deletedByName ? ` بواسطة ${item.deletedByName}` : ""} · السبب: {item.reason}
                    </span>
                  </div>
                  <div className="flex gap-2">
                    <button type="button" className={`${btnSmall} border border-slate-200`} disabled={restore.isPending} onClick={() => restore.mutate(item)}>
                      <RotateCcw className="size-3.5" />
                      رجّعه
                    </button>
                    <button type="button" className={`${btnSmall} border border-[#F3C4BF] text-[#A8261C]`} onClick={() => { setTyped(""); setConfirming({ item }); }}>
                      <Trash2 className="size-3.5" />
                      مسح نهائي
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </QueryState>

      {confirming && (
        <div className="fixed inset-0 z-50 bg-[rgba(10,19,48,0.5)] backdrop-blur-sm animate-fade-in flex items-center justify-center p-4">
          <form
            role="dialog"
            aria-modal="true"
            aria-labelledby="purge-title"
            className="bg-white rounded-[20px] p-6 w-full max-w-[460px] flex flex-col gap-3 animate-modal-in shadow-2xl"
            onSubmit={(e) => {
              e.preventDefault();
              if (typed.trim() === expected && !purge.isPending) purge.mutate(confirming.item);
            }}
          >
            <h2 id="purge-title" className="text-[19px] font-bold">
              {confirming.item ? `مسح «${confirming.item.title}» نهائي` : "مسح كل المحذوف نهائي"}
            </h2>
            <p className={`text-sm ${muted} leading-[1.7]`}>
              ده مينفعش يرجع. البيانات الشخصية بتتمسح، والحجوزات والمعاملات المالية بتفضل من غيرها عشان الحسابات.
            </p>
            <label className="flex flex-col gap-1 text-[13px] font-semibold">
              اكتب <span className="font-bold text-slate-900">{expected}</span> للتأكيد
              <input autoFocus value={typed} onChange={(e) => setTyped(e.target.value)} className={inputClass} />
            </label>
            <div className="flex gap-2 justify-end">
              <button type="button" onClick={() => setConfirming(null)} className={btnOutline}>
                رجوع
              </button>
              <button type="submit" disabled={typed.trim() !== expected || purge.isPending} className={btnDanger}>
                {purge.isPending ? "جاري…" : "مسح نهائي"}
              </button>
            </div>
          </form>
        </div>
      )}
    </Page>
  );
}
