import { ArrowLeft, MapPin, Send } from "lucide-react";
import { useEffect, useMemo, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { MapView, type Area, type Pin } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { isDispatcher, useCompany } from "../context";
import { distanceKm, formatMoney, initials, km } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { ZoneCheck } from "../types";

/** A new order: who it is for, and where, picked on the map so the courier gets a real point, not just an address. */
export function OrderForm() {
  const company = useCompany();
  const [data, reload] = useLoad(async () => {
    const [merchants, zones] = await Promise.all([api.merchants(company.id), api.zones(company.id)]);
    return { merchants: merchants.filter((m) => m.active), zones: zones.filter((z) => z.active) };
  }, [company.id]);
  const [merchantId, setMerchantId] = useState(company.merchantId ?? "");
  const [form, setForm] = useState({ customerName: "", customerPhone: "", dropoffAddress: "", dropoffNotes: "", items: "" });
  const [point, setPoint] = useState<{ latitude: number; longitude: number } | null>(null);
  const [check, setCheck] = useState<ZoneCheck | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setForm({ ...form, [key]: e.target.value });

  useEffect(() => {
    if (!merchantId && data.data?.merchants.length) setMerchantId(data.data.merchants[0]!.id);
  }, [data.data, merchantId]);

  useEffect(() => {
    if (!point) return;
    let current = true;
    setCheck(null);
    api.checkZone(company.id, point.latitude, point.longitude).then((c) => { if (current) setCheck(c); }, () => {});
    return () => { current = false; };
  }, [company.id, point]);

  const merchant = data.data?.merchants.find((m) => m.id === merchantId);
  const pins = useMemo<Pin[]>(() => [
    ...(merchant ? [{ id: "m", latitude: merchant.latitude, longitude: merchant.longitude, label: `Pickup: ${merchant.name}`, text: initials(merchant.name), tone: "ink" as const, shape: "square" as const }] : []),
    ...(point ? [{ id: "d", latitude: point.latitude, longitude: point.longitude, label: "Deliver here", text: "", tone: (check && !check.delivers ? "danger" : "accent") as "danger" | "accent" }] : []),
  ], [merchant, point, check]);
  const areas = useMemo<Area[]>(() => (data.data?.zones ?? []).map((z) => ({ id: z.id, corners: z.corners, label: `${z.name} · ${formatMoney(z.deliveryFee)}`, tone: "accent" as const, dashed: true })), [data.data?.zones]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!point || !merchantId) return;
    setBusy(true);
    setError(null);
    try {
      const order = await api.createOrder(company.id, { merchantId, ...form, ...point });
      navigate(`/c/${company.id}/orders/${order.id}`, true);
    } catch (err) {
      setError(err as ApiError);
      setBusy(false);
    }
  }

  if (data.state === "failed") return <Failed message={data.error} onRetry={reload} />;
  if (!data.data) return <Loading />;
  if (data.data.merchants.length === 0) {
    return <Failed message={isDispatcher(company) ? "Add a merchant first: orders are collected from one." : "Your merchant is paused. Ask the office."} />;
  }

  const fieldError = (name: string) => error?.fields[name]?.[0];
  const outside = check && !check.delivers;

  return (
    <form className="order-form" onSubmit={submit} noValidate>
      <a className="back" {...linkProps(`/c/${company.id}/orders`)}><ArrowLeft size={14} />Orders</a>
      <div className="page-head"><div><h1>New order</h1><p className="muted">It is offered to the nearest free courier as soon as you send it.</p></div></div>
      <div className="form-grid">
        <section className="card section">
          {isDispatcher(company) ? (
            <label className="field">Collect from
              <select className="select" value={merchantId} onChange={(e) => setMerchantId(e.target.value)}>
                {data.data.merchants.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
              </select>
              {merchant && <span className="hint">{merchant.address} · ready in {merchant.prepMinutes} min</span>}
            </label>
          ) : merchant && <div><span className="label">Collect from</span><strong>{merchant.name}</strong><span className="block small muted">{merchant.address}</span></div>}
          <div className="grid-2">
            <label className="field">Customer<input className="input" value={form.customerName} onChange={set("customerName")} autoComplete="off" />
              {fieldError("customerName") && <span className="field-error">{fieldError("customerName")}</span>}</label>
            <label className="field">Phone<input className="input" type="tel" value={form.customerPhone} onChange={set("customerPhone")} autoComplete="off" /></label>
          </div>
          <label className="field">Address<input className="input" value={form.dropoffAddress} onChange={set("dropoffAddress")} placeholder="Street, number, district" autoComplete="off" /></label>
          <label className="field">For the courier<textarea className="textarea short" value={form.dropoffNotes} onChange={set("dropoffNotes")} placeholder="Floor, bell, landmark" maxLength={500} /></label>
          <label className="field">Items<textarea className="textarea short" value={form.items} onChange={set("items")} placeholder="What is in the bag" maxLength={1000} /></label>
        </section>
        <section className="card section">
          <div className="row between"><h2>Where</h2><span className="small muted"><MapPin size={13} /> Click the map at the door</span></div>
          <MapView pins={pins} areas={areas} height={360} onPick={(latitude, longitude) => setPoint({ latitude, longitude })} fitKey="form" />
          {!point ? <p className="small muted">The courier navigates to this point; the address is what they read at the door.</p>
            : outside ? <p className="form-error" role="alert">The company does not deliver here. Pick a point inside a zone.</p>
            : check ? (
              <div className="quote">
                <div><span className="label">Zone</span>{check.zoneName}</div>
                <div><span className="label">Fee</span>{formatMoney(check.deliveryFee ?? 0)}</div>
                {merchant && <div><span className="label">Distance</span>{km(distanceKm(merchant, point))}</div>}
              </div>
            ) : <p className="small muted">Checking the zone…</p>}
        </section>
      </div>
      {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
      <div className="form-actions">
        <button className="button primary" disabled={busy || !point || !!outside || !form.customerName.trim() || !form.dropoffAddress.trim()}><Send size={16} />{busy ? "Sending…" : "Send order"}</button>
        <a className="button" {...linkProps(`/c/${company.id}/orders`)}>Cancel</a>
      </div>
    </form>
  );
}
