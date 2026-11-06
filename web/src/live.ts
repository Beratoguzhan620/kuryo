import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { currentToken } from "./api";
import type { Courier, Offer, OrderSummary, TrackView } from "./types";

export type LiveState = "connecting" | "live" | "reconnecting" | "offline";

export interface LiveHandlers {
  orderChanged?: (order: OrderSummary) => void;
  courierChanged?: (courier: Courier) => void;
  offerChanged?: (offer: Offer | null) => void;
  /** After a reconnect messages may have been missed: reload what the page shows. */
  resync?: () => void;
}

/** Waits a little longer after each failed attempt, up to half a minute, and never gives up. */
const backoff = { nextRetryDelayInMilliseconds: (ctx: { previousRetryCount: number }) => Math.min(30_000, 1000 * 2 ** Math.min(ctx.previousRetryCount, 5)) };

function connect(path: string, withToken: boolean): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(path, withToken ? { accessTokenFactory: () => currentToken() ?? "" } : {})
    .withAutomaticReconnect(backoff)
    .configureLogging(LogLevel.None)
    .build();
}

/**
 * The live feed of one company for as long as the page is open. Every message carries the full current state, so
 * handlers replace what they have; after a reconnect the page reloads once to cover what it missed.
 */
export function useLive(companyId: string | undefined, handlers: LiveHandlers): LiveState {
  const [state, setState] = useState<LiveState>("connecting");
  const latest = useRef(handlers);
  latest.current = handlers;

  useEffect(() => {
    if (!companyId) return;
    const connection = connect("/hubs/live", true);
    let stopped = false;
    connection.on("OrderChanged", (o: OrderSummary) => latest.current.orderChanged?.(o));
    connection.on("CourierChanged", (c: Courier) => latest.current.courierChanged?.(c));
    connection.on("OfferChanged", (o: Offer | null) => latest.current.offerChanged?.(o));
    connection.onreconnecting(() => setState("reconnecting"));
    connection.onreconnected(async () => {
      await connection.invoke("Watch", companyId).catch(() => {});
      setState("live");
      latest.current.resync?.();
    });
    connection.onclose(() => { if (!stopped) setState("offline"); });

    const start = async (attempt: number): Promise<void> => {
      try {
        await connection.start();
        await connection.invoke("Watch", companyId);
        if (stopped) return;
        setState("live");
        // Anything that changed between the page's first load and joining the feed would otherwise be missed.
        latest.current.resync?.();
      } catch {
        if (stopped) return;
        setState("offline");
        setTimeout(() => { if (!stopped && connection.state === HubConnectionState.Disconnected) void start(attempt + 1); }, Math.min(30_000, 2000 * (attempt + 1)));
      }
    };
    void start(0);
    return () => {
      stopped = true;
      void connection.stop();
    };
  }, [companyId]);

  return state;
}

/** The customer's live view of one delivery. */
export function useTrack(token: string, onView: (view: TrackView) => void): LiveState {
  const [state, setState] = useState<LiveState>("connecting");
  const latest = useRef(onView);
  latest.current = onView;

  useEffect(() => {
    const connection = connect("/hubs/track", false);
    let stopped = false;
    connection.on("TrackChanged", (v: TrackView) => latest.current(v));
    const follow = async () => latest.current(await connection.invoke<TrackView>("Follow", token));
    connection.onreconnecting(() => setState("reconnecting"));
    connection.onreconnected(async () => { await follow().catch(() => {}); setState("live"); });
    connection.onclose(() => { if (!stopped) setState("offline"); });
    connection.start().then(follow).then(() => { if (!stopped) setState("live"); }, () => { if (!stopped) setState("offline"); });
    return () => {
      stopped = true;
      void connection.stop();
    };
  }, [token]);

  return state;
}
