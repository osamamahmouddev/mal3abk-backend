"use client";

import React from "react";
import type { Schemas } from "@malabak/api-client";
import { useConfirm } from "@malabak/ui";
import { formatEGP, toArabicDigits, primaryBtn, secondaryBtn } from "@/components/ui";
import { openFile } from "@/lib/api";
import { Check, Download, Printer, ReceiptText, X } from "lucide-react";
import { Pitch } from "@/components/icons";

interface InvoiceModalProps {
  invoice: Schemas["InvoiceDto"] | null;
  onClose: () => void;
}

const fmtDate = (iso?: string | null) =>
  iso ? new Intl.DateTimeFormat("ar-EG", { dateStyle: "medium", timeZone: "Africa/Cairo" }).format(new Date(iso)) : "—";

export function InvoiceModal({ invoice, onClose }: InvoiceModalProps) {
  const confirm = useConfirm();

  if (!invoice) return null;

  const handlePrint = () => {
    window.print();
  };

  const isVoid = invoice.status === "Void";
  const cycleLabel =
    invoice.cycle === "Annual" || invoice.cycle === "1" ? "اشتراك سنوي (١٢ شهر)" : "اشتراك شهري";
  const venueLabel = invoice.venueName || "نادي وملعب رياضي";
  const ownerLabel = invoice.ownerName || "مالك الحساب";
  const ownerPhone = invoice.ownerPhone ? toArabicDigits(invoice.ownerPhone) : "—";
  const downloadUrl = `/api/v1/venue-manager/billing/invoices/${invoice.id}/pdf`;

  const handleDownloadPdf = async () => {
    try {
      await openFile(downloadUrl);
    } catch {
      await confirm({
        title: "تعذر تحميل ملف PDF",
        message: "لم نتمكن من تنزيل الفاتورة بصيغة PDF في الوقت الحالي. تأكد من اتصال الإنترنت وحاول ثانية.",
        confirmLabel: "حسناً",
        cancelLabel: null,
        type: "warning",
        badge: "تحميل الفاتورة",
      });
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="venue-invoice-modal-title"
      className="invoice-modal-overlay fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 sm:p-6 overflow-y-auto"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="invoice-print-container relative w-full max-w-3xl bg-white rounded-3xl shadow-2xl border border-gray-200 overflow-hidden my-auto flex flex-col">
        {/* Top Control Bar (Hidden on Print) */}
        <div className="no-print bg-[#071832] text-white px-6 py-4 flex items-center justify-between border-b border-[#1E293B]">
          <div className="flex items-center gap-3">
            <span className="grid place-items-center size-9 rounded-lg bg-white/10"><ReceiptText className="size-5" /></span>
            <div>
              <h2 id="venue-invoice-modal-title" className="text-sm font-bold text-white">
                فاتورة اشتراك ضريبية رسمية
              </h2>
              <span className="text-xs text-gray-300 font-mono" dir="ltr">
                {invoice.number}
              </span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handlePrint}
              className={`${primaryBtn} min-h-[36px] px-3.5 text-xs bg-emerald-600 hover:bg-emerald-700 font-bold`}
            >
              <Printer className="size-3.5" />
              <span>طباعة / حفظ كـ PDF</span>
            </button>
            <button
              type="button"
              onClick={handleDownloadPdf}
              className={`${secondaryBtn} min-h-[36px] px-3 text-xs bg-white/10 hover:bg-white/20 text-white border-white/20 gap-1.5 cursor-pointer`}
            >
              <Download className="size-3.5" />
              تحميل PDF
            </button>
            <button
              type="button"
              onClick={onClose}
              className="text-gray-400 hover:text-white p-1 rounded-md hover:bg-white/10 cursor-pointer transition-colors"
              title="إغلاق"
              aria-label="إغلاق"
            >
              <X className="size-5" />
            </button>
          </div>
        </div>

        {/* Printable Invoice Sheet */}
        <div className="p-8 sm:p-10 flex flex-col gap-6 text-gray-900 bg-white">
          {/* Header with Logo and Platform Info */}
          <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-6 border-b-2 border-gray-100 pb-6">
            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-3">
                <img
                  src="/images/logo-horizontal.svg"
                  alt="منصة ملعبك"
                  className="h-12 w-auto object-contain"
                  onError={(e) => {
                    (e.currentTarget as HTMLImageElement).src = "/images/logo-horizontal.png";
                  }}
                />
              </div>
              <p className="text-xs text-gray-500 font-medium">
                المنصة المتكاملة لإدارة وتشغيل الملاعب والمنشآت الرياضية
              </p>
              <div className="text-[11px] text-gray-400 flex flex-wrap gap-x-4">
                <span>جمهورية مصر العربية</span>
                <span>•</span>
                <span>سجل تجاري / ملف ضريبي: ٤٩٢-٨١٧-٢٠٢</span>
              </div>
            </div>

            <div className="flex flex-col sm:items-end justify-center gap-1.5 text-right">
              <span className="inline-block px-3 py-1 rounded-full text-xs font-bold bg-[#071832] text-white">
                فاتورة اشتراك رسمية
              </span>
              <div className="text-xl font-black text-[#071832] font-mono tracking-tight" dir="ltr">
                {invoice.number}
              </div>
              <div className="text-xs text-gray-500">
                تاريخ الإصدار: <strong className="text-gray-800">{fmtDate(invoice.issuedAtUtc)}</strong>
              </div>
              <div>
                {isVoid ? (
                  <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-bold bg-red-100 text-red-800 border border-red-200">
                    ملغاة {invoice.voidReason ? `(${invoice.voidReason})` : ""}
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-emerald-100 text-emerald-800 border border-emerald-200">
                    <Check className="size-3.5" strokeWidth={2.5} />
                    مسددة بالكامل
                  </span>
                )}
              </div>
            </div>
          </div>

          {/* Customer / Club Information Banner */}
          <div className="rounded-2xl border-2 border-[#E2E8F0] bg-gradient-to-l from-slate-50 to-[#F8FAFC] p-5">
            <span className="text-[11px] font-bold text-gray-400 uppercase tracking-wider block mb-2">
              بيانات العميل / المنشأة الرياضية (النادي):
            </span>
            <div className="grid sm:grid-cols-2 gap-4">
              <div className="flex flex-col gap-1">
                <div className="flex items-center gap-2">
                  <span className="grid place-items-center size-10 rounded-xl bg-[#071832] text-white shrink-0"><Pitch className="size-5" /></span>
                  <div>
                    <h3 className="text-lg font-black text-[#071832] leading-tight">
                      {venueLabel}
                    </h3>
                    <span className="text-xs text-emerald-700 font-semibold">
                      العميل والمنشأة المشتركة
                    </span>
                  </div>
                </div>
              </div>

              <div className="flex flex-col sm:items-end justify-center text-xs text-gray-600 gap-1 border-t sm:border-t-0 pt-2 sm:pt-0 border-gray-200">
                <div>
                  <span className="text-gray-500">اسم المالك / المسؤول: </span>
                  <strong className="text-gray-900">{ownerLabel}</strong>
                </div>
                <div>
                  <span className="text-gray-500">رقم التواصل: </span>
                  <strong className="text-gray-900 font-mono" dir="ltr">
                    {ownerPhone}
                  </strong>
                </div>
                <div>
                  <span className="text-gray-500">رقم الحساب: </span>
                  <span className="font-mono text-[11px] text-gray-400" dir="ltr">
                    {invoice.ownerId}
                  </span>
                </div>
              </div>
            </div>
          </div>

          {/* Line Items Table */}
          <div className="rounded-xl border border-gray-200 overflow-hidden">
            <table className="w-full text-right text-xs">
              <thead className="bg-[#071832] text-white">
                <tr>
                  <th className="py-3 px-4 font-bold">البند والوصف</th>
                  <th className="py-3 px-4 font-bold text-center">دورة الاشتراك</th>
                  <th className="py-3 px-4 font-bold text-center">فترة التغطية</th>
                  <th className="py-3 px-4 font-bold text-left">المبلغ</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200 bg-white">
                <tr>
                  <td className="py-4 px-4">
                    <strong className="text-sm font-bold text-gray-900 block">
                      اشتراك منصة ملعبك — باقة {invoice.planName}
                    </strong>
                    <span className="text-gray-500 text-[11px] mt-0.5 block">
                      تشغيل المنشأة الرياضية، إدارة الحجوزات، الملاعب الفرعية، والتقارير المالية المتقدمة.
                    </span>
                  </td>
                  <td className="py-4 px-4 text-center font-semibold text-gray-700">
                    {cycleLabel}
                  </td>
                  <td className="py-4 px-4 text-center text-gray-600 font-mono" dir="ltr">
                    {fmtDate(invoice.periodStartUtc)} إلى {fmtDate(invoice.periodEndUtc)}
                  </td>
                  <td className="py-4 px-4 text-left font-bold text-sm text-[#071832]">
                    {formatEGP(invoice.amount)} ج.م
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          {/* Totals & Official Stamp */}
          <div className="flex flex-col sm:flex-row items-center justify-between gap-6 pt-2">
            {/* Stamp & Verification */}
            <div className="flex items-center gap-3">
              <div className="w-20 h-20 rounded-full border-4 border-emerald-600/60 p-1 flex items-center justify-center text-center rotate-[-12deg] bg-emerald-50/50 shadow-xs">
                <div className="w-full h-full rounded-full border-2 border-dashed border-emerald-600 flex flex-col items-center justify-center p-1">
                  <span className="text-[9px] font-black text-emerald-800 leading-none">منصة ملعبك</span>
                  <span className="text-[12px] font-black text-emerald-700 my-0.5">معتمد</span>
                  <span className="text-[8px] font-bold text-emerald-800 leading-none">PAID</span>
                </div>
              </div>
              <div className="text-[11px] text-gray-500 max-w-[260px] leading-relaxed">
                مستند سداد رسمي صادر ومعتمد من نظام الفوترة والاشتراكات بمنصة ملعبك. لا يحتاج لتوقيع خطي.
              </div>
            </div>

            {/* Financial Summary Card */}
            <div className="w-full sm:w-72 bg-gray-50 rounded-2xl p-4 border border-gray-200 flex flex-col gap-2">
              <div className="flex justify-between text-xs text-gray-600">
                <span>المبلغ الأساسي:</span>
                <span className="font-semibold">{formatEGP(invoice.amount)} ج.م</span>
              </div>
              <div className="flex justify-between text-xs text-gray-600">
                <span>ضريبة القيمة المضافة:</span>
                <span className="text-emerald-700 font-medium">مشمولة في السعر</span>
              </div>
              <div className="border-t border-gray-200 pt-2 flex justify-between items-baseline">
                <span className="text-xs font-bold text-gray-900">إجمالي المبلغ المسدد:</span>
                <strong className="text-lg font-black text-brand-red">
                  {formatEGP(invoice.amount)} ج.م
                </strong>
              </div>
            </div>
          </div>

          {/* Footer note */}
          <div className="border-t border-gray-100 pt-4 text-center text-[11px] text-gray-400">
            شكراً لثقتكم في منصة ملعبك • للدعم الفني والاستفسارات: support@malabak.com
          </div>
        </div>

        {/* Bottom Actions (Hidden on Print) */}
        <div className="no-print bg-gray-50 px-6 py-4 border-t border-gray-200 flex items-center justify-between">
          <button
            type="button"
            onClick={onClose}
            className={secondaryBtn}
          >
            إغلاق
          </button>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleDownloadPdf}
              className={`${secondaryBtn} cursor-pointer`}
            >
              تحميل ملف PDF
            </button>
            <button
              type="button"
              onClick={handlePrint}
              className={`${primaryBtn} bg-emerald-600 hover:bg-emerald-700 gap-2`}
            >
              <Printer className="size-4" />
              طباعة الفاتورة / حفظ كـ PDF
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
