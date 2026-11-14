"use client";

import { useConfirm } from "@/components/ConfirmDialog";
import React, { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { card, muted } from "@/components/ui";
import { PaymentMethodForm } from "@/components/PaymentMethodForm";
import { api, unwrap } from "@/lib/api";
import { METHOD_LABELS } from "@/lib/venue-format";
import { X } from "lucide-react";

export function PaymentMethods({ venueId, onMessage }: { venueId: string; onMessage: (message: string) => void }) {
  const [revealed, setRevealed] = useState<Record<string, string>>({});
  const methods = useQuery({
    queryKey: ["payment-methods", venueId],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues/{venueId}/payment-methods", { params: { path: { venueId } } })),
  });

  // Full account details are shown for 30 seconds only; every reveal is audited by the API.
  const reveal = useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST("/api/v1/venue-manager/venues/{venueId}/payment-methods/{id}/reveal", { params: { path: { venueId, id } } })),
    onSuccess: (details) => {
      setRevealed((r) => ({ ...r, [details.id]: details.accountDetails }));
      setTimeout(() => setRevealed((r) => Object.fromEntries(Object.entries(r).filter(([k]) => k !== details.id))), 30_000);
    },
    onError: (e: Error) => onMessage(e.message),
  });

  const [confirm, confirmDialog] = useConfirm();
  const remove = useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE("/api/v1/venue-manager/venues/{venueId}/payment-methods/{id}", { params: { path: { venueId, id } } })),
    onSuccess: async () => {
      await methods.refetch();
      onMessage("طريقة الدفع اتمسحت.");
    },
    onError: (e: Error) => onMessage(e.message),
  });

  const depositText = (m: Schemas["PaymentMethodDto"]) =>
    m.depositType === "Percentage" ? `عربون ${m.depositValue}٪` : m.depositType === "FixedAmount" ? `عربون ${m.depositValue} ج.م` : "المبلغ كامل";

  return (
    <section className={`${card} rounded-[20px] px-5 py-[18px] flex flex-col gap-3`}>
      <h2 className="text-[17px] font-bold">طرق استلام العربون</h2>
      {(methods.data ?? []).map((m) => (
        <div key={m.id} className="border border-[#EDF0F5] rounded-[14px] px-3.5 py-3 flex flex-wrap items-center gap-3">
          <span className="flex-1 min-w-[200px] flex flex-col">
            <span className="text-sm font-bold">
              {METHOD_LABELS[m.methodType] ?? m.methodType} · {depositText(m)}
            </span>
            <span dir="ltr" className={`text-right text-[13px] ${muted}`}>
              {revealed[m.id] ?? m.accountDetailsMasked} · {m.recipientName}
            </span>
          </span>
          <button
            type="button"
            disabled={reveal.isPending}
            onClick={() => reveal.mutate(m.id)}
            className="min-h-[38px] px-3 rounded-[10px] border border-[#C9CFD9] bg-white text-navy-900 text-[13px] font-semibold"
          >
            إظهار
          </button>
          <button
            type="button"
            aria-label="مسح طريقة الدفع"
            disabled={remove.isPending}
            onClick={() => confirm({ title: "تمسح طريقة الدفع دي؟", message: "العملاء مش هيشوفوها تاني في تعليمات الدفع.", confirmLabel: "مسح", destructive: true }, () => remove.mutate(m.id))}
            className="min-w-[38px] min-h-[38px] rounded-[10px] border border-[#F3C4BF] bg-white text-[#A8261C] grid place-items-center"
          >
            <X className="size-4" />
          </button>
        </div>
      ))}
      <PaymentMethodForm venueId={venueId} onAdded={() => methods.refetch()} />
      {confirmDialog}
    </section>
  );
}
