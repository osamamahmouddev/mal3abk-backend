"use client";

import React, { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronLeft, ChevronRight, X } from "lucide-react";

export interface LightboxPhoto {
  src: string;
  title?: string;
}

export interface ImageLightboxProps {
  photo?: LightboxPhoto | null;
  photos?: (string | LightboxPhoto)[];
  index?: number;
  onIndexChange?: (index: number) => void;
  title?: string;
  onClose: () => void;
  onNext?: () => void;
  onPrev?: () => void;
}

const toArabicDigits = (n: number | string) =>
  String(n).replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

export function ImageLightbox({
  photo,
  photos,
  index,
  onIndexChange,
  title,
  onClose,
  onNext,
  onPrev,
}: ImageLightboxProps) {
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  const isGalleryMode = Array.isArray(photos) && photos.length > 0 && typeof index === "number";
  const isOpen = isGalleryMode || Boolean(photo);

  let currentSrc = "";
  let currentTitle = title;
  let counterText = "";
  let hasRight = false;
  let hasLeft = false;
  let handleRight: (() => void) | undefined;
  let handleLeft: (() => void) | undefined;

  if (isGalleryMode) {
    const total = photos.length;
    const safeIdx = Math.max(0, Math.min(index, total - 1));
    const item = photos[safeIdx];
    currentSrc = typeof item === "string" ? item : item.src;
    if (typeof item === "object" && item.title) {
      currentTitle = item.title;
    }
    counterText = `${toArabicDigits(safeIdx + 1)} من ${toArabicDigits(total)}`;

    // In RTL, ChevronRight (>) navigates to previous (earlier item), ChevronLeft (<) navigates to next (later item)
    if (safeIdx > 0) {
      hasRight = true;
      handleRight = () => {
        onIndexChange?.(safeIdx - 1);
        onPrev?.();
      };
    }
    if (safeIdx < total - 1) {
      hasLeft = true;
      handleLeft = () => {
        onIndexChange?.(safeIdx + 1);
        onNext?.();
      };
    }
  } else if (photo) {
    currentSrc = photo.src;
    currentTitle = photo.title || title;
    hasRight = Boolean(onPrev);
    handleRight = onPrev;
    hasLeft = Boolean(onNext);
    handleLeft = onNext;
  }

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
      } else if (e.key === "ArrowRight" && hasRight && handleRight) {
        handleRight();
      } else if (e.key === "ArrowLeft" && hasLeft && handleLeft) {
        handleLeft();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, hasRight, hasLeft, handleRight, handleLeft, onClose]);

  if (!mounted || !isOpen || !currentSrc) return null;

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-label="معاينة الصورة"
      className="fixed inset-0 z-[9999] bg-black/85 backdrop-blur-md flex items-center justify-center p-3 sm:p-6 select-none animate-in fade-in duration-150"
      onClick={onClose}
    >
      <div
        className="relative max-w-5xl w-full max-h-[92vh] bg-slate-900 rounded-2xl overflow-hidden shadow-2xl flex flex-col border border-slate-700/80 animate-in zoom-in-95 duration-150"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-4 sm:px-6 py-3 bg-slate-950 text-white border-b border-slate-800">
          <div className="flex items-center gap-3 min-w-0">
            <span className="text-sm sm:text-base font-bold truncate text-slate-100">
              {currentTitle || "معاينة الصورة"}
            </span>
            {counterText && (
              <span className="text-xs bg-slate-800 text-slate-300 px-2.5 py-0.5 rounded-full font-medium shrink-0">
                {counterText}
              </span>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="إغلاق"
            className="p-1.5 rounded-xl text-slate-400 hover:text-white hover:bg-slate-800 transition cursor-pointer"
          >
            <X className="size-5" />
          </button>
        </div>

        {/* Content Image & Navigation Buttons */}
        <div className="relative p-2 sm:p-4 flex items-center justify-center bg-black/60 overflow-hidden flex-1 min-h-[350px]">
          {/* Right Navigation Button (Previous in RTL) */}
          {hasRight && handleRight && (
            <button
              type="button"
              onClick={handleRight}
              aria-label="الصورة السابقة"
              className="absolute right-3 sm:right-6 z-20 p-2.5 sm:p-3 rounded-full bg-slate-900/80 hover:bg-slate-900 text-white border border-white/20 transition shadow-xl hover:scale-105 active:scale-95 cursor-pointer"
            >
              <ChevronRight className="size-6" />
            </button>
          )}

          {/* Image */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={currentSrc}
            alt={currentTitle || "معاينة الصورة"}
            className="max-h-[75vh] w-auto max-w-full object-contain rounded-lg shadow-xl pointer-events-auto"
          />

          {/* Left Navigation Button (Next in RTL) */}
          {hasLeft && handleLeft && (
            <button
              type="button"
              onClick={handleLeft}
              aria-label="الصورة التالية"
              className="absolute left-3 sm:left-6 z-20 p-2.5 sm:p-3 rounded-full bg-slate-900/80 hover:bg-slate-900 text-white border border-white/20 transition shadow-xl hover:scale-105 active:scale-95 cursor-pointer"
            >
              <ChevronLeft className="size-6" />
            </button>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}
