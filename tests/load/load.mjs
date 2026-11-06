// A busy evening, measured against the running stack: hundreds of couriers riding around and reporting their
// position, orders arriving every few hundred milliseconds, and every simulated courier answering offers, collecting
// and delivering with the customer's code, some of them passing.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        node tests/load/load.mjs [couriers=300] [orders=200] [seconds=60]
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const COURIERS = Number(process.argv[2] ?? 300);
const ORDERS = Number(process.argv[3] ?? 200);
const SECONDS = Number(process.argv[4] ?? 60);
const TICK_MS = 2000;
const run = Date.now().toString(36);
const password = "load-test-password";

const latencies = { ping: [], offer: [], accept: [], create: [], step: [] };
const statuses = new Map();
let errors5xx = 0;

async function call(path, token, body, method = body ? "POST" : "GET", bucket) {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  const ms = performance.now() - started;
  if (bucket) latencies[bucket].push(ms);
  statuses.set(response.status, (statuses.get(response.status) ?? 0) + 1);
  if (response.status >= 500) errors5xx++;
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, ms };
}

async function must(path, token, body, method, bucket) {
  const r = await call(path, token, body, method, bucket);
  if (r.status >= 400) throw new Error(`${method ?? (body ? "POST" : "GET")} ${path}: ${r.status} ${JSON.stringify(r.json)}`);
  return r.json;
}

async function pool(items, limit, task) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await task(items[i], i);
    }
  }));
  return results;
}

function stats(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const at = (p) => sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] ?? 0;
  return { count: sorted.length, p50: Math.round(at(50)), p95: Math.round(at(95)), p99: Math.round(at(99)), max: Math.round(sorted.at(-1) ?? 0) };
}

const report = (name, data) => console.log(`${name.padEnd(34)} ${typeof data === "string" ? data : JSON.stringify(data)}`);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const random = (min, max) => min + Math.random() * (max - min);

// Central Samsun, from Atakum to Canik: the zone, and where everyone rides.
const box = { south: 41.24, north: 41.36, west: 36.24, east: 36.42 };
const inside = () => ({ latitude: +random(box.south + 0.01, box.north - 0.01).toFixed(6), longitude: +random(box.west + 0.01, box.east - 0.01).toFixed(6) });

const owner = await must("/api/auth/register", null, { email: `owner.${run}@example.com`, displayName: "Yük Testi", password });
const token = owner.accessToken;
const company = await must("/api/companies", token, { name: "Yük Testi Kurye" });
const c = `/api/companies/${company.id}`;
await must(c, token, { name: "Yük Testi Kurye", offerSeconds: 15, searchRadiusKm: 8, maxOffers: 8 }, "PUT");
await must(`${c}/zones`, token, { name: "Merkez", corners: [[box.south, box.west], [box.south, box.east], [box.north, box.east], [box.north, box.west]], deliveryFee: 45 });
const merchants = await pool(Array.from({ length: 12 }, (_, i) => i), 4, (i) =>
  must(`${c}/merchants`, token, { name: `Dükkan ${i + 1}`, phone: "0362 000 00 00", address: `Cadde ${i + 1}`, ...inside(), prepMinutes: 5 }));

const setupStarted = performance.now();
const couriers = await pool(Array.from({ length: COURIERS }, (_, i) => i), 24, async (i) => {
  const session = await must("/api/auth/register", null, { email: `kurye${i}.${run}@example.com`, displayName: `Kurye ${i}`, password });
  await must(`${c}/members`, token, { email: session.user.email, role: "Courier" });
  await must(`${c}/couriers/me/status`, session.accessToken, { online: true });
  return { id: session.user.id, token: session.accessToken, at: inside(), order: null, picked: false, passes: 0 };
});
report("setup", `${COURIERS} couriers online in ${Math.round((performance.now() - setupStarted) / 1000)} s`);

// Customers tell the courier the code at the door; the simulation keeps it here.
const codes = new Map();
const created = [];
let running = true;

