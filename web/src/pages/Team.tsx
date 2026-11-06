import { Settings2, UserPlus, Users } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { CourierBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { Empty, Failed, Loading } from "../components/States";
import { useAccount, useCompany } from "../context";
import { initials, roleHint, vehicleLabel } from "../format";
import { useLoad } from "../hooks";
import type { Courier, Member, Merchant, Role, Vehicle } from "../types";

const roles: Role[] = ["Courier", "Merchant", "Dispatcher", "Owner"];
const vehicles: Vehicle[] = ["Bicycle", "Scooter", "Car"];

/** Who works here and as what, each courier's vehicle, and (for owners) how offers work. */
export function Team() {
  const company = useCompany();
  const { refresh, session } = useAccount();
  const owner = company.role === "Owner";
  const [data, reload, setData] = useLoad(async () => {
    const [members, couriers, merchants] = await Promise.all([api.members(company.id), api.couriers(company.id), api.merchants(company.id)]);
    return { members, couriers, merchants };
  }, [company.id]);
  const [adding, setAdding] = useState(false);
  const [vehicleFor, setVehicleFor] = useState<Courier | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (data.state === "failed" && !data.data) return <Failed message={data.error} onRetry={reload} />;
  if (!data.data) return <Loading />;
  const { members, couriers, merchants } = data.data;

  async function change(m: Member, role: Role, merchantId?: string | null) {
    setError(null);
    try {
      const updated = await api.changeRole(company.id, m.userId, role, role === "Merchant" ? merchantId ?? merchants[0]?.id : null);
      setData({ ...data.data!, members: members.map((x) => (x.userId === m.userId ? updated : x)) });
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function remove(m: Member) {
    setError(null);
    try {
      await api.removeMember(company.id, m.userId);
      if (m.userId === session.user.id) { await refresh(); location.assign("/"); return; }
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Team</h1><p className="muted">Roles are per company. A courier added here gets a courier profile and can open the courier app at once.</p></div>
        {owner && <button className="button primary" onClick={() => setAdding(true)}><UserPlus size={16} />Add member</button>}
      </div>
      {error && <p className="form-error" role="alert">{error}</p>}
      <section className="card table-card">
        {members.length === 0 ? <Empty title="No members" icon={<Users size={24} />} /> : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Member</th><th>Role</th><th className="hide-sm">Courier</th><th /></tr></thead>
              <tbody>
                {members.map((m) => {
                  const courier = couriers.find((c) => c.userId === m.userId);
                  return (
                    <tr key={m.userId}>
                      <td><div className="row gap-10"><span className="avatar small-avatar">{initials(m.displayName)}</span><span className="min0"><strong className="block ellipsis">{m.displayName}</strong><span className="small muted ellipsis block">{m.email}</span></span></div></td>
                      <td>
                        {owner && m.userId !== session.user.id ? (
                          <div className="row wrap gap-6">
                            <select className="select narrow" value={m.role} onChange={(e) => change(m, e.target.value as Role, m.merchantId)} aria-label={`Role of ${m.displayName}`}>
                              {roles.map((r) => <option key={r} value={r} disabled={r === "Merchant" && merchants.length === 0}>{r}</option>)}
                            </select>
                            {m.role === "Merchant" && (
                              <select className="select narrow" value={m.merchantId ?? ""} onChange={(e) => change(m, "Merchant", e.target.value)} aria-label={`Merchant of ${m.displayName}`}>
                                {merchants.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
                              </select>
                            )}
                          </div>
                        ) : <span>{m.role}{m.merchantName && <span className="small muted"> · {m.merchantName}</span>}</span>}
                      </td>
                      <td className="hide-sm">{courier ? (
                        <div className="row wrap gap-6"><CourierBadge status={courier.status} /><span className="small muted">{vehicleLabel[courier.vehicle]}{courier.plate && ` · ${courier.plate}`}</span>
                          <button className="link small" onClick={() => setVehicleFor(courier)}>Vehicle</button></div>
                      ) : <span className="faint">—</span>}</td>
                      <td className="actions">{(owner || m.userId === session.user.id) && <button className="button small danger" onClick={() => remove(m)}>{m.userId === session.user.id ? "Leave" : "Remove"}</button>}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
      {owner && <OfferSettings />}
      {adding && <AddMember merchants={merchants} onClose={() => setAdding(false)} onAdded={() => { setAdding(false); reload(); }} />}
      {vehicleFor && <VehicleDialog courier={vehicleFor} onClose={() => setVehicleFor(null)} onSaved={() => { setVehicleFor(null); reload(); }} />}
    </div>
  );
}

function AddMember({ merchants, onClose, onAdded }: { merchants: Merchant[]; onClose: () => void; onAdded: () => void }) {
  const company = useCompany();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Courier");
  const [merchantId, setMerchantId] = useState(merchants[0]?.id ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.addMember(company.id, email, role, role === "Merchant" ? merchantId : null);
      onAdded();
    } catch (err) {
      setError((err as ApiError).message);
      setBusy(false);
    }
  }

  return (
    <Dialog title="Add a member" onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <label className="field">Email<input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus />
          <span className="hint">They need a Kuryo account with this email first.</span></label>
        <fieldset className="stack-8">
          <legend className="section-legend">Role</legend>
          {roles.map((r) => (
            <label key={r} className="check"><input type="radio" name="role" checked={role === r} onChange={() => setRole(r)} disabled={r === "Merchant" && merchants.length === 0} />
              <span><strong>{r}</strong><span className="block small muted">{roleHint[r]}</span></span></label>
          ))}
        </fieldset>
        {role === "Merchant" && (
          <label className="field">Merchant<select className="select" value={merchantId} onChange={(e) => setMerchantId(e.target.value)}>{merchants.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}</select></label>
        )}
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Cancel</button><button className="button primary" disabled={busy || !email.includes("@")}>{busy ? "Adding…" : "Add"}</button></div>
      </form>
    </Dialog>
  );
}

function VehicleDialog({ courier, onClose, onSaved }: { courier: Courier; onClose: () => void; onSaved: () => void }) {
  const company = useCompany();
  const [vehicle, setVehicle] = useState<Vehicle>(courier.vehicle);
  const [plate, setPlate] = useState(courier.plate);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog title={`${courier.displayName}'s vehicle`} onClose={onClose}>
      <form className="stack" onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        try { await api.saveCourier(company.id, courier.userId, vehicle, plate); onSaved(); } catch (err) { setError((err as Error).message); setBusy(false); }
      }}>
        <div className="tabs" role="radiogroup" aria-label="Vehicle">
          {vehicles.map((v) => <button type="button" key={v} role="radio" aria-checked={vehicle === v} className={`tab${vehicle === v ? " active" : ""}`} onClick={() => setVehicle(v)}>{vehicleLabel[v]}</button>)}
        </div>
        <p className="small muted">Estimates use 14 km/h by bicycle, 24 by scooter and 20 by car in town traffic.</p>
        <label className="field">Plate<input className="input mono-input" value={plate} onChange={(e) => setPlate(e.target.value.toLocaleUpperCase("tr-TR"))} placeholder="55 ABC 123" maxLength={16} /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Cancel</button><button className="button primary" disabled={busy}>{busy ? "Saving…" : "Save"}</button></div>
      </form>
    </Dialog>
  );
}

function OfferSettings() {
  const company = useCompany();
  const { refresh } = useAccount();
  const [form, setForm] = useState({ name: company.name, offerSeconds: String(company.offerSeconds), searchRadiusKm: String(company.searchRadiusKm), maxOffers: String(company.maxOffers) });
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      await api.configure(company.id, { name: form.name, offerSeconds: Number(form.offerSeconds), searchRadiusKm: Number(form.searchRadiusKm), maxOffers: Number(form.maxOffers) });
      await refresh();
      setMessage({ ok: true, text: "Saved. New offers follow these settings." });
    } catch (err) {
      setMessage({ ok: false, text: (err as Error).message });
    } finally {
      setBusy(false);
    }
  }

  const seconds = Number(form.offerSeconds) * Number(form.maxOffers);
  return (
    <form className="card section" onSubmit={submit}>
      <div className="row gap-6"><Settings2 size={17} /><h2>How offers work</h2></div>
      <p className="small muted measure">Each order goes to the nearest free courier within the radius, who has a few seconds to answer; then to the next. After the last offer, or once the search has lasted as long as those offers would, a dispatcher takes over.</p>
      <div className="grid-4">
        <label className="field">Company name<input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
        <label className="field">Seconds to answer<input className="input" type="number" min={10} max={300} value={form.offerSeconds} onChange={(e) => setForm({ ...form, offerSeconds: e.target.value })} /></label>
        <label className="field">Search radius (km)<input className="input" type="number" min={0.5} max={50} step={0.5} value={form.searchRadiusKm} onChange={(e) => setForm({ ...form, searchRadiusKm: e.target.value })} /></label>
        <label className="field">Offers per search<input className="input" type="number" min={1} max={20} value={form.maxOffers} onChange={(e) => setForm({ ...form, maxOffers: e.target.value })} /></label>
      </div>
      {seconds > 0 && <p className="small muted">A search lasts at most {Math.round(seconds / 6) / 10} minutes before a dispatcher is asked.</p>}
      {message && <p className={message.ok ? "small ok-text" : "form-error"} role="status">{message.text}</p>}
      <div><button className="button primary" disabled={busy}>{busy ? "Saving…" : "Save settings"}</button></div>
    </form>
  );
}
