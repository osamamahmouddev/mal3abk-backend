"use client";

import React, { Suspense } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Page, card, muted } from "@/components/ui";
import { ManualBookingForm } from "@/components/ManualBookingForm";
import { useVenue } from "@/components/Providers";
import { P } from "@/lib/permissions";

export default function ManualBookingPage() {
  return (
    <Suspense>
      <ManualBooking />
    </Suspense>
  );
}

function ManualBooking() {
  const router = useRouter();
  const params = useSearchParams();
  const { venueId, can } = useVenue();
  const start = params.get("start");

  return (
    <Page className="gap-5 max-w-[1440px] w-full">
      <div className="flex flex-col gap-0.5">
        <span className={`text-[13px] ${muted}`}>
          <Link href="/reservations" className="hover:text-brand-red">
            الحجوزات
          </Link>{" "}
          ‹ حجز يدوي
        </span>
        <h1 className="text-[26px] font-bold text-slate-900">حجز يدوي ومراجعة الجدول</h1>
        <span className={`text-[13px] ${muted}`}>
          للحجوزات بالتليفون أو في الملعب. يعرض جدول الحجوزات والمواعيد لليوم المختار لمنع التعارض والحجز المزدوج فوراً.
        </span>
      </div>
      {venueId && !can(P.bookingsApprove) ? (
        <p role="note" className="bg-[#FEF3D6] text-[#7A4D00] rounded-xl px-4 py-3 text-sm">
          دورك في الملعب ده مايسمحش بتسجيل حجوزات يدوي.
        </p>
      ) : (
        <ManualBookingForm
          showSchedule
          defaults={{
            pitchId: params.get("pitch") ?? undefined,
            date: params.get("date") ?? undefined,
            startMinutes: start && /^\d+$/.test(start) ? Number(start) : undefined,
          }}
          onDone={() => router.push("/reservations?created=1")}
          onCancel={() => router.back()}
        />
      )}
    </Page>
  );
}