/** One courier's evening: ride a little, report, answer an offer, collect, deliver. */
async function ride(courier) {
  await sleep(random(0, TICK_MS));
  while (running) {
    const tick = performance.now();
    courier.at = {
      latitude: +Math.min(box.north - 0.005, Math.max(box.south + 0.005, courier.at.latitude + random(-0.0015, 0.0015))).toFixed(6),
      longitude: +Math.min(box.east - 0.005, Math.max(box.west + 0.005, courier.at.longitude + random(-0.0015, 0.0015))).toFixed(6),
    };
    await call(`${c}/couriers/me/locations`, courier.token, { points: [{ ...courier.at, at: new Date().toISOString() }] }, "POST", "ping");

    if (courier.order) {
      const path = `${c}/orders/${courier.order}`;
      if (!courier.picked) {
        const r = await call(`${path}/pickup`, courier.token, {}, "POST", "step");
        courier.picked = r.status === 200;
        if (r.status === 404 || r.status === 422) courier.order = null;
      } else {
        const r = await call(`${path}/deliver`, courier.token, { code: codes.get(courier.order) }, "POST", "step");
        if (r.status === 200 || r.status >= 400) { courier.order = null; courier.picked = false; }
      }
    } else {
      const offer = await call(`${c}/couriers/me/offer`, courier.token, null, "GET", "offer");
      if (offer.status === 200 && offer.json) {
        // One courier in five passes on an offer, so orders also move down the line.
        if (Math.random() < 0.2) {
          courier.passes++;
          await call(`${c}/offers/${offer.json.id}/decline`, courier.token, {}, "POST", "accept");
        } else {
          const r = await call(`${c}/offers/${offer.json.id}/accept`, courier.token, {}, "POST", "accept");
          if (r.status === 200) courier.order = r.json.id;
        }
      }
    }
    await sleep(Math.max(0, TICK_MS - (performance.now() - tick)));
  }
}

const started = performance.now();
const riders = couriers.map(ride);

// Orders arrive evenly over the window, from random merchants to random doors.
const every = (SECONDS * 1000) / ORDERS;
for (let i = 0; i < ORDERS; i++) {
  const merchant = merchants[i % merchants.length];
  const order = await must(`${c}/orders`, token, {
    merchantId: merchant.id, customerName: `Müşteri ${i}`, customerPhone: "0532 000 00 00", dropoffAddress: `Sokak ${i}`, ...inside(), dropoffNotes: "", items: "1 paket",
  }, "POST", "create");
  codes.set(order.id, order.deliveryCode);
  created.push(order.id);
  await sleep(Math.max(0, started + (i + 1) * every - performance.now()));
}

// Let the last orders find a courier and arrive.
const deadline = performance.now() + 90_000;
let open = ORDERS;
while (performance.now() < deadline) {
  const page = await must(`${c}/orders?status=Searching,Assigned,PickedUp&pageSize=1`, token);
  open = page.total;
  if (open === 0) break;
  await sleep(2000);
}
running = false;
await Promise.all(riders);
const elapsed = (performance.now() - started) / 1000;

// What happened to each order, from its own history.
const histories = await pool(created, 16, (id) => must(`${c}/orders/${id}`, token));
const offerDelay = [];
const assignDelay = [];
const offersPerOrder = [];
const outcome = {};
for (const o of histories) {
  outcome[o.status] = (outcome[o.status] ?? 0) + 1;
  const at = (kind) => o.history.find((h) => h.kind === kind)?.at;
  const createdAt = new Date(at("Created")).getTime();
  if (at("Offered")) offerDelay.push(new Date(at("Offered")).getTime() - createdAt);
  if (at("Assigned")) assignDelay.push(new Date(at("Assigned")).getTime() - createdAt);
  offersPerOrder.push(o.history.filter((h) => h.kind === "Offered").length);
}

const requests = [...statuses.values()].reduce((a, b) => a + b, 0);
report("window", `${Math.round(elapsed)} s, ${COURIERS} couriers, ${ORDERS} orders`);
report("requests", `${requests} (${Math.round(requests / elapsed)}/s), status ${JSON.stringify(Object.fromEntries(statuses))}`);
report("position report (ms)", stats(latencies.ping));
report("offer check (ms)", stats(latencies.offer));
report("accept or pass (ms)", stats(latencies.accept));
report("pickup and deliver (ms)", stats(latencies.step));
report("new order (ms)", stats(latencies.create));
report("order to first offer (ms)", stats(offerDelay));
report("order to courier (ms)", stats(assignDelay));
report("offers per order", stats(offersPerOrder));
report("passes", couriers.reduce((n, x) => n + x.passes, 0));
report("outcome", outcome);
report("server errors", errors5xx);
report("company", c.split("/").pop());

const ok = errors5xx === 0 && (outcome.Delivered ?? 0) + (outcome.Unassigned ?? 0) === ORDERS;
console.log(ok ? "\nEvery order was delivered or handed to a dispatcher, with no server errors." : "\nSomething went wrong: see above.");
process.exit(ok ? 0 : 1);
