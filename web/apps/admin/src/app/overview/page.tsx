"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Page, PageHeader, Pill, Segmented, card, muted, toArabicDigits } from "@/components/ui";

type Period = "week" | "month" | "q";

const PERIODS: Record<Period, { label: string; k: string[] }> = {
  week: { label: "٢٠ – ٢٦ يوليو ٢٠٢٦", k: ["٢١٬٤٨٠", "+٦١٠", "٨٤", "٢٣١", "٧٤٢", "٥٩٠", "٧٨", "[الإيراد]"] },
  month: { label: "يوليو ٢٠٢٦ (١ – ٢٦)", k: ["٢١٬٤٨٠", "+٢٬٣٤٠", "٨٤", "٢٣١", "٢٬٨٩٠", "٢٬٣١٥", "٧٨", "[الإيراد]"] },
  q: { label: "الربع الثالث ٢٠٢٦", k: ["٢١٬٤٨٠", "+٢٬٣٤٠", "٨٤", "٢٣١", "٢٬٨٩٠", "٢٬٣١٥", "٧٨", "[الإيراد]"] },
};

const KPI_LABELS: [string, string][] = [
  ["إجمالي اللاعبين", "مسجلين"],
  ["لاعبين جدد", "في الفترة"],
  ["ملاعب نشطة", "معتمدة ومنشورة"],
  ["ملاعب فرعية", "نشطة"],
  ["طلبات حجز", "في الفترة"],
  ["حجوزات مؤكدة", "عربون معتمد من الملعب"],
  ["اشتراكات نشطة", "مدفوعة أو في التجربة"],
  ["الإيراد الشهري المتكرر", "من اشتراكات مؤكد دفعها بس"],
];

const WEEKS = [310, 345, 390, 420, 405, 480, 525, 590];
const SUBS: [string, number][] = [
  ["Starter", 31],
  ["Growth", 29],
  ["Pro", 6],
  ["تجربة مجانية", 12],
];

const rowLink = "flex gap-3 items-center px-3 py-2.5 rounded-[10px] bg-[#F8F9FB] text-slate-900 hover:bg-[#F1F3F7]";

