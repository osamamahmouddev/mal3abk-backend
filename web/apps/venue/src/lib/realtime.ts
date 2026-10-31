"use client";

import { useEffect, useRef, useState } from "react";
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

/** Public API origin for the WebSocket (the BFF cannot proxy it). Without it the dashboard falls back to polling. */
const HUB_URL = process.env.NEXT_PUBLIC_API_URL ? `${process.env.NEXT_PUBLIC_API_URL.replace(/\/+$/, "")}/hubs/booking` : null;

/**
 * Live booking updates for venue staff. Every (re)connect asks the BFF for a fresh 60-second, single-use ticket (the access
 * token never reaches the browser), joins the venue's staff room, and refreshes booking data when something changes.
 */
export function useVenueRealtime(venueId: string | null, onUpdate?: (update: BookingUpdated) => void) {
  const queryClient = useQueryClient();
  const onUpdateRef = useRef(onUpdate);
  onUpdateRef.current = onUpdate;
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
      queryClient.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith("booking") || q.queryKey[0] === "dashboard" || q.queryKey[0] === "proofs" });
      onUpdateRef.current?.(update);
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
      if (connection.state === HubConnectionState.Connected) void connection.invoke("LeaveStaffRoom", venueId).catch(() => undefined);
      void connection.stop();
    };
  }, [venueId, queryClient]);

  return { connected, enabled: !!HUB_URL };
}
