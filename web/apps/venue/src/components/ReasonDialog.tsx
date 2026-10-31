"use client";

import React, { useEffect, useState } from "react";
import { chip } from "@/components/ui";

/** Confirmation dialog that requires a reason: a preset or typed text. Escape closes it. */
export function ReasonDialog({
  title,
  presets,
  note,
  confirmLabel,
  busy,
  onConfirm,
  onClose,
}: {
  title: string;
  presets: string[];
  note: string;
  confirmLabel: string;
  busy: boolean;
  onConfirm: (reason: string) => void;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  const valid = reason.trim().length > 0;

  return (
    <div className="fixed inset-0 bg-[rgba(15,27,61,0.55)] flex items-center justify-center p-4 z-50">
      <div role="dialog" aria-modal="true" aria-labelledby="reason-title" className="w-full max-w-[460px] bg-white rounded-[20px] p-6 flex flex-col gap-3">
        <h2 id="reason-title" className="text-[19px] font-bold">
          {title}
        </h2>
        <span className="text-sm font-semibold">السبب (مطلوب — بيوصل للاعب)</span>
        <div className="flex flex-wrap gap-2">
          {presets.map((p) => (
            <button key={p} type="button" aria-pressed={reason === p} onClick={() => setReason(p)} className={chip(reason === p, "min-h-[40px] px-3.5 rounded-[10px] text-[13px]")}>
              {p}
            </button>
          ))}
        </div>
        <textarea
          aria-label="سبب تاني"
          rows={2}
          maxLength={500}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="أو اكتب السبب"
          className="border border-[#C9CFD9] rounded-xl px-3 py-2.5 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-navy-900"
        />
        <div className="bg-[#FEF3D6] text-[#6B4400] rounded-xl px-3 py-2.5 text-[13px] leading-5">{note}</div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => onConfirm(reason.trim())}
            disabled={!valid || busy}
            className={`min-h-[46px] px-5 rounded-xl text-[15px] font-bold ${valid && !busy ? "bg-[#A8261C] text-white" : "bg-[#E4E7EC] text-[#6B7383] cursor-not-allowed"}`}
          >
            {busy ? "جاري…" : confirmLabel}
          </button>
          <button type="button" onClick={onClose} className="min-h-[46px] px-5 rounded-xl border border-[#C9CFD9] bg-white text-navy-900 text-[15px] font-semibold">
            رجوع
          </button>
        </div>
      </div>
    </div>
  );
}