export default function OverviewPage() {
  const [period, setPeriod] = useState<Period>("month");
  const P = PERIODS[period];

  return (
    <Page className="gap-5">
      <PageHeader title="نظرة عامة على المنصة" sub={P.label}>
        <Segmented
          label="الفترة"
          value={period}
          onChange={setPeriod}
          items={[
            ["week", "الأسبوع"],
            ["month", "الشهر"],
            ["q", "الربع"],
          ]}
        />
      </PageHeader>

      <div className="grid gap-3 grid-cols-2 md:grid-cols-[repeat(auto-fit,minmax(min(170px,100%),1fr))]">
        {KPI_LABELS.map(([label, sub], i) => (
          <div key={label} className={`${card} rounded-[18px] px-4 py-3.5 flex flex-col gap-1 ${i === 7 ? "!border-ink" : ""}`}>
            <span className={`text-[13px] ${muted}`}>{label}</span>
            <span className="text-[26px] font-bold">{P.k[i]}</span>
            <span className={`text-xs ${muted}`}>{sub}</span>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[1_1_360px] min-w-0 ${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`}>
          <div className="flex justify-between items-center">
            <h2 className="text-base font-bold">ملاعب مستنية الموافقة</h2>
            <Link href="/approvals" className="text-[13px] font-semibold text-ink">
              عرض الكل (٤)
            </Link>
          </div>
          <Link href="/approvals" className={rowLink}>
            <span className="flex-1 flex flex-col">
              <span className="text-sm font-bold">ملاعب الرواد — التجمع الخامس</span>
              <span className={`text-xs ${muted}`}>٤ ملاعب · قدّم من يومين</span>
            </span>
            <Pill tone="warn">مستندات كاملة</Pill>
          </Link>
          <Link href="/approvals" className={rowLink}>
            <span className="flex-1 flex flex-col">
              <span className="text-sm font-bold">نادي الشباب — مدينة نصر</span>
              <span className={`text-xs ${muted}`}>٢ ملعب · قدّم امبارح</span>
            </span>
            <Pill tone="neutral">ناقص مستند</Pill>
          </Link>
        </section>

        <section className={`flex-[1_1_360px] min-w-0 ${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`}>
          <div className="flex justify-between items-center">
            <h2 className="text-base font-bold">نزاعات مفتوحة</h2>
            <Link href="/disputes" className="text-[13px] font-semibold text-ink">
              عرض الكل (٣)
            </Link>
          </div>
          <Link href="/disputes" className={rowLink}>
            <span className="flex-1 flex flex-col">
              <span className="text-sm font-bold">العربون اتحوّل والملعب رافض</span>
              <span className={`text-xs ${muted}`}>
                <span dir="ltr">KH-2507-0121</span> · ملعب المدينة · من ٥ ساعات
              </span>
            </span>
            <Pill tone="bad">عاجل</Pill>
          </Link>
          <Link href="/disputes" className={rowLink}>
            <span className="flex-1 flex flex-col">
              <span className="text-sm font-bold">اعتراض على نتيجة ماتش</span>
              <span className={`text-xs ${muted}`}>الصقور ضد النجوم · ١٩ يوليو</span>
            </span>
            <Pill tone="warn">عادي</Pill>
          </Link>
        </section>
      </div>

      <div className="flex flex-wrap gap-4 items-start">
        <section className={`flex-[2_1_420px] min-w-0 ${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`}>
          <h2 className="text-base font-bold">الحجوزات المؤكدة أسبوعياً</h2>
          <div
            role="img"
            aria-label={`الحجوزات المؤكدة آخر ٨ أسابيع: ${WEEKS.map(toArabicDigits).join("، ")}`}
            className="grid grid-cols-8 gap-2.5 items-end h-[160px]"
          >
            {WEEKS.map((v, i) => (
              <div key={i} className="flex flex-col items-center gap-1 h-full justify-end">
                <span className="text-[11px] text-[#3B4352]">{toArabicDigits(v)}</span>
                <div
                  className={`w-full max-w-[36px] rounded-t-md rounded-b-sm ${i === 7 ? "bg-brand-red" : "bg-navy-900"}`}
                  style={{ height: Math.round((v / 600) * 110) }}
                />
                <span className={`text-[11px] ${muted}`}>أ{toArabicDigits(i + 1)}</span>
              </div>
            ))}
          </div>
        </section>
        <section className={`flex-[1_1_280px] min-w-0 ${card} rounded-[18px] px-5 py-4 flex flex-col gap-3`}>
          <h2 className="text-base font-bold">توزيع الاشتراكات</h2>
          {SUBS.map(([label, v]) => (
            <div key={label} className="flex flex-col gap-1">
              <div className="flex justify-between text-[13px]">
                <span>{label}</span>
                <span className="font-bold">{toArabicDigits(v)}</span>
              </div>
              <div className="h-2 rounded bg-[#EEF0F5] overflow-hidden">
                <div
                  className={`h-full ${label === "تجربة مجانية" ? "bg-brand-gold" : "bg-navy-900"}`}
                  style={{ width: `${Math.round((v / 31) * 100)}%` }}
                />
              </div>
            </div>
          ))}
        </section>
      </div>

      <section className={`${card} rounded-[18px] px-5 py-4 flex flex-col gap-2.5`}>
        <div className="flex justify-between items-center">
          <h2 className="text-base font-bold">آخر الإجراءات (سجل التدقيق)</h2>
          <Link href="/audit" className="text-[13px] font-semibold text-ink">
            سجل كامل
          </Link>
        </div>
        {[
          ["سارة منصور", "وافقت على ملعب «أكاديمية الدلتا — فرع ٢»", "١٠:١٢ ص"],
          ["عمرو حمدي", "فعّل اشتراك Growth لـ«ملاعب الرواد» بعد تأكيد التحويل", "أمس ٦:٤٠ م"],
          ["سارة منصور", "أوقفت حساب لاعب بعد ٣ بلاغات مؤكدة — السبب مسجل", "أمس ٢:١٥ م"],
        ].map(([who, what, when]) => (
          <div key={what} className="flex gap-2.5 text-sm">
            <span className="flex-1">
              <strong>{who}</strong> {what}
            </span>
            <span className={`text-xs ${muted}`}>{when}</span>
          </div>
        ))}
      </section>
    </Page>
  );
}
