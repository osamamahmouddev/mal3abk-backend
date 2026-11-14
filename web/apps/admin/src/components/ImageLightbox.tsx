"use client";

import React, { useEffect } from "react";
import { ChevronLeft, ChevronRight, X } from "lucide-react";

export interface LightboxPhoto {
  src: string;
  title?: string;
}

export function ImageLightbox({
  photo,
  onClose,
  onNext,
  onPrev,
}: {
  photo: LightboxPhoto | null;
  onClose: () => void;
  onNext?: () => void;
  onPrev?: () => void;
}) {
  useEffect(() => {
    if (!photo) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowLeft" && onPrev) onPrev();
      if (e.key === "ArrowRight" && onNext) onNext();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [photo, onClose, onNext, onPrev]);

  if (!photo) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="معاينة الصورة"
      className="fixed inset-0 z-50 bg-black/85 backdrop-blur-xs flex items-center justify-center p-4 select-none animate-in fade-in duration-150"
      onClick={onClose}
    >
      <div
        className="relative max-w-4xl w-full bg-slate-900 rounded-2xl overflow-hidden shadow-2xl flex flex-col border border-slate-700"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 bg-slate-950 text-white border-b border-slate-800">
          <span className="text-sm font-bold truncate">{photo.title || "معاينة الصورة"}</span>
          <button
            type="button"
            onClick={onClose}
            aria-label="إغلاق"
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
          >
            <X className="size-5" />
          </button>
        </div>

        {/* Content Image & Navigation */}
        <div className="relative p-3 flex items-center justify-center bg-black/50 overflow-auto max-h-[calc(90vh-60px)] min-h-[300px]">
          {onPrev && (
            <button
              type="button"
              onClick={onPrev}
              aria-label="الصورة السابقة"
              className="absolute right-4 z-10 p-2.5 rounded-full bg-black/60 hover:bg-black/80 text-white border border-white/20 transition shadow-lg"
            >
              <ChevronRight className="size-6" />
            </button>
          )}

          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={photo.src}
            alt={photo.title || "معاينة الصورة"}
            className="max-h-[75vh] w-auto max-w-full object-contain rounded-lg shadow-lg pointer-events-auto"
          />

          {onNext && (
            <button
              type="button"
              onClick={onNext}
              aria-label="الصورة التالية"
              className="absolute left-4 z-10 p-2.5 rounded-full bg-black/60 hover:bg-black/80 text-white border border-white/20 transition shadow-lg"
            >
              <ChevronLeft className="size-6" />
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
