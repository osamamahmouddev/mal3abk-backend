"use client";

import React, { useState } from "react";
import { ImagePlus, Loader2 } from "lucide-react";
import { toArabicDigits } from "./ui";

const ACCEPT = ["image/jpeg", "image/png", "image/webp"];
const PARALLEL = 3;

/**
 * "Add photos" tile: pick several files at once or drop them on it. Uploads run a few at a time with a running count;
 * files of another type are skipped and reported once.
 */
export function PhotoDropTile({
  upload,
  onDone,
  onError,
  label = "إضافة صور",
  className = "aspect-[4/3]",
}: {
  /** Uploads one file and records it (gallery call or form state). */
  upload: (file: File) => Promise<void>;
  onDone?: () => unknown;
  onError: (message: string) => void;
  label?: string;
  className?: string;
}) {
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null);
  const [over, setOver] = useState(false);
  const busy = progress !== null;

  const run = async (list: FileList | File[] | null) => {
    const all = Array.from(list ?? []);
    const files = all.filter((f) => ACCEPT.includes(f.type));
    if (files.length < all.length) onError("فيه ملفات اتسابت: المسموح صور JPG أو PNG أو WebP بس.");
    if (files.length === 0) return;

    setProgress({ done: 0, total: files.length });
    const failures: string[] = [];
    let next = 0;
    const worker = async () => {
      while (next < files.length) {
        const file = files[next++];
        try {
          await upload(file);
        } catch (e) {
          failures.push((e as Error).message);
        }
        setProgress((p) => (p ? { ...p, done: p.done + 1 } : p));
      }
    };
    await Promise.all(Array.from({ length: Math.min(PARALLEL, files.length) }, worker));
    await onDone?.();
    setProgress(null);
    if (failures.length) onError(`${toArabicDigits(failures.length)} من الصور مترفعتش: ${failures[0]}`);
  };

  return (
    <label
      onDragOver={(e) => {
        e.preventDefault();
        if (!busy) setOver(true);
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => {
        e.preventDefault();
        setOver(false);
        if (!busy) run(e.dataTransfer.files);
      }}
      className={`rounded-xl border-2 border-dashed flex flex-col items-center justify-center gap-1 px-2 text-center text-xs cursor-pointer transition-all duration-150 ${
        over ? "border-brand-red bg-[#FDECEA] text-brand-red scale-[1.02]" : "border-[#C9CFD9] hover:border-navy-900 text-[#5B6475] hover:text-navy-900 bg-slate-50/70 hover:bg-slate-100"
      } ${busy ? "cursor-wait" : ""} ${className}`}
    >
      {busy ? <Loader2 className="size-5 animate-spin" /> : <ImagePlus className="size-5" strokeWidth={1.75} />}
      <span className="text-[11px] font-semibold">
        {busy ? `بيترفع ${toArabicDigits(progress.done)} من ${toArabicDigits(progress.total)}` : over ? "سيب الصور هنا" : label}
      </span>
      {!busy && !over && <span className="text-[10px] leading-tight opacity-80">اختار كذا صورة مرة واحدة أو اسحبهم هنا</span>}
      {busy && (
        <span className="w-3/4 h-1 rounded-full bg-[#E4E7EC] overflow-hidden" aria-hidden="true">
          <span className="block h-full bg-navy-900 transition-[width] duration-300" style={{ width: `${(progress.done / progress.total) * 100}%` }} />
        </span>
      )}
      <input
        type="file"
        multiple
        accept={ACCEPT.join(",")}
        className="hidden"
        disabled={busy}
        onChange={(e) => {
          run(e.target.files);
          e.target.value = "";
        }}
      />
    </label>
  );
}
