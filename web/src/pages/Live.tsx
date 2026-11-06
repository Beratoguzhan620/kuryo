import { AlertTriangle, Bike, PackageSearch, Plus } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { api } from "../api";
import { AssignDialog } from "../components/AssignDialog";
import { CourierBadge, LiveBadge, StatusBadge } from "../components/Badges";
import { MapView, type Area, type Pin } from "../components/MapView";
import { Empty, Failed, Loading } from "../components/States";
import { useCompany } from "../context";
import { initials, isFresh, isOpen, plural, relative, formatTime, vehicleLabel } from "../format";
import { useLoad, useNow } from "../hooks";
import { useLive } from "../live";
import { linkProps, navigate } from "../router";
import type { Courier, OrderSummary } from "../types";

const OPEN = ["Searching", "Unassigned", "Assigned", "PickedUp"] as const;

/** The dispatcher's screen: open orders by what they need, couriers on the map, all of it live. */
export function Live() {
  const company = useCompany();
  const now = useNow(15_000);
  const [data, reload] = useLoad(async () => {
    const [orders, couriers, merchants, zones] = await Promise.all([
      api.orders(company.id, { status: [...OPEN], pageSize: 100 }),
      api.couriers(company.id),
      api.merchants(company.id),
      api.zones(company.id),
    ]);
    return { orders: orders.items, couriers, merchants, zones };
  }, [company.id]);
  const [orders, setOrders] = useState<OrderSummary[] | null>(null);
  const [couriers, setCouriers] = useState<Courier[] | null>(null);
  const [assigning, setAssigning] = useState<OrderSummary | null>(null);
  const [focus, setFocus] = useState<string | null>(null);

  // Live messages replace what was loaded; a reload starts from the server's answer again.
  const loadedOrders = data.data?.orders;
  const loadedCouriers = data.data?.couriers;
  const [seen, setSeen] = useState<unknown>(null);
  if (data.data && seen !== data.data) {
    setSeen(data.data);
    setOrders(loadedOrders ?? []);
    setCouriers(loadedCouriers ?? []);
  }

  const live = useLive(company.id, {
    orderChanged: useCallback((o: OrderSummary) => setOrders((list) => {
      if (!list) return list;
      const rest = list.filter((x) => x.id !== o.id);
      return isOpen(o.status) ? [o, ...rest] : rest;
    }), []),
    courierChanged: useCallback((c: Courier) => setCouriers((list) => list && [c, ...list.filter((x) => x.userId !== c.userId)]), []),
    resync: reload,
  });

  const byStatus = useMemo(() => {
    const list = [...(orders ?? [])].sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    return {
      waiting: list.filter((o) => o.status === "Unassigned"),
      searching: list.filter((o) => o.status === "Searching"),
      moving: list.filter((o) => o.status === "Assigned" || o.status === "PickedUp"),
    };
  }, [orders]);

  const sortedCouriers = useMemo(() => [...(couriers ?? [])].sort((a, b) => order(a) - order(b) || a.displayName.localeCompare(b.displayName, "tr")), [couriers]);

  const pins = useMemo<Pin[]>(() => {
    const result: Pin[] = [];
    for (const m of data.data?.merchants ?? []) {
      if (m.active) result.push({ id: `m-${m.id}`, latitude: m.latitude, longitude: m.longitude, label: m.name, text: initials(m.name), tone: "ink", shape: "square" });
    }
    for (const o of orders ?? []) {
      result.push({ id: `o-${o.id}`, latitude: o.latitude, longitude: o.longitude, label: `${o.code} · ${o.dropoffAddress}`, text: o.code.slice(-4),
        tone: o.status === "Unassigned" ? "danger" : o.status === "Searching" ? "warn" : "muted", shape: "square", onClick: () => navigate(`/c/${company.id}/orders/${o.id}`) });
    }
    for (const c of couriers ?? []) {
      if (c.status === "Offline" || c.latitude === null || c.longitude === null) continue;
      result.push({ id: `c-${c.userId}`, latitude: c.latitude, longitude: c.longitude, label: `${c.displayName} · ${c.status === "Busy" ? c.orderCode : "free"}`,
        text: initials(c.displayName), tone: !isFresh(c.locationAt, now) ? "muted" : c.status === "Busy" ? "accent" : "ink", onClick: () => setFocus(c.userId) });
    }
    return result;
  }, [data.data?.merchants, orders, couriers, company.id, now]);

  const areas = useMemo<Area[]>(() => (data.data?.zones ?? []).filter((z) => z.active).map((z) => ({ id: z.id, corners: z.corners, label: z.name, tone: "accent" as const, dashed: true })), [data.data?.zones]);

  if (data.state === "failed" && !orders) return <Failed message={data.error} onRetry={reload} />;
  if (!orders || !couriers) return <Loading label="Opening the live map" />;

  const online = couriers.filter((c) => c.status !== "Offline");
  const empty = (data.data?.zones.length ?? 0) === 0;

  return (
    <div className="live-page">
      <div className="page-head">
        <div>
          <div className="row gap-10"><h1>Live</h1><LiveBadge state={live} /></div>
          <p className="muted">{plural(orders.length, "open order")} · {plural(online.length, "courier")} online · {plural(online.filter((c) => c.status === "Available").length, "free courier")}</p>
        </div>
        <a className="button primary" {...linkProps(`/c/${company.id}/orders/new`)}><Plus size={16} />New order</a>
      </div>

      {empty && (
        <div className="notice"><AlertTriangle size={16} /><span className="grow">Draw a delivery zone first: orders are only taken for addresses inside one.</span><a className="button small" {...linkProps(`/c/${company.id}/zones`)}>Draw a zone</a></div>
      )}
      {byStatus.waiting.length > 0 && (
        <div className="notice bad" role="alert"><AlertTriangle size={16} /><span className="grow">{plural(byStatus.waiting.length, "order")} found no courier and {byStatus.waiting.length === 1 ? "needs" : "need"} you.</span></div>
      )}

      <div className="live-grid">
        <section className="card lanes" aria-label="Open orders">
          <Lane title="Needs a courier" tone="danger" orders={byStatus.waiting} empty="Nothing waiting for you." render={(o) => (
            <div className="row gap-6"><button className="button small primary" onClick={() => setAssigning(o)}>Assign</button>
              <SearchAgain orderId={o.id} /></div>
          )} />
          <Lane title="Finding a courier" tone="warn" orders={byStatus.searching} empty="No order is looking for a courier." render={(o) => <span className="small muted">{plural(o.offersMade, "offer")} so far</span>} />
          <Lane title="On the way" tone="accent" orders={byStatus.moving} empty="No courier is carrying an order." render={(o) => (
            <span className="small muted">{o.courierName}{o.estimatedDeliveryAt && ` · due ${formatTime(o.estimatedDeliveryAt)}`}</span>
          )} />
        </section>

        <section className="card live-map">
          <MapView pins={pins} areas={areas} height="100%" fitKey={company.id} />
        </section>

        <section className="card couriers" aria-label="Couriers">
          <div className="section-head"><h2>Couriers</h2><span className="badge plain">{online.length}/{couriers.length}</span></div>
          {couriers.length === 0 ? (
            <Empty title="No couriers yet" icon={<Bike size={22} />}><a className="link" {...linkProps(`/c/${company.id}/team`)}>Add one to the team</a></Empty>
          ) : (
            <ul className="courier-list">
              {sortedCouriers.map((c) => (
                <li key={c.userId} className={focus === c.userId ? "focus" : undefined}>
                  <span className="avatar small-avatar">{initials(c.displayName)}</span>
                  <span className="grow min0">
                    <strong className="block ellipsis">{c.displayName}</strong>
                    <span className="small muted block">
                      {vehicleLabel[c.vehicle]}
                      {c.orderId && c.orderCode ? <> · <a {...linkProps(`/c/${company.id}/orders/${c.orderId}`)}>{c.orderCode}</a></> : null}
                      {c.status !== "Offline" && (c.locationAt ? ` · seen ${relative(c.locationAt, now)}` : " · no location")}
                    </span>
                  </span>
                  <CourierBadge status={c.status} />
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      {assigning && (
        <AssignDialog orderId={assigning.id} code={assigning.code} pickup={pickupOf(assigning, data.data?.merchants ?? [])} onClose={() => setAssigning(null)}
          onDone={() => setAssigning(null)} />
      )}
    </div>
  );
}

const order = (c: Courier) => (c.status === "Available" ? 0 : c.status === "Busy" ? 1 : 2);

function pickupOf(o: OrderSummary, merchants: { id: string; latitude: number; longitude: number }[]) {
  const m = merchants.find((x) => x.id === o.merchantId);
  return m ? { latitude: m.latitude, longitude: m.longitude } : { latitude: o.latitude, longitude: o.longitude };
}

function Lane({ title, tone, orders, empty, render }: { title: string; tone: string; orders: OrderSummary[]; empty: string; render: (o: OrderSummary) => React.ReactNode }) {
  const company = useCompany();
  return (
    <div className="lane-block">
      <div className="lane-title"><span className={`lane-dot ${tone}`} aria-hidden="true" /><h3>{title}</h3><span className="badge plain">{orders.length}</span></div>
      {orders.length === 0 ? <p className="small faint lane-empty"><PackageSearch size={14} />{empty}</p> : (
        <ul className="lane-list">
          {orders.map((o) => (
            <li key={o.id} className="lane-card">
              <div className="row between">
                <a className="strong-link mono" {...linkProps(`/c/${company.id}/orders/${o.id}`)}>{o.code}</a>
                <StatusBadge status={o.status} />
              </div>
              <span className="small ellipsis">{o.merchantName} → {o.dropoffAddress}</span>
              <div className="row between wrap gap-6">{render(o)}<span className="small faint">{relative(o.createdAt)}</span></div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function SearchAgain({ orderId }: { orderId: string }) {
  const company = useCompany();
  const [busy, setBusy] = useState(false);
  return (
    <button className="button small" disabled={busy} onClick={async () => {
      setBusy(true);
      try { await api.searchAgain(company.id, orderId); } finally { setBusy(false); }
    }}>{busy ? "Sending…" : "Offer again"}</button>
  );
}
