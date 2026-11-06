import { Plus, Store } from "lucide-react";
import { useMemo, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { Dialog } from "../components/Dialog";
import { MapView, type Pin } from "../components/MapView";
import { Empty, Failed, SkeletonRows } from "../components/States";
import { useCompany } from "../context";
import { initials } from "../format";
import { useLoad } from "../hooks";
import type { Merchant } from "../types";

/** The shops and restaurants the company collects from, each with its pickup point and usual preparation time. */
export function Merchants() {
  const company = useCompany();
  const [merchants, reload, setMerchants] = useLoad(() => api.merchants(company.id), [company.id]);
  const [editing, setEditing] = useState<Merchant | "new" | null>(null);

  const saved = (m: Merchant) => {
    setMerchants([...(merchants.data ?? []).filter((x) => x.id !== m.id), m].sort((a, b) => a.name.localeCompare(b.name, "tr")));
    setEditing(null);
  };

  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Merchants</h1><p className="muted">Where couriers collect. Give a merchant's staff the Merchant role to let them send their own orders.</p></div>
        <button className="button primary" onClick={() => setEditing("new")}><Plus size={16} />Add merchant</button>
      </div>
      <section className="card table-card">
        {merchants.state === "failed" ? <Failed message={merchants.error} onRetry={reload} /> : merchants.data?.length === 0 ? (
          <Empty title="No merchants yet" icon={<Store size={24} />}><button className="link" onClick={() => setEditing("new")}>Add the first one</button></Empty>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Merchant</th><th className="hide-sm">Pickup</th><th>Prep</th><th className="right">Open orders</th><th /></tr></thead>
              {!merchants.data ? <SkeletonRows columns={5} /> : (
                <tbody>
                  {merchants.data.map((m) => (
                    <tr key={m.id} className={m.active ? undefined : "paused"}>
                      <td><strong>{m.name}</strong>{m.phone && <span className="block small muted">{m.phone}</span>}</td>
                      <td className="hide-sm small muted">{m.address}</td>
                      <td>{m.prepMinutes} min</td>
                      <td className="right">{m.openOrders}{!m.active && <span className="badge plain ml-6">Paused</span>}</td>
                      <td className="actions"><button className="button small" onClick={() => setEditing(m)}>Edit</button></td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
      </section>
      {editing && <MerchantDialog merchant={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={saved} />}
    </div>
  );
}

function MerchantDialog({ merchant, onClose, onSaved }: { merchant: Merchant | null; onClose: () => void; onSaved: (m: Merchant) => void }) {
  const company = useCompany();
  const [form, setForm] = useState({
    name: merchant?.name ?? "", phone: merchant?.phone ?? "", address: merchant?.address ?? "", prepMinutes: String(merchant?.prepMinutes ?? 15), active: merchant?.active ?? true,
  });
  const [point, setPoint] = useState(merchant ? { latitude: merchant.latitude, longitude: merchant.longitude } : null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pins = useMemo<Pin[]>(() => (point ? [{ id: "m", ...point, label: form.name || "Pickup", text: initials(form.name || "?"), tone: "ink", shape: "square" }] : []), [point, form.name]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!point) return;
    setBusy(true);
    setError(null);
    try {
      const body = { ...form, prepMinutes: Number(form.prepMinutes), ...point };
      onSaved(merchant ? await api.updateMerchant(company.id, merchant.id, body) : await api.createMerchant(company.id, body));
    } catch (err) {
      setError((err as ApiError).message);
      setBusy(false);
    }
  }

  return (
    <Dialog title={merchant ? `Edit ${merchant.name}` : "Add merchant"} onClose={onClose} wide>
      <form className="stack" onSubmit={submit}>
        <div className="grid-2">
          <label className="field">Name<input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} autoFocus /></label>
          <label className="field">Phone<input className="input" type="tel" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} /></label>
        </div>
        <div className="grid-2">
          <label className="field">Address<input className="input" value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} /></label>
          <label className="field">Usual preparation (minutes)<input className="input" type="number" min={0} max={120} value={form.prepMinutes} onChange={(e) => setForm({ ...form, prepMinutes: e.target.value })} />
            <span className="hint">Estimates wait for this before the courier leaves.</span></label>
        </div>
        <div className="field">Pickup point<MapView pins={pins} height={260} onPick={(latitude, longitude) => setPoint({ latitude, longitude })} fitKey="merchant" />
          <span className="hint">{point ? `${point.latitude.toFixed(5)}, ${point.longitude.toFixed(5)}` : "Click the map at the shop's door."}</span></div>
        {merchant && <label className="check"><input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} /><span>Sending orders <span className="block small muted">A paused merchant cannot send new orders.</span></span></label>}
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onClose}>Cancel</button>
          <button className="button primary" disabled={busy || !point || form.name.trim().length < 2 || form.address.trim().length < 3}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </Dialog>
  );
}
