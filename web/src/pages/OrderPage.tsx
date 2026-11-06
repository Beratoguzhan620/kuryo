import { ArrowLeft, Check, Copy, ExternalLink, Phone, RotateCcw, UserRoundCheck, X } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { api } from "../api";
import { AssignDialog } from "../components/AssignDialog";
import { StatusBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { MapView, type Line, type Pin } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { isDispatcher, useCompany } from "../context";
import { eventLabel, formatDayTime, formatMoney, formatTime, initials, km, relative, vehicleLabel } from "../format";
import { useLoad } from "../hooks";
import { useLive } from "../live";
import { linkProps } from "../router";
import type { Order } from "../types";

type Action = "assign" | "release" | "cancel" | "confirm" | null;

export function OrderPage({ orderId }: { orderId: string }) {
  const company = useCompany();
  const [order, reload, setOrder] = useLoad(() => api.order(company.id, orderId), [company.id, orderId]);
  const courierId = order.data?.courier?.id;
  const [trail, reloadTrail] = useLoad(
    () => (courierId && isDispatcher(company) ? api.trail(company.id, courierId, orderId) : Promise.resolve([])),
    [company.id, orderId, courierId],
  );
  const [action, setAction] = useState<Action>(null);
  const [copied, setCopied] = useState(false);

  useLive(company.id, {
    orderChanged: useCallback((o: { id: string }) => { if (o.id === orderId) reload(); }, [orderId, reload]),
    courierChanged: useCallback((c: { userId: string }) => { if (c.userId === courierId) { reload(); reloadTrail(); } }, [courierId, reload, reloadTrail]),
    resync: reload,
  });

  const o = order.data;
  const pins = useMemo<Pin[]>(() => {
    if (!o) return [];
    const list: Pin[] = [
      { id: "p", latitude: o.pickup.latitude, longitude: o.pickup.longitude, label: `Pickup: ${o.merchant.name}`, text: initials(o.merchant.name), tone: "ink", shape: "square" },
      { id: "d", latitude: o.dropoff.latitude, longitude: o.dropoff.longitude, label: `Drop-off: ${o.dropoff.address}`, text: "", tone: "accent", shape: "square" },
    ];
    if (o.courier?.latitude != null && o.courier.longitude != null && (o.status === "Assigned" || o.status === "PickedUp")) {
      list.push({ id: "c", latitude: o.courier.latitude, longitude: o.courier.longitude, label: o.courier.name, text: initials(o.courier.name), tone: "accent" });
    }
    return list;
  }, [o]);
  const lines = useMemo<Line[]>(() => {
    if (!o) return [];
    const result: Line[] = [{ points: [[o.pickup.latitude, o.pickup.longitude], [o.dropoff.latitude, o.dropoff.longitude]], tone: "muted", dashed: true }];
    if (trail.data && trail.data.length > 1) result.push({ points: trail.data.map((p) => [p.latitude, p.longitude]), tone: "accent" });
    return result;
  }, [o, trail.data]);

  if (order.state === "failed" && !o) return <Failed message={order.error} onRetry={reload} />;
  if (!o) return <Loading />;

  const dispatcher = isDispatcher(company);
  const canCancel = o.status !== "Delivered" && o.status !== "Cancelled" && (dispatcher || o.status !== "PickedUp");
  const link = o.trackingToken ? `${location.origin}/t/${o.trackingToken}` : null;

  async function copy() {
    if (!link) return;
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard can be blocked; the link is also there to open.
    }
  }

  return (
    <div className="order-page">
      <a className="back" {...linkProps(`/c/${company.id}/orders`)}><ArrowLeft size={14} />Orders</a>
      <div className="page-head">
        <div>
          <span className="mono muted">{o.code}</span>
          <h1>{o.customerName}</h1>
          <div className="row wrap gap-6 head-badges"><StatusBadge status={o.status} /><span className="badge plain">{o.zoneName} · {formatMoney(o.deliveryFee)}</span><span className="badge plain">{km(o.distanceKm)}</span></div>
        </div>
        <div className="row wrap gap-6">
          {dispatcher && (o.status === "Searching" || o.status === "Unassigned") && <button className="button primary" onClick={() => setAction("assign")}><UserRoundCheck size={16} />Assign</button>}
          {dispatcher && o.status === "Unassigned" && <button className="button" onClick={async () => setOrder(await api.searchAgain(company.id, o.id))}><RotateCcw size={16} />Offer again</button>}
          {dispatcher && o.status === "Assigned" && <button className="button" onClick={() => setAction("release")}>Take back</button>}
          {dispatcher && o.status === "PickedUp" && <button className="button" onClick={() => setAction("confirm")}><Check size={16} />Confirm delivered</button>}
          {canCancel && <button className="button danger" onClick={() => setAction("cancel")}><X size={16} />Cancel</button>}
        </div>
      </div>

      <div className="order-layout">
        <div className="stack">
          <section className="card section">
            <div className="facts">
              <div><span className="label">Collect from</span><strong>{o.merchant.name}</strong><span className="small muted">{o.pickup.address}</span>
                {o.merchant.phone && <a className="small" href={`tel:${o.merchant.phone}`}><Phone size={12} />{o.merchant.phone}</a>}</div>
              <div><span className="label">Deliver to</span><strong>{o.dropoff.address}</strong>{o.dropoffNotes && <span className="small muted pre">{o.dropoffNotes}</span>}
                <a className="small" href={`tel:${o.customerPhone}`}><Phone size={12} />{o.customerPhone}</a></div>
              <div><span className="label">Times</span>
                <span className="small">Ready {formatTime(o.readyAt)}</span>
                {o.estimatedDeliveryAt && o.status !== "Delivered" && <span className="small">Due {formatTime(o.estimatedDeliveryAt)} ({relative(o.estimatedDeliveryAt)})</span>}
                {o.deliveredAt && <span className="small ok-text">Delivered {formatTime(o.deliveredAt)}</span>}
              </div>
            </div>
            {o.items && <div><span className="label">Items</span><p className="pre">{o.items}</p></div>}
            {o.cancelReason && <p className="form-error">Cancelled: {o.cancelReason}</p>}
          </section>
          <section className="card map-card"><MapView pins={pins} lines={lines} height={380} fitKey={o.id} /></section>
        </div>

        <aside className="stack">
          <section className="card section">
            <h2>Courier</h2>
            {o.courier ? (
              <div className="row gap-10">
                <span className="avatar">{initials(o.courier.name)}</span>
                <span className="grow min0"><strong className="block">{o.courier.name}</strong>
                  <span className="small muted">{vehicleLabel[o.courier.vehicle]}{o.courier.plate && ` · ${o.courier.plate}`}{o.courier.locationAt && ` · seen ${relative(o.courier.locationAt)}`}</span></span>
              </div>
            ) : o.pendingOffer ? (
              <p className="small">Offered to <strong>{o.pendingOffer.courierName}</strong>, {km(o.pendingOffer.toPickupKm)} from the pickup. Answer due {formatTime(o.pendingOffer.expiresAt)}.</p>
            ) : <p className="small muted">{o.status === "Unassigned" ? "Nobody took it. Assign a courier or offer it again." : o.status === "Searching" ? "Looking for the nearest free courier…" : "No courier."}</p>}
            {o.offersMade > 0 && !o.courier && <span className="small faint">{o.offersMade} offer{o.offersMade === 1 ? "" : "s"} in this search</span>}
          </section>

          {o.deliveryCode && (
            <section className="card section">
              <h2>For the customer</h2>
              <div><span className="label">Delivery code</span><span className="code-big">{o.deliveryCode}</span>
                <span className="small muted">The customer gives this to the courier at the door.{o.codeAttempts > 0 && ` ${o.codeAttempts} wrong ${o.codeAttempts === 1 ? "try" : "tries"} so far.`}</span></div>
              {link && (
                <div className="row wrap gap-6">
                  <button className="button small" onClick={copy}><Copy size={14} />{copied ? "Copied" : "Copy tracking link"}</button>
                  <a className="button small" href={link} target="_blank" rel="noreferrer noopener"><ExternalLink size={14} />Open</a>
                </div>
              )}
            </section>
          )}

          <section className="card section">
            <h2>History</h2>
            <ol className="history">
              {[...o.history].reverse().map((h) => (
                <li key={h.id} className={h.kind === "Unassigned" || h.kind === "WrongCode" || h.kind === "Cancelled" ? "flagged" : h.kind === "Delivered" ? "good" : undefined}>
                  <span><strong>{eventLabel[h.kind]}</strong>{h.detail && <span className="muted"> · {h.detail}</span>}</span>
                  <span className="small faint">{formatDayTime(h.at)}{h.actorName && ` · ${h.actorName}`}</span>
                </li>
              ))}
            </ol>
          </section>
        </aside>
      </div>

      {action === "assign" && <AssignDialog orderId={o.id} code={o.code} pickup={o.pickup} onClose={() => setAction(null)} onDone={(next) => { setOrder(next); setAction(null); }} />}
      {action === "release" && <ReasonDialog title={`Take ${o.code} back`} hint="The courier is freed and the order is offered again." label="Take back" run={(r) => api.release(company.id, o.id, r)} onClose={() => setAction(null)} onDone={setOrder} />}
      {action === "confirm" && <ReasonDialog title={`Close ${o.code} without the code`} hint="For when the customer cannot give the code. Say how you confirmed the delivery." label="Confirm delivered" run={(r) => api.confirmDelivered(company.id, o.id, r)} onClose={() => setAction(null)} onDone={setOrder} />}
      {action === "cancel" && <ReasonDialog title={`Cancel ${o.code}`} hint={o.courier ? "The courier is freed and told." : "Any offer still out is taken back."} label="Cancel order" danger run={(r) => api.cancel(company.id, o.id, r)} onClose={() => setAction(null)} onDone={setOrder} />}
    </div>
  );
}

function ReasonDialog({ title, hint, label, danger = false, run, onClose, onDone }: {
  title: string; hint: string; label: string; danger?: boolean; run: (reason: string) => Promise<Order>; onClose: () => void; onDone: (order: Order) => void;
}) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog title={title} onClose={onClose}>
      <form className="stack" onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        try {
          onDone(await run(reason));
          onClose();
        } catch (err) {
          setError((err as Error).message);
          setBusy(false);
        }
      }}>
        <p className="small muted">{hint}</p>
        <label className="field">Reason<input className="input" value={reason} onChange={(e) => setReason(e.target.value)} autoFocus minLength={3} maxLength={300} /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onClose}>Back</button>
          <button className={`button ${danger ? "danger" : "primary"}`} disabled={busy || reason.trim().length < 3}>{busy ? "Saving…" : label}</button>
        </div>
      </form>
    </Dialog>
  );
}
