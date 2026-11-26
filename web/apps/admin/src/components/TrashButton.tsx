"use client";

import React, { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { btnDanger, btnOutline, inputClass, muted } from "@/components/ui";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

export type TrashKind = "user" | "venue" | "team" | "crm";

const LABEL: Record<TrashKind, { what: string; body: string }> = {
  user: { what: "الحساب", body: "الحساب هيتقفل ويتطلّع من كل الأجهزة على طول. تقدر ترجّعه من «المحذوف مؤخراً» خلال ٣٠ يوم، وبعدها بياناته الشخصية بتتمسح نهائي." },
  venue: { what: "الملعب", body: "الملعب هيختفي من التطبيق على طول ومحدش هيقدر يحجز فيه. تقدر ترجّعه من «المحذوف مؤخراً» خلال ٣٠ يوم." },
  team: { what: "الفريق", body: "الفريق هيختفي من التطبيق والترتيب. تقدر ترجّعه بأعضائه من «المحذوف مؤخراً» خلال ٣٠ يوم." },
  crm: { what: "العميل", body: "العميل هيتشال من متابعة الملاعب. تقدر ترجّعه بملاحظاته من «المحذوف مؤخراً» خلال ٣٠ يوم." },
};

/**
 * Deletes a user, venue, team or CRM contact into the admin trash: asks why (kept in the audit log), for a venue its name
 * typed (SECURITY.md), and a fresh authenticator code or password (step-up) before sending.
 */
export function TrashButton({
  kind,
  id,
  name,
  onDeleted,
  className = "",
}: {
  kind: TrashKind;
  id: string;
  name: string;
  onDeleted?: () => void;
  className?: string;
}) {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const [mounted, setMounted] = useState(false);
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [confirmName, setConfirmName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const needsName = kind === "venue";

  useEffect(() => {
    setMounted(true);
  }, []);

  const remove = useMutation({
    mutationFn: () =>
      stepUp((headers) =>
        unwrap(
          api.POST("/api/v1/admin/trash/items/{kind}/{id}", {
            params: { path: { kind, id } },
            headers,
            body: { reason: reason.trim(), confirmName: needsName ? confirmName.trim() : null },
          }),
        ),
      ),
    onSuccess: async () => {
      setOpen(false);
      setReason("");
      setConfirmName("");
      await queryClient.invalidateQueries();
      onDeleted?.();
    },
    onError: (e: Error) => setError(e.message),
  });

  const ready = reason.trim().length >= 5 && (!needsName || confirmName.trim() === name.trim());

  return (
    <>
      <button type="button" className={`${btnDanger} ${className}`} onClick={() => { setError(null); setOpen(true); }}>
        <Trash2 className="size-4" />
        حذف {LABEL[kind].what}
      </button>
      {open &&
        mounted &&
        createPortal(
          <div className="fixed inset-0 z-[100] bg-[rgba(10,19,48,0.65)] backdrop-blur-sm animate-fade-in flex items-center justify-center p-4">
            <form
              role="dialog"
              aria-modal="true"
              aria-labelledby="trash-title"
              className="bg-white rounded-[20px] p-6 w-full max-w-[460px] flex flex-col gap-3 animate-modal-in shadow-2xl"
              onSubmit={(e) => {
                e.preventDefault();
                if (ready && !remove.isPending) remove.mutate();
              }}
            >
              <h2 id="trash-title" className="text-[19px] font-bold">
                حذف «{name}»
              </h2>
              <p className={`text-sm ${muted} leading-[1.7]`}>{LABEL[kind].body}</p>
              <label className="flex flex-col gap-1 text-[13px] font-semibold">
                السبب (بيتسجل في سجل التدقيق)
                <input autoFocus value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} className={inputClass} placeholder="ليه بيتحذف؟" />
              </label>
              {needsName && (
                <label className="flex flex-col gap-1 text-[13px] font-semibold">
                  اكتب اسم الملعب للتأكيد: <span className="font-bold text-slate-900">{name}</span>
                  <input value={confirmName} onChange={(e) => setConfirmName(e.target.value)} className={inputClass} />
                </label>
              )}
              {error && (
                <p role="alert" className="text-xs bg-[#FDECEA] text-[#A8261C] rounded-lg px-3 py-2">
                  {error}
                </p>
              )}
              <div className="flex gap-2 justify-end">
                <button type="button" onClick={() => setOpen(false)} className={btnOutline}>
                  رجوع
                </button>
                <button type="submit" disabled={!ready || remove.isPending} className={btnDanger}>
                  {remove.isPending ? "جاري…" : "حذف"}
                </button>
              </div>
            </form>
          </div>,
          document.body,
        )}
    </>
  );
}
