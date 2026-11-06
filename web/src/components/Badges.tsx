import { courierLabel, courierTone, statusLabel, statusTone } from "../format";
import type { LiveState } from "../live";
import type { CourierStatus, OrderStatus } from "../types";

export function StatusBadge({ status }: { status: OrderStatus }) {
  return <span className={`badge ${statusTone[status]}`}>{statusLabel[status]}</span>;
}

export function CourierBadge({ status }: { status: CourierStatus }) {
  return <span className={`badge ${courierTone[status]}`}><span className={`dot ${status}`} aria-hidden="true" />{courierLabel[status]}</span>;
}

const liveText: Record<LiveState, string> = { connecting: "Connecting", live: "Live", reconnecting: "Reconnecting", offline: "Offline" };

/** Whether the screen is receiving live updates. */
export function LiveBadge({ state }: { state: LiveState }) {
  return <span className={`live-badge ${state}`} role="status"><span className="dot" aria-hidden="true" />{liveText[state]}</span>;
}
