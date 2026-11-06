import { ClipboardList, Plus, Search } from "lucide-react";
import { useCallback, useState } from "react";
import { api } from "../api";
import { LiveBadge, StatusBadge } from "../components/Badges";
import { Empty, Failed, SkeletonRows } from "../components/States";
import { canSendOrders, isDispatcher, useCompany } from "../context";
import { formatDayTime, formatTime } from "../format";
import { useDebounced, useLoad } from "../hooks";
import { useLive } from "../live";
import { linkProps, navigate } from "../router";
import type { OrderStatus } from "../types";

const filters: { key: string; label: string; status: OrderStatus[] }[] = [
  { key: "open", label: "Open", status: ["Searching", "Unassigned", "Assigned", "PickedUp"] },
  { key: "waiting", label: "Needs a courier", status: ["Unassigned"] },
  { key: "done", label: "Delivered", status: ["Delivered"] },
  { key: "cancelled", label: "Cancelled", status: ["Cancelled"] },
  { key: "all", label: "All", status: [] },
];

/** Every order of the company (or, for a merchant, of their shop), newest first. */
export function Orders() {
  const company = useCompany();
  const [filter, setFilter] = useState("open");
  const [text, setText] = useState("");
  const [page, setPage] = useState(1);
  const q = useDebounced(text);
  const status = filters.find((f) => f.key === filter)!.status;
  const [orders, reload] = useLoad(() => api.orders(company.id, { status, q, page, pageSize: 25 }), [company.id, filter, q, page]);
  const live = useLive(company.id, { orderChanged: useCallback(() => reload(), [reload]), resync: reload });

  const data = orders.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <div className="row gap-10"><h1>Orders</h1><LiveBadge state={live} /></div>
          <p className="muted">{isDispatcher(company) ? "Every order the company carries." : "The orders of your merchant."}</p>
        </div>
        {canSendOrders(company) && <a className="button primary" {...linkProps(`/c/${company.id}/orders/new`)}><Plus size={16} />New order</a>}
      </div>
      <section className="card table-card">
        <div className="toolbar">
          <div className="tabs" role="tablist" aria-label="Show">
            {filters.map((f) => (
              <button key={f.key} role="tab" aria-selected={filter === f.key} className={`tab${filter === f.key ? " active" : ""}`} onClick={() => { setFilter(f.key); setPage(1); }}>{f.label}</button>
            ))}
          </div>
          <label className="search grow-sm"><Search size={15} /><input className="input" placeholder="Code, customer or address" value={text} onChange={(e) => { setText(e.target.value); setPage(1); }} aria-label="Search orders" /></label>
        </div>
        {orders.state === "failed" ? <Failed message={orders.error} onRetry={reload} /> : data && data.items.length === 0 ? (
          <Empty title={q ? "No order matches" : "No orders here"} icon={<ClipboardList size={24} />}>
            {canSendOrders(company) && !q && <a className="link" {...linkProps(`/c/${company.id}/orders/new`)}>Send the first one</a>}
          </Empty>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Order</th><th>Status</th><th>Merchant</th><th className="hide-sm">Deliver to</th><th className="hide-sm">Courier</th><th className="right">Due</th></tr></thead>
              {!data ? <SkeletonRows columns={6} /> : (
                <tbody>
                  {data.items.map((o) => (
                    <tr key={o.id} className="clickable" onClick={() => navigate(`/c/${company.id}/orders/${o.id}`)}>
                      <td><a className="strong-link mono" {...linkProps(`/c/${company.id}/orders/${o.id}`)} onClick={(e) => e.stopPropagation()}>{o.code}</a><span className="block small faint">{formatDayTime(o.createdAt)}</span></td>
                      <td><StatusBadge status={o.status} /></td>
                      <td className="ellipsis max-180">{o.merchantName}</td>
                      <td className="hide-sm"><span className="block ellipsis max-260">{o.customerName}</span><span className="small muted block ellipsis max-260">{o.dropoffAddress}</span></td>
                      <td className="hide-sm">{o.courierName ?? <span className="faint">—</span>}</td>
                      <td className="right nowrap">{o.estimatedDeliveryAt ? formatTime(o.estimatedDeliveryAt) : <span className="faint">—</span>}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
        {data && data.total > data.pageSize && (
          <div className="pager">
            <span>{data.total} orders · page {page} of {pages}</span>
            <div className="row gap-6">
              <button className="button small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
              <button className="button small" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
