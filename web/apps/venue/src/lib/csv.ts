/**
 * Quotes a CSV cell and neutralizes spreadsheet formula injection: a value starting with = + - @ (or a tab/CR) is
 * prefixed with a quote so Excel shows it as text instead of running it.
 */
export function csvCell(value: string): string {
  const safe = /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
  return /[",\n\r]/.test(safe) || safe !== value ? `"${safe.replace(/"/g, '""')}"` : safe;
}

/** Builds a CSV (UTF-8 with BOM, so Excel shows Arabic) and saves it in the browser. */
export function downloadCsv(fileName: string, header: string[], rows: string[][]) {
  const text = [header, ...rows].map((row) => row.map(csvCell).join(",")).join("\r\n");
  const blob = new Blob(["﻿", text], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}
