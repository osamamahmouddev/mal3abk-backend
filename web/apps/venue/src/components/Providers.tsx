"use client";

import React, { createContext, useContext, useEffect, useMemo, useState } from "react";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { api, unwrap } from "@/lib/api";

type Venue = Schemas["VenueDto"];

interface VenueContextValue {
  venues: Venue[];
  venue: Venue | null;
  venueId: string | null;
  isLoading: boolean;
  selectVenue: (id: string) => void;
}

const VenueContext = createContext<VenueContextValue | null>(null);
const STORAGE_KEY = "malabak.venueId";

function VenueProvider({ children }: { children: React.ReactNode }) {
  const [selected, setSelected] = useState<string | null>(null);
  const venues = useQuery({
    queryKey: ["venues"],
    queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues")),
  });

  useEffect(() => {
    try {
      setSelected(window.localStorage.getItem(STORAGE_KEY));
    } catch {
      // storage unavailable: fall back to the first venue
    }
  }, []);

  const value = useMemo<VenueContextValue>(() => {
    const list = venues.data ?? [];
    const venue = list.find((v) => v.id === selected) ?? list[0] ?? null;
    return {
      venues: list,
      venue,
      venueId: venue?.id ?? null,
      isLoading: venues.isLoading,
      selectVenue: (id) => {
        setSelected(id);
        try {
          window.localStorage.setItem(STORAGE_KEY, id);
        } catch {
          // ignore
        }
      },
    };
  }, [venues.data, venues.isLoading, selected]);

  return <VenueContext.Provider value={value}>{children}</VenueContext.Provider>;
}

export function useVenue(): VenueContextValue {
  const context = useContext(VenueContext);
  if (!context) throw new Error("useVenue must be used inside <Providers>.");
  return context;
}

export function Providers({ children, signedIn }: { children: React.ReactNode; signedIn: boolean }) {
  const [client] = useState(
    () => new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false } } }),
  );
  return (
    <QueryClientProvider client={client}>{signedIn ? <VenueProvider>{children}</VenueProvider> : children}</QueryClientProvider>
  );
}
