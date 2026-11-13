"use client";

import React, { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

interface AddAreaInlineProps {
  governorateCode: string | undefined;
  inputClassName: string;
  onAdded: (nameAr: string) => void;
}

/** "My area is not in the list": adds it to the governorate for everyone and picks it. */
export const AddAreaInline: React.FC<AddAreaInlineProps> = ({ governorateCode, inputClassName, onAdded }) => {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");

  const add = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/venues/areas", { body: { governorateCode, nameAr: name.trim() } })),
    onSuccess: async (area) => {
      await queryClient.invalidateQueries({ queryKey: ["reference", "governorates"] });
      onAdded(area.nameAr);
      setName("");
      setOpen(false);
    },
  });

  if (!governorateCode) return null;

  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)} className="self-start text-[13px] font-semibold text-brand-red hover:underline">
        منطقتك مش في القايمة؟ ضيفها
      </button>
    );
  }

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex gap-2">
        <input
          autoFocus
          value={name}
          maxLength={60}
          placeholder="اسم المنطقة"
          onChange={(e) => setName(e.target.value)}
          className={inputClassName}
        />
        <button
          type="button"
          disabled={name.trim().length < 2 || add.isPending}
          onClick={() => add.mutate()}
          className="min-h-[44px] px-4 rounded-xl bg-navy-900 text-white text-sm font-bold disabled:opacity-50"
        >
          إضافة
        </button>
        <button type="button" onClick={() => setOpen(false)} className="min-h-[44px] px-3 text-sm font-semibold text-[#5B6475]">
          إلغاء
        </button>
      </div>
      {add.error && <span role="alert" className="text-[13px] text-[#A8261C]">{(add.error as Error).message}</span>}
    </div>
  );
};
