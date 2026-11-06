import { Bike, Building2, ChevronRight, Plus } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api";
import { Empty } from "../components/States";
import { useAccount } from "../context";
import { initials, plural } from "../format";
import { linkProps, navigate } from "../router";
import type { Company } from "../types";

/** Where each role starts: couriers in their app, merchants in their orders, dispatchers on the live map. */
export const startOf = (c: Company) => (c.role === "Courier" ? "/courier" : c.role === "Merchant" ? `/c/${c.id}/orders` : `/c/${c.id}/live`);

export function Home() {
  const { me, refresh } = useAccount();
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (me.companies.length === 1) navigate(startOf(me.companies[0]!), true);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await api.createCompany(name);
      await refresh();
      navigate(`/c/${created.id}/zones`);
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <div className="narrow-page">
      <div className="page-head"><div><h1>Your companies</h1><p className="muted">Draw where you deliver, add merchants and couriers, and orders find their courier on their own.</p></div></div>
      {me.companies.length === 0 ? (
        <section className="card section">
          <Empty title="Nothing here yet" icon={<Building2 size={26} />}>
            <span className="muted measure">Create a courier company to start, or ask its owner to add you to the team with this account's email.</span>
          </Empty>
        </section>
      ) : (
        <ul className="org-list card">
          {me.companies.map((c) => (
            <li key={c.id}>
              <a {...linkProps(startOf(c))}>
                <span className="org-avatar">{initials(c.name)}</span>
                <span className="grow"><strong className="block">{c.name}</strong>
                  <span className="small muted">{c.role} · {plural(c.openOrders, "open order")} · {plural(c.couriersOnline, "courier")} online</span></span>
                {c.role === "Courier" ? <Bike size={18} className="muted" /> : <ChevronRight size={18} className="muted" />}
              </a>
            </li>
          ))}
        </ul>
      )}
      <form className="card section" onSubmit={create}>
        <h2>New courier company</h2>
        <label className="field">Name<input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="Hızlı Kurye Samsun" required minLength={2} maxLength={80} /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div><button className="button primary" disabled={busy || name.trim().length < 2}><Plus size={16} />Create company</button></div>
      </form>
    </div>
  );
}
