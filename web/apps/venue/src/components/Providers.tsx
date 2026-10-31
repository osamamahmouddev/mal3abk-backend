"use client";

import React, {
    createContext,
    useContext,
    useEffect,
    useMemo,
    useState,
} from "react";
import {
    QueryClient,
    QueryClientProvider,
    useQuery,
} from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { api, unwrap } from "@/lib/api";

type Venue = Schemas["VenueDto"];

interface VenueContextValue {
    venues: Venue[];
    venue: Venue | null;
    venueId: string | null;
    isLoading: boolean;
    selectVenue: (id: string) => void;
    /** True when the signed-in user's role at the selected venue includes the permission (e.g. "venue.bookings.approve"). */
    can: (permission: string) => boolean;
    /** Whether a session exists (pages reachable while signed out use it to adapt). */
    signedIn: boolean;
}

const defaultVenueContext: VenueContextValue = {
    venues: [],
    venue: null,
    venueId: null,
    isLoading: false,
    selectVenue: () => {},
    can: () => false,
    signedIn: false,
};

const VenueContext = createContext<VenueContextValue>(defaultVenueContext);
const STORAGE_KEY = "malabak.venueId";

function VenueProvider({
    children,
    signedIn,
}: {
    children: React.ReactNode;
    signedIn: boolean;
}) {
    const [selected, setSelected] = useState<string | null>(null);
    const venues = useQuery({
        queryKey: ["venues"],
        enabled: signedIn,
        queryFn: () => unwrap(api.GET("/api/v1/venue-manager/venues")),
    });

    // The API enforces every permission; this only hides actions the role cannot take.
    const permissions = useQuery({
        queryKey: ["my-permissions"],
        enabled: signedIn,
        staleTime: 5 * 60_000,
        queryFn: () => unwrap(api.GET("/api/v1/me/permissions")),
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
        const granted = new Set(permissions.data?.venues.find((v) => v.venueId === venue?.id)?.permissions ?? []);
        return {
            venues: list,
            venue,
            venueId: venue?.id ?? null,
            isLoading: signedIn ? venues.isLoading : false,
            can: (permission) => granted.has(permission),
            signedIn,
            selectVenue: (id) => {
                setSelected(id);
                try {
                    window.localStorage.setItem(STORAGE_KEY, id);
                } catch {
                    // ignore
                }
            },
        };
    }, [venues.data, venues.isLoading, selected, signedIn, permissions.data]);

    return (
        <VenueContext.Provider value={value}>{children}</VenueContext.Provider>
    );
}

export function useVenue(): VenueContextValue {
    const context = useContext(VenueContext);
    return context ?? defaultVenueContext;
}

export function Providers({
    children,
    signedIn,
}: {
    children: React.ReactNode;
    signedIn: boolean;
}) {
    const [client] = useState(
        () =>
            new QueryClient({
                defaultOptions: {
                    queries: {
                        staleTime: 30_000,
                        retry: 1,
                        refetchOnWindowFocus: false,
                    },
                },
            }),
    );
    return (
        <QueryClientProvider client={client}>
            <VenueProvider signedIn={signedIn}>{children}</VenueProvider>
        </QueryClientProvider>
    );
}
