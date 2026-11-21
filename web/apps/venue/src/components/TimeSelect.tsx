"use client";

import React from "react";
import { toArabicDigits } from "./ui";

/** "18:30" → "٦:٣٠ م", with a plain-words hint for the hours people misread (noon, midnight, after midnight). */
export function timeLabel(hhmm: string, opts: { nextDay?: boolean } = {}): string {
  const [h, m] = hhmm.split(":").map(Number);
  const hour12 = h % 12 || 12;
  const base = `${toArabicDigits(hour12)}:${toArabicDigits(String(m).padStart(2, "0"))} ${h < 12 ? "ص" : "م"}`;
  if (h === 0 && m === 0) return `${base} (نص الليل)`;
  if (h === 12 && m === 0) return `${base} (الضهر)`;
  if (opts.nextDay) return `${base} (بعد نص الليل)`;
  return base;
}

const pad = (n: number) => String(n).padStart(2, "0");

/** Every step through the day, starting at 6 AM so a late-night closing time sits at the end of the list. */
function options(step: number) {
  const list: string[] = [];
  for (let i = 0; i < (24 * 60) / step; i++) {
    const minutes = (6 * 60 + i * step) % (24 * 60);
    list.push(`${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`);
  }
  return list;
}

/**
 * Clock-free time picker: native time inputs show 24-hour or locale formats that owners misread ("02:00" for 2 AM).
 * Pass `after` on a closing time so hours past midnight are labelled as the next morning.
 */
export function TimeSelect({
  value,
  onChange,
  after,
  step = 30,
  ariaLabel,
  className = "",
}: {
  value: string;
  onChange: (value: string) => void;
  after?: string;
  step?: number;
  ariaLabel: string;
  className?: string;
}) {
  const list = options(step);
  if (value && !list.includes(value)) list.unshift(value);
  return (
    <select aria-label={ariaLabel} value={value} onChange={(e) => onChange(e.target.value)} className={`cursor-pointer ${className}`}>
      {list.map((t) => (
        <option key={t} value={t}>
          {timeLabel(t, { nextDay: after !== undefined && t <= after })}
        </option>
      ))}
    </select>
  );
}

/** One readable sentence for a day's hours, e.g. "من ٦:٠٠ م لحد ٢:٠٠ ص (بعد نص الليل)". */
export function hoursSentence(open: string, close: string): string {
  if (open === close) return "مفتوح ٢٤ ساعة";
  return `من ${timeLabel(open)} لحد ${timeLabel(close, { nextDay: close < open })}`;
}
