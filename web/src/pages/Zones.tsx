import { Map as MapIcon, Plus, Undo2 } from "lucide-react";
import { useMemo, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { MapView, type Area, type Pin } from "../components/MapView";
import { Empty, Failed, Loading } from "../components/States";
import { useCompany } from "../context";
import { formatMoney } from "../format";
import { useLoad } from "../hooks";
import type { Zone } from "../types";

interface Draft { id: string | null; name: string; fee: string; corners: [number, number][]; active: boolean }

const blank: Draft = { id: null, name: "", fee: "", corners: [], active: true };

/** Where the company delivers, drawn corner by corner on the map, each area with its own fee. */
export function Zones() {
  const company = useCompany();
  const [zones, reload, setZones] = useLoad(() => api.zones(company.id), [company.id]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const areas = useMemo<Area[]>(() => {
    const list = (zones.data ?? []).filter((z) => z.id !== draft?.id).map((z) => ({ id: z.id, corners: z.corners, label: `${z.name} · ${formatMoney(z.deliveryFee)}`, tone: (z.active ? "accent" : "muted") as "accent" | "muted" }));
    if (draft && draft.corners.length > 0) list.push({ id: "draft", corners: draft.corners, label: draft.name || "New zone", tone: "accent", dashed: true } as Area & { tone: "accent" });
    return list;
  }, [zones.data, draft]);
  const pins = useMemo<Pin[]>(() => (draft?.corners ?? []).map(([lat, lng], i) => ({ id: `k${i}`, latitude: lat, longitude: lng, label: `Corner ${i + 1}`, text: String(i + 1), tone: "ink" })), [draft]);

  const edit = (z: Zone) => { setError(null); setDraft({ id: z.id, name: z.name, fee: String(z.deliveryFee), corners: z.corners, active: z.active }); };

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!draft) return;
    setBusy(true);
    setError(null);
    try {
      const body = { name: draft.name, corners: draft.corners, deliveryFee: Number(draft.fee.replace(",", ".")), active: draft.active };
      const saved = draft.id ? await api.updateZone(company.id, draft.id, body) : await api.createZone(company.id, body);
      setZones([...(zones.data ?? []).filter((z) => z.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name, "tr")));
      setDraft(null);
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setBusy(false);
    }
  }

  if (zones.state === "failed" && !zones.data) return <Failed message={zones.error} onRetry={reload} />;
  if (!zones.data) return <Loading />;

  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Zones</h1><p className="muted">Orders are taken for addresses inside an active zone. Where zones overlap, the cheaper fee applies.</p></div>
        {!draft && <button className="button primary" onClick={() => { setError(null); setDraft(blank); }}><Plus size={16} />Draw a zone</button>}
      </div>
      <div className="zones-grid">
        <section className="card map-card">
          <MapView pins={pins} areas={areas} height={540} onPick={draft ? (lat, lng) => setDraft({ ...draft, corners: [...draft.corners, [lat, lng]] }) : undefined} fitKey={draft ? (draft.id ? `zone-${draft.id}` : null) : "all"} />
        </section>
        <aside className="stack">
          {draft ? (
            <form className="card section" onSubmit={save}>
              <h2>{draft.id ? "Change zone" : "New zone"}</h2>
              <p className="small muted">Click the map to place corners in order around the area. {draft.corners.length < 3 ? `${3 - draft.corners.length} more to go.` : `${draft.corners.length} corners.`}</p>
              <label className="field">Name<input className="input" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="Atakum" maxLength={60} /></label>
              <label className="field">Delivery fee (₺)<input className="input" inputMode="decimal" value={draft.fee} onChange={(e) => setDraft({ ...draft, fee: e.target.value })} placeholder="45" /></label>
              {draft.id && <label className="check"><input type="checkbox" checked={draft.active} onChange={(e) => setDraft({ ...draft, active: e.target.checked })} /><span>Taking orders</span></label>}
              {error && <p className="form-error" role="alert">{error}</p>}
              <div className="row wrap gap-6">
                <button className="button primary" disabled={busy || draft.corners.length < 3 || !draft.name.trim() || !draft.fee}>{busy ? "Saving…" : "Save zone"}</button>
                <button type="button" className="button" disabled={draft.corners.length === 0} onClick={() => setDraft({ ...draft, corners: draft.corners.slice(0, -1) })}><Undo2 size={15} />Undo corner</button>
                <button type="button" className="button" onClick={() => setDraft({ ...draft, corners: [] })} disabled={draft.corners.length === 0}>Clear</button>
                <button type="button" className="link" onClick={() => setDraft(null)}>Cancel</button>
              </div>
            </form>
          ) : null}
          <section className="card">
            {zones.data.length === 0 ? (
              <Empty title="No zones yet" icon={<MapIcon size={24} />}><span className="small muted measure">Draw the first area you deliver to; the company takes no orders until there is one.</span></Empty>
            ) : (
              <ul className="zone-list">
                {zones.data.map((z) => (
                  <li key={z.id}>
                    <button onClick={() => edit(z)} aria-label={`Change ${z.name}`}>
                      <span className="grow min0"><strong className="block ellipsis">{z.name}</strong><span className="small muted">{z.areaKm2.toFixed(1)} km² · {z.corners.length} corners</span></span>
                      <span className="nowrap">{formatMoney(z.deliveryFee)}</span>
                      {!z.active && <span className="badge plain">Off</span>}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </aside>
      </div>
    </div>
  );
}
