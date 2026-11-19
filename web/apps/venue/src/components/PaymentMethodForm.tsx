"use client";

import React, { useMemo, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { api, unwrap } from "@/lib/api";
import { METHOD_LABELS } from "@/lib/venue-format";
import { Plus } from "lucide-react";

const field = "min-h-[46px] border border-[#C9CFD9] rounded-xl px-3 text-[15px] text-slate-900 bg-white focus:outline-none focus:ring-2 focus:ring-navy-900";

export function PaymentMethodForm({ venueId, onAdded }: { venueId: string; onAdded: () => void }) {
  const [draft, setDraft] = useState({ methodType: "MobileWallet", recipientName: "", accountDetails: "", depositType: 1, depositValue: 50 });
  const [error, setError] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/venue-manager/venues/{venueId}/payment-methods", {
          params: { path: { venueId } },
          body: {
            methodType: draft.methodType,
            recipientName: draft.recipientName.trim(),
            accountDetails: draft.accountDetails.trim(),
            instructions: null,
            depositType: draft.depositType as Schemas["DepositType"],
            depositValue: draft.depositValue,
          },
        }),
      ),
    onSuccess: () => {
      setDraft({ ...draft, recipientName: "", accountDetails: "" });
      setError(null);
      onAdded();
    },
    onError: (e: Error) => setError(e.message),
  });

  const hint = useMemo(
    () => ({ MobileWallet: "01xxxxxxxxx", InstaPay: "name@instapay أو رقم الموبايل", BankTransfer: "IBAN (EG…) أو رقم الحساب" })[draft.methodType],
    [draft.methodType],
  );

  return (
    <div className="border-2 border-dashed border-[#9AA3B2] rounded-2xl p-4 grid grid-cols-1 sm:grid-cols-2 gap-3">
      <label className="flex flex-col gap-1.5 text-sm font-semibold">
        الطريقة
        <select value={draft.methodType} onChange={(e) => setDraft({ ...draft, methodType: e.target.value })} className={field}>
          {Object.entries(METHOD_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1.5 text-sm font-semibold">
        اسم المستلم
        <input value={draft.recipientName} onChange={(e) => setDraft({ ...draft, recipientName: e.target.value })} className={field} />
      </label>
      <label className="flex flex-col gap-1.5 text-sm font-semibold sm:col-span-2">
        بيانات التحويل
        <input
          dir="ltr"
          placeholder={hint}
          value={draft.accountDetails}
          onChange={(e) => setDraft({ ...draft, accountDetails: e.target.value })}
          className={`${field} text-right`}
        />
      </label>
      <label className="flex flex-col gap-1.5 text-sm font-semibold">
        العربون
        <select value={draft.depositType} onChange={(e) => setDraft({ ...draft, depositType: Number(e.target.value) })} className={field}>
          <option value={1}>مبلغ ثابت (ج.م)</option>
          <option value={0}>نسبة من السعر (٪)</option>
          <option value={2}>المبلغ كامل</option>
        </select>
      </label>
      {draft.depositType !== 2 && (
        <label className="flex flex-col gap-1.5 text-sm font-semibold">
          القيمة
          <input
            type="number"
            min={1}
            value={draft.depositValue}
            onChange={(e) => setDraft({ ...draft, depositValue: Number(e.target.value) })}
            className={field}
          />
        </label>
      )}
      {error && (
        <span role="alert" className="text-[13px] text-[#A8261C] sm:col-span-2">
          {error}
        </span>
      )}
      <button
        type="button"
        disabled={!draft.recipientName.trim() || !draft.accountDetails.trim() || add.isPending}
        onClick={() => add.mutate()}
        className="btn-press sm:col-span-2 min-h-[46px] rounded-xl bg-navy-900 hover:bg-navy-800 disabled:opacity-60 text-white text-sm font-bold inline-flex items-center justify-center gap-2 transition-colors"
      >
        <Plus className="size-4" strokeWidth={2.5} />
        إضافة طريقة دفع
      </button>
    </div>
  );
}
