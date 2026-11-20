"use client";

import { useQuery } from "@tanstack/react-query";
import { api, unwrap } from "./api";

/**
 * Venues keep the city and area as they were entered: a code ("cairo"), an English name ("Nasr City") or an Arabic one
 * ("مدينة نصر"). This shows any of them in Arabic from the reference list, and leaves a name it doesn't know as typed.
 */
export function usePlaceNames() {
  const governorates = useQuery({
    queryKey: ["reference-governorates"],
    queryFn: () => unwrap(api.GET("/api/v1/reference/governorates")),
    staleTime: 5 * 60_000,
  });

  // Until the list loads, or if it fails, names show as stored.
  const list = Array.isArray(governorates.data) ? governorates.data : [];
  const same = (a: string, b?: string | null) => !!b && a.trim().toLowerCase() === b.trim().toLowerCase();

  const city = (value?: string | null) => {
    if (!value) return "";
    return list.find((g) => same(value, g.code) || same(value, g.nameEn) || same(value, g.nameAr))?.nameAr ?? value;
  };

  const area = (value?: string | null, cityValue?: string | null) => {
    if (!value) return "";
    const inCity = list.find((g) => cityValue && (same(cityValue, g.code) || same(cityValue, g.nameEn) || same(cityValue, g.nameAr)));
    const pool = inCity ? [inCity, ...list] : list;
    for (const g of pool) {
      const hit = g.areas.find((a) => same(value, a.code) || same(value, a.nameEn) || same(value, a.nameAr));
      if (hit) return hit.nameAr;
    }
    return value;
  };

  return { city, area };
}
