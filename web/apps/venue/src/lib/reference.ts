"use client";

import { useQuery } from "@tanstack/react-query";
import { api, unwrap } from "./api";

const day = 24 * 60 * 60 * 1000;

/** Reference lists (governorates, facilities, surfaces). They rarely change, so they are cached for a day. */
export function useReference() {
  const governorates = useQuery({
    queryKey: ["reference", "governorates"],
    queryFn: () => unwrap(api.GET("/api/v1/reference/governorates")),
    staleTime: day,
  });
  const facilities = useQuery({
    queryKey: ["reference", "facilities"],
    queryFn: () => unwrap(api.GET("/api/v1/reference/facilities")),
    staleTime: day,
  });
  const surfaces = useQuery({
    queryKey: ["reference", "surfaces"],
    queryFn: () => unwrap(api.GET("/api/v1/reference/surfaces")),
    staleTime: day,
  });
  return {
    governorates: governorates.data ?? [],
    facilities: facilities.data ?? [],
    surfaces: surfaces.data ?? [],
  };
}
