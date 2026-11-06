import { Bike, Car, Check, Clock, KeyRound, Package, Phone, Store } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, ApiError } from "../api";
import { LiveBadge } from "../components/Badges";
import { MapView, type Line, type Pin } from "../components/MapView";
import { Empty, Loading } from "../components/States";
import { formatTime, initials, relative } from "../format";
import { useNow } from "../hooks";
import { useTrack } from "../live";
import type { TrackView } from "../types";

const steps = [
  { key: "placed", label: "Order placed" },
  { key: "assigned", label: "Courier on the way" },
  { key: "picked", label: "Out for delivery" },
  { key: "delivered", label: "Delivered" },
] as const;

function stepOf(v: TrackView) {
  if (v.status === "Delivered") return 3;
  if (v.status === "PickedUp") return 2;
  if (v.status === "Assigned") return 1;
  return 0;
}

/** What the customer sees from the link: no sign-in, no personal details, the courier moving towards them. */
export function TrackPage({ token }: { token: string }) {
  const [view, setView] = useState<TrackView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const now = useNow(20_000);
  useEffect(() => {
    api.track(token).then(setView, (e: ApiError) => setError(e.status === 404 ? "gone" : e.message));
  }, [token]);
  const live = useTrack(token, useCallback((v: TrackView) => setView(v), []));

  const pins = useMemo<Pin[]>(() => {
    if (!view) return [];
    const list: Pin[] = [
      { id: "p", ...view.pickup, label: view.merchantName, text: initials(view.merchantName), tone: "ink", shape: "square" },
      { id: "d", ...view.dropoff, label: "You", text: "", tone: "accent", shape: "square" },
    ];
    if (view.courier?.latitude != null && view.courier.longitude != null) {
      list.push({ id: "c", latitude: view.courier.latitude, longitude: view.courier.longitude, label: view.courier.firstName, text: initials(view.courier.firstName), tone: "accent" });
    }
    return list;
  }, [view]);
  const lines = useMemo<Line[]>(() => (view ? [{ points: [[view.pickup.latitude, view.pickup.longitude], [view.dropoff.latitude, view.dropoff.longitude]], tone: "muted", dashed: true }] : []), [view]);

  if (error) {
    return (
      <div className="track-page"><div className="track-card card">
        <Empty title={error === "gone" ? "This tracking link has ended" : "Could not load your delivery"} icon={<Package size={26} />}>
          <span className="muted measure">{error === "gone" ? "Links stop working a day after the delivery. Contact the shop if something is wrong." : error}</span>
        </Empty>
      </div></div>
    );
  }
  if (!view) return <div className="track-page"><Loading label="Finding your delivery" /></div>;

  const step = stepOf(view);
  const cancelled = view.status === "Cancelled";
  const Vehicle = view.courier?.vehicle === "Car" ? Car : Bike;

  return (
    <div className="track-page">
      <div className="track-card card">
        <div className="track-head">
          <div className="grow min0">
            <span className="mono muted small">{view.code} · {view.merchantName}</span>
            <h1>{cancelled ? "Order cancelled" : view.status === "Delivered" ? "Delivered" : view.estimatedDeliveryAt ? `Arriving around ${formatTime(view.estimatedDeliveryAt)}` : "Your order is being prepared"}</h1>
            {!cancelled && view.status !== "Delivered" && view.estimatedDeliveryAt && <p className="muted">{relative(view.estimatedDeliveryAt, now)}</p>}
            {view.status === "Delivered" && view.deliveredAt && <p className="muted">at {formatTime(view.deliveredAt)}</p>}
          </div>
          <LiveBadge state={live} />
        </div>

        {!cancelled && (
          <ol className="track-steps">
            {steps.map((s, i) => (
              <li key={s.key} className={i < step ? "done" : i === step ? "now" : ""}>
                <span className="track-dot" aria-hidden="true">{i < step || view.status === "Delivered" ? <Check size={12} /> : null}</span>{s.label}
              </li>
            ))}
          </ol>
        )}

        {view.deliveryCode && (
          <div className="track-code">
            <KeyRound size={18} />
            <span className="grow">Give this code to the courier at the door</span>
            <strong className="code-big">{view.deliveryCode}</strong>
          </div>
        )}

        <MapView pins={pins} lines={lines} height={300} fitKey={view.code} />

        <div className="track-facts">
          {view.courier ? (
            <div className="row gap-10"><span className="avatar"><Vehicle size={16} /></span>
              <span className="min0"><strong className="block">{view.courier.firstName}</strong><span className="small muted">{view.courier.locationAt ? `seen ${relative(view.courier.locationAt, now)}` : "your courier"}</span></span></div>
          ) : !cancelled && <div className="row gap-10"><span className="avatar"><Clock size={16} /></span><span className="small muted">Finding a courier near {view.merchantName}…</span></div>}
          <div className="row gap-10"><span className="avatar"><Store size={16} /></span>
            <span className="min0"><strong className="block">{view.merchantName}</strong>{view.merchantPhone && <a className="small" href={`tel:${view.merchantPhone}`}><Phone size={12} />{view.merchantPhone}</a>}</span></div>
        </div>
      </div>
      <p className="small faint center-text">Delivered by Kuryo · this page updates by itself</p>
    </div>
  );
}
