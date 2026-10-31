"use client";

import React, { createContext, useContext, useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, HubConnectionState, HttpTransportType, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";

export interface BookingUpdated {
  bookingId: string;
  reference: string;
  venueId: string;
  status: string;
  updatedAtUtc: string;
}

type Listener = (update: BookingUpdated) => void;

interface RealtimeValue {
  /** True while the live connection is up and joined to the venue's staff room. */
  connected: boolean;
  /** False when NEXT_PUBLIC_API_URL is not set: pages keep polling instead. */
  enabled: boolean;
  /** Subscribe to booking updates; returns the unsubscribe function. */
  subscribe: (listener: Listener) => () => void;
}

const RealtimeContext = createContext<RealtimeValue>({ connected: false, enabled: false, subscribe: () => () => undefined });

/** Public API origin for the WebSocket (the BFF cannot proxy it). */
const HUB_URL = process.env.NEXT_PUBLIC_API_URL ? `${process.env.NEXT_PUBLIC_API_URL.replace(/\/+$/, "")}/hubs/booking` : null;

/** Query keys refreshed on every booking change (bookings, calendar, dashboard, proofs and the notification centre). */
const LIVE_KEYS = ["dashboard", "proofs", "calendar", "notifications", "customers"];

/**
 * One live connection for the whole dashboard. Every (re)connect asks the BFF for a fresh 60-second, single-use ticket
 * (the access token never reaches the browser) and joins the selected venue's staff room.
 */
export function RealtimeProvider({ venueId, children }: { venueId: string | null; children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const listeners = useRef(new Set<Listener>());
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!venueId || !HUB_URL) return;

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
        accessTokenFactory: async () => {
          const { data, response } = await api.POST("/api/v1/realtime/ticket");
          if (!response.ok) throw new Error("realtime ticket refused");
          return (data as unknown as { ticket: string }).ticket;
        },
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("BookingUpdated", (update: BookingUpdated) => {
      if (update.venueId !== venueId) return;
      queryClient.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith("booking") || LIVE_KEYS.includes(String(q.queryKey[0])) });
      listeners.current.forEach((listener) => listener(update));
    });

    const join = () => connection.invoke("JoinStaffRoom", venueId).then(() => setConnected(true));
    connection.onreconnected(() => void join().catch(() => setConnected(false)));
    connection.onreconnecting(() => setConnected(false));
    connection.onclose(() => setConnected(false));

    let stopped = false;
    connection
      .start()
      .then(() => (stopped ? undefined : join()))
      .catch(() => setConnected(false));

    return () => {
      stopped = true;
      setConnected(false);
      if (connection.state === HubConnectionState.Connected) void connection.invoke("LeaveStaffRoom", venueId).catch(() => undefined);
      void connection.stop();
    };
  }, [venueId, queryClient]);

  const value = React.useMemo<RealtimeValue>(
    () => ({
      connected,
      enabled: !!HUB_URL,
      subscribe: (listener) => {
        listeners.current.add(listener);
        return () => listeners.current.delete(listener);
      },
    }),
    [connected],
  );

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

/** Live status, and an optional callback for each booking update at the selected venue. */
export function useVenueRealtime(onUpdate?: Listener) {
  const value = useContext(RealtimeContext);
  const callback = useRef(onUpdate);
  callback.current = onUpdate;
  useEffect(() => value.subscribe((update) => callback.current?.(update)), [value]);
  return { connected: value.connected, enabled: value.enabled };
}
