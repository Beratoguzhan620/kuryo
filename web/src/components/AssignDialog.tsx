import { Bike } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { useCompany } from "../context";
import { useLoad } from "../hooks";
import { distanceKm, isFresh, km, relative, vehicleLabel } from "../format";
import type { Order, Place } from "../types";
import { Dialog } from "./Dialog";
import { Empty, Failed, Loading } from "./States";

/** Picks a free courier for an order by hand, nearest to the pickup first. */
export function AssignDialog({ orderId, code, pickup, onClose, onDone }: { orderId: string; code: string; pickup: Pick<Place, "latitude" | "longitude">; onClose: () => void; onDone: (order: Order) => void }) {
  const company = useCompany();
  const [couriers, reload] = useLoad(() => api.couriers(company.id), [company.id]);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const free = (couriers.data ?? [])
    .filter((c) => c.status === "Available")
    .map((c) => ({ ...c, away: c.latitude !== null && c.longitude !== null ? distanceKm({ latitude: c.latitude, longitude: c.longitude }, pickup) : null }))
    .sort((a, b) => (a.away ?? 1e9) - (b.away ?? 1e9));

  async function assign(courierId: string) {
    setBusy(courierId);
    setError(null);
    try {
      onDone(await api.assign(company.id, orderId, courierId));
    } catch (e) {
      setError((e as Error).message);
      setBusy(null);
      reload();
    }
  }

  return (
    <Dialog title={`Assign ${code}`} onClose={onClose}>
      <p className="small muted">Free couriers, nearest to the pickup first. Any offer still out for this order is taken back.</p>
      {couriers.state === "failed" ? <Failed message={couriers.error} onRetry={reload} />
        : !couriers.data ? <Loading label="Finding couriers" />
        : free.length === 0 ? <Empty title="No courier is free" icon={<Bike size={22} />}><span className="small muted">Couriers who are online and not carrying an order appear here.</span></Empty>
        : (
          <ul className="pick-list">
            {free.map((c) => (
              <li key={c.userId}>
                <button onClick={() => assign(c.userId)} disabled={busy !== null} aria-label={`Assign to ${c.displayName}`}>
                  <span className="grow min0">
                    <strong className="block ellipsis">{c.displayName}</strong>
                    <span className="small muted">{vehicleLabel[c.vehicle]}{c.plate && ` · ${c.plate}`} · {c.locationAt ? `seen ${relative(c.locationAt)}` : "no location yet"}</span>
                  </span>
                  <span className={`small nowrap ${c.away !== null && isFresh(c.locationAt) ? "" : "faint"}`}>{busy === c.userId ? "Assigning…" : c.away !== null ? km(c.away) : "—"}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      {error && <p className="form-error" role="alert">{error}</p>}
    </Dialog>
  );
}
