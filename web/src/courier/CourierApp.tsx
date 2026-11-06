import { Bike, Check, LocateFixed, LocateOff, LogOut, MapPin, Navigation, Package, Phone, Power, Store, Timer, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { LiveBadge } from "../components/Badges";
import { MapView, type Pin } from "../components/MapView";
import { Brand } from "../components/Shell";
import { Empty, Failed, Loading } from "../components/States";
import { useAccount } from "../context";
import { formatMoney, formatTime, initials, km, relative, secondsLeft } from "../format";
import { useNow } from "../hooks";
import { useLive } from "../live";
import { linkProps } from "../router";
import type { Company, Courier, Offer, Order } from "../types";
import { startTracker, type Point, type TrackerState } from "./tracker";

const COMPANY = "kuryo.courier-company";

function remembered(companies: Company[]): Company | null {
  try {
    const id = localStorage.getItem(COMPANY);
    return companies.find((c) => c.id === id) ?? companies[0] ?? null;
  } catch {
    return companies[0] ?? null;
  }
}

/** The courier's phone: go online, answer offers, collect and deliver. Built to be used with one thumb. */
export function CourierApp() {
  const { me, signOut } = useAccount();
  const companies = me.companies.filter((c) => c.role === "Courier");
  const [company, setCompany] = useState<Company | null>(() => remembered(companies));

  const pick = (c: Company) => {
    try { localStorage.setItem(COMPANY, c.id); } catch { /* the choice lasts until the tab closes */ }
    setCompany(c);
  };

  return (
    <div className="courier-app">
      <header className="courier-bar">
        <Brand />
        <div className="grow" />
        {companies.length > 1 && company && (
          <select className="select narrow" value={company.id} onChange={(e) => pick(companies.find((c) => c.id === e.target.value)!)} aria-label="Company">
            {companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        )}
        <button className="icon-button" onClick={signOut} aria-label="Sign out"><LogOut size={18} /></button>
      </header>
      <main className="courier-main">
        {company ? <Shift key={company.id} company={company} /> : (
          <Empty title="You are not a courier anywhere yet" icon={<Bike size={26} />}>
            <span className="muted measure">Ask the courier company to add you to their team as a courier, with {me.user.email}.</span>
            <a className="button" {...linkProps("/")}>Back</a>
          </Empty>
        )}
      </main>
    </div>
  );
}

function Shift({ company }: { company: Company }) {
  const [courier, setCourier] = useState<Courier | null>(null);
  const [offer, setOffer] = useState<Offer | null>(null);
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [tracker, setTracker] = useState<TrackerState>("idle");
  const [here, setHere] = useState<Point | undefined>();
  const [finished, setFinished] = useState<string | null>(null);
  useEffect(() => {
    if (!finished) return;
    const timer = setTimeout(() => setFinished(null), 6000);
    return () => clearTimeout(timer);
  }, [finished]);

  const load = useCallback(async () => {
    try {
      const [me, current] = await Promise.all([api.myCourier(company.id), api.myOffer(company.id)]);
      setCourier(me);
      setOffer(current ?? null);
      setOrder(me.orderId ? await api.order(company.id, me.orderId) : null);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }, [company.id]);
  useEffect(() => { void load(); }, [load]);

  const live = useLive(company.id, {
    offerChanged: setOffer,
    courierChanged: useCallback((c: Courier) => {
      setCourier(c);
      if (!c.orderId) setOrder(null);
      else if (c.orderId !== order?.id) void api.order(company.id, c.orderId).then(setOrder, () => {});
    }, [company.id, order?.id]),
    orderChanged: useCallback((o: { id: string }) => { if (o.id === order?.id) void api.order(company.id, o.id).then(setOrder, () => {}); }, [company.id, order?.id]),
    resync: load,
  });

  const online = courier ? courier.status !== "Offline" : false;
  useEffect(() => {
    if (!online) { setTracker("idle"); return; }
    return startTracker(company.id, (state, last) => { setTracker(state); if (last) setHere(last); });
  }, [online, company.id]);

  async function toggle() {
    if (!courier) return;
    setBusy(true);
    setError(null);
    try {
      setCourier(await api.setOnline(company.id, !online));
      if (online) setOffer(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (!courier) return error ? <Failed message={error} onRetry={load} /> : <Loading label="Opening your shift" />;

  return (
    <div className="shift">
      <section className={`duty card ${online ? "on" : ""}`}>
        <div className="grow min0">
          <strong className="block">{online ? (courier.status === "Busy" ? "On a delivery" : "Online, waiting for offers") : "Offline"}</strong>
          <span className="small muted">{company.name} · <LocationText state={tracker} online={online} here={here} /></span>
        </div>
        <LiveBadge state={live} />
        <button className={`button ${online ? "" : "primary"}`} onClick={toggle} disabled={busy || courier.status === "Busy"} aria-pressed={online}>
          <Power size={16} />{online ? "Go offline" : "Go online"}
        </button>
      </section>
      {error && <p className="form-error" role="alert">{error}</p>}
      {finished && <p className="notice good" role="status"><Check size={16} />{finished} delivered. You are free for the next offer.</p>}
      {tracker === "denied" && <p className="notice bad"><LocateOff size={16} />Location is blocked for this site. Allow it in the browser so offers can find you.</p>}

      {order ? <Delivery order={order} company={company} here={here} onChange={setOrder}
          onDelivered={() => { setFinished(order.code); setOrder(null); void load(); }} onDone={() => { setOrder(null); void load(); }} />
        : offer ? <OfferCard offer={offer} company={company} onAccepted={(o) => { setOffer(null); setOrder(o); }} onGone={() => setOffer(null)} />
        : online ? (
          <Empty title="No offer right now" icon={<Timer size={26} />}>
            <span className="muted measure">Keep this screen open. When an order is near you it appears here, and you have {company.offerSeconds} seconds to take it.</span>
          </Empty>
        ) : (
          <Empty title="You are offline" icon={<Bike size={26} />}><span className="muted measure">Go online to share your location and get offers.</span></Empty>
        )}
    </div>
  );
}

function LocationText({ state, online, here }: { state: TrackerState; online: boolean; here?: Point }) {
  if (!online) return <>location not shared</>;
  if (state === "sharing" && here) return <><LocateFixed size={12} /> sharing location</>;
  if (state === "denied") return <>location blocked</>;
  if (state === "unavailable") return <>no location</>;
  return <>finding you…</>;
}

function OfferCard({ offer, company, onAccepted, onGone }: { offer: Offer; company: Company; onAccepted: (o: Order) => void; onGone: () => void }) {
  const now = useNow(250);
  const [busy, setBusy] = useState<"accept" | "decline" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const left = secondsLeft(offer.expiresAt, now);
  const total = Math.max(1, (new Date(offer.expiresAt).getTime() - new Date(offer.offeredAt).getTime()) / 1000);

  useEffect(() => { if (left === 0 && !busy) { const t = setTimeout(onGone, 1500); return () => clearTimeout(t); } }, [left, busy, onGone]);

  const pins = useMemo<Pin[]>(() => [
    { id: "p", ...offer.pickup, label: offer.merchantName, text: initials(offer.merchantName), tone: "ink", shape: "square" },
    { id: "d", ...offer.dropoff, label: "Drop-off", text: "", tone: "accent", shape: "square" },
  ], [offer]);

  async function answer(kind: "accept" | "decline") {
    setBusy(kind);
    setError(null);
    try {
      if (kind === "accept") onAccepted(await api.accept(company.id, offer.id));
      else { await api.decline(company.id, offer.id); onGone(); }
    } catch (e) {
      setError(e instanceof ApiError && e.code === "offer_gone" ? "Too late: this offer has gone to someone else." : (e as Error).message);
      setBusy(null);
      if (e instanceof ApiError && e.code === "offer_gone") setTimeout(onGone, 1500);
    }
  }

  return (
    <section className="offer card" aria-live="assertive" aria-label={`Offer ${offer.orderCode}`}>
      <div className="offer-head">
        <div><span className="label">New offer</span><strong className="offer-fee">{formatMoney(offer.deliveryFee)}</strong></div>
        <div className={`countdown${left <= 5 ? " urgent" : ""}`} role="timer" aria-label={`${left} seconds left`}>{left}<span>s</span></div>
      </div>
      <div className="meter" aria-hidden="true"><span style={{ width: `${(left / total) * 100}%` }} /></div>
      <ol className="route">
        <li><Store size={16} /><span><strong>{offer.merchantName}</strong><span className="block small muted">{offer.pickup.address} · {km(offer.toPickupKm)} from you · ready {formatTime(offer.readyAt)}</span></span></li>
        <li><MapPin size={16} /><span><strong>{offer.dropoff.address}</strong><span className="block small muted">{km(offer.tripKm)} ride</span></span></li>
      </ol>
      <MapView pins={pins} height={180} fitKey={offer.id} />
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="offer-actions">
        <button className="button wide" onClick={() => answer("decline")} disabled={busy !== null || left === 0}><X size={18} />{busy === "decline" ? "Passing…" : "Pass"}</button>
        <button className="button primary wide" onClick={() => answer("accept")} disabled={busy !== null || left === 0}><Check size={18} />{busy === "accept" ? "Taking…" : "Take it"}</button>
      </div>
    </section>
  );
}

function Delivery({ order, company, here, onChange, onDelivered, onDone }: {
  order: Order; company: Company; here?: Point; onChange: (o: Order) => void; onDelivered: () => void; onDone: () => void;
}) {
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const picked = order.status === "PickedUp";
  const target = picked ? order.dropoff : order.pickup;

  const pins = useMemo<Pin[]>(() => [
    { id: "t", latitude: target.latitude, longitude: target.longitude, label: picked ? "Drop-off" : order.merchant.name, text: picked ? "" : initials(order.merchant.name), tone: picked ? "accent" : "ink", shape: "square" },
    ...(here ? [{ id: "me", latitude: here.latitude, longitude: here.longitude, label: "You", text: "", tone: "accent" as const }] : []),
  ], [target, here, picked, order.merchant.name]);

  async function run(step: () => Promise<Order>) {
    setBusy(true);
    setError(null);
    try {
      const next = await step();
      if (next.status === "Delivered") onDelivered();
      else onChange(next);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function deliver(e: FormEvent) {
    e.preventDefault();
    await run(() => api.deliver(company.id, order.id, code));
    setCode("");
  }

  const directions = `https://www.openstreetmap.org/directions?to=${target.latitude},${target.longitude}`;

  return (
    <section className="delivery card" aria-label={`Delivery ${order.code}`}>
      <div className="steps" aria-label="Progress">
        <span className="step done"><Check size={13} />Taken</span>
        <span className={`step ${picked ? "done" : "now"}`}>{picked ? <Check size={13} /> : <Store size={13} />}Collect</span>
        <span className={`step ${picked ? "now" : ""}`}><Package size={13} />Deliver</span>
      </div>
      <div className="row between"><span className="mono muted">{order.code}</span>{order.estimatedDeliveryAt && <span className="small muted">due {formatTime(order.estimatedDeliveryAt)}</span>}</div>
      {picked ? (
        <div className="stop">
          <span className="label">Deliver to</span>
          <strong className="stop-title">{order.customerName}</strong>
          <span>{order.dropoff.address}</span>
          {order.dropoffNotes && <span className="small muted pre">{order.dropoffNotes}</span>}
          <div className="row wrap gap-6"><a className="button small" href={`tel:${order.customerPhone}`}><Phone size={14} />Call</a><a className="button small" href={directions} target="_blank" rel="noreferrer noopener"><Navigation size={14} />Directions</a></div>
        </div>
      ) : (
        <div className="stop">
          <span className="label">Collect from</span>
          <strong className="stop-title">{order.merchant.name}</strong>
          <span>{order.pickup.address}</span>
          <span className="small muted">Ready {formatTime(order.readyAt)} ({relative(order.readyAt)}){order.items && ` · ${order.items}`}</span>
          <div className="row wrap gap-6">{order.merchant.phone && <a className="button small" href={`tel:${order.merchant.phone}`}><Phone size={14} />Call</a>}<a className="button small" href={directions} target="_blank" rel="noreferrer noopener"><Navigation size={14} />Directions</a></div>
        </div>
      )}
      <MapView pins={pins} height={200} fitKey={`${order.id}-${order.status}`} />
      {error && <p className="form-error" role="alert">{error}</p>}
      {picked ? (
        <form className="code-form" onSubmit={deliver}>
          <label className="field">The customer's 4-digit code
            <input className="input code-input" inputMode="numeric" pattern="[0-9]{4}" maxLength={4} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))} autoComplete="one-time-code" aria-label="Delivery code" />
          </label>
          <button className="button primary wide" disabled={busy || code.length !== 4}><Check size={18} />{busy ? "Checking…" : "Delivered"}</button>
        </form>
      ) : (
        <button className="button primary wide" onClick={() => run(() => api.pickUp(company.id, order.id))} disabled={busy}><Package size={18} />{busy ? "Saving…" : "I have the order"}</button>
      )}
      {!picked && <HandBack order={order} company={company} onDone={onDone} />}
    </section>
  );
}

function HandBack({ order, company, onDone }: { order: Order; company: Company; onDone: () => void }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  if (!open) return <button className="link small center-self" onClick={() => setOpen(true)}>I cannot take this one</button>;
  return (
    <form className="stack-8" onSubmit={async (e) => {
      e.preventDefault();
      try { await api.release(company.id, order.id, reason); onDone(); } catch (err) { setError((err as Error).message); }
    }}>
      <label className="field">Why?<input className="input" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Flat tyre" autoFocus /></label>
      {error && <p className="form-error">{error}</p>}
      <div className="row gap-6"><button className="button small danger" disabled={reason.trim().length < 3}>Hand it back</button><button type="button" className="link small" onClick={() => setOpen(false)}>Keep it</button></div>
    </form>
  );
}
